namespace FlashEditor.Map {
    /// <summary>
    ///     A reading of <see cref="MapTileCache"/>'s validity counters, taken before a render.
    /// </summary>
    /// <remarks>
    ///     Exists so the "is this still wanted" test and the store into the cache can happen as one
    ///     step under the cache's own lock. Testing outside and then calling in leaves a window in
    ///     which an edit or a settings change lands between the two, and the tile that results is
    ///     worse than a missing one: every later lookup finds it, so no repaint ever reports it as
    ///     a miss and nothing re-requests it.
    /// </remarks>
    /// <param name="Generation">
    ///     Moves whenever the whole cache, or a band of it, is thrown away for a settings change.
    /// </param>
    /// <param name="SquareEpoch">
    ///     Moves whenever the square, or one of its eight neighbours, is edited. Per square rather
    ///     than global so one edit does not refuse every render in flight across the world.
    /// </param>
    public readonly record struct MapTileStamp(long Generation, long SquareEpoch);
}
