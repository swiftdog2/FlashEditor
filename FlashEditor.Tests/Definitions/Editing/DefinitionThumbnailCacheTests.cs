//The test project is not nullable-annotated, and this file implements an interface whose members
//are. Enabled per file rather than per project so the switch cannot change how anything else here
//compiles.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using Xunit;

namespace FlashEditor.Tests.Definitions.Editing
{
    /// <summary>
    ///     <see cref="DefinitionThumbnailCache"/>, driven by a renderer that can be held mid-render.
    /// </summary>
    /// <remarks>
    ///     Everything worth testing here is concurrency - the eviction order, the queue discipline,
    ///     the generation refusal, the promise that a paint never waits - and none of it is about
    ///     the cache's contents. A real 639 cache would make every one of these tests slow,
    ///     serialised against every other cache-backed suite, and unable to reproduce a race on
    ///     demand. The fake renderer here can be blocked and released at a chosen instant, which is
    ///     what makes "a tile produced against a cache that has since been cleared is refused"
    ///     something a test can actually state rather than hope for.
    ///     <para>
    ///     The renderers themselves are covered separately, against a real cache, because what they
    ///     produce is a claim about the data.
    ///     </para>
    /// </remarks>
    public sealed class DefinitionThumbnailCacheTests
    {
        //An index the fake renderer claims. Any value does; index 8 is what the sprite renderer
        //would claim in production, so the tests read the way the real thing is wired.
        private const int DrawableIndex = RSConstants.SPRITES_INDEX;

        private const int Side = 8;

        /// <summary>
        ///     A miss returns null immediately and the tile arrives afterwards.
        /// </summary>
        [Fact]
        public void TryGet_MissesFirst_ThenServesTheProducedTile()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            Assert.Null(cache.TryGet(DrawableIndex, 1, Side));

            renderer.WaitForRenders(1);
            WaitUntil(() => cache.TryGet(DrawableIndex, 1, Side) != null);

            Bitmap? tile = cache.TryGet(DrawableIndex, 1, Side);
            Assert.NotNull(tile);
            Assert.Equal(Side, tile!.Width);
        }

        /// <summary>
        ///     A paint never waits for a decode.
        /// </summary>
        /// <remarks>
        ///     The contract that makes the whole design work: the grid stays scrollable while the
        ///     cache is being read. With the producer held inside a render, a hundred lookups still
        ///     have to come straight back - the only lock a paint can meet is another lookup's, and
        ///     no render is ever held across it. Without that, a fling through a large grid would
        ///     freeze the form for as long as the slowest record takes to decode, which for a
        ///     texture graph is measured in seconds.
        /// </remarks>
        [Fact]
        public void TryGet_DoesNotWaitForARenderInFlight()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            renderer.Hold();
            cache.TryGet(DrawableIndex, 1, Side);
            renderer.WaitForRenderStarted();

            var clock = Stopwatch.StartNew();
            for (int id = 2; id < 102; id++)
                Assert.Null(cache.TryGet(DrawableIndex, id, Side));
            clock.Stop();

            //Still held, and still the only render taken, so a hundred lookups have gone past a
            //producer that is stopped dead. Anything but a prompt return means one of them waited.
            Assert.Equal(1, renderer.Started);
            Assert.Equal(0, renderer.Completed);
            Assert.True(clock.ElapsedMilliseconds < 500,
                "A hundred lookups took " + clock.ElapsedMilliseconds + "ms with the producer held.");

