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
        private readonly Func<LiveEditKind, int, byte[]> readDefinition;
        private readonly Action<LiveEditBatch> sink;
        private readonly ITimer timer;

        private List<MapRegion> squares = new List<MapRegion>();
        private List<(LiveEditKind Kind, int Id)> definitions = new List<(LiveEditKind, int)>();

        /// <summary>
        ///     Creates a coalescer.
        /// </summary>
        /// <param name="runExclusive">
        ///     Runs its argument while holding the map store's exclusive section. Every encode and
        ///     definition read happens inside one call, because the squares are live objects the
        ///     editor mutates on the UI thread.
        /// </param>
        /// <param name="readDefinition">
        ///     Returns the committed bytes of a floor definition. Throwing or returning null skips
        ///     that definition and reports it.
        /// </param>
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
            this.readDefinition = readDefinition ?? throw new ArgumentNullException(nameof(readDefinition));
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

        /// <summary>
        ///     Marks a floor definition dirty and restarts the quiet period. Its bytes are read
        ///     through the definition source at flush time, so they must already be committed.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     <paramref name="kind"/> is not <see cref="LiveEditKind.FloorUnderlay"/> or
        ///     <see cref="LiveEditKind.FloorOverlay"/>, or <paramref name="id"/> does not fit the
        ///     16-bit id field.
        /// </exception>
        public void MarkDefinition(LiveEditKind kind, int id) {
            if (kind != LiveEditKind.FloorUnderlay && kind != LiveEditKind.FloorOverlay)
                throw new ArgumentException($"{kind} is not a floor definition kind.", nameof(kind));
            if (id < 0 || id > ushort.MaxValue)
                throw new ArgumentException($"Id {id} does not fit the 16-bit id field.", nameof(id));
            lock (gate) {
                if (!definitions.Contains((kind, id)))
                    definitions.Add((kind, id));
                timer.Change(QuietPeriod, Timeout.InfiniteTimeSpan);
            }
        }

        private void Flush() {
            List<MapRegion> takenSquares;
            List<(LiveEditKind Kind, int Id)> takenDefinitions;
            lock (gate) {
                takenSquares = squares;
                takenDefinitions = definitions;
                squares = new List<MapRegion>();
                definitions = new List<(LiveEditKind, int)>();
            }

            //Serialised so that a slow flush cannot hand the sink older bytes after a newer one.
            lock (flushGate) {
                var frames = new List<LiveEditFrame>();
                var failures = new List<LiveEditFailure>();
                bool ran = false;
                try {
                    runExclusive(() => {
                        //Each item catches its own failure, so this runs to the end once entered.
                        foreach (MapRegion square in takenSquares)
                            EncodeSquare(square, frames, failures);
                        foreach ((LiveEditKind Kind, int Id) definition in takenDefinitions)
                            ReadDefinition(definition, frames, failures);
                        ran = true;
                    });
                }
                catch (Exception e) {
                    if (!ran) {
                        foreach (MapRegion square in takenSquares)
                            failures.Add(new LiveEditFailure(KindsOf(square.Layer).Terrain, square.GetRegionID(), e.Message));
                        foreach ((LiveEditKind kind, int id) in takenDefinitions)
                            failures.Add(new LiveEditFailure(kind, id, e.Message));
                    }
                }

                try {
                    sink(new LiveEditBatch(frames, failures));
                }
                catch {
                    //Discarded by contract; see the sink parameter on the constructor.
                }
            }
        }

        private static void EncodeSquare(MapRegion square, List<LiveEditFrame> frames, List<LiveEditFailure> failures) {
            int id = square.GetRegionID();
            (LiveEditKind terrainKind, LiveEditKind locationsKind) = KindsOf(square.Layer);
            LiveEditKind attempting = terrainKind;
            try {
                byte[] terrain = RegionCodec.EncodeTerrain(square);
                attempting = locationsKind;
                byte[] locations = RegionCodec.EncodeLocations(square);
                frames.Add(new LiveEditFrame(terrainKind, id, terrain));
                frames.Add(new LiveEditFrame(locationsKind, id, locations));
            }
            catch (Exception e) {
                failures.Add(new LiveEditFailure(attempting, id, e.Message));
            }
        }

        private void ReadDefinition((LiveEditKind Kind, int Id) definition, List<LiveEditFrame> frames,
            List<LiveEditFailure> failures) {
            try {
                byte[] bytes = readDefinition(definition.Kind, definition.Id)
                               ?? throw new InvalidOperationException("The definition source returned no bytes.");
                frames.Add(new LiveEditFrame(definition.Kind, definition.Id, bytes));
            }
            catch (Exception e) {
                failures.Add(new LiveEditFailure(definition.Kind, definition.Id, e.Message));
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