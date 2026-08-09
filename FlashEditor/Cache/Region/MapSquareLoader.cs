using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.IO;

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
        ///     How many squares land here is a property of the cache, not of the format, and it
        ///     moves a long way between the two supported ones: the vanilla b639 capture holds 1649
        ///     of its 1684 <c>l</c> groups as ciphertext against the repack's 659, because the
        ///     repack has already decrypted most of them in place. Every keyed group decrypts in
        ///     both, which is what <c>RealCacheXteaCoverageTests</c> pins; the unkeyed remainder is
        ///     62 and 61. Never treat either figure as a constant.
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
        ///     Whether the square has underwater terrain.
        /// </summary>
        /// <remarks>
        ///     Far fewer squares have a seabed than have a surface, so this is a separate question
        ///     from <see cref="Exists"/> rather than a property of the same square.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns><c>true</c> when a <c>um</c> group exists for this square.</returns>
        public bool ExistsUnderwater(int regionX, int regionY) =>
            ResolveGroup(MapSquareNames.UnderwaterTerrain(regionX, regionY)) != -1;

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

            Region region = new Region(MapSquareNames.RegionId(regionX, regionY), MapSquareLayer.Surface);
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

        /// <summary>Planes an underwater terrain file carries.</summary>
        /// <remarks>
        ///     One, and the client agrees: the underwater scene is built as
        ///     <c>new Class305_Sub1(1, ...)</c> (Class181.java:230) against the surface scene's
        ///     four (<c>:216</c>). Decoding a <c>um</c> file with more planes runs off the end of
        ///     the buffer on every shipped square.
        /// </remarks>
        public const int UnderwaterPlanes = 1;

        /// <summary>
        ///     Loads the underwater terrain for a square, if it has any.
        /// </summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The decoded square, or <c>null</c> when there is no underwater terrain.</returns>
        public Region LoadUnderwater(int regionX, int regionY) =>
            LoadUnderwater(regionX, regionY, out _);

        /// <summary>
        ///     Loads the underwater terrain for a square, and where readable its locations.
        /// </summary>
        /// <remarks>
        ///     The returned square is tagged <see cref="MapSquareLayer.Underwater"/>, which is what
        ///     sends <see cref="Save"/> at the <c>um</c> and <c>ul</c> groups rather than at the
        ///     surface pair.
        ///
        ///     No <c>ul</c> group is encrypted in either supported cache, so
        ///     <see cref="LocationLoadResult.MissingKey"/> is not expected here - but the same
        ///     fallback is used rather than a different one, because an unreadable loc file has to
        ///     produce an empty list and a square that refuses to save either way.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <param name="locationResult">Why the locations were or were not read.</param>
        /// <returns>The decoded square, or <c>null</c> when there is no underwater terrain.</returns>
        public Region LoadUnderwater(int regionX, int regionY, out LocationLoadResult locationResult) {
            locationResult = LocationLoadResult.NotPresent;

            int group = ResolveGroup(MapSquareNames.UnderwaterTerrain(regionX, regionY));
            if (group == -1)
                return null;

            Region region = new Region(MapSquareNames.RegionId(regionX, regionY), MapSquareLayer.Underwater);
            region.LoadTerrain(ReadGroup(group), UnderwaterPlanes);

            int locGroup = ResolveGroup(MapSquareNames.UnderwaterLocations(regionX, regionY));
            if (locGroup == -1)
                return region;

            JagStream locs = TryReadGroup(locGroup);
            if (locs == null) {
                locationResult = LocationLoadResult.MissingKey;
                return region;
            }

            region.LoadLocations(locs);
            locationResult = LocationLoadResult.Loaded;
            return region;
        }

        /// <summary>
        ///     Loads a square's NPC spawn table, the <c>n</c> family.
        /// </summary>
        /// <remarks>
        ///     <b>No XTEA key is passed, deliberately.</b> The client has the wiring backwards -
        ///     <c>Class181.java:76-77</c> hands the real keys to <c>n</c>, which is never
        ///     encrypted, while <c>:44</c> hands <c>null</c> to <c>l</c>, which is the only family
        ///     that ever is. Reproducing that would make every spawn table fail to read.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The spawns, or <c>null</c> when the square has no spawn table.</returns>
        public List<NpcSpawn>? LoadNpcSpawns(int regionX, int regionY) {
            int group = ResolveGroup(MapSquareNames.NpcSpawns(regionX, regionY));
            return group == -1 ? null : RegionCodec.DecodeNpcSpawns(ReadGroup(group));
        }

        /// <summary>
        ///     Stages a square's NPC spawn table back into the cache.
        /// </summary>
        /// <remarks>
        ///     As with <see cref="Save"/>, this only stages: nothing reaches disk until the cache
        ///     is committed. The caller owns the decision that something changed, because a spawn
        ///     table is a plain list with no dirty flag to consult - and re-encoding an unchanged
        ///     one would bump the archive version and its CRC for nothing.
        /// </remarks>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <param name="spawns">The spawns to write.</param>
        /// <returns><c>true</c> when the square has a spawn group to write to.</returns>
        public bool SaveNpcSpawns(int regionX, int regionY, IReadOnlyList<NpcSpawn> spawns) {
            if (spawns == null) throw new ArgumentNullException(nameof(spawns));

            int group = ResolveGroup(MapSquareNames.NpcSpawns(regionX, regionY));
            if (group == -1)
                return false;

            cache.WriteFile(RSConstants.MAPS_INDEX, group, 0,
                new JagStream(RegionCodec.EncodeNpcSpawns(spawns)));
            return true;
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
        ///
        ///     <b>The target family comes from the square, not from this method.</b> Until 2026-08-04
        ///     the surface names were resolved unconditionally, so saving a square that came back
        ///     from <see cref="LoadUnderwater"/> wrote its single plane of seabed over the
        ///     four-plane <c>m</c> group - a silent, total loss of that square's surface terrain,
        ///     with a shorter file, a fresh CRC and no error anywhere. <see cref="Region.Layer"/>
        ///     is recorded at load precisely so this cannot be inferred wrongly.
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
                int terrainGroup = ResolveGroup(MapSquareNames.Terrain(region.Layer, regionX, regionY));
                if (terrainGroup != -1)
                    cache.WriteFile(RSConstants.MAPS_INDEX, terrainGroup, 0,
                        new JagStream(RegionCodec.EncodeTerrain(region)));

                //Only write locations back where the square actually had a readable loc file. A
                //square whose locations could not be decrypted decoded to an empty list, and
                //writing that empty list would erase every object in it.
                int locGroup = ResolveGroup(MapSquareNames.Locations(region.Layer, regionX, regionY));
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
