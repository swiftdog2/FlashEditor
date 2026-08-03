using FlashEditor.cache;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     The group ids within JS5 index 2 that this editor models.
    /// </summary>
    /// <remarks>
    ///     Index 2 is thirty-five unrelated config families sharing one index, so a group id here is
    ///     a <b>type selector</b> rather than a page of ids: nothing arithmetic relates a definition
    ///     id to a group, which is why index 2 has no row in <see cref="CacheAddressing.TryGetFor"/>
    ///     and every family is addressed as <c>SingleGroup(group)</c> with the file id as the
    ///     definition id.
    ///     <para>
    ///     Every id below is read off the provider's own <c>getChildsInFolder(0, n)</c> call in the
    ///     637 client rather than assumed from the index order, and the group id list the reference
    ///     table declares is not <c>0..48</c> - slot 0 of idx2 carries a dead record whose length is
    ///     0xFF0000, so a blind read of it asks the store for a 16 MB sector chain. Enumerate the
    ///     table.
    ///     </para>
    /// </remarks>
    public static class ConfigGroup {
        /// <summary>Floor underlays. Already modelled by <see cref="FloorUnderlayDefinition"/>.</summary>
        public const int FloorUnderlay = RSConstants.FLOOR_UNDERLAY_GROUP;

        /// <summary>Item containers, sized by capacity. Class8.java:163.</summary>
        public const int Container = 5;

        /// <summary>Parameter types, keyed by the op-249 param blocks. Class365.java:102.</summary>
        public const int ParameterType = 11;

        /// <summary>Client strings. Class239.java:75.</summary>
        public const int ClientString = 15;

        /// <summary>Player variables (varps). Class139.java:19.</summary>
        public const int VarPlayer = 16;

        /// <summary>Client variables. Class132.java:117.</summary>
        public const int ClientVariable = 19;

        /// <summary>Cursors. Class11.java:33.</summary>
        public const int Cursor = 33;

        /// <summary>Floor overlays. Already modelled by <see cref="FloorOverlayDefinition"/>.</summary>
        public const int FloorOverlay = RSConstants.FLOOR_OVERLAY_GROUP;

        /// <summary>Map scene icons. Already modelled by <see cref="MapSceneIconDefinition"/>.</summary>
        public const int MapSceneIcon = RSConstants.MAP_SCENE_GROUP;

        /// <summary>World map elements. Class341.java:141.</summary>
        public const int MapElement = RSConstants.MAP_ELEMENT_GROUP;

        /// <summary>Damage marks. Class121.java:102.</summary>
        public const int DamageMark = 46;
    }
}
