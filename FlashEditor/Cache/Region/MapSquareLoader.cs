using System;
using FlashEditor.cache;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Why a map square's location file could not be read.
    /// </summary>
    public enum LocationLoadResult {
        /// <summary>Decoded successfully.</summary>
        Loaded,

        /// <summary>No <c>l</c> group exists for this square.</summary>
        NotPresent,

        /// <summary>
        ///     The group is XTEA encrypted and no key is available for it.
        /// </summary>
        /// <remarks>
        ///     Not an error. 659 of the 1684 shipped <c>l</c> groups are encrypted and 131 of those
        ///     have no key in any published dump. The client reaches the same state and renders the
        ///     square with no objects, so an empty loc list is the correct outcome.
        /// </remarks>
        MissingKey
    }

    /// <summary>
    ///     Reads map squares out of JS5 index 5.
    /// </summary>
    /// <remarks>
    ///     See <c>reference/hydra-637-maps/01-cache-access.md</c>. Index 5 is name-hash addressed,
    ///     every group holds exactly one file, and only the <c>l</c> family is ever encrypted.
    /// </remarks>
    public class MapSquareLoader {
        private readonly RSCache cache;

        /// <summary>Creates a loader over an open cache.</summary>
        /// <param name="cache">The cache to read from.</param>
        public MapSquareLoader(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Resolves an index-5 group name to its archive id.
        /// </summary>
        /// <param name="name">A name from <see cref="MapSquareNames"/>.</param>
        /// <returns>The archive id, or -1 when this square has no group of that family.</returns>
        public int ResolveGroup(string name) {
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.MAPS_INDEX);
            return table == null ? -1 : table.GetArchiveId(name);
        }

        /// <summary>
        ///     Whether the square has a terrain file, and therefore exists at all.
        /// </summary>
        public bool Exists(int regionX, int regionY) =>
            ResolveGroup(MapSquareNames.Terrain(regionX, regionY)) != -1;

        /// <summary>
        ///     Loads a map square's terrain and, where readable, its locations.
        /// </summary>
        /// <param name="regionX">Region X, 0..255.</param>
        /// <param name="regionY">Region Y, 0..255.</param>
        /// <param name="locationResult">Why the locations were or were not read.</param>
        /// <returns>The decoded square, or <c>null</c> when it has no terrain file.</returns>
        public Region Load(int regionX, int regionY, out LocationLoadResult locationResult) {
            locationResult = LocationLoadResult.NotPresent;

            int terrainGroup = ResolveGroup(MapSquareNames.Terrain(regionX, regionY));
            if (terrainGroup == -1)
                return null;

            Region region = new Region(MapSquareNames.RegionId(regionX, regionY));
            region.LoadTerrain(ReadGroup(terrainGroup));

            int locGroup = ResolveGroup(MapSquareNames.Locations(regionX, regionY));
            if (locGroup == -1)
                return region;

            JagStream locs = TryReadGroup(locGroup);
            if (locs == null) {
                //Encrypted with no usable key. The square keeps its terrain and has no objects,
                //which is exactly what the client shows for these.
                locationResult = LocationLoadResult.MissingKey;
                return region;
            }

            region.LoadLocations(locs);
            locationResult = LocationLoadResult.Loaded;
            return region;
        }

        /// <summary>
        ///     Loads the underwater terrain for a square, if it has any.
        /// </summary>
        /// <remarks>
        ///     Single plane. Every one of the 900 shipped <c>um</c> files fails to consume exactly
        ///     with more than one, and none carries an extras tail.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The decoded square, or <c>null</c> when there is no underwater terrain.</returns>
        public Region LoadUnderwater(int regionX, int regionY) {
            int group = ResolveGroup(MapSquareNames.UnderwaterTerrain(regionX, regionY));
            if (group == -1)
                return null;

            Region region = new Region(MapSquareNames.RegionId(regionX, regionY));
            region.LoadTerrain(ReadGroup(group), 1);

            int locGroup = ResolveGroup(MapSquareNames.UnderwaterLocations(regionX, regionY));
            if (locGroup != -1) {
                JagStream locs = TryReadGroup(locGroup);
                if (locs != null)
                    region.LoadLocations(locs);
            }

            return region;
        }

        /// <summary>
        ///     Returns the single file held by an index-5 group.
        /// </summary>
        /// <remarks>
        ///     Every index-5 group holds exactly one file, so the container payload is the file and
        ///     there is no archive layer to unpack.
        /// </remarks>
        /// <param name="groupId">The archive id.</param>
        /// <returns>The decompressed, and where necessary decrypted, payload.</returns>
        private JagStream ReadGroup(int groupId) {
            RSContainer container = cache.GetContainer(RSConstants.MAPS_INDEX, groupId);
            JagStream stream = container.GetStream();
            stream.Seek0();
            return stream;
        }

        /// <summary>
        ///     The raw decoded bytes of an index-5 group.
        /// </summary>
        /// <param name="groupId">The archive id.</param>
        /// <returns>A copy of the payload.</returns>
        public byte[] ReadGroupBytes(int groupId) => ReadGroup(groupId).ToArray();

        /// <summary>
        ///     As <see cref="ReadGroup"/>, but returns <c>null</c> instead of throwing when the
        ///     group cannot be decoded for want of a key.
        /// </summary>
        private JagStream TryReadGroup(int groupId) {
            try {
                return ReadGroup(groupId);
            }
            catch (Exception) {
                //An encrypted payload with no key fails wherever the codec first notices, so the
                //exception type carries nothing worth matching on.
                return null;
            }
        }
    }
}
