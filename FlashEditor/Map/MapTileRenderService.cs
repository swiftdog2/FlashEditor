using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using FlashEditor.Cache.Util;

namespace FlashEditor.Map {
    /// <summary>
    ///     Renders map square bitmaps off the UI thread, one square at a time.
    /// </summary>
    /// <remarks>
    ///     <b>One dedicated thread, not the thread pool, and not more than one.</b>
    ///     <see cref="MapRasteriser"/> holds six unsynchronised memo dictionaries and mutable zoom
    ///     and relief settings, and the JS5 decode path underneath is not thread-safe either. Every
    ///     decode, blend and rasterisation therefore happens on this one thread. The upgrade path, if
    ///     the sweep ever proves too slow, is a rasteriser per thread plus a reader lock over the
    ///     cache, measured before and after - not simply widening a pool.
    ///
    ///     Completions are counted rather than announced. A whole-world sweep completes 1684 tiles,
    ///     and 1684 repaints would cost far more than the renders; the view polls
    ///     <see cref="ReadyCount"/> on a timer and repaints only when it has moved.
    /// </remarks>
    public sealed class MapTileRenderService : IDisposable {
        /// <summary>The tile window inside the 3x3 apron that is actually painted.</summary>
        private static readonly Rectangle CentreSquare = new Rectangle(64, 64, 64, 64);

        private readonly MapSquareStore store;
        private readonly MapRasteriser rasteriser;
        private readonly MapOverviewRasteriser overview;
        private readonly MapTileCache tiles = new MapTileCache();

        private readonly object gate = new object();
        private readonly List<MapTileKey> priority = new List<MapTileKey>();
        private readonly Queue<MapTileKey> sweep = new Queue<MapTileKey>();
        private readonly HashSet<MapTileKey> outstanding = new HashSet<MapTileKey>();
        private readonly ManualResetEventSlim wake = new ManualResetEventSlim(false);
        private readonly Thread worker;

        private MapRenderSignature signature;
        private volatile bool stopping;
        private int readyCount;

        /// <summary>Creates the service and starts its render thread.</summary>
        /// <param name="store">The square store to decode through.</param>
        /// <param name="rasteriser">
        ///     The rasteriser to draw with. It becomes owned by the render thread: nothing else may
        ///     call it, and nothing may write its zoom or relief settings.
        /// </param>
        public MapTileRenderService(MapSquareStore store, MapRasteriser rasteriser) {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.rasteriser = rasteriser ?? throw new ArgumentNullException(nameof(rasteriser));
            overview = new MapOverviewRasteriser(rasteriser);

            worker = new Thread(Run) {
                IsBackground = true,
                Name = "MapTileRenderer",

                //Below normal so a sweep of 1684 squares cannot starve the UI thread it is drawing
                //for. The sweep is meant to fill in behind the user, not to compete with them.
                Priority = ThreadPriority.BelowNormal
            };
            worker.Start();
        }

        /// <summary>
        ///     Raised on the <b>render thread</b> after each tile completes.
        /// </summary>
        /// <remarks>
        ///     A handler must marshal before touching any control. The view does not use this; it
        ///     polls <see cref="ReadyCount"/> from a timer instead, because a per-tile repaint over a
        ///     whole-world sweep costs more than the sweep does.
        /// </remarks>
        public event EventHandler TilesReady;

        /// <summary>The rendered tiles. The view draws from here and must drain its retired queue.</summary>
        public MapTileCache Tiles => tiles;

        /// <summary>What the renderer is currently drawing for.</summary>
        public MapRenderSignature Signature {
            get { lock (gate) return signature; }
        }

        /// <summary>Tiles requested and not yet drawn.</summary>
        public int PendingCount {
            get { lock (gate) return priority.Count + sweep.Count; }
        }

        /// <summary>Tiles completed since the service started. Monotonic; used to poll for repaints.</summary>
        public int ReadyCount => Volatile.Read(ref readyCount);

