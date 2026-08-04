using System;

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
    ///
    ///     Both supported 639 caches carry the same index-5 table shape: 1684 <c>m</c>, 1684
    ///     <c>l</c>, 900 <c>um</c>, 900 <c>ul</c> and 35 <c>n</c>, and every group holds one file.
    ///     A test still reads those figures off the reference table rather than from here, because
    ///     the claim a sweep makes is that it covered every group the table declares.
    /// </remarks>
    public static class MapSquareNames {
        /// <summary>Terrain: heights, underlay, overlay and tile flags.</summary>
        public static string Terrain(int regionX, int regionY) => "m" + regionX + "_" + regionY;

        /// <summary>Locations: static object placements. Some are XTEA encrypted.</summary>
        public static string Locations(int regionX, int regionY) => "l" + regionX + "_" + regionY;

        /// <summary>Underwater terrain, single plane.</summary>
        public static string UnderwaterTerrain(int regionX, int regionY) => "um" + regionX + "_" + regionY;

        /// <summary>Underwater locations.</summary>
        public static string UnderwaterLocations(int regionX, int regionY) => "ul" + regionX + "_" + regionY;

        /// <summary>
        ///     NPC spawn table.
        /// </summary>
        /// <remarks>
        ///     The one family the client passes XTEA keys to, and the one family that is never
        ///     encrypted (Class181.java:76-77). Both live region-load paths null its id array
        ///     before use, so it is dead on the client side, but the data is real and
        ///     <c>Particle_Sub3_Sub2.method3005</c> still reads it.
        /// </remarks>
        public static string NpcSpawns(int regionX, int regionY) => "n" + regionX + "_" + regionY;

        /// <summary>
        ///     The terrain group name for a square in a given layer.
        /// </summary>
        /// <remarks>
        ///     The write path resolves through here rather than calling <see cref="Terrain(int,int)"/>
        ///     directly, so that "which family am I saving to" is answered by the square instead of
        ///     assumed by the caller.
        /// </remarks>
        /// <param name="layer">Which family the square was read from.</param>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The group name.</returns>
        public static string Terrain(MapSquareLayer layer, int regionX, int regionY) =>
            layer switch {
                MapSquareLayer.Surface => Terrain(regionX, regionY),
                MapSquareLayer.Underwater => UnderwaterTerrain(regionX, regionY),
                _ => throw new ArgumentOutOfRangeException(nameof(layer))
            };

        /// <summary>
        ///     The location group name for a square in a given layer.
        /// </summary>
        /// <param name="layer">Which family the square was read from.</param>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The group name.</returns>
        public static string Locations(MapSquareLayer layer, int regionX, int regionY) =>
            layer switch {
                MapSquareLayer.Surface => Locations(regionX, regionY),
                MapSquareLayer.Underwater => UnderwaterLocations(regionX, regionY),
                _ => throw new ArgumentOutOfRangeException(nameof(layer))
            };

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
