using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Holds grid tiles for definition ids, bounded by bytes and filled by one background
    ///     producer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Not every index can be turned into a picture, and this refuses to pretend otherwise.</b>
    ///     Index 8 sprites and index 9 textures can. <b>Index 7 models cannot</b>: everything in this
    ///     project that turns a model into pixels is OpenGL, split between <c>Rendering.ModelRenderer</c>
    ///     and private fields of the editor form, against the one GL context in the application - which
    ///     lives on the UI thread, so rendering on it would block the message pump, and there is no CPU
    ///     rasteriser and no offscreen path to use instead. Index 20 and 21 animations cannot either,
    ///     because an animation is a pose applied to a skeleton applied to a model and so needs that
    ///     same chain. <see cref="TryGet"/> returns null for both, permanently and without queueing
    ///     anything, and the caller is expected to say so on screen.
    ///     </para>
    ///     <para>
    ///     <b>An <c>ImageList</c> is not an option here and no amount of tuning makes it one.</b> It is
    ///     eager, so every slot has to exist before the rows bind, and index 7 alone declares over
    ///     sixty thousand ids; and inserting into one that a populated grid is already bound to costs
    ///     about twenty-four times what inserting into an unbound one does, which is a penalty on
    ///     exactly the lazy access pattern a large grid needs. Tiles are handed back as unattached
    ///     bitmaps instead, which is what the sprite and font grids already do.
    ///     </para>
    ///     <para>
    ///     The shape is <c>Map.MapTileCache</c>'s, deliberately, because its decisions were paid for
    ///     once already: a byte budget rather than an entry count, an LRU ordered by what was last
    ///     <i>drawn</i> rather than by what was last produced, eviction that queues for disposal
    ///     instead of freeing on the spot, and a generation checked inside the insert lock. What is
    ///     not carried across is its permanent exempt band: that exists because its coarse levels
    ///     cost a whole-cache re-decode to rebuild, and no definition thumbnail does.
    ///     </para>
    /// </remarks>
    public sealed class DefinitionThumbnailCache : IDefinitionThumbnailSource, IDisposable {
        /// <summary>
        ///     Bytes of tiles held before the cache starts evicting.
        /// </summary>
        /// <remarks>
        ///     Sized to make scrolling back and forth free rather than to hold an index. At a 48x48
        ///     tile, 48 x 48 x 4 = 9,216 bytes, so 64 MiB is about 7,200 tiles; a 1080p grid at a
        ///     row height of about 52 pixels shows roughly twenty rows, so the budget is some three
        ///     hundred times the visible working set. It is also about a ninth of what holding one
        ///     tile for every model the cache declares would cost, which is the arithmetic that
        ///     settles this as a cache rather than as a preload.
        /// </remarks>
        public const long DefaultByteBudget = 64L << 20;

        /// <summary>
        ///     Requests that may be waiting at once before the oldest are dropped.
        /// </summary>
        /// <remarks>
        ///     A fling through tens of thousands of rows would otherwise queue a decode for every
        ///     row it passed, nearly all of them off screen by the time the producer reached them.
        ///     Dropping is free and self-correcting: the next paint calls <see cref="TryGet"/>
        ///     again for whatever is still visible, and that re-enqueues it.
        /// </remarks>
        public const int MaxPendingRequests = 256;

        /// <summary>
        ///     Ids recorded as having no tile before the oldest such record is dropped.
        /// </summary>
        /// <remarks>
        ///     A miss that produced nothing has to be remembered or the next paint asks for it
        ///     again, and the one after that, forever - a busy loop between the paint and the
        ///     producer that no counter would show. These records cost no pixels, so the byte
        ///     budget cannot bound them and they need their own.
        /// </remarks>
        public const int MaxNegativeEntries = 4096;

        /// <summary>
        ///     Evicted tiles held for a <see cref="DrainRetired"/> that may never come.
        /// </summary>
        /// <remarks>
        ///     Past this the oldest are <b>dropped rather than disposed</b>. Dropping a reference is
        ///     never a use-after-free, and the finaliser reclaims the handle once the UI thread has
        ///     let go of it too, so a host that forgets to drain pays a finaliser instead of a
        ///     <c>Dispose</c> rather than growing without limit. Disposing them here to save that
        ///     would reintroduce exactly the race the retirement queue exists to remove.
        /// </remarks>
        public const int MaxRetiredHeld = 512;

        /* Evicting to a low-water mark rather than to the budget. Eviction sorts the evictable
           entries once, so freeing a single tile per insert would sort per insert for as long as
           the cache stayed full; freeing an eighth of the budget amortises that sort over the
           thousands of inserts it takes to fill the headroom again. */
        private const int EvictionHeadroomDivisor = 8;

        //Coalescing floor for TilesReady. A repaint per tile is what this exists to avoid, and a
        //backlog still has to show progress rather than going quiet until it finishes.
        private const int NotifyIntervalMilliseconds = 100;

        private readonly object gate = new object();
        private readonly Dictionary<ThumbnailKey, Entry> entries = new Dictionary<ThumbnailKey, Entry>();
        private readonly Queue<Bitmap> retired = new Queue<Bitmap>();

        /* Two structures for one queue. The list is the order - newest at the end, taken from the
           end, dropped from the front - and the set is the coalescer. A key stays in the set from
           the moment it is requested until the moment its tile is filed, which is what stops the
           paints that happen while a decode is running from queueing it a second time. */
        private readonly List<ThumbnailKey> pending = new List<ThumbnailKey>();
        private readonly HashSet<ThumbnailKey> pendingKeys = new HashSet<ThumbnailKey>();

        private readonly IDefinitionThumbnailRenderer[] renderers;
        private readonly long byteBudget;
        private readonly AutoResetEvent requested = new AutoResetEvent(false);
        private readonly Thread producer;

        private long bytes;
        private long clock;
        private long generation;
        private int negatives;
        private bool stopping;

        /// <summary>
        ///     A cache that draws whatever this project can draw from the given open cache.
        /// </summary>
        /// <param name="cache">The open cache to read from.</param>
        /// <param name="byteBudget">Bytes of tiles to hold before evicting.</param>
        public DefinitionThumbnailCache(RSCache cache, long byteBudget = DefaultByteBudget)
            : this(StandardRenderers(cache), byteBudget) {
        }

        /// <summary>
        ///     A cache over a stated set of renderers.
        /// </summary>
        /// <remarks>
        ///     The renderers are separated from the caching for one reason: everything interesting
        ///     here is concurrency - the eviction order, the queue discipline, the generation
        ///     refusal - and none of it can be tested against a real cache without decoding, which
        ///     makes the test slow, serialised against every other cache-backed suite, and unable
        ///     to reproduce a race on demand. With a renderer that can be held mid-render on
        ///     command, all of it is testable headless.
        /// </remarks>
        /// <param name="renderers">The renderers, asked in order.</param>
        /// <param name="byteBudget">Bytes of tiles to hold before evicting.</param>
        public DefinitionThumbnailCache(IEnumerable<IDefinitionThumbnailRenderer> renderers,
            long byteBudget = DefaultByteBudget) {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            if (byteBudget <= 0) throw new ArgumentOutOfRangeException(nameof(byteBudget));

            this.renderers = new List<IDefinitionThumbnailRenderer>(renderers).ToArray();
            this.byteBudget = byteBudget;

            /* A dedicated thread rather than the pool. This one blocks waiting for work for the
               whole life of the cache, which is precisely what a pool thread must not do, and it
               is a background thread so it cannot hold the process open. */
            producer = new Thread(Produce) {
                IsBackground = true,
                Name = "DefinitionThumbnails"
            };
            producer.Start();
        }

        /// <inheritdoc/>
        public event EventHandler? TilesReady;

        /// <summary>Bytes of tiles currently held.</summary>
        public long Bytes { get { lock (gate) return bytes; } }

        /// <summary>Entries currently held, tiles and recorded misses together.</summary>
        public int Count { get { lock (gate) return entries.Count; } }

        /// <summary>Requests waiting for the producer.</summary>
        public int PendingCount { get { lock (gate) return pending.Count; } }

        /// <summary>Tiles waiting for a UI-thread <see cref="DrainRetired"/>.</summary>
        public int RetiredCount { get { lock (gate) return retired.Count; } }

        /// <summary>
        ///     The tile for an id, or null when there is not one to draw.
        /// </summary>
        /// <remarks>
        ///     <b>Never decodes and never waits for a decode.</b> Everything it does happens inside
        ///     one short critical section that no render is ever held across, so the worst a paint
        ///     can wait for is another paint's dictionary lookup. A miss queues the work and returns
        ///     null; the renderer draws its placeholder, the grid stays scrollable, and
        ///     <see cref="TilesReady"/> asks for a repaint once something has landed.
        ///     <para>
        ///     A hit counts as a use, which is the whole reason this and not the producer drives the
        ///     LRU: called from the paint, "least recently used" means "least recently on screen". A
        ///     producer-driven order would evict the rows under the cursor to make room for rows
        ///     nobody has scrolled to.
        ///     </para>
        /// </remarks>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="id">The id.</param>
        /// <param name="side">The tile side in pixels.</param>
        /// <returns>The tile, or null.</returns>
        public Bitmap? TryGet(int indexId, int id, int side) {
            if (side <= 0)
                return null;

            var key = new ThumbnailKey(indexId, id, side);
            bool wake = false;

            lock (gate) {
                if (entries.TryGetValue(key, out Entry? entry)) {
                    entry.LastUsed = ++clock;
                    return entry.Tile;
                }

                //A disposed cache holds nothing and can produce nothing. Answered rather than
                //thrown from, because this is called from a paint and a form being torn down can
                //still be asked to draw one more frame.
                if (stopping)
                    return null;

                /* Recorded as having no tile without ever reaching the queue. An index nothing can
                   draw would otherwise put one request per visible row into the queue on every
                   paint, for a producer that can only decline them. */
                if (RendererFor(indexId) == null) {
                    File(key, null, generation);
                    return null;
                }

                if (pendingKeys.Add(key)) {
                    pending.Add(key);

                    //Dropped from the front, which is the oldest request. The newest is what is on
                    //screen; a queue that served the oldest first would render what the user
                    //scrolled past and reach the visible rows last.
                    while (pending.Count > MaxPendingRequests) {
                        pendingKeys.Remove(pending[0]);
                        pending.RemoveAt(0);
                    }

                    wake = true;
                }
            }

            if (wake)
                requested.Set();

            return null;
        }

        /// <inheritdoc/>
        public int DrainRetired() {
            List<Bitmap> doomed;

            lock (gate) {
                if (retired.Count == 0)
                    return 0;

                doomed = new List<Bitmap>(retired);
                retired.Clear();
            }

            //Outside the lock: releasing a GDI handle should never block the producer.
            foreach (Bitmap tile in doomed)
                tile.Dispose();

            return doomed.Count;
        }

        /// <summary>
        ///     Drops every tile and refuses every one still being produced.
        /// </summary>
        /// <remarks>
        ///     For a cache close or a rebind. The generation moves inside the same lock the insert
        ///     takes, which is the entire point of having one: a tile drawn from a cache that has
        ///     since been closed is refused rather than filed. Deciding staleness outside the cache
        ///     and then calling in leaves a window where the close lands between the two, and the
        ///     tile that results is never re-requested, because every later lookup finds it and no
        ///     repaint reports it as a miss.
        /// </remarks>
        public void Clear() {
            lock (gate) {
                generation++;

                foreach (KeyValuePair<ThumbnailKey, Entry> pair in entries)
                    if (pair.Value.Tile != null)
                        Retire(pair.Value.Tile);

                entries.Clear();
                pending.Clear();
                pendingKeys.Clear();
                bytes = 0;
                negatives = 0;
            }
        }

        /// <summary>Stops the producer and disposes every tile, held and retired.</summary>
        /// <remarks>
        ///     Unlike eviction this frees immediately, so it must run once nothing can be drawing:
        ///     after the grid that was reading it has gone.
        /// </remarks>
        public void Dispose() {
            lock (gate) {
                if (stopping)
                    return;
                stopping = true;
            }

            requested.Set();

            /* Joined rather than abandoned, so a render in flight finishes before its bitmaps are
               freed underneath it. Bounded, because the producer is a background thread and a
               render that has somehow wedged must not stop the application from closing. */
            producer.Join(TimeSpan.FromSeconds(5));

            var doomed = new List<Bitmap>();

            lock (gate) {
                foreach (KeyValuePair<ThumbnailKey, Entry> pair in entries)
                    if (pair.Value.Tile != null)
                        doomed.Add(pair.Value.Tile);

                entries.Clear();
                pending.Clear();
                pendingKeys.Clear();

                while (retired.Count > 0)
                    doomed.Add(retired.Dequeue());

                bytes = 0;
                negatives = 0;
            }

            foreach (Bitmap tile in doomed)
                tile.Dispose();

            foreach (IDefinitionThumbnailRenderer renderer in renderers)
                (renderer as IDisposable)?.Dispose();

            /* The wait handle is deliberately left to its finaliser. Disposing it opens a race with
               no upside: a render that outlived the join comes back to a disposed handle and throws
               from a background thread, where nothing catches it and the process goes down, and a
               paint that arrives during teardown can reach Set between the join and the dispose.
               One handle per cache, and there is one cache. */
        }

        /// <summary>
        ///     The renderers this project can actually supply, in the order they are asked.
        /// </summary>
        /// <remarks>
        ///     Models and animations are absent on purpose rather than unfinished. See the type
        ///     remarks for why neither can be drawn without the one GL context in the application.
        /// </remarks>
        /// <param name="cache">The open cache to read from.</param>
        /// <returns>The renderers.</returns>
        private static IEnumerable<IDefinitionThumbnailRenderer> StandardRenderers(RSCache cache) {
            if (cache == null) throw new ArgumentNullException(nameof(cache));

            yield return new SpriteThumbnailRenderer(cache);
            yield return new TextureThumbnailRenderer(cache);
        }

        private IDefinitionThumbnailRenderer? RendererFor(int indexId) {
            foreach (IDefinitionThumbnailRenderer renderer in renderers)
                if (renderer.Handles(indexId))
                    return renderer;

            return null;
        }

        /// <summary>
        ///     The producer: one request at a time, for the whole life of the cache.
        /// </summary>
        /// <remarks>
        ///     One thread and one decode at a time, which is a decision rather than a simplification.
        ///     Decode buffers in this project are not pooled, so every archive decode allocates fresh
        ///     and the large ones land on the large object heap; running N of them at once multiplies
        ///     that directly. It also buys nothing on the sprite path, where the read and the inflate
        ///     happen inside the cache's own container lock and every extra thread would simply queue
        ///     behind the first.
        /// </remarks>
        private void Produce() {
            bool unannounced = false;
            long lastNotify = 0;

            while (true) {
                ThumbnailKey key = default;
                long stamp = 0;
                bool taken = false;
                IDefinitionThumbnailRenderer? renderer = null;

                lock (gate) {
                    if (stopping)
                        return;

                    //From the end, which is the newest request, which is what is on screen.
                    if (pending.Count > 0) {
                        key = pending[pending.Count - 1];
                        pending.RemoveAt(pending.Count - 1);
                        stamp = generation;
                        taken = true;
                        renderer = RendererFor(key.IndexId);
                    }
                }

                if (renderer == null) {
                    /* Either nothing was queued, or something was queued for an index no renderer
                       claims - which TryGet does not allow, but which must still leave the queue
                       and the coalescer agreeing rather than holding a key nothing will ever
                       clear. */
                    if (taken)
                        lock (gate)
                            File(key, null, stamp);

                    //Announce whatever landed before going quiet, so a paint is not left waiting on
                    //a coalescing window that will never close.
                    if (unannounced) {
                        unannounced = false;
                        lastNotify = Environment.TickCount64;
                        TilesReady?.Invoke(this, EventArgs.Empty);
                    }

                    requested.WaitOne();
                    continue;
                }

                Bitmap? tile;
                try {
                    tile = renderer.Render(key.IndexId, key.Id, key.Side);
                }
                catch (Exception) {
                    /* Swallowed on purpose, and it is the one place in this file that is. A single
                       malformed record must not take the producer down and leave every later row
                       showing a placeholder for the rest of the session; the id is recorded as
                       having no tile, which is what the grid shows and what stops it being asked
                       for again. */
                    tile = null;
                }

                bool landed;
                bool queueEmpty;

                lock (gate) {
                    landed = File(key, tile, stamp) && tile != null;
                    queueEmpty = pending.Count == 0;
                }

                unannounced |= landed;

                if (!unannounced)
                    continue;

                //One event for a batch, not one per tile. A repaint costs the visible rows; a
                //repaint per tile costs them once per tile.
                if (!queueEmpty && Environment.TickCount64 - lastNotify < NotifyIntervalMilliseconds)
                    continue;

                unannounced = false;
                lastNotify = Environment.TickCount64;
                TilesReady?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        ///     Files a produced tile, or the fact that there was not one. <b>Under the lock.</b>
        /// </summary>
        /// <remarks>
        ///     The staleness check is here, inside the same lock <see cref="Clear"/> takes, rather
        ///     than at the call site. A producer that compared generations itself and then called in
        ///     would leave a window for the clear to land between the two, and the tile filed after
        ///     it would never be re-requested: every later lookup would find it, so no repaint would
        ///     report it as a miss.
        /// </remarks>
        /// <param name="key">What was asked for.</param>
        /// <param name="tile">The tile, or null recording that there is none.</param>
        /// <param name="stamp">The generation read when the render started.</param>
        /// <returns><c>false</c> when the tile was refused as stale and retired instead.</returns>
        private bool File(ThumbnailKey key, Bitmap? tile, long stamp) {
            pendingKeys.Remove(key);

            if (stamp != generation) {
                if (tile != null)
                    Retire(tile);

                return false;
            }

            if (entries.TryGetValue(key, out Entry? existing))
                Remove(key, existing);

            long size = tile == null ? 0 : (long) tile.Width * tile.Height * 4;
            entries[key] = new Entry { Tile = tile, Bytes = size, LastUsed = ++clock };
            bytes += size;

            if (tile == null)
                negatives++;

            EvictWhileOverBudget();
            EvictExcessNegatives();
            return true;
        }

        private void Remove(ThumbnailKey key, Entry entry) {
            entries.Remove(key);
            bytes -= entry.Bytes;

            if (entry.Tile == null)
                negatives--;
            else
                Retire(entry.Tile);
        }

        private void Retire(Bitmap tile) {
            retired.Enqueue(tile);

            //Dropped, never disposed - see MaxRetiredHeld. Losing the reference is safe at any
            //moment; freeing the handle is not.
            while (retired.Count > MaxRetiredHeld)
                retired.Dequeue();
        }

        private void EvictWhileOverBudget() {
            if (bytes <= byteBudget)
                return;

            long target = byteBudget - byteBudget / EvictionHeadroomDivisor;
            EvictOldest(entry => entry.Tile != null, () => bytes <= target);
        }

        private void EvictExcessNegatives() {
            if (negatives <= MaxNegativeEntries)
                return;

            //Headroom for the same reason the byte budget has it, and it matters more here: an
            //index nothing can draw records one of these per row straight from the paint, so
            //evicting exactly one per insert would sort the whole population once per visible cell
            //per frame for as long as the user kept scrolling.
            int target = MaxNegativeEntries - MaxNegativeEntries / EvictionHeadroomDivisor;
            EvictOldest(entry => entry.Tile == null, () => negatives <= target);
        }

        /// <summary>
        ///     Drops matching entries oldest first until the caller says to stop. Under the lock.
        /// </summary>
        /// <remarks>
        ///     The candidates are ordered once and then walked, rather than the dictionary being
        ///     rescanned for a new minimum per eviction. At a small tile side the cache can hold
        ///     tens of thousands of entries, and a rescan per eviction is quadratic in exactly the
        ///     case a byte budget exists to survive.
        /// </remarks>
        private void EvictOldest(Func<Entry, bool> matches, Func<bool> satisfied) {
            var candidates = new List<KeyValuePair<ThumbnailKey, Entry>>();

            foreach (KeyValuePair<ThumbnailKey, Entry> pair in entries)
                if (matches(pair.Value))
                    candidates.Add(pair);

            candidates.Sort((left, right) => left.Value.LastUsed.CompareTo(right.Value.LastUsed));

            foreach (KeyValuePair<ThumbnailKey, Entry> pair in candidates) {
                if (satisfied())
                    return;

                Remove(pair.Key, pair.Value);
            }
        }

        /// <summary>
        ///     What a tile is for. The side is part of it because the same asset at two sizes is two
        ///     pictures, and a grid and a picker want different ones.
        /// </summary>
        private readonly record struct ThumbnailKey(int IndexId, int Id, int Side);

        private sealed class Entry {
            /// <summary>The tile, or null recording that this id has none to draw.</summary>
            public Bitmap? Tile { get; set; }

            public long Bytes { get; set; }

            public long LastUsed { get; set; }
        }
    }
}
