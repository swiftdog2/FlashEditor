using System;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     The rectangle an area's overview map is drawn into, and the world-to-map translation that
    ///     puts things in it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Nothing on disk states this rectangle.</b> The client derives it from the area's own
    ///     zone list at <c>Class339.java:29-35</c>: the least destination corner rounded <i>down</i>
    ///     to a map square is the origin, and the greatest destination corner rounded down plus one
    ///     square is the far edge. Rounding to squares is what makes a raster block, which is
    ///     addressed in whole map squares, land on an integer offset.
    ///     </para>
    ///     <para>
    ///     <b>Two coordinate spaces meet here and they are not the same space.</b> A raster block
    ///     names map-square coordinates in the <i>destination</i> space already, so it is placed by
    ///     subtracting the origin (<c>Class278.java:527-529</c>). A static element names a
    ///     <i>world</i> position, so it has to be pushed through the zone that contains it first
    ///     (<c>Class278.java:474-478</c> by way of <c>Node_Sub6.method982</c>). Treating either as
    ///     the other draws a picture that is the right size with everything in the wrong place, which
    ///     is why the two are separate methods rather than one.
    ///     </para>
    /// </remarks>
    public readonly struct WorldMapCanvas {
        /// <summary>Tiles along the edge of a map square, which the canvas is rounded to.</summary>
        public const int MapSquareTiles = 64;

        private WorldMapCanvas(int originX, int originY, int width, int height) {
            OriginX = originX;
            OriginY = originY;
            Width = width;
            Height = height;
        }

        /// <summary>The map x the canvas's left column is at.</summary>
        public int OriginX { get; }

        /// <summary>The map y the canvas's bottom row is at.</summary>
        public int OriginY { get; }

        /// <summary>The canvas width, in tiles.</summary>
        public int Width { get; }

        /// <summary>The canvas height, in tiles.</summary>
        public int Height { get; }

        /// <summary>Whether the area gave no rectangle to draw into.</summary>
        /// <remarks>
        ///     A real answer rather than an error: an area with no zone has nothing to draw and one
        ///     occurs in both caches, so a caller has to handle it.
        /// </remarks>
        public bool IsEmpty => Width <= 0 || Height <= 0;

        /// <summary>The canvas an area's zones describe.</summary>
        /// <param name="area">The area, whose zones are the only statement of its extent.</param>
        /// <returns>The canvas, which is empty when the area declares no zone.</returns>
        public static WorldMapCanvas For(WorldMapAreaDefinition area) {
            if (area == null)
                throw new ArgumentNullException(nameof(area));

            if (area.Zones.Count == 0)
                return new WorldMapCanvas(0, 0, 0, 0);

            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (WorldMapZone zone in area.Zones) {
                minX = Math.Min(minX, zone.DestinationMinX);
                minY = Math.Min(minY, zone.DestinationMinY);
                maxX = Math.Max(maxX, zone.DestinationMaxX);
                maxY = Math.Max(maxY, zone.DestinationMaxY);
            }

            int originX = ToSquare(minX);
            int originY = ToSquare(minY);

            return new WorldMapCanvas(originX, originY,
                ToSquare(maxX) + MapSquareTiles - originX,
                ToSquare(maxY) + MapSquareTiles - originY);
        }

        /// <summary>
        ///     Where a world position lands on this canvas, or false when no zone covers it.
        /// </summary>
        /// <remarks>
        ///     False is an ordinary answer. The client draws nothing for a placement no zone claims
        ///     (<c>Class278.java:474-479</c> skips when the lookup fails), and placements outside
        ///     their own area's zones do occur, so a caller that treated this as a failure would
        ///     report a defect for something the game does too.
        ///     <para>
        ///     The first matching zone wins, which is the client's own rule -
        ///     <c>Node_Sub46_Sub10.method1573</c> returns on the first hit rather than preferring a
        ///     smaller or later rectangle.
        ///     </para>
        /// </remarks>
        /// <param name="area">The area whose zones translate the position.</param>
        /// <param name="plane">The plane the position is on.</param>
        /// <param name="worldX">The world x, in tiles.</param>
        /// <param name="worldY">The world y, in tiles.</param>
        /// <param name="x">The canvas column, when this returns true.</param>
        /// <param name="y">The canvas row, when this returns true.</param>
        /// <returns>Whether a zone covers the position.</returns>
        public bool TryPlace(WorldMapAreaDefinition area, int plane, int worldX, int worldY,
            out int x, out int y) {
            if (area == null)
                throw new ArgumentNullException(nameof(area));

            foreach (WorldMapZone zone in area.Zones) {
                if (zone.Plane != plane ||
                    worldX < zone.SourceMinX || worldX > zone.SourceMaxX ||
                    worldY < zone.SourceMinY || worldY > zone.SourceMaxY)
                    continue;

                x = worldX - zone.SourceMinX + zone.DestinationMinX - OriginX;
                y = worldY - zone.SourceMinY + zone.DestinationMinY - OriginY;
                return true;
            }

            x = 0;
            y = 0;
            return false;
        }

        /// <summary>Rounds a map coordinate down to the map square that contains it.</summary>
        /// <param name="value">A map coordinate, in tiles.</param>
        /// <returns>The coordinate of the square's low edge.</returns>
        private static int ToSquare(int value) {
            return value / MapSquareTiles * MapSquareTiles;
        }
    }
}
