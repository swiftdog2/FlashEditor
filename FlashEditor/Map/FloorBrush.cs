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
        /// <remarks>
        ///     The cap is checked on the stored value, which is the unit
        ///     <see cref="MapToolLimits"/> limits. The palette lists every record the table declares
        ///     and some sit past what a tile can hold, so those are refused rather than clamped to a
        ///     floor the user did not pick.
        /// </remarks>
        /// <param name="pick">The definition the user chose.</param>
        /// <returns>The stored value, or a refusal when a tile cannot hold it.</returns>
        public static FloorBrushLoad Load(FloorPick pick) {
            int stored = pick.Id + 1;
            int cap = pick.Kind == FloorKind.Underlay
                ? MapToolLimits.MaximumUnderlayId
                : MapToolLimits.MaximumOverlayId;

            if (stored > cap)
                return new FloorBrushLoad(0, Name(pick.Kind) + " " + pick.Id +
                    " is past the " + (cap - 1) + " a tile can store for it, so the brush was left where it was.");

            return new FloorBrushLoad(stored, null);
        }

        private static string Name(FloorKind kind) => kind == FloorKind.Underlay ? "underlay" : "overlay";

        /// <summary>Reads a tile's floor as the pick that would reproduce it.</summary>
        /// <remarks>
        ///     The overlay wins over the underlay beneath it, because it is what the tile visibly
        ///     is. The pick carries a definition id like a palette pick does, so the stored value
        ///     survives the round trip through <see cref="Load"/> unchanged.
        /// </remarks>
        /// <param name="square">The square holding the tile.</param>
        /// <param name="plane">The tile's plane.</param>
        /// <param name="x">The tile's local x.</param>
        /// <param name="y">The tile's local y.</param>
        /// <returns>The pick, or <c>null</c> when the tile stores neither field.</returns>
        public static FloorPick? PickUp(FlashEditor.Cache.Region.Region square, int plane, int x, int y) {
            int overlay = square.GetOverlayId(plane, x, y);
            if (overlay != 0)
                return new FloorPick(FloorKind.Overlay, overlay - 1);

            int underlay = square.GetUnderlayId(plane, x, y);
            if (underlay != 0)
                return new FloorPick(FloorKind.Underlay, underlay - 1);

            return null;
        }
    }

    /// <summary>What loading a pick into the brush produced.</summary>
    /// <param name="StoredValue">The tile value the brush now writes; meaningless when refused.</param>
    /// <param name="Refusal">Why the pick cannot go on a tile, or <c>null</c> when it can.</param>
    public readonly record struct FloorBrushLoad(int StoredValue, string? Refusal);
}
