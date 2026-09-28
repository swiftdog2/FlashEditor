using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.Region;
using FlashEditor.LiveEdit;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.LiveEdit {
    /// <summary>
    ///     Pins the coalescer at its public seam: what it marks, when it flushes, and which frames
    ///     a flush hands on. Time is a <see cref="ManualTimeProvider"/>, so every window boundary
    ///     is exact rather than raced against a real timer.
    /// </summary>
    public sealed class LiveEditCoalescerTests {
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(150);

        private readonly ManualTimeProvider time = new ManualTimeProvider();
        private readonly List<LiveEditBatch> batches = new List<LiveEditBatch>();
        private int exclusiveSections;

        private LiveEditCoalescer NewCoalescer(Func<LiveEditKind, int, byte[]> readDefinition = null) =>
            new LiveEditCoalescer(
                action => { exclusiveSections++; action(); },
                readDefinition ?? ((kind, id) => throw new InvalidOperationException("no definitions in this test")),
                batches.Add,
                time);

        private static MapRegion Square(int x, int y, MapSquareLayer layer = MapSquareLayer.Surface) =>
            new MapRegion(MapSquareNames.RegionId(x, y), layer);

        [Fact]
        public void ManyMarksInsideTheQuietPeriodFlushOnceWithEachSquareOnce() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            MapRegion a = Square(50, 50);
            MapRegion b = Square(51, 50);

            coalescer.Mark(a);
            time.Advance(TimeSpan.FromMilliseconds(100));
            coalescer.Mark(b);
            time.Advance(TimeSpan.FromMilliseconds(100));
            coalescer.Mark(a);
            time.Advance(TimeSpan.FromMilliseconds(149));
            Assert.Empty(batches);

            time.Advance(TimeSpan.FromMilliseconds(1));

            LiveEditBatch batch = Assert.Single(batches);
            Assert.Empty(batch.Failures);
            Assert.Equal(1, exclusiveSections);
            Assert.Equal(
                new[] {
                    (LiveEditKind.Terrain, 0x3232), (LiveEditKind.Locations, 0x3232),
                    (LiveEditKind.Terrain, 0x3332), (LiveEditKind.Locations, 0x3332)
                },
                batch.Frames.Select(f => (f.Kind, f.Id)));
            Assert.Equal(RegionCodec.EncodeTerrain(a), batch.Frames[0].Payload);
            Assert.Equal(RegionCodec.EncodeLocations(a), batch.Frames[1].Payload);
        }

        [Fact]
        public void MarksAfterAFlushOpenANewWindowHoldingOnlyTheNewSquares() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            coalescer.Mark(Square(50, 50));
            time.Advance(Quiet);
            Assert.Single(batches);

            coalescer.Mark(Square(10, 20));
            time.Advance(Quiet);

            Assert.Equal(2, batches.Count);
            Assert.Equal(
                new[] { (LiveEditKind.Terrain, 0x0A14), (LiveEditKind.Locations, 0x0A14) },
                batches[1].Frames.Select(f => (f.Kind, f.Id)));
        }

        [Fact]
        public void NothingMarkedFlushesNothing() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            coalescer.Mark(Square(50, 50));
            time.Advance(Quiet);
            time.Advance(TimeSpan.FromSeconds(10));

            Assert.Single(batches);
        }

        [Fact]
        public void AnUnderwaterSquareFlushesAsItsUnderwaterFamily() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            MapRegion surface = Square(50, 50);
            MapRegion underwater = Square(50, 50, MapSquareLayer.Underwater);
            coalescer.Mark(surface);
            coalescer.Mark(underwater);
            time.Advance(Quiet);

            //Same square id on both layers: they are two squares, and neither may absorb the other.
            Assert.Equal(
                new[] {
                    (LiveEditKind.Terrain, 0x3232), (LiveEditKind.Locations, 0x3232),
                    (LiveEditKind.UnderwaterTerrain, 0x3232), (LiveEditKind.UnderwaterLocations, 0x3232)
                },
                Assert.Single(batches).Frames.Select(f => (f.Kind, f.Id)));
        }

        [Fact]
        public void AnEncodeThatThrowsIsReportedAndTheOtherSquaresStillFlush() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            MapRegion bad = Square(50, 50);
            //One step (-32 on plane 0) is the reserved height byte, so the terrain cannot encode.
            bad.SetTileHeight(0, 0, 0, -32);
            Assert.Throws<InvalidOperationException>(() => RegionCodec.EncodeTerrain(bad));

            coalescer.Mark(bad);
            coalescer.Mark(Square(51, 50));
            time.Advance(Quiet);

            LiveEditBatch batch = Assert.Single(batches);
            Assert.Equal(
                new[] { (LiveEditKind.Terrain, 0x3332), (LiveEditKind.Locations, 0x3332) },
                batch.Frames.Select(f => (f.Kind, f.Id)));
            LiveEditFailure failure = Assert.Single(batch.Failures);
            Assert.Equal((LiveEditKind.Terrain, 0x3232), (failure.Kind, failure.Id));
            Assert.Contains("not encodable", failure.Message);
        }

        [Fact]
        public void ASinkThatThrowsDoesNotEscapeTheFlushAndTheNextWindowStillFlushes() {
            int calls = 0;
            using var coalescer = new LiveEditCoalescer(
                action => action(),
                (kind, id) => Array.Empty<byte>(),
                _ => { calls++; throw new InvalidOperationException("sink down"); },
                time);

            coalescer.Mark(Square(50, 50));
            time.Advance(Quiet);
            coalescer.Mark(Square(50, 50));
            time.Advance(Quiet);

            Assert.Equal(2, calls);
        }

        [Fact]
        public void AnExclusiveSectionThatThrowsReportsEverySquareAsFailed() {
            using var coalescer = new LiveEditCoalescer(
                _ => throw new InvalidOperationException("store closed"),
                (kind, id) => Array.Empty<byte>(),
                batches.Add,
                time);

            coalescer.Mark(Square(50, 50));
            time.Advance(Quiet);

            LiveEditBatch batch = Assert.Single(batches);
            Assert.Empty(batch.Frames);
            LiveEditFailure failure = Assert.Single(batch.Failures);
            Assert.Equal((LiveEditKind.Terrain, 0x3232), (failure.Kind, failure.Id));
            Assert.Contains("store closed", failure.Message);
        }

        [Fact]
        public void AnExclusiveSectionThatRefusesDropsTheSquaresButStillFlushesTheDefinitions() {
            using var coalescer = new LiveEditCoalescer(
                _ => throw new InvalidOperationException("no map store is bound"),
                (kind, id) => new byte[] { 9 },
                batches.Add,
                time);

            coalescer.Mark(Square(50, 50));
            coalescer.MarkDefinition(LiveEditKind.FloorUnderlay, 4);
            time.Advance(Quiet);

            LiveEditBatch batch = Assert.Single(batches);
            LiveEditFrame frame = Assert.Single(batch.Frames);
            Assert.Equal((LiveEditKind.FloorUnderlay, 4), (frame.Kind, frame.Id));
            LiveEditFailure failure = Assert.Single(batch.Failures);
            Assert.Equal((LiveEditKind.Terrain, 0x3232), (failure.Kind, failure.Id));
            Assert.Contains("no map store is bound", failure.Message);
        }

        [Fact]
        public void ABatchOfDefinitionsAloneTakesNoExclusiveSection() {
            using LiveEditCoalescer coalescer = NewCoalescer((kind, id) => new byte[] { 1 });

            coalescer.MarkDefinition(LiveEditKind.FloorOverlay, 2);
            time.Advance(Quiet);

            Assert.Single(Assert.Single(batches).Frames);
            Assert.Equal(0, exclusiveSections);
        }

        [Fact]
        public void FloorDefinitionsFlushAfterTheSquaresWithTheirCommittedBytes() {
            var reads = new List<(LiveEditKind, int)>();
            using LiveEditCoalescer coalescer = NewCoalescer((kind, id) => {
                reads.Add((kind, id));
                return new[] { (byte) kind, (byte) id };
            });

            coalescer.MarkDefinition(LiveEditKind.FloorOverlay, 7);
            coalescer.Mark(Square(50, 50));
            coalescer.MarkDefinition(LiveEditKind.FloorUnderlay, 9);
            coalescer.MarkDefinition(LiveEditKind.FloorOverlay, 7);
            time.Advance(Quiet);

            LiveEditBatch batch = Assert.Single(batches);
            Assert.Equal(
                new[] {
                    (LiveEditKind.Terrain, 0x3232), (LiveEditKind.Locations, 0x3232),
                    (LiveEditKind.FloorOverlay, 7), (LiveEditKind.FloorUnderlay, 9)
                },
                batch.Frames.Select(f => (f.Kind, f.Id)));
            Assert.Equal(new byte[] { 5, 7 }, batch.Frames[2].Payload);
            Assert.Equal(new byte[] { 4, 9 }, batch.Frames[3].Payload);
            Assert.Equal(new[] { (LiveEditKind.FloorOverlay, 7), (LiveEditKind.FloorUnderlay, 9) }, reads);
        }

        [Fact]
        public void ADefinitionThatCannotBeReadIsReportedAndSkipped() {
            using LiveEditCoalescer coalescer = NewCoalescer((kind, id) =>
                id == 1 ? throw new InvalidOperationException("no such file") : new byte[] { 1 });

            coalescer.MarkDefinition(LiveEditKind.FloorUnderlay, 1);
            coalescer.MarkDefinition(LiveEditKind.FloorUnderlay, 2);
            time.Advance(Quiet);

            LiveEditBatch batch = Assert.Single(batches);
            Assert.Equal((LiveEditKind.FloorUnderlay, 2), (Assert.Single(batch.Frames).Kind, batch.Frames[0].Id));
            LiveEditFailure failure = Assert.Single(batch.Failures);
            Assert.Equal((LiveEditKind.FloorUnderlay, 1), (failure.Kind, failure.Id));
        }

        [Fact]
        public void MarksFollowedByAResetProduceNoFlush() {
            using LiveEditCoalescer coalescer = NewCoalescer((kind, id) => new byte[] { 1 });
            coalescer.Mark(Square(50, 50));
            coalescer.MarkDefinition(LiveEditKind.FloorOverlay, 7);

            coalescer.Reset();
            time.Advance(Quiet);

            Assert.Empty(batches);
            Assert.Equal(0, exclusiveSections);
        }

        [Fact]
        public void AResetDuringAFlushDropsTheBatchThatFlushWasBuilding() {
            LiveEditCoalescer coalescer = null;
            coalescer = new LiveEditCoalescer(
                action => { coalescer.Reset(); action(); },
                (kind, id) => new byte[] { 1 },
                batches.Add,
                time);
            using (coalescer) {
                coalescer.Mark(Square(50, 50));
                time.Advance(Quiet);
            }

            Assert.Empty(batches);
        }

        [Fact]
        public void MarksAfterAResetFlushOnlyTheNewSquare() {
            using LiveEditCoalescer coalescer = NewCoalescer();
            coalescer.Mark(Square(50, 50));
            coalescer.Reset();

            coalescer.Mark(Square(10, 20));
            time.Advance(Quiet);

            Assert.Equal(
                new[] { (LiveEditKind.Terrain, 0x0A14), (LiveEditKind.Locations, 0x0A14) },
                Assert.Single(batches).Frames.Select(f => (f.Kind, f.Id)));
        }

        [Theory]
        [InlineData(LiveEditKind.Terrain)]
        [InlineData(LiveEditKind.Locations)]
        [InlineData(LiveEditKind.UnderwaterTerrain)]
        [InlineData(LiveEditKind.UnderwaterLocations)]
        [InlineData(LiveEditKind.Rebuild)]
        public void MarkDefinitionRefusesAKindThatIsNotAFloorDefinition(LiveEditKind kind) {
            using LiveEditCoalescer coalescer = NewCoalescer();
            Assert.Throws<ArgumentException>(() => coalescer.MarkDefinition(kind, 1));
        }
    }
}