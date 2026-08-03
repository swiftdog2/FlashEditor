using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.Util;

namespace FlashEditor.Map {
    /// <summary>
    ///     Holds rendered map square bitmaps, in two bands with different eviction rules.
    /// </summary>
    /// <remarks>
    ///     The pyramid inverts the memory problem a flat whole-world bitmap has. Every square at
    ///     every overview level - one pixel per tile down to one pixel per sixteen - costs 21,824
    ///     bytes, so all 1684 of them together are 35 MiB and can simply be kept. Those are also the
    ///     levels nobody wants to rebuild, because rebuilding them means decoding the whole cache
    ///     again. The expensive levels, 256 KB to 4 MB a square, are exactly the levels where only
    ///     tens of squares fit on screen, so a byte budget four times the worst working set makes
    ///     panning back and forth at high zoom free.
    ///
    ///     <b>Eviction never disposes on the spot.</b> The renderer runs on a background thread and
    ///     can evict a bitmap that the UI thread is inside <c>DrawImage</c> on, which is a
    ///     use-after-free with no exception to catch. Evicted and invalidated bitmaps queue instead,
    ///     and <see cref="DrainRetired"/> disposes them at the top of a paint, before anything is
    ///     drawn - by which point nothing painted in the previous frame can still be in use.
    /// </remarks>
    public sealed class MapTileCache : IDisposable {
        /// <summary>
        ///     Bytes the detail band may hold before it starts evicting.
        /// </summary>
        /// <remarks>
        ///     A 1600x1000 viewport with a one-square ring holds 48 MB at level 4, and less at every
        ///     coarser level. Four times the worst case is deliberate headroom, not slack.
        /// </remarks>
        public const long DetailByteBudget = 192L << 20;

        /// <summary>The coarsest detail level. At or below this, tiles are never evicted.</summary>
        public const int FirstDetailLevel = 1;

        private readonly object gate = new object();
        private readonly Dictionary<MapTileKey, Entry> entries = new Dictionary<MapTileKey, Entry>();
        private readonly Queue<DirectBitmap> retired = new Queue<DirectBitmap>();
        private readonly Dictionary<int, long> squareEpochs = new Dictionary<int, long>();

        private long detailBytes;
        private long overviewBytes;
        private long clock;
        private long generation;
        private int baseLevelCount;

        /// <summary>Bytes held by the evictable detail band.</summary>
        public long DetailBytes { get { lock (gate) return detailBytes; } }

        /// <summary>Bytes held by the permanent overview band.</summary>
        public long OverviewBytes { get { lock (gate) return overviewBytes; } }

        /// <summary>Tiles currently held.</summary>
        public int Count { get { lock (gate) return entries.Count; } }

        /// <summary>
        ///     Squares that currently hold a level-0 tile.
        /// </summary>
        /// <remarks>
        ///     The figure the status line wants for "N of 1684 rendered". A monotonic completion
        ///     counter cannot say it: it also counts detail tiles, re-renders after an edit and
        ///     re-renders after a plane change, so it climbs past the square count and stops
        ///     meaning anything. This is a population, so it can only fall when something is
        ///     actually thrown away.
        /// </remarks>
        public int BaseLevelSquareCount { get { lock (gate) return baseLevelCount; } }

        /// <summary>Bitmaps waiting for a UI-thread <see cref="DrainRetired"/>.</summary>
        public int RetiredCount { get { lock (gate) return retired.Count; } }

        /// <summary>Whether a level belongs to the permanent overview band.</summary>
        /// <param name="level">A pyramid level.</param>
        /// <returns><c>true</c> when tiles at that level are exempt from eviction.</returns>
        public static bool IsOverview(int level) => level < FirstDetailLevel;

        /// <summary>
        ///     Looks a tile up, counting the read as a use.
        /// </summary>
        /// <param name="key">The tile.</param>
        /// <param name="bitmap">The rendered bitmap, or <c>null</c>.</param>
        /// <returns><c>true</c> when the tile was held.</returns>
        public bool TryGet(MapTileKey key, out DirectBitmap bitmap) {
            lock (gate) {
                if (entries.TryGetValue(key, out Entry entry)) {
                    entry.LastUsed = ++clock;
                    bitmap = entry.Bitmap;
                    return true;
                }
            }

            bitmap = null;
            return false;
        }

