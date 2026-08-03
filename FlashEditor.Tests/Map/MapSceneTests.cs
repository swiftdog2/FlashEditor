using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Cache.Region;
using FlashEditor.Map;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins scene coordinate mapping and the screen-to-tile transform.
    /// </summary>
    /// <remarks>
    ///     Coordinate errors here are the kind that produce a picture which looks fine until you
    ///     click on it, so they are worth asserting rather than eyeballing. Scene Y runs north and
    ///     screen Y runs down, and that flip is the thing most likely to be wrong.
    /// </remarks>
    public sealed class MapSceneTests
    {
        private static MapScene ThreeByThree(int originRegionX = 49, int originRegionY = 49)
        {
            var squares = new MapRegion[3, 3];
            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    squares[dx, dy] = new MapRegion(MapSquareNames.RegionId(originRegionX + dx, originRegionY + dy));
            return MapScene.FromSquares(originRegionX, originRegionY, squares);
        }

        [Fact]
        public void SceneDimensionsFollowTheSquareGrid()
        {
            MapScene scene = ThreeByThree();

            Assert.Equal(3, scene.SquaresX);
            Assert.Equal(3, scene.SquaresY);
            Assert.Equal(192, scene.WidthTiles);
            Assert.Equal(192, scene.HeightTiles);
            Assert.Equal(49 * 64, scene.BaseX);
            Assert.Equal(49 * 64, scene.BaseY);
        }

        [Fact]
        public void SceneTilesMapBackToTheirSquare()
        {
            MapScene scene = ThreeByThree();

            //The centre square occupies scene tiles 64..127 on both axes.
            Assert.Equal(MapSquareNames.RegionId(50, 50), scene.SquareAt(64, 64).GetRegionID());
            Assert.Equal(MapSquareNames.RegionId(50, 50), scene.SquareAt(127, 127).GetRegionID());

            //And its neighbours sit either side of that band.
            Assert.Equal(MapSquareNames.RegionId(49, 49), scene.SquareAt(63, 63).GetRegionID());
            Assert.Equal(MapSquareNames.RegionId(51, 51), scene.SquareAt(128, 128).GetRegionID());
        }

        [Fact]
        public void OutOfBoundsTilesHaveNoSquare()
        {
            MapScene scene = ThreeByThree();

            Assert.Null(scene.SquareAt(-1, 0));
            Assert.Null(scene.SquareAt(0, -1));
            Assert.Null(scene.SquareAt(192, 0));
            Assert.Null(scene.SquareAt(0, 192));
        }

        [Fact]
        public void AbsentSquaresReadAsEmptyRatherThanThrowing()
        {
            //The sea and the edges of the world are genuinely missing squares.
            var squares = new MapRegion[3, 3];
            squares[1, 1] = new MapRegion(MapSquareNames.RegionId(50, 50));
            MapScene scene = MapScene.FromSquares(49, 49, squares);

            Assert.Null(scene.SquareAt(0, 0));
            Assert.Equal(0, scene.UnderlayId(0, 0, 0));
            Assert.Equal(0, scene.OverlayId(0, 0, 0));
            Assert.Equal(0, scene.TileFlags(0, 0, 0));

            int[,] grid = scene.UnderlayGrid(0);
            Assert.Equal(192, grid.GetLength(0));
            Assert.Equal(0, grid[0, 0]);
        }

        [Fact]
        public void LocationsAreTranslatedIntoSceneCoordinates()
        {
            var centre = new MapRegion(MapSquareNames.RegionId(50, 50));

            //A single loc at local (3, 7) of the centre square.
            var stream = new JagStream(BuildLocStream(objectId: 1234, localX: 3, localY: 7, plane: 0, shape: 10, rotation: 2));
            centre.LoadLocations(stream);

            var squares = new MapRegion[3, 3];
            squares[1, 1] = centre;
            MapScene scene = MapScene.FromSquares(49, 49, squares);

            var found = new List<(Location, int, int)>();
            foreach (var entry in scene.Locations(0))
                found.Add(entry);

            Assert.Single(found);
            (Location loc, int sceneX, int sceneY) = found[0];

            Assert.Equal(1234, loc.Id);
            Assert.Equal(10, loc.Shape);
            Assert.Equal(2, loc.Orientation);

            //Centre square starts at scene tile 64.
            Assert.Equal(64 + 3, sceneX);
            Assert.Equal(64 + 7, sceneY);
        }

        [Fact]
        public void PlaneFilteringSelectsOnlyThatPlane()
        {
            var centre = new MapRegion(MapSquareNames.RegionId(50, 50));
            centre.LoadLocations(new JagStream(BuildLocStream(1234, 3, 7, plane: 2, shape: 10, rotation: 0)));

            var squares = new MapRegion[1, 1];
            squares[0, 0] = centre;
            MapScene scene = MapScene.FromSquares(50, 50, squares);

            Assert.Empty(scene.Locations(0));
            Assert.Single(scene.Locations(2));
            Assert.Single(scene.Locations(-1));
        }

        /// <summary>
        ///     A screen point maps back to the tile it was drawn from.
        /// </summary>
        /// <remarks>
        ///     Scene Y runs north and screen Y runs down, so the transform is not a plain scale.
        ///     The rasteriser draws scene tile <c>y</c> at screen row <c>height - 1 - y</c>, and
        ///     hit-testing has to undo exactly that.
        /// </remarks>
        [Fact]
        public void HitTestUndoesTheVerticalFlip()
        {
            using var viewer = new MapViewerControl();
            MapScene scene = ThreeByThree();

            //A null rasteriser leaves the bitmap unrendered, which HitTest does not need.
            viewer.Show(scene, null);
            viewer.TilePixels = 4;

            //Show centres the view on the middle square; pin it so the arithmetic is checkable.
            viewer.ViewOffset = Point.Empty;

            //At a zero offset the top-left pixel is the northernmost scene row.
            TileHit topLeft = viewer.HitTest(new Point(0, 0));
            Assert.NotNull(topLeft);
            Assert.Equal(0, topLeft.SceneX);
            Assert.Equal(scene.HeightTiles - 1, topLeft.SceneY);

            //And the bottom-left pixel is scene row 0.
            TileHit bottomLeft = viewer.HitTest(new Point(0, (scene.HeightTiles * 4) - 1));
            Assert.NotNull(bottomLeft);
            Assert.Equal(0, bottomLeft.SceneY);
        }

        [Fact]
        public void HitTestReportsRegionAndLocalCoordinates()
        {
            using var viewer = new MapViewerControl();
            MapScene scene = ThreeByThree();
            viewer.Show(scene, null);
            viewer.TilePixels = 1;
            viewer.ViewOffset = Point.Empty;

            //Scene tile (64, 64) is local (0, 0) of square 50_50.
            int screenY = scene.HeightTiles - 1 - 64;
            TileHit hit = viewer.HitTest(new Point(64, screenY));

            Assert.NotNull(hit);
            Assert.Equal(50, hit.RegionX);
            Assert.Equal(50, hit.RegionY);
            Assert.Equal(0, hit.LocalX);
            Assert.Equal(0, hit.LocalY);
            Assert.Equal(49 * 64 + 64, hit.WorldX);
        }

        [Fact]
        public void HitTestOffTheSceneReturnsNothing()
        {
            using var viewer = new MapViewerControl();
            viewer.Show(ThreeByThree(), null);
            viewer.TilePixels = 4;
            viewer.ViewOffset = Point.Empty;

            Assert.Null(viewer.HitTest(new Point(-10, 0)));
            Assert.Null(viewer.HitTest(new Point(100000, 0)));
        }

        /// <summary>
        ///     Every vertex of a uniform scene reads that uniform height, including the shared ones.
        /// </summary>
        /// <remarks>
        ///     This is the test that fails if the vertex seam is left unfixed. A square's heights
        ///     array is sized 65x65 but its decoder only writes 0..63, so an implementation that
        ///     resolves a shared vertex to its own square's index 64 returns zero at every multiple
        ///     of 64 - 385 of the 37,249 vertices in a 3x3 scene. It also proves the outer rim does
        ///     not leak zeros.
        /// </remarks>
        [Fact]
        public void EveryVertexOfAUniformSceneReadsTheUniformHeight()
        {
            const int height = -320;
            MapScene scene = ThreeByThree();

            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    for (int x = 0; x < 64; x++)
                        for (int y = 0; y < 64; y++)
                            scene.Square(dx, dy).SetTileHeight(0, x, y, height);

            for (int vx = 0; vx <= scene.WidthTiles; vx++)
                for (int vy = 0; vy <= scene.HeightTiles; vy++)
                    Assert.Equal(height, scene.VertexHeight(0, vx, vy));
        }

        /// <summary>
        ///     Reading heights never marks a square dirty.
        /// </summary>
        /// <remarks>
        ///     The most valuable test here, because the rejected fix - copying neighbour heights
        ///     into each square's index 64 - passes every other test in this file and fails only
        ///     this one. <c>Region.SetTileHeight</c> sets <c>Dirty</c> outside its bounds guard, so
        ///     stitching would dirty all nine squares; <c>RegionCodec.EncodeTerrain</c> then skips
        ///     its verbatim fast path and the save path offers to rewrite eight untouched archives.
        /// </remarks>
        [Fact]
        public void ReadingHeightsDirtiesNoSquare()
        {
            MapScene scene = ThreeByThree();

            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    scene.Square(dx, dy).ClearDirty();

            for (int plane = 0; plane < 4; plane++)
                scene.HeightGrid(plane);

            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    Assert.False(scene.Square(dx, dy).Dirty,
                        $"square {dx},{dy} was dirtied by a read");
        }

        /// <summary>Editing one tile dirties that square and no other.</summary>
        [Fact]
        public void EditingOneTileDirtiesOnlyItsOwnSquare()
        {
            MapScene scene = ThreeByThree();

            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    scene.Square(dx, dy).ClearDirty();

            scene.Square(1, 1).SetTileHeight(0, 10, 10, -640);

            for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                    Assert.Equal(dx == 1 && dy == 1, scene.Square(dx, dy).Dirty);
        }

        [Fact]
        public void HeightGridIsOneLargerThanTheTileGrid()
        {
            int[,] grid = ThreeByThree().HeightGrid(0);

            Assert.Equal(193, grid.GetLength(0));
            Assert.Equal(193, grid.GetLength(1));
        }

        /// <summary>
        ///     Absent squares flatten to the nearest loaded ground rather than to zero.
        /// </summary>
        /// <remarks>
        ///     Heights are negative and zero is sea level, so a zero-filled hole is higher than the
        ///     land beside it. Left as zero, every coastline would grow a wall.
        /// </remarks>
        [Fact]
        public void AbsentSquaresFlattenToTheNearestLoadedNeighbour()
        {
            const int height = -320;

            var squares = new MapRegion[3, 3];
            squares[1, 1] = new MapRegion(MapSquareNames.RegionId(50, 50));
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 64; y++)
                    squares[1, 1].SetTileHeight(0, x, y, height);

            MapScene scene = MapScene.FromSquares(49, 49, squares);

            for (int vx = 0; vx <= scene.WidthTiles; vx++)
                for (int vy = 0; vy <= scene.HeightTiles; vy++)
                    Assert.Equal(height, scene.VertexHeight(0, vx, vy));
        }

        /// <summary>A plane a square does not carry clamps rather than throwing.</summary>
        /// <remarks>
        ///     Underwater squares decode a single plane, so this arm is reachable with real data.
        /// </remarks>
        [Fact]
        public void APlaneASquareDoesNotCarryClampsRatherThanThrowing()
        {
            var single = new MapRegion(MapSquareNames.RegionId(50, 50));
            single.LoadTerrain(new JagStream(SinglePlaneTerrain()), 1);

            var squares = new MapRegion[1, 1];
            squares[0, 0] = single;
            MapScene scene = MapScene.FromSquares(50, 50, squares);

            Assert.Equal(1, single.PlaneCount);
            Assert.Equal(0, scene.VertexHeight(2, 10, 10));
            Assert.Equal(0, scene.VertexHeight(3, 64, 64));
        }

        /// <summary>A one-plane terrain file: every tile ends immediately with no stored height.</summary>
        private static byte[] SinglePlaneTerrain()
        {
            var stream = new JagStream();
            for (int i = 0; i < 64 * 64; i++)
                stream.WriteByte(0);
            return stream.Flip().ToArray();
        }

        /// <summary>
        ///     The tab panel builds and unbinds without a cache present.
        /// </summary>
        /// <remarks>
        ///     The editor constructs this panel as a field initialiser in the designer file, so a
        ///     throw here takes the whole form down before it can report anything. Binding null is
        ///     the state it sits in until a cache is opened.
        /// </remarks>
        [Fact]
        public void PanelConstructsAndUnbindsCleanly()
        {
            using var panel = new MapEditorPanel();
            panel.Bind(null);
            panel.LoadRegion(50, 50);
        }

        /// <summary>
        ///     Builds a minimal loc stream carrying exactly one placement.
        /// </summary>
        private static byte[] BuildLocStream(int objectId, int localX, int localY, int plane, int shape, int rotation)
        {
            var stream = new JagStream();

            //Id delta from the -1 accumulator, then the terminator after one object.
            stream.WriteUnsignedSmart(objectId + 1);

            int position = (plane << 12) | (localX << 6) | localY;
            stream.WriteUnsignedSmart(position + 1);
            stream.WriteByte((byte) ((shape << 2) | rotation));
            stream.WriteByte(0);  //end of positions
            stream.WriteByte(0);  //end of objects

            return stream.Flip().ToArray();
        }
    }
}
