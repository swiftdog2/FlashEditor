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