        /// <summary>
        ///     Reads the validity counters for a square, to be handed back to <see cref="Put"/>.
        /// </summary>
        /// <remarks>
        ///     Take this <b>before</b> reading any of the data the render will draw from. Anything
        ///     that retires a tile between the stamp and the put - an edit through
        ///     <see cref="InvalidateSquare"/>, or a settings change through <see cref="Clear"/> -
        ///     moves a counter, and the put is then refused rather than filing pixels that were
        ///     drawn from data nobody is looking at any more.
        /// </remarks>
        /// <param name="regionX">Region X of the square about to be rendered.</param>
        /// <param name="regionY">Region Y of the square about to be rendered.</param>
        /// <returns>The stamp to pass to <see cref="Put"/>.</returns>
        public MapTileStamp Stamp(int regionX, int regionY) {
            lock (gate)
                return new MapTileStamp(generation, EpochOf(regionX, regionY));
        }

        /// <summary>
        ///     Stores a rendered tile, evicting from the detail band if that puts it over budget.
        /// </summary>
        /// <remarks>
        ///     The stamp check happens inside the same lock that <see cref="InvalidateSquare"/> and
        ///     <see cref="Clear"/> take, which is the whole point of it. Checking staleness outside
        ///     the cache and then calling in leaves a window where the invalidation lands between
        ///     the two, and the tile that results is never re-requested: every later lookup finds
        ///     it, so no repaint reports it as a miss.
        /// </remarks>
        /// <param name="key">The tile.</param>
        /// <param name="bitmap">The bitmap. The cache takes ownership either way.</param>
        /// <param name="stamp">What <see cref="Stamp"/> returned before the render started.</param>
        /// <returns><c>false</c> when the tile was refused as stale and retired instead.</returns>
        public bool Put(MapTileKey key, DirectBitmap bitmap, MapTileStamp stamp) {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

            lock (gate) {
                if (stamp.Generation != generation || stamp.SquareEpoch != EpochOf(key.RegionX, key.RegionY)) {
                    retired.Enqueue(bitmap);
                    return false;
                }

                if (entries.TryGetValue(key, out Entry existing))
                    Retire(key, existing);

                long bytes = (long) bitmap.Width * bitmap.Height * 4;
                entries[key] = new Entry { Bitmap = bitmap, Bytes = bytes, LastUsed = ++clock };

                if (IsOverview(key.Level))
                    overviewBytes += bytes;
                else
                    detailBytes += bytes;

                if (key.Level == 0)
                    baseLevelCount++;

                EvictWhileOverBudget();
                return true;
            }
        }

        private long EpochOf(int regionX, int regionY) =>
            squareEpochs.TryGetValue(PackSquare(regionX, regionY), out long epoch) ? epoch : 0;

        //Coordinates are 0..255 but the 3x3 apron reaches one square outside the world on each
        //edge, so the pack has to survive -1 and 256 rather than assuming a byte.
        private static int PackSquare(int regionX, int regionY) => (regionX << 16) ^ (regionY & 0xFFFF);

        /// <summary>
        ///     Marks a tile as used, which is what keeps it alive.
        /// </summary>
        /// <remarks>
        ///     Called from the paint loop rather than from the renderer, so the LRU is ordered by
        ///     what is being <em>looked at</em> rather than by what was most recently produced. A
        ///     background sweep would otherwise evict the tiles under the cursor to make room for
        ///     tiles nobody has scrolled to.
        /// </remarks>
        /// <param name="key">The tile drawn.</param>
        public void Touch(MapTileKey key) {
            lock (gate) {
                if (entries.TryGetValue(key, out Entry entry))
                    entry.LastUsed = ++clock;
            }
        }

        /// <summary>
        ///     Drops every level of a square and of its eight neighbours.
        /// </summary>
        /// <remarks>
        ///     The 3x3 is not caution. The underlay blend reaches five tiles across a boundary, so an
        ///     edit near an edge changes the neighbour's colour; and a height edit changes vertices
        ///     the neighbour shares, and therefore its relief. One rule covers both, and getting it
        ///     wrong leaves a stale one-tile band along an edge that no repaint clears.
        /// </remarks>
        /// <param name="regionX">Region X of the edited square.</param>
        /// <param name="regionY">Region Y of the edited square.</param>
        public void InvalidateSquare(int regionX, int regionY) {
            lock (gate) {
                for (int dx = -1; dx <= 1; dx++) {
                    for (int dy = -1; dy <= 1; dy++) {
                        //Bumped whether or not anything is held for the square: the tile that has
                        //to be stopped is usually the one still being rendered, which is precisely
                        //the one the entries dictionary has nothing for yet.
                        int packed = PackSquare(regionX + dx, regionY + dy);
                        squareEpochs[packed] = EpochOf(regionX + dx, regionY + dy) + 1;

                        for (int level = MapCamera.MinLevel; level <= MapCamera.MaxLevel; level++) {
                            var key = new MapTileKey(regionX + dx, regionY + dy, level);
                            if (entries.TryGetValue(key, out Entry entry))
                                Retire(key, entry);
                        }
                    }
                }
            }
        }

