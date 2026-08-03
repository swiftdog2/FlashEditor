namespace FlashEditor.Map {
    /// <summary>
    ///     Everything about a render that is not the square or the zoom level.
    /// </summary>
    /// <remarks>
    ///     Passed with each request and compared again when the request completes, so the background
    ///     renderer never reads mutable UI state and a tile rendered against a signature the user has
    ///     already moved off is dropped rather than cached under the new one.
    ///
    ///     <see cref="Generation"/> exists so a caller can force a rebuild that no field change would
    ///     otherwise describe - a reopened cache, for instance, whose bytes differ while every
    ///     display setting is identical.
    /// </remarks>
    /// <param name="Plane">The plane being drawn, 0..3.</param>
    /// <param name="Layers">Which layers the user asked for, before per-level reduction.</param>
    /// <param name="ReliefStrength">Relief shading strength, 0 to 1.</param>
    /// <param name="ReliefAzimuth">Relief light azimuth, degrees clockwise from north.</param>
    /// <param name="ReliefAltitude">Relief light altitude, degrees above the horizon.</param>
    /// <param name="Generation">Bumped to invalidate everything without changing a setting.</param>
    public readonly record struct MapRenderSignature(
        int Plane,
        MapLayers Layers,
        float ReliefStrength,
        double ReliefAzimuth,
        double ReliefAltitude,
        int Generation);
}
