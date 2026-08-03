namespace FlashEditor.Map {
    /// <summary>
    ///     Identifies one rendered map square at one zoom level.
    /// </summary>
    /// <remarks>
    ///     Plane, layers and relief are deliberately <b>not</b> part of the key. They are global
    ///     toggles: changing any of them invalidates essentially every tile at every level, so a key
    ///     that carried them would only ever hold one live value at a time while making the cache
    ///     look partitioned. They live in <see cref="MapRenderSignature"/> instead, which clears the
    ///     cache wholesale when it changes.
    /// </remarks>
    /// <param name="RegionX">Region X, 0..255.</param>
    /// <param name="RegionY">Region Y, 0..255.</param>
    /// <param name="Level">
    ///     <c>log2</c> of pixels per tile, -4 to 4. Level 2 is the client's four pixels per tile.
    /// </param>
    public readonly record struct MapTileKey(int RegionX, int RegionY, int Level);
}
