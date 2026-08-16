namespace FlashEditor.Map {
    /// <summary>
    ///     The largest value each map field can hold, and why.
    /// </summary>
    /// <remarks>
    ///     <b>Stated once because two paths enforce it.</b> The option bar caps the box the user
    ///     types into, and <see cref="MapAreaEdits"/> refuses a fill that would exceed the same
    ///     bound. Two copies of the number is how one of them ends up a build behind the other, and
    ///     the failure mode of the underlay bound in particular is silent: 175 wraps in the stored
    ///     byte and the tile decodes as an entirely different floor, with nothing in the editor or
    ///     the client reporting an error.
    /// </remarks>
    public static class MapToolLimits {
        /// <summary>
        ///     The highest underlay id a tile can store.
        /// </summary>
        /// <remarks>
        ///     A tile writes its underlay as <c>id + 81</c> in a single byte
        ///     (<c>RegionCodec.EncodeTile</c>), so 174 is the last id that survives the encoder. The
        ///     floor table declares more than that, which is why picking one past the cap has to
        ///     refuse out loud rather than clamp: the record the user pointed at exists and simply
        ///     cannot be put on a tile.
        /// </remarks>
        public const int MaximumUnderlayId = 174;

        /// <summary>
        ///     The highest overlay id a tile can store.
        /// </summary>
        /// <remarks>
        ///     Written as a bare byte, so the whole byte is available.
        /// </remarks>
        public const int MaximumOverlayId = 255;

        /// <summary>
        ///     The highest object definition id the place tool will write.
        /// </summary>
        /// <remarks>
        ///     A location id is a smart delta in the loc file with no byte to overflow, so the bound
        ///     here is the addressable range of index 16 rather than a storage limit.
        /// </remarks>
        public const int MaximumLocationId = 65535;
    }
}
