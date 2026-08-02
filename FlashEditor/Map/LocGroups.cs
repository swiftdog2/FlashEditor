namespace FlashEditor.Map {
    /// <summary>The four scene slots a location shape routes to.</summary>
    public enum LocGroup {
        /// <summary>Shapes 0-3. Straight and corner walls.</summary>
        Wall = 0,

        /// <summary>Shapes 4-8. Things hung on a wall.</summary>
        WallDecoration = 1,

        /// <summary>Shapes 9-21. Everything that stands on the ground.</summary>
        GameObject = 2,

        /// <summary>Shape 22. Flat things laid on the ground.</summary>
        GroundDecoration = 3
    }

    /// <summary>Maps a location shape code to its scene slot.</summary>
    /// <remarks>
    ///     The table is <c>Class64_Sub17.anIntArray3685</c>. The client's own dispatcher duplicates
    ///     the same partition as a hardcoded if/else chain rather than reading the array, so both
    ///     forms exist in the source; they agree.
    /// </remarks>
    public static class LocGroups {
        private static readonly int[] Table =
            { 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3 };

        /// <summary>The highest valid shape code.</summary>
        public const int MaxShape = 22;

        /// <summary>
        ///     The slot a shape routes to.
        /// </summary>
        /// <param name="shape">A shape code, 0..22.</param>
        /// <returns>The slot, defaulting to <see cref="LocGroup.GameObject"/> for a bad code.</returns>
        public static LocGroup Of(int shape) {
            if (shape < 0 || shape >= Table.Length)
                return LocGroup.GameObject;
            return (LocGroup) Table[shape];
        }
    }
}