            renderer.Release();
        }

        /// <summary>
        ///     The same id asked for repeatedly is rendered once.
        /// </summary>
        /// <remarks>
        ///     A paint asks for every visible cell, and the paints keep coming while the producer
        ///     works. Without coalescing, one slow record would be queued once per repaint until it
        ///     landed, and the queue would fill with copies of the row the user is looking at
        ///     instead of the rows beside it.
        /// </remarks>
        [Fact]
        public void Requests_ForTheSameIdAreCoalesced()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            renderer.Hold();

            for (int repaint = 0; repaint < 50; repaint++)
                cache.TryGet(DrawableIndex, 7, Side);

            renderer.WaitForRenderStarted();
            Assert.Equal(1, cache.PendingCount + renderer.Started - renderer.Completed);

            renderer.Release();
            WaitUntil(() => cache.TryGet(DrawableIndex, 7, Side) != null);

            //One more round of paints, now that it is held. A hit must not queue anything either.
            for (int repaint = 0; repaint < 50; repaint++)
                cache.TryGet(DrawableIndex, 7, Side);

            Assert.Equal(1, renderer.Completed);
            Assert.Equal(0, cache.PendingCount);
        }

        /// <summary>
        ///     The queue is served newest first and sheds its oldest requests.
        /// </summary>
        /// <remarks>
        ///     The newest request is the one on screen. Serving oldest first would render everything
        ///     the user scrolled past and reach the visible rows last, and an unbounded queue would
        ///     keep doing that for every row a fling crossed. Dropping is free because the next
        ///     paint re-asks for whatever is still visible.
        /// </remarks>
        [Fact]
        public void Queue_ServesNewestFirst_AndDropsTheOldestBeyondItsBound()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            renderer.Hold();

            //One request to occupy the producer, so everything after it queues rather than being
            //taken as it arrives.
            cache.TryGet(DrawableIndex, 0, Side);
            renderer.WaitForRenderStarted();

            int overflow = DefinitionThumbnailCache.MaxPendingRequests + 20;
            for (int id = 1; id <= overflow; id++)
                cache.TryGet(DrawableIndex, id, Side);

            Assert.Equal(DefinitionThumbnailCache.MaxPendingRequests, cache.PendingCount);

            renderer.Release();
            WaitUntil(() => cache.PendingCount == 0 && renderer.Completed >= 2);
            WaitUntil(() => renderer.Completed >= DefinitionThumbnailCache.MaxPendingRequests);

            IReadOnlyList<int> order = renderer.RenderedIds;

            //Id 0 was already in flight. The next taken must be the newest queued, not the oldest.
            Assert.Equal(0, order[0]);
            Assert.Equal(overflow, order[1]);

            //The oldest twenty were shed rather than rendered.
            Assert.DoesNotContain(1, order);
            Assert.DoesNotContain(20, order);
            Assert.Contains(21, order);
        }

        /// <summary>
        ///     The cache stays under its byte budget.
        /// </summary>
        /// <remarks>
        ///     Bytes rather than entries, because tile sizes differ by orders of magnitude between
        ///     a grid cell and a picker tile and an entry count bounds neither.
        /// </remarks>
        [Fact]
        public void Eviction_KeepsTheCacheUnderItsByteBudget()
        {
            long budget = 20L * Side * Side * 4;

            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer }, budget);

            for (int id = 0; id < 200; id++)
            {
                cache.TryGet(DrawableIndex, id, Side);
                WaitUntil(() => renderer.Completed > id);
            }

            Assert.True(cache.Bytes <= budget,
                "Held " + cache.Bytes + " bytes against a budget of " + budget + ".");
            Assert.True(cache.Bytes > 0, "The cache evicted everything it was given.");
        }

        /// <summary>
        ///     Eviction drops what was least recently drawn, not what was least recently produced.
        /// </summary>
        /// <remarks>
        ///     The distinction is the whole reason the LRU is touched from <c>TryGet</c>. Ordering
        ///     by production would evict the rows under the cursor to make room for rows nobody has
        ///     scrolled to, which is the opposite of what a cache is for.
        /// </remarks>
        [Fact]
        public void Eviction_DropsTheLeastRecentlyDrawn()
        {
            //Two tiles fit; the third has to displace one of them. The low-water mark means an
            //eviction frees more than one tile's worth, so the survivor is what is asserted rather
            //than the exact population.
            long budget = 2L * Side * Side * 4;

            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer }, budget);

            Produce(cache, renderer, 1);
            Produce(cache, renderer, 2);

            //Id 1 is drawn again, so id 2 becomes the oldest by use despite being the newest by
            //production.
            Assert.NotNull(cache.TryGet(DrawableIndex, 1, Side));

            Produce(cache, renderer, 3);

            Assert.NotNull(cache.TryGet(DrawableIndex, 3, Side));
            Assert.Null(cache.TryGet(DrawableIndex, 2, Side));
        }

        /// <summary>
        ///     A tile produced against a cache that has since been cleared is refused.
        /// </summary>
        /// <remarks>
        ///     The failure this prevents is silent and permanent. A tile filed after the clear is
        ///     never re-requested, because every later lookup finds it, so no repaint reports it as
        ///     a miss - the grid simply shows the previous cache's picture for the rest of the
        ///     session. Checking the generation outside the insert lock would leave exactly that
        ///     window.
        /// </remarks>
        [Fact]
        public void Clear_RefusesATileProducedAgainstTheOldGeneration()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            renderer.Hold();
            cache.TryGet(DrawableIndex, 5, Side);
            renderer.WaitForRenderStarted();

            cache.Clear();
            renderer.Release();
            WaitUntil(() => renderer.Completed >= 1);

            //Refused, so the lookup misses and queues it again rather than finding the stale tile.
            Assert.Null(cache.TryGet(DrawableIndex, 5, Side));
            Assert.True(cache.RetiredCount >= 1, "The refused tile was neither filed nor retired.");
        }

        /// <summary>
        ///     Draining disposes what eviction retired, and only then.
        /// </summary>
        /// <remarks>
        ///     Eviction cannot free on the spot: the producer evicts, and the UI thread may be
        ///     inside <c>DrawImage</c> on the very bitmap it chose. There is no exception to catch
        ///     for that, so the tile queues and the paint frees it at the top of the next frame.
        /// </remarks>
        [Fact]
        public void DrainRetired_DisposesEvictedTiles_AndNothingBefore()
        {
            long budget = 2L * Side * Side * 4;

            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer }, budget);

            Produce(cache, renderer, 1);
            Bitmap? first = cache.TryGet(DrawableIndex, 1, Side);
            Assert.NotNull(first);

            Produce(cache, renderer, 2);
            Produce(cache, renderer, 3);

            Assert.True(cache.RetiredCount > 0, "Nothing was retired despite the budget being passed.");

            //Still alive: the UI thread could be drawing it, which is the entire reason it queued.
            Assert.Equal(Side, first!.Width);

            int freed = cache.DrainRetired();
            Assert.True(freed > 0);
            Assert.Equal(0, cache.RetiredCount);
            Assert.Equal(0, cache.DrainRetired());
        }

        /// <summary>
        ///     An index no renderer claims is answered once and never queued.
        /// </summary>
        /// <remarks>
        ///     Models are the case: nothing in this project turns one into pixels outside the single
        ///     UI-thread GL context. A grid of them would otherwise put one request per visible row
        ///     into the queue on every paint, for a producer that can only decline them.
        /// </remarks>
        [Fact]
        public void AnIndexWithNoRenderer_IsAnsweredWithoutQueueingAnything()
        {
            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            for (int repaint = 0; repaint < 20; repaint++)
                Assert.Null(cache.TryGet(RSConstants.MODELS_INDEX, 15748, Side));

            Assert.Equal(0, cache.PendingCount);
            Assert.Equal(0, renderer.Started);
        }

        /// <summary>
        ///     An id that produced nothing is not asked for again.
        /// </summary>
        /// <remarks>
        ///     Without the record, the paint and the producer form a busy loop: the lookup misses,
        ///     queues, the render yields nothing, the next paint misses again. Nothing on screen
        ///     changes, so it would never be noticed.
        /// </remarks>
        [Fact]
        public void AnIdThatRendersNothing_IsNotAskedAgain()
        {
            using var renderer = new ProbeRenderer(DrawableIndex) { BlankIds = { 4 } };
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            cache.TryGet(DrawableIndex, 4, Side);
            WaitUntil(() => renderer.Completed >= 1);

            for (int repaint = 0; repaint < 20; repaint++)
                Assert.Null(cache.TryGet(DrawableIndex, 4, Side));

            WaitUntil(() => cache.PendingCount == 0);
            Assert.Equal(1, renderer.Started);
        }

        /// <summary>
        ///     One malformed record does not take the producer with it.
        /// </summary>
        /// <remarks>
        ///     A decode that throws is a fact about one record. If it ended the producer thread,
        ///     every row after it would show a placeholder for the rest of the session and nothing
        ///     would say why.
        /// </remarks>
        [Fact]
        public void ARendererThatThrows_LeavesTheProducerRunning()
        {
            using var renderer = new ProbeRenderer(DrawableIndex) { ThrowingIds = { 9 } };
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            cache.TryGet(DrawableIndex, 9, Side);
            WaitUntil(() => renderer.Completed >= 1);

            cache.TryGet(DrawableIndex, 10, Side);
            WaitUntil(() => cache.TryGet(DrawableIndex, 10, Side) != null);

            Assert.NotNull(cache.TryGet(DrawableIndex, 10, Side));
            Assert.Null(cache.TryGet(DrawableIndex, 9, Side));
        }

        /// <summary>
        ///     One event for a batch of tiles, not one per tile.
        /// </summary>
        /// <remarks>
        ///     The event costs a repaint of everything on screen. Raising it per tile would cost
        ///     that once per tile while a sweep is running, which is the same waste as calling
        ///     <c>RefreshObjects</c> per row.
        /// </remarks>
        [Fact]
        public void TilesReady_IsCoalesced()
        {
            const int Tiles = 60;

            using var renderer = new ProbeRenderer(DrawableIndex);
            using var cache = new DefinitionThumbnailCache(new[] { renderer });

            int raised = 0;
            cache.TilesReady += (_, _) => Interlocked.Increment(ref raised);

            renderer.Hold();
            for (int id = 0; id < Tiles; id++)
                cache.TryGet(DrawableIndex, id, Side);

            renderer.WaitForRenderStarted();
            renderer.Release();

            WaitUntil(() => renderer.Completed >= Tiles);
            WaitUntil(() => Volatile.Read(ref raised) > 0);

            //Well under one per tile. The exact figure depends on how the coalescing window falls,
            //so the assertion is the property rather than a count.
            Assert.InRange(Volatile.Read(ref raised), 1, Tiles / 4);
        }

        /// <summary>
        ///     Disposal stops the producer and frees everything, held and retired.
        /// </summary>
        [Fact]
        public void Dispose_StopsTheProducer()
        {
            var renderer = new ProbeRenderer(DrawableIndex);
            var cache = new DefinitionThumbnailCache(new[] { renderer });

            Produce(cache, renderer, 1);
            cache.Dispose();

            Assert.Equal(0, cache.Count);
            Assert.Equal(0, cache.RetiredCount);

            //Idempotent: a panel disposed twice must not throw out of a Dispose.
            cache.Dispose();
            renderer.Dispose();
        }

        private static void Produce(DefinitionThumbnailCache cache, ProbeRenderer renderer, int id)
        {
            int before = renderer.Completed;
            cache.TryGet(DrawableIndex, id, Side);
            WaitUntil(() => renderer.Completed > before);
        }

        /// <summary>
        ///     Spins until a condition holds, so a test states an outcome rather than a delay.
        /// </summary>
        private static void WaitUntil(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds > 10_000)
                    throw new TimeoutException("The producer did not reach the expected state.");

                Thread.Sleep(1);
            }
        }

        /// <summary>
        ///     A renderer that can be held inside a render and asked what it was given.
        /// </summary>
        private sealed class ProbeRenderer : IDefinitionThumbnailRenderer, IDisposable
        {
            private readonly int indexId;
            private readonly ManualResetEventSlim gate = new ManualResetEventSlim(true);
            private readonly ManualResetEventSlim entered = new ManualResetEventSlim(false);
            private readonly List<int> rendered = new List<int>();

            private int started;
            private int completed;

            internal ProbeRenderer(int indexId)
            {
                this.indexId = indexId;
            }

            /// <summary>Ids for which the renderer returns null.</summary>
            internal HashSet<int> BlankIds { get; } = new HashSet<int>();

            /// <summary>Ids for which the renderer throws.</summary>
            internal HashSet<int> ThrowingIds { get; } = new HashSet<int>();

            internal int Started => Volatile.Read(ref started);

            internal int Completed => Volatile.Read(ref completed);

            internal IReadOnlyList<int> RenderedIds
            {
                get { lock (rendered) return rendered.ToArray(); }
            }

            public bool Handles(int id) => id == indexId;

            public Bitmap? Render(int index, int id, int side)
            {
                Interlocked.Increment(ref started);
                lock (rendered) rendered.Add(id);
                entered.Set();

                gate.Wait();

                try
                {
                    if (ThrowingIds.Contains(id))
                        throw new InvalidOperationException("Probe failure for id " + id + ".");

                    return BlankIds.Contains(id) ? null : new Bitmap(side, side, PixelFormat.Format32bppArgb);
                }
                finally
                {
                    Interlocked.Increment(ref completed);
                }
            }

            /// <summary>Blocks the next render inside <see cref="Render"/>.</summary>
            internal void Hold()
            {
                entered.Reset();
                gate.Reset();
            }

            /// <summary>Lets a held render finish.</summary>
            internal void Release() => gate.Set();

            /// <summary>Waits until a render has actually begun, rather than until it was queued.</summary>
            internal void WaitForRenderStarted()
            {
                if (!entered.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The producer never entered a render.");
            }

            internal void WaitForRenders(int count)
            {
                WaitUntil(() => Completed >= count);
            }

            public void Dispose()
            {
                gate.Set();
                gate.Dispose();
                entered.Dispose();
            }
        }
    }
}
