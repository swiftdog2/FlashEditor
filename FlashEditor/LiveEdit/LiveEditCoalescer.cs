using System;
using System.Collections.Generic;
using System.Threading;
using FlashEditor.Cache.Region;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.LiveEdit {
    /// <summary>
    ///     Collects dirty map squares and floor definitions and hands them on as one batch of
    ///     leg-A frames once edits have been quiet for <see cref="QuietPeriod"/>.
    /// </summary>
    /// <remarks>
    ///     A brush stroke marks the same square dozens of times; the client rebuilds its scene once
    ///     per batch, so collapsing the stroke into one batch is what keeps it from rebuilding once
    ///     per tile. Squares are encoded at flush time rather than at mark time, so a batch always
    ///     carries the square as it is when sent, and undoing back to an unedited square sends its
    ///     original bytes because <see cref="RegionCodec"/> returns those for a clean square.
    /// </remarks>
    public sealed class LiveEditCoalescer : IDisposable {
        /// <summary>How long edits must pause before the marked set is flushed.</summary>
        public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(150);

        private readonly object gate = new object();
        private readonly object flushGate = new object();
        private readonly Action<Action> runExclusive;
        private readonly Action<LiveEditBatch> sink;
        private readonly ITimer timer;

        private List<MapRegion> squares = new List<MapRegion>();

        /// <summary>
        ///     Creates a coalescer.
        /// </summary>
        /// <param name="runExclusive">
        ///     Runs its argument while holding the map store's exclusive section. Every encode and
        ///     definition read happens inside one call, because the squares are live objects the
        ///     editor mutates on the UI thread.
        /// </param>
        /// <param name="readDefinition">Returns the committed bytes of a floor definition.</param>
        /// <param name="sink">
        ///     Receives each batch on the flushing thread. It must not block for long, and an
        ///     exception from it is discarded, because the flush runs on a timer thread where an
        ///     unhandled exception ends the process.
        /// </param>
        /// <param name="time">The clock and timer source; <see cref="TimeProvider.System"/> when null.</param>
        public LiveEditCoalescer(
            Action<Action> runExclusive,
            Func<LiveEditKind, int, byte[]> readDefinition,
            Action<LiveEditBatch> sink,
            TimeProvider? time = null) {
            this.runExclusive = runExclusive ?? throw new ArgumentNullException(nameof(runExclusive));
            ArgumentNullException.ThrowIfNull(readDefinition);
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            timer = (time ?? TimeProvider.System).CreateTimer(
                _ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        ///     Marks a square dirty and restarts the quiet period. The square is read at flush
        ///     time, not now. A square is its layer plus its id, so the surface and underwater
        ///     squares at one coordinate are marked separately.
        /// </summary>
        public void Mark(MapRegion square) {
            ArgumentNullException.ThrowIfNull(square);
            lock (gate) {
                int at = squares.FindIndex(s => s.Layer == square.Layer && s.GetRegionID() == square.GetRegionID());
                if (at < 0) squares.Add(square);
                else squares[at] = square;
                timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
            }
        }

        private void Flush() {
            List<MapRegion> taken;
            lock (gate) {
                taken = squares;
                squares = new List<MapRegion>();
            }

            var frames = new List<LiveEditFrame>();
            var failures = new List<LiveEditFailure>();
            lock (flushGate) {
                runExclusive(() => {
                    foreach (MapRegion square in taken) {
                        int id = square.GetRegionID();
                        (LiveEditKind terrain, LiveEditKind locations) = KindsOf(square.Layer);
                        frames.Add(new LiveEditFrame(terrain, id, RegionCodec.EncodeTerrain(square)));
                        frames.Add(new LiveEditFrame(locations, id, RegionCodec.EncodeLocations(square)));
                    }
                });
                sink(new LiveEditBatch(frames, failures));
            }
        }

        private static (LiveEditKind Terrain, LiveEditKind Locations) KindsOf(MapSquareLayer layer) =>
            layer == MapSquareLayer.Underwater
                ? (LiveEditKind.UnderwaterTerrain, LiveEditKind.UnderwaterLocations)
                : (LiveEditKind.Terrain, LiveEditKind.Locations);

        /// <summary>Stops the timer; anything still marked is dropped.</summary>
        public void Dispose() => timer.Dispose();
    }

    /// <summary>
    ///     One flush: the frames to send, in order, and what could not be encoded.
    /// </summary>
    /// <param name="Frames">Each dirty square's two files in mark order, then each dirty definition.</param>
    /// <param name="Failures">Squares and definitions skipped, each with the reason.</param>
    public sealed record LiveEditBatch(IReadOnlyList<LiveEditFrame> Frames, IReadOnlyList<LiveEditFailure> Failures);

    /// <summary>
    ///     A square or definition a flush skipped. A square is skipped whole, never half-sent,
    ///     because the server rebuilds collision from the <c>m</c> and <c>l</c> pair together.
    /// </summary>
    /// <param name="Kind">The file that failed; for a square, whichever of its two files did.</param>
    /// <param name="Id">The square id or definition id.</param>
    /// <param name="Message">Why it was skipped.</param>
    public sealed record LiveEditFailure(LiveEditKind Kind, int Id, string Message);
}
