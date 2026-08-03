using System;
using System.Collections.Generic;
using System.Threading;
using FlashEditor.Cache.Region;

//System.Drawing.Region arrives via the WinForms implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     Owns every decoded map square, and is the only thing that touches the cache for them.
    /// </summary>
    /// <remarks>
    ///     Two invariants, and both fail silently if broken.
    ///
    ///     <b>Instance identity.</b> <c>MapEditHistory</c> holds <c>IMapEdit.Target</c>, which is a
    ///     <see cref="MapRegion"/> reference. If the store ever hands out a second instance for the
    ///     same coordinates, every edit in the history points at an orphan: undo appears to do
    ///     nothing, the inspector shows unedited values, and a save writes the wrong bytes, with no
    ///     exception anywhere. So a square is loaded once, and the moment it is edited it moves into
    ///     a pinned set that eviction cannot reach.
    ///
    ///     <b>Serialised cache access.</b> The whole class runs under one lock, and the load itself
    ///     happens inside it. The JS5 decode path is not thread-safe, and this is where a UI thread
    ///     inspecting a tile and a render thread decoding a square would otherwise meet. Each
    ///     <see cref="GetOrLoad"/> takes and releases the lock on its own, so the longest a UI read
    ///     can be blocked is one square decode rather than the nine a neighbourhood needs.
    /// </remarks>
    public sealed class MapSquareStore : IDisposable {
        /// <summary>
        ///     Clean squares kept decoded before the oldest is dropped.
        /// </summary>
        /// <remarks>
        ///     A square is roughly 350 KB of grids and raw bytes, so this is around 45 MB. It is also
        ///     sized against the sweep order: three consecutive world rows hold about 90 existing
        ///     squares, which is what a row-major sweep needs resident to decode each square once
        ///     rather than once per neighbour that borrows it as an apron.
        /// </remarks>
        public const int ResidentSquareBudget = 128;

        /// <summary>Map squares along each axis of the world.</summary>
        public const int WorldSquares = 256;

        private readonly MapSquareLoader loader;
        private readonly object gate = new object();

        private readonly Dictionary<int, MapRegion> pinned = new Dictionary<int, MapRegion>();
        private readonly Dictionary<int, MapRegion> resident = new Dictionary<int, MapRegion>();
        private readonly LinkedList<int> order = new LinkedList<int>();
        private readonly Dictionary<int, LinkedListNode<int>> nodes = new Dictionary<int, LinkedListNode<int>>();
        private readonly HashSet<int> missingKeys = new HashSet<int>();
        private readonly bool[,] presence = new bool[WorldSquares, WorldSquares];

        private int missingKeyCount;

        /// <summary>
        ///     Builds the store and scans which squares exist.
        /// </summary>
        /// <remarks>
        ///     The scan is 65,536 name hashes against the index-5 reference table and decodes no map
        ///     data at all. It is done here rather than in the world navigator so that it runs once
        ///     per cache open instead of once per consumer.
        /// </remarks>
        /// <param name="loader">The loader to read squares through.</param>
        public MapSquareStore(MapSquareLoader loader) {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));

            for (int rx = 0; rx < WorldSquares; rx++) {
                for (int ry = 0; ry < WorldSquares; ry++) {
                    if (!loader.Exists(rx, ry))
                        continue;
                    presence[rx, ry] = true;
                    SquareCount++;
                }
            }
        }

        /// <summary>Which of the 65,536 possible squares the cache actually carries.</summary>
        public bool[,] PresenceMap => presence;

        /// <summary>How many squares the cache carries. 1684 in the reference cache.</summary>
        public int SquareCount { get; }

        /// <summary>
        ///     Squares that exist but whose locations could not be decrypted, as packed region ids.
        /// </summary>
        /// <remarks>
        ///     Grows as squares are loaded rather than being known up front, because the only way to
        ///     find out is to try the key. Reported in the status line so an empty-looking square
        ///     reads as "we cannot open this" rather than as "there is nothing here".
        /// </remarks>
        public IReadOnlyCollection<int> MissingKeyRegions {
            get { lock (gate) return new List<int>(missingKeys); }
        }

        /// <summary>
        ///     How many squares have failed to decrypt, without taking the lock or allocating.
        /// </summary>
        /// <remarks>
        ///     The status line reads this on every pan, zoom and hover.
        ///     <see cref="MissingKeyRegions"/> cannot serve that: it takes the same lock
        ///     <see cref="GetOrLoad"/> holds for a whole JS5 read, gunzip and XTEA decrypt, so a
        ///     drag would stall on whatever square the render thread happened to be decoding, and
        ///     it copies the id set out to be counted. Keep the collection for the rare caller that
        ///     wants the ids themselves.
        /// </remarks>
        public int MissingKeyCount => Volatile.Read(ref missingKeyCount);

        /// <summary>Whether the cache has terrain for a square.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns><c>true</c> when the square exists.</returns>
        public bool Exists(int regionX, int regionY) =>
            regionX >= 0 && regionY >= 0 && regionX < WorldSquares && regionY < WorldSquares
            && presence[regionX, regionY];

        /// <summary>
        ///     Returns a square, decoding it if necessary. Blocks; call it from the render thread.
        /// </summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The square, or <c>null</c> when the cache has none.</returns>
        public MapRegion GetOrLoad(int regionX, int regionY) {
            if (!Exists(regionX, regionY))
                return null;

            int id = MapSquareNames.RegionId(regionX, regionY);

            lock (gate) {
                if (pinned.TryGetValue(id, out MapRegion held))
                    return held;

                if (resident.TryGetValue(id, out MapRegion known)) {
                    MarkUsed(id);
                    return known;
                }

                MapRegion square = loader.Load(regionX, regionY, out LocationLoadResult result);
                if (square == null)
                    return null;

                if (result == LocationLoadResult.MissingKey && missingKeys.Add(id))
                    Volatile.Write(ref missingKeyCount, missingKeys.Count);

                resident[id] = square;
                MarkUsed(id);
                TrimToBudget();
                return square;
            }
        }

        /// <summary>
        ///     Returns a square only if it is already decoded, without touching the cache.
        /// </summary>
        /// <remarks>
        ///     For the UI thread, where a decode would show up as a stall on every mouse move.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <param name="square">The square, or <c>null</c>.</param>
        /// <returns><c>true</c> when it was resident.</returns>
        public bool TryGetResident(int regionX, int regionY, out MapRegion square) {
            square = null;
            if (!Exists(regionX, regionY))
                return false;

            int id = MapSquareNames.RegionId(regionX, regionY);

            lock (gate) {
                if (pinned.TryGetValue(id, out square))
                    return true;

                if (resident.TryGetValue(id, out square)) {
                    MarkUsed(id);
                    return true;
                }
            }

            square = null;
            return false;
        }

        /// <summary>
        ///     Builds the 3x3 neighbourhood a square has to be rendered out of.
        /// </summary>
        /// <remarks>
        ///     A square cannot be coloured alone: the underlay blend reaches five tiles across every
        ///     boundary and the relief stencil reads the shared vertices. The apron is what makes a
        ///     per-square render seamless.
        ///
        ///     When <paramref name="loadMissing"/> is set the scene also carries a snapshot of every
        ///     square's location list, taken under this store's lock. The rasteriser would otherwise
        ///     iterate the live list while the UI thread applied an add or a remove, which throws
        ///     partway through a square. That race does not exist in a single-threaded viewer; it is
        ///     created by rendering in the background, so it is fixed here rather than tolerated.
        /// </remarks>
        /// <param name="regionX">Region X of the centre square.</param>
        /// <param name="regionY">Region Y of the centre square.</param>
        /// <param name="loadMissing">
        ///     <c>true</c> to decode absent squares, which blocks. <c>false</c> reads only what is
        ///     already resident, which is what the UI thread wants.
        /// </param>
        /// <returns>A scene whose centre square is the one asked for.</returns>
        public MapScene SceneAround(int regionX, int regionY, bool loadMissing) {
            var grid = new MapRegion[3, 3];
            IReadOnlyList<Location>[,] snapshots = loadMissing ? new IReadOnlyList<Location>[3, 3] : null;

            for (int dx = 0; dx < 3; dx++) {
                for (int dy = 0; dy < 3; dy++) {
                    int rx = regionX - 1 + dx;
                    int ry = regionY - 1 + dy;

                    MapRegion square = loadMissing
                        ? GetOrLoad(rx, ry)
                        : (TryGetResident(rx, ry, out MapRegion known) ? known : null);

                    grid[dx, dy] = square;

                    if (snapshots != null && square != null)
                        snapshots[dx, dy] = LocationSnapshot(square);
                }
            }

            return MapScene.FromSquares(regionX - 1, regionY - 1, grid, snapshots);
        }

        /// <summary>
        ///     Copies a square's location list under the lock.
        /// </summary>
        /// <param name="square">The square.</param>
        /// <returns>A private copy, safe to iterate while the list is edited.</returns>
        public Location[] LocationSnapshot(MapRegion square) {
            if (square == null) throw new ArgumentNullException(nameof(square));

            lock (gate) {
                List<Location> live = square.GetLocations();
                var copy = new Location[live.Count];
                live.CopyTo(copy);
                return copy;
            }
        }

        /// <summary>
        ///     Moves an edited square out of reach of eviction.
        /// </summary>
        /// <remarks>
        ///     Call this every time an edit is applied. A dirty square that gets evicted and reloaded
        ///     comes back as a different instance with the edit gone, while the undo history still
        ///     points at the instance that had it.
        /// </remarks>
        /// <param name="square">The square that was edited.</param>
        public void PinEdited(MapRegion square) {
            if (square == null) throw new ArgumentNullException(nameof(square));

            int id = square.GetRegionID();

            lock (gate) {
                resident.Remove(id);
                if (nodes.TryGetValue(id, out LinkedListNode<int> node)) {
                    order.Remove(node);
                    nodes.Remove(id);
                }

                pinned[id] = square;
            }
        }

        /// <summary>
        ///     Every square holding unsaved changes, with its coordinates.
        /// </summary>
        /// <remarks>
        ///     The save path asks the store rather than the undo history on purpose.
        ///     <c>Region.Dirty</c> is never cleared by an undo, so a fully-undone square still counts
        ///     as dirty and is still offered for save; that is existing behaviour, and it is exactly
        ///     why the history is the wrong thing to enumerate.
        /// </remarks>
        /// <returns>The dirty squares.</returns>
        public IReadOnlyList<(MapRegion Square, int RegionX, int RegionY)> DirtySquares() {
            var dirty = new List<(MapRegion Square, int RegionX, int RegionY)>();

            lock (gate) {
                foreach (KeyValuePair<int, MapRegion> pair in pinned)
                    if (pair.Value.Dirty)
                        dirty.Add((pair.Value, MapSquareNames.RegionX(pair.Key), MapSquareNames.RegionY(pair.Key)));

                foreach (KeyValuePair<int, MapRegion> pair in resident)
                    if (pair.Value.Dirty)
                        dirty.Add((pair.Value, MapSquareNames.RegionX(pair.Key), MapSquareNames.RegionY(pair.Key)));
            }

            dirty.Sort((a, b) => a.RegionX != b.RegionX
                ? a.RegionX.CompareTo(b.RegionX)
                : a.RegionY.CompareTo(b.RegionY));
            return dirty;
        }

        /// <summary>
        ///     Runs an action holding the store's lock.
        /// </summary>
        /// <remarks>
        ///     For the save path, which reads and writes the same <c>RSCache</c> the render thread
        ///     decodes from. Nothing else in the editor may touch that cache concurrently, and this
        ///     is the one lock that already serialises it.
        /// </remarks>
        /// <param name="action">What to run.</param>
        public void RunExclusive(Action action) {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (gate) action();
        }

        private void MarkUsed(int id) {
            if (nodes.TryGetValue(id, out LinkedListNode<int> node)) {
                order.Remove(node);
                order.AddFirst(node);
                return;
            }

            nodes[id] = order.AddFirst(id);
        }

        private void TrimToBudget() {
            while (resident.Count > ResidentSquareBudget && order.Last != null) {
                int id = order.Last.Value;
                order.RemoveLast();
                nodes.Remove(id);
                resident.Remove(id);
            }
        }

        /// <summary>Drops every decoded square, pinned ones included.</summary>
        /// <remarks>
        ///     Only safe once nothing holds a reference into the history, which in practice means
        ///     when the panel unbinds from a cache.
        /// </remarks>
        public void Dispose() {
            lock (gate) {
                pinned.Clear();
                resident.Clear();
                order.Clear();
                nodes.Clear();
                missingKeys.Clear();
                Volatile.Write(ref missingKeyCount, 0);
            }
        }
    }
}
