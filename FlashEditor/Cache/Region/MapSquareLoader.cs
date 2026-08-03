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
        ///     Not an error. The client reaches the same state and renders the square with no
        ///     objects, so an empty loc list is the correct outcome.
        ///     <para>
        ///     Measured by <c>RealCacheXteaCoverageTests</c>: of the 1684 shipped <c>l</c> groups,
        ///     659 are encrypted in the reference cache and <b>61</b> of those have no key in the
        ///     shipped dump. Every one of the remaining 598 decrypts. An earlier revision of this
        ///     comment put the unkeyed figure at 131, which no measurement supports.
        ///     </para>
        ///     <para>
        ///     The encrypted count is a property of the cache, not of the format, and it moves: the
        ///     same sweep over the OpenRS2 b639 archive finds 1649 encrypted and 62 unkeyed, because
        ///     that copy stores as ciphertext the squares the reference cache holds as plaintext.
        ///     Readable coverage lands in the same place either way, so do not treat 659 as a
        ///     constant.
        ///     </para>
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
        ///     Stages a square's terrain and locations back into the cache.
        /// </summary>
        /// <remarks>
        ///     <b>This does not touch the disk.</b> The store keeps every write staged in memory
        ///     until the cache is committed, which is what makes a multi-square edit land as one
        ///     consistent set of files rather than a sequence of 188MB rewrites.
        ///
        ///     A square that has not been edited stages nothing at all. Rewriting an untouched
        ///     archive would bump its version and recompute its CRC over bytes that did not need to
        ///     change, which tells every client its cached copy is stale for no reason.
        ///
        ///     Both files go through one batch, so the index-5 reference table - a 114KB payload -
        ///     is encoded once rather than once per file.
        /// </remarks>
        /// <param name="region">The square to save.</param>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns><c>true</c> when something was written.</returns>
        public bool Save(Region region, int regionX, int regionY) {
            if (region == null) throw new ArgumentNullException(nameof(region));

            if (!region.Dirty)
                return false;

            using (cache.BeginBatch()) {
                int terrainGroup = ResolveGroup(MapSquareNames.Terrain(regionX, regionY));
                if (terrainGroup != -1)
                    cache.WriteFile(RSConstants.MAPS_INDEX, terrainGroup, 0,
                        new JagStream(RegionCodec.EncodeTerrain(region)));

                //Only write locations back where the square actually had a readable loc file. A
                //square whose locations could not be decrypted decoded to an empty list, and
                //writing that empty list would erase every object in it.
                int locGroup = ResolveGroup(MapSquareNames.Locations(regionX, regionY));
                if (locGroup != -1 && region.RawLocations.Length > 0)
                    cache.WriteFile(RSConstants.MAPS_INDEX, locGroup, 0,
                        new JagStream(RegionCodec.EncodeLocations(region)));
            }

            region.ClearDirty();
            return true;
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
