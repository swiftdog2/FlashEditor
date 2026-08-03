using System;
using System.Collections.Generic;
using FlashEditor.Cache.Region;

//System.Drawing.Region arrives via the WinForms implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     A rectangular block of map squares loaded together, with the tile grids flattened into
    ///     scene coordinates.
    /// </summary>
    /// <remarks>
    ///     A scene exists because a single square cannot be coloured on its own. The underlay blend
    ///     reaches <see cref="UnderlayBlender.ReachForward"/> tiles past a tile in one direction and
    ///     <see cref="UnderlayBlender.ReachBack"/> in the other, so the tiles along a square's edge
    ///     need their neighbours' underlays to come out right. Loading one square and blending it
    ///     alone produces a visible seam at every boundary.
    ///
    ///     Scene coordinates run from the south-west corner of the south-west square. Squares that
    ///     do not exist in the cache are left null and read as empty, which is what the sea and the
    ///     edges of the world are.
    ///
    ///     Heights are the exception to that: they read as the nearest loaded vertex rather than as
    ///     zero, because heights are negative and zero is sea level, so an empty square filled with
    ///     zeros would raise a wall along every coastline rather than leaving a flat. See
    ///     <see cref="VertexHeight"/>.
    ///
    ///     This type is renderer-agnostic on purpose: it holds data, not pixels, so a 2D rasteriser
    ///     and a future 3D view can share it.
    /// </remarks>
    public sealed class MapScene {
        private readonly MapRegion[,] squares;

        /// <summary>Region X of the south-west square.</summary>
        public int OriginRegionX { get; }

        /// <summary>Region Y of the south-west square.</summary>
        public int OriginRegionY { get; }

        /// <summary>Squares along X.</summary>
        public int SquaresX { get; }

        /// <summary>Squares along Y.</summary>
        public int SquaresY { get; }

        /// <summary>Scene width in tiles.</summary>
        public int WidthTiles => SquaresX * MapRegion.WIDTH;

        /// <summary>Scene height in tiles.</summary>
        public int HeightTiles => SquaresY * MapRegion.HEIGHT;

        /// <summary>Absolute world X of the scene's western edge.</summary>
        public int BaseX => OriginRegionX * MapRegion.WIDTH;

        /// <summary>Absolute world Y of the scene's southern edge.</summary>
        public int BaseY => OriginRegionY * MapRegion.HEIGHT;

        /// <summary>Squares that exist in the cache but whose locations could not be decrypted.</summary>
        public IReadOnlyList<int> SquaresMissingKeys { get; }

        private MapScene(int originRegionX, int originRegionY, MapRegion[,] squares, List<int> missingKeys) {
            OriginRegionX = originRegionX;
            OriginRegionY = originRegionY;
            this.squares = squares;
            SquaresX = squares.GetLength(0);
            SquaresY = squares.GetLength(1);
            SquaresMissingKeys = missingKeys;
        }

        /// <summary>
        ///     Loads a square and the ring of squares around it.
        /// </summary>
        /// <param name="loader">The map square loader.</param>
        /// <param name="centreRegionX">Region X of the square of interest.</param>
        /// <param name="centreRegionY">Region Y of the square of interest.</param>
        /// <param name="apron">
        ///     Squares to load on every side. One is enough for a correct blend, since 64 tiles
        ///     comfortably exceeds the window's reach of 5.
        /// </param>
        /// <returns>The loaded scene.</returns>
        public static MapScene Load(MapSquareLoader loader, int centreRegionX, int centreRegionY, int apron = 1) {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            if (apron < 0) throw new ArgumentOutOfRangeException(nameof(apron));

            int side = apron * 2 + 1;
            var grid = new MapRegion[side, side];
            var missingKeys = new List<int>();

            for (int dx = 0; dx < side; dx++) {
                for (int dy = 0; dy < side; dy++) {
                    int rx = centreRegionX - apron + dx;
                    int ry = centreRegionY - apron + dy;

                    if (rx < 0 || ry < 0 || rx > 255 || ry > 255)
                        continue;

                    MapRegion region = loader.Load(rx, ry, out LocationLoadResult result);
                    if (region == null)
                        continue;

                    grid[dx, dy] = region;
                    if (result == LocationLoadResult.MissingKey)
                        missingKeys.Add(MapSquareNames.RegionId(rx, ry));
                }
            }

            return new MapScene(centreRegionX - apron, centreRegionY - apron, grid, missingKeys);
        }

        /// <summary>Builds a scene from squares already loaded, for tests and for editing.</summary>
        /// <param name="originRegionX">Region X of the south-west square.</param>
        /// <param name="originRegionY">Region Y of the south-west square.</param>
        /// <param name="squares">Squares indexed <c>[dx, dy]</c> from the origin. Nulls allowed.</param>
        /// <returns>The scene.</returns>
        public static MapScene FromSquares(int originRegionX, int originRegionY, MapRegion[,] squares) {
            if (squares == null) throw new ArgumentNullException(nameof(squares));
            return new MapScene(originRegionX, originRegionY, squares, new List<int>());
        }

        /// <summary>The square covering a scene tile, or <c>null</c> when that square is absent.</summary>
        /// <param name="sceneX">Scene tile X.</param>
        /// <param name="sceneY">Scene tile Y.</param>
        /// <returns>The square, or <c>null</c>.</returns>
        public MapRegion SquareAt(int sceneX, int sceneY) {
            if (sceneX < 0 || sceneY < 0 || sceneX >= WidthTiles || sceneY >= HeightTiles)
                return null;
            return squares[sceneX / MapRegion.WIDTH, sceneY / MapRegion.HEIGHT];
        }

        /// <summary>The square at a grid offset from the origin, or <c>null</c>.</summary>
        /// <param name="dx">Squares east of the origin.</param>
        /// <param name="dy">Squares north of the origin.</param>
        /// <returns>The square, or <c>null</c>.</returns>
        public MapRegion Square(int dx, int dy) {
            if (dx < 0 || dy < 0 || dx >= SquaresX || dy >= SquaresY)
                return null;
            return squares[dx, dy];
        }

        /// <summary>
        ///     Flattens one plane's underlay ids into a scene-sized grid.
        /// </summary>
        /// <remarks>
        ///     This is what <see cref="UnderlayBlender.Blend"/> consumes. Absent squares read as 0,
        ///     which the blender treats as contributing nothing rather than as black.
        /// </remarks>
        /// <param name="plane">The plane.</param>
        /// <returns>Underlay ids indexed <c>[sceneX, sceneY]</c>.</returns>
        public int[,] UnderlayGrid(int plane) {
            int[,] grid = new int[WidthTiles, HeightTiles];

            for (int sx = 0; sx < WidthTiles; sx++) {
                for (int sy = 0; sy < HeightTiles; sy++) {
                    MapRegion square = squares[sx / MapRegion.WIDTH, sy / MapRegion.HEIGHT];
                    if (square == null || plane >= square.PlaneCount)
                        continue;
                    grid[sx, sy] = square.GetUnderlayId(plane, sx % MapRegion.WIDTH, sy % MapRegion.HEIGHT);
                }
            }

            return grid;
        }

        /// <summary>
        ///     The terrain height at a scene vertex, in world units.
        /// </summary>
        /// <remarks>
        ///     Heights live on a vertex grid one larger than the tile grid on each axis: tile
        ///     <c>(x, y)</c> is bounded by vertices <c>(x, y)</c>, <c>(x+1, y)</c>, <c>(x+1, y+1)</c>
        ///     and <c>(x, y+1)</c>. The client holds one array for a whole scene
        ///     (Class305.java:127 allocates <c>[planes][sceneW+1][sceneH+1]</c>), so the "+1 vertex"
        ///     of one square <em>is</em> the "+0 vertex" of the next and every shared vertex has
        ///     exactly one owner.
        ///
        ///     This port gives each <see cref="MapRegion"/> a private <c>[4, 65, 65]</c> array whose
        ///     decode loop only writes indices 0..63, so index 64 is permanently zero and a shared
        ///     vertex has two candidate owners of which only one holds real data. Ownership is
        ///     resolved here rather than by copying neighbour values into each square's index 64,
        ///     because <c>Region.SetTileHeight</c> sets <c>Dirty</c> outside its bounds guard:
        ///     stitching a 3x3 scene would mark all nine squares dirty, and the save path would then
        ///     offer to rewrite eight untouched archives and change their CRCs.
        ///
        ///     Nothing here ever indexes a square at 64, so that latent defect stays unreachable
        ///     rather than merely worked around.
        /// </remarks>
        /// <param name="plane">The plane.</param>
        /// <param name="vx">Scene vertex X, 0 to <see cref="WidthTiles"/> inclusive.</param>
        /// <param name="vy">Scene vertex Y, 0 to <see cref="HeightTiles"/> inclusive.</param>
        /// <returns>The height in world units. More negative is higher ground.</returns>
        public int VertexHeight(int plane, int vx, int vy) {
            int x = Math.Clamp(vx, 0, WidthTiles);
            int y = Math.Clamp(vy, 0, HeightTiles);

            //Vertex WidthTiles would be the last square's local index 64, which nothing writes, so
            //it resolves to that square's last owned vertex instead.
            int dx = Math.Min(x / MapRegion.WIDTH, SquaresX - 1);
            int dy = Math.Min(y / MapRegion.HEIGHT, SquaresY - 1);
            int lx = Math.Min(x - dx * MapRegion.WIDTH, MapRegion.WIDTH - 1);
            int ly = Math.Min(y - dy * MapRegion.HEIGHT, MapRegion.HEIGHT - 1);

            if (Owns(dx, dy, plane))
                return squares[dx, dy].GetTileHeight(plane, lx, ly);

            return NearestOwnedVertexHeight(plane, dx, dy, lx, ly);
        }

        /// <summary>
        ///     Whether a square is present and carries the requested plane.
        /// </summary>
        /// <remarks>
        ///     The plane check is not redundant. The 900 shipped underwater <c>um</c> squares decode
        ///     a single plane, so a scene mixing them with surface squares has genuine per-square
        ///     plane holes and not merely missing squares.
        /// </remarks>
        private bool Owns(int dx, int dy, int plane) =>
            dx >= 0 && dy >= 0 && dx < SquaresX && dy < SquaresY
            && squares[dx, dy] != null
            && plane >= 0 && plane < squares[dx, dy].PlaneCount;

        /// <summary>
        ///     The height of the nearest vertex that a loaded square owns.
        /// </summary>
        /// <remarks>
        ///     Reached for the sea, past the edge of the world, or for a plane a square does not
        ///     carry. Returning zero there would put a cliff along every coastline: heights are
        ///     negative, real ground sits around -320 to -2000, and a synthetic 0 is therefore
        ///     higher than the land beside it. Flattening toward the nearest real ground is what the
        ///     client does for holes in its own map (Class305.method3567).
        ///
        ///     Rings outward by Chebyshev distance, west and south before east and north, so the
        ///     tie-break is stable and testable. Searching only west and south would be wrong: a
        ///     scene with just its centre square loaded has nothing west of the first column.
        /// </remarks>
        private int NearestOwnedVertexHeight(int plane, int dx, int dy, int lx, int ly) {
            int reach = Math.Max(SquaresX, SquaresY);

            for (int radius = 1; radius <= reach; radius++) {
                for (int ox = -radius; ox <= radius; ox++) {
                    for (int oy = -radius; oy <= radius; oy++) {
                        if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != radius)
                            continue;
                        if (!Owns(dx + ox, dy + oy, plane))
                            continue;

                        //Take the height from the found square's facing edge rather than from an
                        //arbitrary interior tile.
                        int nlx = ox == 0 ? lx : ox < 0 ? MapRegion.WIDTH - 1 : 0;
                        int nly = oy == 0 ? ly : oy < 0 ? MapRegion.HEIGHT - 1 : 0;
                        return squares[dx + ox, dy + oy].GetTileHeight(plane, nlx, nly);
                    }
                }
            }

            //No square anywhere in the scene carries this plane.
            return 0;
        }

        /// <summary>
        ///     Flattens one plane's heights into a scene-sized vertex grid.
        /// </summary>
        /// <remarks>
        ///     One larger than the tile grid on each axis, which is what a gradient stencil needs.
        ///     Rebuilt on every call rather than memoised, deliberately: nothing notifies this type
        ///     when <c>Region.SetTileHeight</c> runs, so a cache would go stale the instant the
        ///     raise tool fired, which is the one case relief shading exists for.
        /// </remarks>
        /// <param name="plane">The plane.</param>
        /// <returns>Heights indexed <c>[vertexX, vertexY]</c>, sized one larger than the tile grid.</returns>
        public int[,] HeightGrid(int plane) {
            int[,] grid = new int[WidthTiles + 1, HeightTiles + 1];

            for (int vx = 0; vx <= WidthTiles; vx++)
                for (int vy = 0; vy <= HeightTiles; vy++)
                    grid[vx, vy] = VertexHeight(plane, vx, vy);

            return grid;
        }

        /// <summary>The overlay id at a scene tile, or 0 when there is none.</summary>
        public int OverlayId(int plane, int sceneX, int sceneY) =>
            Sample(plane, sceneX, sceneY, (r, x, y) => r.GetOverlayId(plane, x, y));

        /// <summary>The overlay shape at a scene tile.</summary>
        public int OverlayShape(int plane, int sceneX, int sceneY) =>
            Sample(plane, sceneX, sceneY, (r, x, y) => r.GetOverlayShape(plane, x, y));

        /// <summary>The overlay rotation at a scene tile.</summary>
        public int OverlayRotation(int plane, int sceneX, int sceneY) =>
            Sample(plane, sceneX, sceneY, (r, x, y) => r.GetOverlayRotation(plane, x, y));

        /// <summary>The underlay id at a scene tile, or 0 when there is none.</summary>
        public int UnderlayId(int plane, int sceneX, int sceneY) =>
            Sample(plane, sceneX, sceneY, (r, x, y) => r.GetUnderlayId(plane, x, y));

        /// <summary>The tile flag byte at a scene tile.</summary>
        public int TileFlags(int plane, int sceneX, int sceneY) =>
            Sample(plane, sceneX, sceneY, (r, x, y) => r.GetRenderRule(plane, x, y));

        private int Sample(int plane, int sceneX, int sceneY, Func<MapRegion, int, int, int> read) {
            MapRegion square = SquareAt(sceneX, sceneY);
            if (square == null || plane < 0 || plane >= square.PlaneCount)
                return 0;
            return read(square, sceneX % MapRegion.WIDTH, sceneY % MapRegion.HEIGHT);
        }

        /// <summary>
        ///     Every location in the scene, with its position translated into scene tiles.
        /// </summary>
        /// <param name="plane">The plane to collect, or -1 for all planes.</param>
        /// <returns>Locations paired with their scene coordinates.</returns>
        public IEnumerable<(Location Loc, int SceneX, int SceneY)> Locations(int plane = -1) {
            for (int dx = 0; dx < SquaresX; dx++) {
                for (int dy = 0; dy < SquaresY; dy++) {
                    MapRegion square = squares[dx, dy];
                    if (square == null)
                        continue;

                    int offsetX = dx * MapRegion.WIDTH;
                    int offsetY = dy * MapRegion.HEIGHT;

                    foreach (Location loc in square.GetLocations()) {
                        if (plane >= 0 && loc.Plane != plane)
                            continue;
                        yield return (loc, offsetX + loc.LocalX, offsetY + loc.LocalY);
                    }
                }
            }
        }
    }
}
