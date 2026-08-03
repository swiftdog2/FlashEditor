namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     An absolute world coordinate, and the conversions to the region and packed
    ///     forms the cache addresses things by.
    /// </summary>
    /// <remarks>
    ///     This deliberately carries no viewport state. The class used to hold a
    ///     <c>mapSize</c> (the client's 104/120/136/168 tile render window) alongside
    ///     local-coordinate and chunk accessors that subtracted a half-window from the
    ///     absolute coordinate. Nothing in the editor ever read them - the editor works in
    ///     whole map squares, and <see cref="Location"/> stores the square-local
    ///     coordinates verbatim because they cannot be recomputed from an absolute
    ///     coordinate alone. Window-relative coordinates belong to whatever is drawing,
    ///     not to the coordinate itself.
    /// </remarks>
    public class Position {
        private int x;
        private int y;
        private int height;

        /// <summary>Creates an absolute world position.</summary>
        /// <param name="x">Absolute world X.</param>
        /// <param name="y">Absolute world Y.</param>
        /// <param name="height">Plane, 0..3.</param>
        public Position(int x, int y, int height) {
            this.x = x;
            this.y = y;
            this.height = height;
        }

        /// <summary>The map square X, 64 tiles per square.</summary>
        public int GetRegionX() {
            return (x >> 6);
        }

        /// <summary>The map square Y, 64 tiles per square.</summary>
        public int GetRegionY() {
            return (y >> 6);
        }

        /// <summary>
        ///     The packed region id, the form index 5 group names are built from.
        /// </summary>
        /// <remarks>Same packing as <see cref="MapSquareNames.RegionId"/>.</remarks>
        public int GetRegionID() {
            return ((GetRegionX() << 8) + GetRegionY());
        }

        /// <summary>Absolute world X.</summary>
        public int GetX() {
            return x;
        }

        /// <summary>Absolute world Y.</summary>
        public int GetY() {
            return y;
        }

        /// <summary>Plane, 0..3.</summary>
        public int GetHeight() {
            return height;
        }

        /// <summary>
        ///     The coordinate packed into a single word, 14 bits per axis and the plane
        ///     in the top nibble.
        /// </summary>
        public int ToPositionPacked() {
            return y + (x << 14) + (height << 28);
        }

        /// <inheritdoc/>
        public override string ToString() {
            return "X: " + GetX() + ", Y: " + GetY() + ", Height: " + GetHeight();
        }
    }
}