        /// <summary>Drops everything, for a render signature change.</summary>
        public void Clear() {
            lock (gate) {
                generation++;

                foreach (KeyValuePair<MapTileKey, Entry> pair in entries)
                    retired.Enqueue(pair.Value.Bitmap);

                entries.Clear();
                detailBytes = 0;
                overviewBytes = 0;
                baseLevelCount = 0;
            }
        }

        /// <summary>
        ///     Drops only the tiles at the given levels.
        /// </summary>
        /// <remarks>
        ///     For a settings change that does not reach every level. Ticking a layer that
        ///     <c>MapRasteriser.EffectiveLayers</c> masks off below level 1 changes nothing in the
        ///     permanent overview band, and clearing that band anyway costs a whole-world re-decode
        ///     to reproduce a picture that comes back byte for byte identical.
        ///
        ///     The generation still moves, so any render already in flight is refused. That is
        ///     conservative rather than exact - a refused tile is simply re-requested - and it is
        ///     what keeps a partial clear as safe as a full one.
        /// </remarks>
        /// <param name="levels">The levels to drop. An empty set drops nothing at all.</param>
        public void ClearLevels(IReadOnlyCollection<int> levels) {
            if (levels == null) throw new ArgumentNullException(nameof(levels));

            lock (gate) {
                generation++;

                if (levels.Count == 0)
                    return;

                var doomed = new List<MapTileKey>();
                foreach (KeyValuePair<MapTileKey, Entry> pair in entries)
                    if (levels.Contains(pair.Key.Level))
                        doomed.Add(pair.Key);

                foreach (MapTileKey key in doomed)
                    Retire(key, entries[key]);
            }
        }

        /// <summary>
        ///     Disposes everything evicted since the last call. <b>UI thread only.</b>
        /// </summary>
        /// <remarks>
        ///     Call this at the top of a paint, before drawing. Anywhere else and it can free a
        ///     bitmap the current frame is still blitting.
        /// </remarks>
        /// <returns>How many bitmaps were disposed.</returns>
        public int DrainRetired() {
            List<DirectBitmap> doomed;

            lock (gate) {
                if (retired.Count == 0)
                    return 0;

                doomed = new List<DirectBitmap>(retired);
                retired.Clear();
            }

            //Disposed outside the lock: a GDI handle release should never block a render thread.
            foreach (DirectBitmap bitmap in doomed)
                bitmap.Dispose();

            return doomed.Count;
        }

        private void Retire(MapTileKey key, Entry entry) {
            entries.Remove(key);
            retired.Enqueue(entry.Bitmap);

            if (IsOverview(key.Level))
                overviewBytes -= entry.Bytes;
            else
                detailBytes -= entry.Bytes;

            if (key.Level == 0)
                baseLevelCount--;
        }

        private void EvictWhileOverBudget() {
            while (detailBytes > DetailByteBudget) {
                MapTileKey oldest = default;
                Entry oldestEntry = null;

                foreach (KeyValuePair<MapTileKey, Entry> pair in entries) {
                    if (IsOverview(pair.Key.Level))
                        continue;
                    if (oldestEntry == null || pair.Value.LastUsed < oldestEntry.LastUsed) {
                        oldest = pair.Key;
                        oldestEntry = pair.Value;
                    }
                }

                if (oldestEntry == null)
                    return;

                Retire(oldest, oldestEntry);
            }
        }

        /// <summary>Disposes every held and retired bitmap.</summary>
        /// <remarks>
        ///     Unlike eviction this does dispose immediately, so it must run once nothing can be
        ///     drawing: after the render thread has been joined and the view has been disposed.
        /// </remarks>
        public void Dispose() {
            List<DirectBitmap> doomed = new List<DirectBitmap>();

            lock (gate) {
                foreach (KeyValuePair<MapTileKey, Entry> pair in entries)
                    doomed.Add(pair.Value.Bitmap);
                entries.Clear();

                while (retired.Count > 0)
                    doomed.Add(retired.Dequeue());

                detailBytes = 0;
                overviewBytes = 0;
                baseLevelCount = 0;
            }

            foreach (DirectBitmap bitmap in doomed)
                bitmap.Dispose();
        }

        private sealed class Entry {
            public DirectBitmap Bitmap { get; set; }
            public long Bytes { get; set; }
            public long LastUsed { get; set; }
        }
    }
}
