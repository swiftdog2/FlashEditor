namespace FlashEditor.Map {
    /// <summary>
    ///     Converts between a floor definition id and the value a tile stores for it.
    /// </summary>
    /// <remarks>
    ///     A tile stores both floor fields as definition id + 1, with 0 meaning none. The client
    ///     reads them back as stored - 1: <c>Class305.java:925</c> for the overlay and
    ///     <c>Class305.java:932</c> for the underlay, whose stored value is already
    ///     <c>opcode - 81</c> (<c>Class305.java:1995</c>). The brush holds the stored value, so a
    ///     palette pick converts on the way in and every caption converts on the way out.
    /// </remarks>
    public static class FloorBrush {
        /// <summary>Turns a palette pick into the value the brush writes into a tile.</summary>
        /// <param name="pick">The definition the user chose.</param>
        /// <returns>The stored value, or a refusal when a tile cannot hold it.</returns>
        public static FloorBrushLoad Load(FloorPick pick) {
            return new FloorBrushLoad(pick.Id + 1, null);
        }
    }

    /// <summary>What loading a pick into the brush produced.</summary>
    /// <param name="StoredValue">The tile value the brush now writes; meaningless when refused.</param>
    /// <param name="Refusal">Why the pick cannot go on a tile, or <c>null</c> when it can.</param>
    public readonly record struct FloorBrushLoad(int StoredValue, string? Refusal);
}
