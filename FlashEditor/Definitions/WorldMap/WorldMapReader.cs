using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     Reads index 23 through its name hashes, which is the only way in.
    /// </summary>
    /// <remarks>
    ///     Every lookup here goes through <see cref="WorldMapNaming"/> rather than a literal id, for
    ///     the reasons recorded there. The reader is deliberately not a cache: an area raster is
    ///     megabytes and the largest is 4.7 MB, so holding all 39 would cost more than the whole
    ///     rest of the index put together.
    /// </remarks>
    public sealed class WorldMapReader {
        private readonly RSCache cache;

        /// <summary>Binds a reader to an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        public WorldMapReader(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Every area the <c>details</c> group declares, ascending by area id.
        /// </summary>
        /// <returns>The decoded areas.</returns>
        /// <exception cref="FileNotFoundException">The index has no group named <c>details</c>.</exception>
        public IReadOnlyList<WorldMapAreaDefinition> ReadAreas() {
            int groupId = WorldMapNaming.GroupIdFor(cache, WorldMapNaming.DetailsGroup);
            if (groupId < 0)
                throw new FileNotFoundException(
                    "Index " + RSConstants.WORLD_MAP + " has no group named '" +
                    WorldMapNaming.DetailsGroup + "', so no area can be addressed.");

            var areas = new List<WorldMapAreaDefinition>();
            foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(RSConstants.WORLD_MAP, groupId)) {
                file.Value.Seek0();
                areas.Add(new WorldMapAreaDefinition { Id = file.Key }.Decode(file.Value));
            }

            return areas;
        }

        /// <summary>
        ///     The overview raster for an area, or <c>null</c> when the index holds none.
        /// </summary>
        /// <param name="internalName">The area's internal name, as its details record spells it.</param>
        /// <returns>The decoded raster, or <c>null</c>.</returns>
        public WorldMapAreaRaster? ReadRaster(string internalName) {
            int groupId = WorldMapNaming.GroupIdFor(cache, internalName);
            if (groupId < 0)
                return null;

            int fileId = WorldMapNaming.FileIdFor(cache, groupId, WorldMapNaming.RasterFile);
            if (fileId < 0)
                return null;

            JagStream payload = new JagStream(cache.ReadFileBytes(RSConstants.WORLD_MAP, groupId, fileId));
            return new WorldMapAreaRaster().Decode(payload);
        }

        /// <summary>
        ///     The fixed-position icons for an area, which is empty when the area has no group.
        /// </summary>
        /// <remarks>
        ///     Three areas genuinely have no static-element group and the client falls back to an
        ///     empty list for them (<c>Class181.java:86-100</c>), so an absent group is an ordinary
        ///     answer here rather than a failure.
        /// </remarks>
        /// <param name="internalName">The area's internal name.</param>
        /// <returns>The decoded elements, ascending by file id.</returns>
        public IReadOnlyList<WorldMapElement> ReadStaticElements(string internalName) {
            int groupId = WorldMapNaming.GroupIdFor(cache,
                WorldMapNaming.StaticElementGroupFor(internalName));
            if (groupId < 0)
                return Array.Empty<WorldMapElement>();

            var elements = new List<WorldMapElement>();
            foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(RSConstants.WORLD_MAP, groupId)) {
                file.Value.Seek0();
                elements.Add(new WorldMapElement { Id = file.Key }.Decode(file.Value));
            }

            return elements;
        }

        /// <summary>Whether an area has a static-element group at all.</summary>
        /// <param name="internalName">The area's internal name.</param>
        /// <returns>Whether the group exists.</returns>
        public bool HasStaticElements(string internalName) {
            return WorldMapNaming.GroupIdFor(cache,
                WorldMapNaming.StaticElementGroupFor(internalName)) >= 0;
        }
    }
}
