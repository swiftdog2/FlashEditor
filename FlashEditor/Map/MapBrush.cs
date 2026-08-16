using System;
using System.Collections.Generic;

namespace FlashEditor.Map {
    /// <summary>The outline a brush stamps, at whatever size it is set to.</summary>
    public enum MapBrushShape {
        /// <summary>A filled block, size by size. What a one-tile brush always was.</summary>
        Square,

        /// <summary>A filled disc inscribed in the block.</summary>
        Round,

        /// <summary>A filled diamond inscribed in the block, which follows the tile diagonals.</summary>
        Diamond
    }

    /// <summary>
    ///     The tiles one click of a brush covers.
    /// </summary>
    /// <remarks>
    ///     Separate from the tools that use it, and free of both WinForms and the cache, because
    ///     every arm of it is off-by-one country: a size is a count of tiles rather than a radius,
    ///     an even size has no centre tile, and a disc drawn from tile centres is not the disc drawn
    ///     from tile corners. Keeping the arithmetic here is what makes it checkable without a map.
    /// </remarks>
    public static class MapBrush {
        /// <summary>
        ///     The widest brush the option bar offers, in tiles.
        /// </summary>
        /// <remarks>
        ///     A square is 64 tiles across, so a 33-tile brush is already half a square and its
        ///     footprint reaches into four of them from anywhere near a corner. Past that a brush is
        ///     doing a selection's job worse than the selection tools do it.
        /// </remarks>
        public const int MaximumSize = 33;

        /// <summary>
        ///     The tiles a brush of a given size and shape covers when clicked on one tile.
        /// </summary>
        /// <remarks>
        ///     <b>An even size grows north and east.</b> There is no centre tile in an even block, so
        ///     one of the four candidate placements has to be chosen and stated: the clicked tile is
        ///     the south-west of the middle four. Left to a rounding it would move depending on the
        ///     sign of the coordinate, which is how a brush ends up drawing half a tile off in one
        ///     quadrant of the world.
        ///     <para>
        ///     Tiles outside the world are dropped here rather than by the caller, so a brush near
        ///     the edge paints the part of itself that exists instead of refusing.
        ///     </para>
        /// </remarks>
        /// <param name="worldX">World X of the clicked tile.</param>
        /// <param name="worldY">World Y of the clicked tile.</param>
        /// <param name="size">Tiles across, at least one.</param>
        /// <param name="shape">The outline.</param>
        /// <returns>The covered tiles, in world coordinates.</returns>
        public static IEnumerable<(int WorldX, int WorldY)> Footprint(int worldX, int worldY, int size,
            MapBrushShape shape) {
            int side = Math.Clamp(size, 1, MaximumSize);

            //Half the block below and behind the clicked tile, the remainder above and in front.
            int back = (side - 1) / 2;
            int forward = side - 1 - back;

            //Measured from the block's centre in half-tile units, so an even side has its centre on
            //a tile boundary and an odd one has it on a tile.
            double centreX = worldX - back + (side - 1) / 2.0;
            double centreY = worldY - back + (side - 1) / 2.0;
            double radius = side / 2.0;

            for (int y = worldY - back; y <= worldY + forward; y++) {
                for (int x = worldX - back; x <= worldX + forward; x++) {
                    if (x < 0 || y < 0 || x >= MapCamera.WorldTiles || y >= MapCamera.WorldTiles)
                        continue;

                    if (!Covers(shape, x - centreX, y - centreY, radius))
                        continue;

                    yield return (x, y);
                }
            }
        }

        /// <summary>
        ///     Whether a tile at an offset from the brush centre is inside the outline.
        /// </summary>
        /// <remarks>
        ///     The half-tile added to the radius is what stops a round brush of size 3 coming out as
        ///     a plus sign: measured centre to centre, the four diagonal tiles of a 3x3 sit at
        ///     distance 1.41 against a radius of 1.5, which is inside - but at size 5 the corners sit
        ///     at 2.83 against 2.5 and drop out, which is the disc the user asked for.
        /// </remarks>
        private static bool Covers(MapBrushShape shape, double dx, double dy, double radius) {
            switch (shape) {
                case MapBrushShape.Round:
                    return dx * dx + dy * dy <= radius * radius;
                case MapBrushShape.Diamond:
                    return Math.Abs(dx) + Math.Abs(dy) <= radius;
                default:
                    return true;
            }
        }
    }
}