        /// <summary>
        ///     How many squares currently hold an overview tile, for a "N of 1684" readout.
        /// </summary>
        /// <remarks>
        ///     Not <see cref="ReadyCount"/>, which is a repaint-poll token: it counts detail tiles
        ///     and every re-render as well, so it walks straight past the square count.
        /// </remarks>
        public int RenderedSquareCount => tiles.BaseLevelSquareCount;

        /// <summary>
        ///     Changes what is being drawn, and throws away whatever the change actually affects.
        /// </summary>
        /// <remarks>
        ///     A plane or relief change invalidates every tile at every level, the 35 MiB overview
        ///     band included, so it is expensive by nature. The relief slider is debounced upstream
        ///     for exactly that reason.
        ///
        ///     A layer change is not the same thing.
        ///     <see cref="MapRasteriser.EffectiveLayers"/> masks most of the object layers off
        ///     below level 1, so ticking "Game objects" while zoomed out changes no pixel in the
        ///     overview band - and clearing that band for it costs a whole-world re-decode to
        ///     reproduce an identical picture. Only the levels whose effective layers actually
        ///     differ are dropped.
        /// </remarks>
        /// <param name="value">The new signature.</param>
        public void SetSignature(MapRenderSignature value) {
            MapRenderSignature previous;

            lock (gate) {
                if (signature.Equals(value))
                    return;

                previous = signature;
                signature = value;
                priority.Clear();
                sweep.Clear();
                outstanding.Clear();
            }

            if (!OnlyLayersDiffer(previous, value)) {
                tiles.Clear();
                return;
            }

            var affected = new List<int>();
            for (int level = MapCamera.MinLevel; level <= MapCamera.MaxLevel; level++)
                if (MapRasteriser.EffectiveLayers(previous.Layers, level)
                    != MapRasteriser.EffectiveLayers(value.Layers, level))
                    affected.Add(level);

            tiles.ClearLevels(affected);
        }

        /// <summary>
        ///     Whether two signatures agree on everything except which layers were asked for.
        /// </summary>
        /// <remarks>
        ///     <c>Generation</c> is deliberately ignored. It moves on every bump, so comparing it
        ///     would make every change look like a wholesale one and defeat the partial clear.
        ///     Anything that wants a genuine wholesale invalidation changes a setting as well.
        /// </remarks>
        private static bool OnlyLayersDiffer(MapRenderSignature a, MapRenderSignature b) =>
            a.Plane == b.Plane
            && a.ReliefStrength == b.ReliefStrength
            && a.ReliefAzimuth == b.ReliefAzimuth
            && a.ReliefAltitude == b.ReliefAltitude
            && a.Layers != b.Layers;

        /// <summary>
        ///     Replaces the visible-tile queue, nearest first.
        /// </summary>
        /// <remarks>
        ///     Replaced rather than appended: what was visible two pans ago is not worth drawing, and
        ///     an append-only queue turns a fast pan into a backlog of tiles nobody will look at.
        /// </remarks>
        /// <param name="keys">The missing tiles, already ordered by distance from the viewport centre.</param>
        public void RequestVisible(IReadOnlyList<MapTileKey> keys) {
            if (keys == null) throw new ArgumentNullException(nameof(keys));

            lock (gate) {
                priority.Clear();

                foreach (MapTileKey key in keys) {
                    if (outstanding.Contains(key))
                        continue;
                    priority.Add(key);
                }

                if (priority.Count > 0)
                    wake.Set();
            }
        }

