namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Builds the index-5 group names for a map square.
    /// </summary>
    /// <remarks>
    ///     Index 5 has no region-to-file table. Every map lookup hashes one of these names and
    ///     looks it up in the reference table's identifier map (Class61.java:49-57).
    ///
    ///     There is <em>no underscore after the prefix</em>: the name is <c>m50_50</c>, not
    ///     <c>m_50_50</c>. The bundled server builds the underscored form
    ///     (MapFetcher.java:127) and it hashes to nothing in this cache - measured, 0 matches for
    ///     the <c>l_</c> form against 1684 for <c>l</c>.
    /// </remarks>
    public static class MapSquareNames {
        /// <summary>Terrain: heights, underlay, overlay and tile flags. 1684 in the shipped cache.</summary>
        public static string Terrain(int regionX, int regionY) => "m" + regionX + "_" + regionY;

        /// <summary>Locations: static object placements. 1684 in the shipped cache, 659 encrypted.</summary>
        public static string Locations(int regionX, int regionY) => "l" + regionX + "_" + regionY;

        /// <summary>Underwater terrain, single plane. 900 in the shipped cache.</summary>
        public static string UnderwaterTerrain(int regionX, int regionY) => "um" + regionX + "_" + regionY;

        /// <summary>Underwater locations. 900 in the shipped cache.</summary>
        public static string UnderwaterLocations(int regionX, int regionY) => "ul" + regionX + "_" + regionY;

        /// <summary>
        ///     NPC spawn table. Only 35 exist in the shipped cache.
        /// </summary>
        /// <remarks>
        ///     The one family the client passes XTEA keys to, and the one family that is never
        ///     encrypted. Both live region-load paths null its id array before use, so it is dead
        ///     on the client side.
        /// </remarks>
        public static string NpcSpawns(int regionX, int regionY) => "n" + regionX + "_" + regionY;

        /// <summary>Terrain group name from a packed region id.</summary>
        public static string Terrain(int regionId) => Terrain(RegionX(regionId), RegionY(regionId));

        /// <summary>Location group name from a packed region id.</summary>
        public static string Locations(int regionId) => Locations(RegionX(regionId), RegionY(regionId));

        /// <summary>Extracts the region X from a packed region id.</summary>
        public static int RegionX(int regionId) => (regionId >> 8) & 0xFF;

        /// <summary>Extracts the region Y from a packed region id.</summary>
        public static int RegionY(int regionId) => regionId & 0xFF;

        /// <summary>Packs a region X and Y into a region id.</summary>
        public static int RegionId(int regionX, int regionY) => (regionX << 8) | regionY;
    }
}
