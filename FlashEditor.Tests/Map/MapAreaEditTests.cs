using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the three rules an area fill has to obey, none of which the byte sweeps can see.
    /// </summary>
    /// <remarks>
    ///     The underlay cap refuses rather than clamps, a fill is one undo step rather than ten
    ///     thousand, and a tile that already holds the value is not written at all. Each of them is
    ///     silent when broken: a clamp writes a floor the user did not choose, ten thousand history
    ///     entries look identical to one until you try to undo, and a needless write dirties an
    ///     archive whose contents never changed.
    /// </remarks>
    public sealed class MapAreaEditTests
    {
        private const int RegionX = 50, RegionY = 50;

        [Fact]
        public void AFillWritesEveryCoveredTileAsOneUndoStep()
        {
            MapRegion square = Square();
            var tiles = MapSelection.RectangleTiles(RegionX * 64 + 10, RegionY * 64 + 10,
                RegionX * 64 + 13, RegionY * 64 + 13).ToList();

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay, new MapAreaOptions { Value = 40 });

            Assert.False(result.WasRefused);
            Assert.NotNull(result.Edit);
            Assert.Equal(16, result.Changed);
            Assert.Equal(16, result.Edit.Edits.Count);
            Assert.Equal(1, result.Squares);

            result.Edit.Apply();
            Assert.Equal(40, square.GetUnderlayId(0, 10, 10));
            Assert.Equal(40, square.GetUnderlayId(0, 13, 13));

            //And one Undo, not sixteen.
            result.Edit.Undo();
            Assert.Equal(0, square.GetUnderlayId(0, 10, 10));
            Assert.Equal(0, square.GetUnderlayId(0, 13, 13));
        }

        /// <summary>
        ///     Painting past the underlay cap refuses and writes nothing.
        /// </summary>
        /// <remarks>
        ///     A tile stores its underlay as <c>id + 81</c> in one byte, so 175 wraps and the tile
        ///     decodes as an entirely different floor with nothing reporting an error. Clamping to
        ///     174 would write a floor the user did not choose across the whole selection, which is
        ///     strictly worse than doing nothing - and this is the check the area path must not be
        ///     able to route around, since it reaches the edit types directly.
        /// </remarks>
        [Fact]
        public void AnUnderlayPastTheCapIsRefusedRatherThanClamped()
        {
            MapRegion square = Square();
            var tiles = MapSelection.RectangleTiles(RegionX * 64 + 10, RegionY * 64 + 10,
                RegionX * 64 + 11, RegionY * 64 + 11).ToList();

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = MapToolLimits.MaximumUnderlayId + 1 });

            Assert.True(result.WasRefused);
            Assert.Null(result.Edit);
            Assert.Contains(MapToolLimits.MaximumUnderlayId.ToString(), result.Refusal);

            //Nothing was clamped into the square on the way past.
            Assert.Equal(0, square.GetUnderlayId(0, 10, 10));
            Assert.False(square.Dirty);
        }

        [Fact]
        public void TheCapIsTheHighestIdThatIsStillAccepted()
        {
            MapRegion square = Square();
            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = MapToolLimits.MaximumUnderlayId });

            Assert.False(result.WasRefused);
            Assert.Equal(1, result.Changed);
        }

        [Fact]
        public void AnOverlayPastItsByteIsRefusedToo()
        {
            MapRegion square = Square();
            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            Assert.True(Build(square, tiles, MapAreaTool.Overlay,
                new MapAreaOptions { Value = MapToolLimits.MaximumOverlayId + 1 }).WasRefused);

            Assert.False(Build(square, tiles, MapAreaTool.Overlay,
                new MapAreaOptions { Value = MapToolLimits.MaximumOverlayId }).WasRefused);
        }

        /// <summary>A tile already holding the value is skipped rather than rewritten.</summary>
        /// <remarks>
        ///     Not an optimisation. A no-op write marks the square dirty, and a dirty square is
        ///     re-encoded and written back on save - which changes that archive's CRC and drags in
        ///     the reference-table entry of every archive packed beside it.
        /// </remarks>
        [Fact]
        public void TilesThatAlreadyHoldTheValueAreSkippedAndCounted()
        {
            MapRegion square = Square();
            square.SetUnderlayId(0, 10, 10, 40);
            square.SetUnderlayId(0, 11, 10, 40);

            var tiles = MapSelection.RectangleTiles(RegionX * 64 + 10, RegionY * 64 + 10,
                RegionX * 64 + 11, RegionY * 64 + 11).ToList();

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = 40 });

            Assert.Equal(2, result.Changed);
            Assert.Equal(2, result.Skipped);
        }

        [Fact]
        public void AFillThatChangesNothingProducesNoEditAtAll()
        {
            MapRegion square = Square();
            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = 0 });

            Assert.False(result.WasRefused);
            Assert.Null(result.Edit);
            Assert.Equal(0, result.Changed);
            Assert.Equal(1, result.Skipped);
        }

        /// <summary>An overlay fill compares all three fields, not just the id.</summary>
        [Fact]
        public void AnOverlayWhoseShapeChangesIsNotSkipped()
        {
            MapRegion square = Square();
            square.SetOverlayId(0, 10, 10, 7);
            square.SetOverlayShape(0, 10, 10, 0);
            square.SetOverlayRotation(0, 10, 10, 0);

            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            MapAreaEditResult sameShape = Build(square, tiles, MapAreaTool.Overlay,
                new MapAreaOptions { Value = 7, OverlayShape = 0, OverlayRotation = 0 });
            Assert.Equal(0, sameShape.Changed);

            MapAreaEditResult newShape = Build(square, tiles, MapAreaTool.Overlay,
                new MapAreaOptions { Value = 7, OverlayShape = 3, OverlayRotation = 0 });
            Assert.Equal(1, newShape.Changed);
        }

        /// <summary>The flag tool sets and clears the blocked bit and leaves the other seven.</summary>
        /// <remarks>
        ///     Over an area it sets rather than toggles, because a toggle across a selection
        ///     produces a checkerboard of whatever was there before.
        /// </remarks>
        [Fact]
        public void TheBlockedFillSetsOneBitAndLeavesTheRestOfTheByte()
        {
            MapRegion square = Square();
            square.SetRenderRule(0, 10, 10, 0x0A);

            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            Build(square, tiles, MapAreaTool.BlockedFlag, new MapAreaOptions { Blocked = true })
                .Edit.Apply();
            Assert.Equal(0x0B, square.GetRenderRule(0, 10, 10));

            Build(square, tiles, MapAreaTool.BlockedFlag, new MapAreaOptions { Blocked = false })
                .Edit.Apply();
            Assert.Equal(0x0A, square.GetRenderRule(0, 10, 10));
        }

        /// <summary>A fill spanning squares reports every one of them.</summary>
        /// <remarks>
        ///     The square count is what the status line puts in front of the user, because every one
        ///     of them is rewritten on save.
        /// </remarks>
        [Fact]
        public void AFillAcrossASquareBoundaryReportsBothSquares()
        {
            MapRegion west = new MapRegion(MapSquareNames.RegionId(RegionX, RegionY));
            MapRegion east = new MapRegion(MapSquareNames.RegionId(RegionX + 1, RegionY));

            MapRegion Resolve(int worldX, int worldY) =>
                worldX / 64 == RegionX ? west : worldX / 64 == RegionX + 1 ? east : null;

            var tiles = MapSelection.RectangleTiles((RegionX + 1) * 64 - 1, RegionY * 64 + 10,
                (RegionX + 1) * 64, RegionY * 64 + 10).ToList();

            MapAreaEditResult result = MapAreaEdits.Build(tiles, 0, MapAreaTool.Underlay,
                new MapAreaOptions { Value = 40 }, Resolve);

            Assert.Equal(2, result.Changed);
            Assert.Equal(2, result.Squares);
            Assert.Equal(2, result.Edit.Targets.Count());
        }

        /// <summary>A tile with no square is skipped rather than throwing out of the fill.</summary>
        [Fact]
        public void TilesWithNoSquareAreDroppedQuietly()
        {
            MapRegion square = Square();
            var tiles = MapSelection.RectangleTiles((RegionX + 1) * 64 - 2, RegionY * 64 + 10,
                (RegionX + 1) * 64 + 1, RegionY * 64 + 10).ToList();

            MapAreaEditResult result = Build(square, tiles, MapAreaTool.Underlay,
                new MapAreaOptions { Value = 40 });

            //Two of the four tiles fall in the square to the east, which Resolve does not know.
            Assert.Equal(2, result.Changed);
            Assert.Equal(1, result.Squares);
        }

        /// <summary>
        ///     A plane the square does not carry is skipped rather than throwing.
        /// </summary>
        /// <remarks>
        ///     The 900 shipped underwater squares carry a single plane and <c>Region</c> indexes its
        ///     grids unguarded, so a selection dragged across one on plane 1 would throw part way
        ///     through the fill and leave the history holding half a stroke.
        /// </remarks>
        [Fact]
        public void APlaneTheSquareDoesNotCarryIsSkipped()
        {
            var underwater = new MapRegion(MapSquareNames.RegionId(RegionX, RegionY),
                MapSquareLayer.Underwater);

            /* Region.Allocate is private and the constructor always takes four planes whatever the
               layer, so the single-plane shape the 900 shipped underwater squares actually have
               comes from decoding one and cannot be built here. The guard does not distinguish the
               two: a plane at or past PlaneCount is skipped rather than indexed, so asking for one
               past the last exercises the same branch a real underwater square reaches. */
            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            MapAreaEditResult result = MapAreaEdits.Build(tiles, underwater.PlaneCount,
                MapAreaTool.Underlay, new MapAreaOptions { Value = 40 }, (_, _) => underwater);

            Assert.False(result.WasRefused);
            Assert.Null(result.Edit);
            Assert.False(underwater.Dirty);
        }

        /// <summary>
        ///     A height fill moves each tile's own vertex and skips the reserved step.
        /// </summary>
        /// <remarks>
        ///     Step 1 has no encoding: the decoder maps a stored 1 to 0, so a height exactly one
        ///     step below the reference would be rejected by the encoder on save. The step arithmetic
        ///     is shared with the single-tile tools rather than copied, which is what this pins.
        /// </remarks>
        [Fact]
        public void AHeightFillStepsPastTheReservedValue()
        {
            MapRegion square = Square();
            var tiles = new[] { (RegionX * 64 + 10, RegionY * 64 + 10) };

            int before = square.GetTileHeight(0, 10, 10);

            MapAreaEditResult raised = Build(square, tiles, MapAreaTool.RaiseHeight, new MapAreaOptions());
            raised.Edit.Apply();

            int after = square.GetTileHeight(0, 10, 10);

            //Negative is up, and the step of one is skipped, so the first raise from a flat tile
            //lands two steps rather than one.
            Assert.True(after < before);
            Assert.Equal(0, (before - after) % MapRegion.HEIGHT_UNITS_PER_STEP);
            Assert.NotEqual(1, (before - after) / MapRegion.HEIGHT_UNITS_PER_STEP);
        }

        private static MapRegion Square() => new MapRegion(MapSquareNames.RegionId(RegionX, RegionY));

        private static MapAreaEditResult Build(MapRegion square,
            IEnumerable<(int WorldX, int WorldY)> tiles, MapAreaTool tool, MapAreaOptions options)
        {
            return MapAreaEdits.Build(tiles, 0, tool, options,
                (worldX, worldY) => worldX / 64 == RegionX && worldY / 64 == RegionY ? square : null);
        }
    }
}
