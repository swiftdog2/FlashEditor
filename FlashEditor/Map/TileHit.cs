namespace FlashEditor.Map {
    /// <summary>Identifies the tile under the cursor.</summary>
    /// <remarks>
    ///     Moved out of the viewer control because two different views now produce one: the legacy
    ///     single-scene viewer and the whole-world view, which has no single scene to be relative to.
    ///     <see cref="WorldX"/> and <see cref="WorldY"/> are therefore the primary coordinate, and a
    ///     caller that needs scene coordinates derives them from the scene it actually holds, as
    ///     <c>hit.WorldX - scene.BaseX</c>.
    /// </remarks>
    public sealed class TileHit {
        /// <summary>
        ///     Scene tile X, relative to whichever scene the producing view built.
        /// </summary>
        /// <remarks>
        ///     Only meaningful together with the scene it came from. The whole-world view fills it in
        ///     for the 3x3 neighbourhood centred on <see cref="RegionX"/> and <see cref="RegionY"/>,
        ///     which is the scene the editor builds for that hit, so it agrees with
        ///     <c>WorldX - scene.BaseX</c> there. Prefer deriving it that way rather than trusting
        ///     this field against an unrelated scene.
        /// </remarks>
        public int SceneX { get; init; }

        /// <summary>Scene tile Y, with the same caveat as <see cref="SceneX"/>.</summary>
        public int SceneY { get; init; }

        /// <summary>Absolute world X.</summary>
        public int WorldX { get; init; }

        /// <summary>Absolute world Y.</summary>
        public int WorldY { get; init; }

        /// <summary>Region X of the square this tile belongs to.</summary>
        public int RegionX { get; init; }

        /// <summary>Region Y of the square this tile belongs to.</summary>
        public int RegionY { get; init; }

        /// <summary>Tile X within its square, 0..63.</summary>
        public int LocalX { get; init; }

        /// <summary>Tile Y within its square, 0..63.</summary>
        public int LocalY { get; init; }

        /// <summary>The plane being viewed.</summary>
        public int Plane { get; init; }
    }
}