        /// <summary>
        ///     Queues every existing square at the coarsest levels, so the world silhouette fills in.
        /// </summary>
        /// <remarks>
        ///     Row-major from the camera's own row, and deliberately not a spiral. A spiral's working
        ///     set spans many world rows at once, which thrashes the store's 128-square budget and
        ///     re-decodes each square up to nine times as its neighbours borrow it for an apron.
        ///     Three consecutive rows hold around 90 existing squares, so row-major decodes each
        ///     square about once - and starting at the camera's row still fills in from where the
        ///     user is looking.
        /// </remarks>
        /// <param name="startRegionY">The world row to start from; the sweep wraps around to it.</param>
        /// <param name="level">The level to sweep, which should be in the permanent overview band.</param>
        public void RequestOverviewSweep(int startRegionY = 0, int level = 0) {
            var keys = new List<MapTileKey>();
            int start = Math.Clamp(startRegionY, 0, MapSquareStore.WorldSquares - 1);

            for (int step = 0; step < MapSquareStore.WorldSquares; step++) {
                int ry = (start + step) % MapSquareStore.WorldSquares;
                for (int rx = 0; rx < MapSquareStore.WorldSquares; rx++)
                    if (store.Exists(rx, ry))
                        keys.Add(new MapTileKey(rx, ry, level));
            }

            lock (gate) {
                sweep.Clear();
                foreach (MapTileKey key in keys)
                    if (!tiles.TryGet(key, out _))
                        sweep.Enqueue(key);

                if (sweep.Count > 0)
                    wake.Set();
            }
        }

        /// <summary>Looks a rendered tile up without requesting anything.</summary>
        /// <param name="key">The tile.</param>
        /// <param name="bitmap">The bitmap, or <c>null</c>.</param>
        /// <returns><c>true</c> when it has been rendered.</returns>
        public bool TryGetTile(MapTileKey key, out DirectBitmap bitmap) => tiles.TryGet(key, out bitmap);

        /// <summary>Drops the rendered tiles around an edited square at every level.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        public void InvalidateSquare(int regionX, int regionY) => tiles.InvalidateSquare(regionX, regionY);

        /// <summary>
        ///     Puts a key back at the head of the priority queue after a refused store.
        /// </summary>
        /// <remarks>
        ///     A refusal means the square was edited, or the settings changed, while the render was
        ///     in flight - so the tile is genuinely wanted, just not the one that was drawn.
        ///     Without this it is neither cached nor queued: the paint that reported it missing has
        ///     already run and skipped it because it was still outstanding, and nothing schedules
        ///     another, so the edited square would sit as a placeholder until the user happened to
        ///     pan or zoom.
        /// </remarks>
        private void Requeue(MapTileKey key) {
            lock (gate) {
                if (!priority.Contains(key))
                    priority.Insert(0, key);
                wake.Set();
            }
        }

        private bool TryTake(out MapTileKey key, out MapRenderSignature current) {
            lock (gate) {
                current = signature;

                if (priority.Count > 0) {
                    key = priority[0];
                    priority.RemoveAt(0);
                    outstanding.Add(key);
                    return true;
                }

                if (sweep.Count > 0) {
                    key = sweep.Dequeue();
                    outstanding.Add(key);
                    return true;
                }

                //Reset inside the lock, so a request made after this point cannot be lost.
                wake.Reset();
                key = default;
                return false;
            }
        }

        private void Run() {
            while (!stopping) {
                if (!TryTake(out MapTileKey key, out MapRenderSignature current)) {
                    //A bounded wait rather than an unbounded one, so a missed signal costs a quarter
                    //of a second rather than a hung sweep.
                    wake.Wait(250);
                    continue;
                }

                try {
                    if (tiles.TryGet(key, out _))
                        continue;

                    if (key.Level <= 0)
                        RenderOverviewPyramid(key, current);
                    else
                        RenderDetailTile(key, current);
                }
                catch (Exception) {
                    //A square that will not decode must not take the render thread down with it: the
                    //rest of the world still renders, and the tile simply stays a placeholder.
                }
                finally {
                    lock (gate) outstanding.Remove(key);
                }
            }
        }

