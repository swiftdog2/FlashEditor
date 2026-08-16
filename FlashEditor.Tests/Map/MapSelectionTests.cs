using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the selection set, the brush footprint and the wand flood.
    /// </summary>
    /// <remarks>
    ///     All three are pure arithmetic over tile coordinates, which is exactly the kind of code
    ///     that looks right and is off by one. None of it needs a cache or a window, so it is
    ///     checkable here rather than by eye - which matters more than usual, because everything
    ///     these three feed goes on to write to a map square.
    /// </remarks>
    public sealed class MapSelectionTests
    {
        [Fact]
        public void ARectangleTakesBothCornersWhicheverWayItWasDragged()
        {
            var upRight = new HashSet<(int, int)>(MapSelection.RectangleTiles(10, 10, 12, 11));
            var downLeft = new HashSet<(int, int)>(MapSelection.RectangleTiles(12, 11, 10, 10));

            Assert.Equal(6, upRight.Count);
            Assert.True(upRight.SetEquals(downLeft));
            Assert.Contains((10, 10), upRight);
            Assert.Contains((12, 11), upRight);
        }

        [Fact]
        public void RectangleTileCountAgreesWithTheTilesItWouldBuild()
        {
            Assert.Equal(MapSelection.RectangleTiles(3, 7, 9, 11).Count(),
                MapSelection.RectangleTileCount(3, 7, 9, 11));

            //One tile, which is what a press and release on the same tile draws.
            Assert.Equal(1, MapSelection.RectangleTileCount(5, 5, 5, 5));
        }

        [Fact]
        public void AddKeepsWhatWasThereAndSubtractTakesItOut()
        {
            var selection = new MapSelection();

            selection.Apply(MapSelection.RectangleTiles(0, 0, 3, 3), MapSelectionMode.Replace);
            Assert.Equal(16, selection.Count);

            selection.Apply(MapSelection.RectangleTiles(4, 0, 5, 3), MapSelectionMode.Add);
            Assert.Equal(24, selection.Count);

            selection.Apply(MapSelection.RectangleTiles(0, 0, 1, 3), MapSelectionMode.Subtract);
            Assert.Equal(16, selection.Count);
            Assert.False(selection.Contains(0, 0));
            Assert.True(selection.Contains(2, 0));
        }

        [Fact]
        public void ReplaceThrowsAwayTheOldSelection()
        {
            var selection = new MapSelection();

            selection.Apply(MapSelection.RectangleTiles(0, 0, 3, 3), MapSelectionMode.Replace);
            selection.Apply(MapSelection.RectangleTiles(100, 100, 101, 101), MapSelectionMode.Replace);

            Assert.Equal(4, selection.Count);
            Assert.False(selection.Contains(0, 0));
        }

        /// <summary>
        ///     The square count is what the status line reports, and it is counted rather than
        ///     derived from the bounding box.
        /// </summary>
        /// <remarks>
        ///     A selection straddling a square corner touches four archives, and every one of them
        ///     is re-encoded and rewritten when the cache is saved. The user has no way to know that
        ///     unless the editor says it, which is why this number exists at all.
        /// </remarks>
        [Fact]
        public void TheSquareSpanCountsSquaresRatherThanBoundingThem()
        {
            var selection = new MapSelection();

            //Two tiles either side of the boundary between squares 0,0 and 1,0.
            selection.Apply(new[] { (63, 10), (64, 10) }, MapSelectionMode.Replace);
            Assert.Equal(2, selection.SquareCount);

            //And a block sitting on the corner of four.
            selection.Apply(MapSelection.RectangleTiles(63, 63, 64, 64), MapSelectionMode.Replace);
            Assert.Equal(4, selection.Count);
            Assert.Equal(4, selection.SquareCount);

            //An L shape whose bounding box spans four squares while it touches three.
            selection.Apply(new[] { (10, 10), (70, 10), (10, 70) }, MapSelectionMode.Replace);
            Assert.Equal(3, selection.SquareCount);
        }

        [Fact]
        public void SquaresListsEachTouchedSquareOnce()
        {
            var selection = new MapSelection();
            selection.Apply(MapSelection.RectangleTiles(60, 60, 67, 67), MapSelectionMode.Replace);

            List<(int RegionX, int RegionY)> squares = selection.Squares.ToList();

            Assert.Equal(4, squares.Count);
            Assert.Equal(squares.Count, squares.Distinct().Count());
            Assert.Contains((0, 0), squares);
            Assert.Contains((1, 1), squares);
        }

        /// <summary>A selection past the cap is refused whole rather than trimmed.</summary>
        /// <remarks>
        ///     Trimming would land a shape the user did not draw, and every square it reached would
        ///     still be pinned and rewritten. Refusing says so and changes nothing.
        /// </remarks>
        [Fact]
        public void AnOversizedSelectionIsRefusedAndLeavesTheOldOneAlone()
        {
            var selection = new MapSelection();
            selection.Apply(MapSelection.RectangleTiles(0, 0, 3, 3), MapSelectionMode.Replace);

            int side = 600;
            Assert.True((long) side * side > MapSelection.MaximumTiles);

            MapSelectionResult result =
                selection.Apply(MapSelection.RectangleTiles(0, 0, side - 1, side - 1), MapSelectionMode.Replace);

            Assert.True(result.WasRefused);
            Assert.False(result.DidChange);
            Assert.NotNull(result.Refusal);
            Assert.Equal(16, selection.Count);
        }

        /// <summary>A plane step drops the selection rather than carrying it.</summary>
        /// <remarks>
        ///     A tile coordinate means a different tile on another plane, so a selection that
        ///     survived a plane change would apply a fill to terrain nobody looked at.
        /// </remarks>
        [Fact]
        public void ChangingPlaneEmptiesTheSelection()
        {
            var selection = new MapSelection();
            selection.Apply(MapSelection.RectangleTiles(0, 0, 3, 3), MapSelectionMode.Replace);

            selection.SetPlane(0);
            Assert.Equal(16, selection.Count);

            selection.SetPlane(1);
            Assert.True(selection.IsEmpty);
            Assert.Equal(1, selection.Plane);
        }

        [Fact]
        public void BoundsIsTheSmallestBlockHoldingTheShape()
        {
            var selection = new MapSelection();
            selection.Apply(new[] { (5, 9), (7, 9), (5, 12) }, MapSelectionMode.Replace);

            Rectangle bounds = selection.Bounds;

            Assert.Equal(5, bounds.Left);
            Assert.Equal(9, bounds.Top);
            Assert.Equal(3, bounds.Width);
            Assert.Equal(4, bounds.Height);
        }

        [Fact]
        public void AnEmptySelectionHasAnEmptyBounds()
        {
            Assert.True(new MapSelection().Bounds.IsEmpty);
        }

        [Fact]
        public void AnOddBrushIsCentredOnTheClickedTile()
        {
            var covered = new HashSet<(int, int)>(MapBrush.Footprint(100, 100, 3, MapBrushShape.Square));

            Assert.Equal(9, covered.Count);
            Assert.Contains((99, 99), covered);
            Assert.Contains((101, 101), covered);
        }

        /// <summary>An even brush grows north and east, which is stated rather than rounded to.</summary>
        [Fact]
        public void AnEvenBrushPutsTheClickedTileAtTheSouthWestOfTheMiddleFour()
        {
            var covered = new HashSet<(int, int)>(MapBrush.Footprint(100, 100, 4, MapBrushShape.Square));

            Assert.Equal(16, covered.Count);
            Assert.Contains((99, 99), covered);
            Assert.Contains((102, 102), covered);
            Assert.DoesNotContain((98, 98), covered);
        }

        [Fact]
        public void ASizeOneBrushIsOneTileWhateverItsShape()
        {
            foreach (MapBrushShape shape in new[] {
                         MapBrushShape.Square, MapBrushShape.Round, MapBrushShape.Diamond
                     })
                Assert.Single(MapBrush.Footprint(50, 50, 1, shape));
        }

        /// <summary>
        ///     A round brush is a disc rather than a plus sign, and stops being a square.
        /// </summary>
        /// <remarks>
        ///     The two sizes are the ones that tell the arithmetic apart. At 3 the diagonal tiles sit
        ///     inside the radius and the disc is the whole 3x3; at 7 they do not, so a round brush
        ///     that still covered its corners would be a square with a different name.
        /// </remarks>
        [Fact]
        public void ARoundBrushDropsItsCornersOnceItIsBigEnoughTo()
        {
            var three = new HashSet<(int, int)>(MapBrush.Footprint(50, 50, 3, MapBrushShape.Round));
            Assert.Equal(9, three.Count);

            var seven = new HashSet<(int, int)>(MapBrush.Footprint(50, 50, 7, MapBrushShape.Round));
            Assert.True(seven.Count < 49);
            Assert.DoesNotContain((47, 47), seven);
            Assert.Contains((50, 47), seven);
        }

        [Fact]
        public void ADiamondBrushKeepsItsAxesAndDropsItsCorners()
        {
            var covered = new HashSet<(int, int)>(MapBrush.Footprint(50, 50, 5, MapBrushShape.Diamond));

            Assert.Contains((50, 48), covered);
            Assert.Contains((48, 50), covered);
            Assert.DoesNotContain((48, 48), covered);
        }

        [Fact]
        public void ABrushAtTheWorldEdgePaintsThePartOfItselfThatExists()
        {
            var covered = new HashSet<(int, int)>(MapBrush.Footprint(0, 0, 5, MapBrushShape.Square));

            Assert.All(covered, tile => Assert.True(tile.Item1 >= 0 && tile.Item2 >= 0));
            Assert.Contains((0, 0), covered);
            Assert.Equal(9, covered.Count);
        }

        /// <summary>The wand takes the connected run and nothing across a gap.</summary>
        /// <remarks>
        ///     Four-connected, not eight. Diagonally touching tiles share no edge, and a wand that
        ///     crossed corners would leak through the single-tile diagonal gaps paths and coastlines
        ///     are full of.
        /// </remarks>
        [Fact]
        public void TheWandTakesTheConnectedRunAndDoesNotCrossADiagonal()
        {
            MapScene scene = ThreeByThree();
            MapRegion centre = scene.Square(1, 1);

            //Two blocks of underlay 40 touching only at a corner, inside the centre square.
            for (int x = 10; x < 13; x++)
                for (int y = 10; y < 13; y++)
                    centre.SetUnderlayId(0, x, y, 40);

            for (int x = 13; x < 16; x++)
                for (int y = 13; y < 16; y++)
                    centre.SetUnderlayId(0, x, y, 40);

            //Scene tiles are the square-local ones plus one square of apron on each axis.
            MapWandResult result = MapWand.Flood(scene, 0, scene.BaseX + 64 + 11, scene.BaseY + 64 + 11,
                MapWandField.Underlay, 0);

            Assert.Equal(9, result.Tiles.Count);
            Assert.Equal(40, result.MatchedValue);
            Assert.False(result.ReachedTileLimit);
        }

        [Fact]
        public void TheWandReportsRunningIntoTheEdgeOfWhatIsLoaded()
        {
            MapScene scene = ThreeByThree();

            //Everything decodes to underlay 0 in a freshly built square, so a flood from anywhere
            //takes the whole nine squares and runs into their outer edge.
            MapWandResult result = MapWand.Flood(scene, 0, scene.BaseX + 64, scene.BaseY + 64,
                MapWandField.Underlay, 0);

            Assert.True(result.ReachedSceneEdge || result.ReachedTileLimit);
            Assert.Equal(0, result.MatchedValue);
        }

        /// <summary>The two limits are reported separately because they mean opposite things.</summary>
        [Fact]
        public void TheWandStopsAtItsTileLimitAndSaysSoRatherThanSayingItRanOutOfScene()
        {
            MapScene scene = ThreeByThree();

            MapWandResult result = MapWand.Flood(scene, 0, scene.BaseX + 64, scene.BaseY + 64,
                MapWandField.Underlay, 0, tileLimit: 50);

            Assert.Equal(50, result.Tiles.Count);
            Assert.True(result.ReachedTileLimit);
        }

        [Fact]
        public void TheWandReturnsNothingForAClickOutsideTheScene()
        {
            MapScene scene = ThreeByThree();

            MapWandResult result = MapWand.Flood(scene, 0, scene.BaseX - 1, scene.BaseY,
                MapWandField.Underlay, 0);

            Assert.Empty(result.Tiles);
        }

        /// <summary>Every tile the wand returns is distinct.</summary>
        /// <remarks>
        ///     The flood marks a tile as seen when it is enqueued rather than when it is dequeued. A
        ///     tile with two already-queued neighbours would otherwise be enqueued twice and appear
        ///     twice in the output, which the selection's own set would hide - and which would then
        ///     produce two edits for one tile inside a composite, so undo would run the second one
        ///     backwards over a value the first had already restored.
        /// </remarks>
        [Fact]
        public void TheWandNeverReturnsTheSameTileTwice()
        {
            MapScene scene = ThreeByThree();

            MapWandResult result = MapWand.Flood(scene, 0, scene.BaseX + 64, scene.BaseY + 64,
                MapWandField.Underlay, 0, tileLimit: 500);

            Assert.Equal(result.Tiles.Count, result.Tiles.Distinct().Count());
        }

        private static MapScene ThreeByThree(int originRegionX = 49, int originRegionY = 49)
        {
            var squares = new MapRegion[3, 3];
            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    squares[dx, dy] = new MapRegion(MapSquareNames.RegionId(originRegionX + dx, originRegionY + dy));
            return MapScene.FromSquares(originRegionX, originRegionY, squares);
        }
    }
}
