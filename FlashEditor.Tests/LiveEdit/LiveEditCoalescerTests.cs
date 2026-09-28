using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.Region;
using FlashEditor.LiveEdit;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.LiveEdit
{
    /// <summary>
    ///     Pins the coalescer at its public seam: what it marks, when it flushes, and which frames
    ///     a flush hands on. Time is a <see cref="ManualTimeProvider"/>, so every window boundary
    ///     is exact rather than raced against a real timer.
    /// </summary>
    public sealed class LiveEditCoalescerTests
    {
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
        public void ManyMarksInsideTheQuietPeriodFlushOnceWithEachSquareOnce()
        {
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
    }
}