        /// <summary>
        ///     Renders every overview level of a square from the one decode the request paid for.
        /// </summary>
        /// <remarks>
        ///     The whole point of the permanent overview band is that it is never rebuilt, so it is
        ///     worth filling completely the first time a square is touched: the four reductions cost
        ///     1360 more pixel writes against a decode that dominates everything, and they mean
        ///     zooming out after a sweep is instant rather than another 1684 decodes.
        /// </remarks>
        private void RenderOverviewPyramid(MapTileKey key, MapRenderSignature current) {
            if (!store.Exists(key.RegionX, key.RegionY))
                return;

            //Taken before the scene is read, so anything that invalidates this square between here
            //and the put is caught by the put rather than being overwritten by it.
            MapTileStamp stamp = tiles.Stamp(key.RegionX, key.RegionY);

            MapScene scene = store.SceneAround(key.RegionX, key.RegionY, loadMissing: true);
            MapLayers effective = MapRasteriser.EffectiveLayers(current.Layers, key.Level);

            IReadOnlyList<(int Level, DirectBitmap Bitmap)> pyramid = overview.RenderPyramid(
                scene, CentreSquare, current.Plane, effective, MapCamera.MinLevel, current.ReliefStrength);

            bool stale;
            lock (gate) stale = !signature.Equals(current);

            bool published = false;

            foreach ((int level, DirectBitmap bitmap) in pyramid) {
                if (stale) {
                    //Rendered for settings the user has already moved off. Caching it would put the
                    //old plane's picture under the new plane's key.
                    bitmap.Dispose();
                    continue;
                }

                //The cache re-checks under its own lock and retires the bitmap itself when the
                //answer has changed since the stamp, so ownership is handed over either way.
                published |= tiles.Put(new MapTileKey(key.RegionX, key.RegionY, level), bitmap, stamp);
            }

            if (!published) {
                //Refused rather than merely stale: the square moved on underneath the render, so
                //it still wants a tile and has to be asked for again.
                if (!stale)
                    Requeue(key);
                return;
            }

            Interlocked.Increment(ref readyCount);
            TilesReady?.Invoke(this, EventArgs.Empty);
        }

        private void RenderDetailTile(MapTileKey key, MapRenderSignature current) {
            if (!store.Exists(key.RegionX, key.RegionY))
                return;

            MapTileStamp stamp = tiles.Stamp(key.RegionX, key.RegionY);

            MapScene scene = store.SceneAround(key.RegionX, key.RegionY, loadMissing: true);
            MapLayers effective = MapRasteriser.EffectiveLayers(current.Layers, key.Level);

            int tilePixels = 1 << key.Level;

            //Written here rather than by the UI thread: these are the only mutable fields on the
            //shared rasteriser, and the whole point of the signature is that the worker reads its
            //settings once per request instead of racing the slider.
            rasteriser.TilePixels = tilePixels;
            rasteriser.HillshadeStrength = current.ReliefStrength;
            rasteriser.HillshadeAzimuth = current.ReliefAzimuth;
            rasteriser.HillshadeAltitude = current.ReliefAltitude;

            var bitmap = new DirectBitmap(CentreSquare.Width * tilePixels, CentreSquare.Height * tilePixels);

            try {
                rasteriser.RenderWindow(scene, current.Plane, CentreSquare, bitmap, effective);
            }
            catch (Exception) {
                bitmap.Dispose();
                throw;
            }

            bool stale;
            lock (gate) stale = !signature.Equals(current);

            if (stale) {
                bitmap.Dispose();
                return;
            }

            if (!tiles.Put(key, bitmap, stamp)) {
                Requeue(key);
                return;
            }

            Interlocked.Increment(ref readyCount);
            TilesReady?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Stops the render thread and frees every cached bitmap.</summary>
        /// <remarks>
        ///     Joins before disposing the cache, because the thread is the only other thing that
        ///     touches it. The join is bounded: a render that has wedged on a bad square must not
        ///     stop the editor from closing.
        /// </remarks>
        public void Dispose() {
            stopping = true;
            wake.Set();

            if (worker.IsAlive)
                worker.Join(TimeSpan.FromSeconds(2));

            tiles.Dispose();

            //The wake handle is deliberately not disposed. The join above is bounded, so a render
            //wedged on a bad square can still be inside Wait when this returns, and disposing it
            //under that thread would throw on a background thread with nothing to catch it.
        }
    }
}
