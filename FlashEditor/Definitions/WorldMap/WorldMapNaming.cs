using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     How index 23 is addressed: by hashed name at both the group and the file level.
    /// </summary>
    /// <remarks>
    ///     Nothing on this index can be reached by arithmetic, which is why
    ///     <see cref="CacheAddressing"/> records it as <see cref="CacheIdShape.NameHashed"/>. Three
    ///     facts make a fixed id wrong rather than merely fragile:
    ///     <list type="bullet">
    ///     <item>The <c>details</c> group is id <b>1</b>, not 0, and the client never assumes
    ///     otherwise - <c>Class278.java:171</c> asks for it by name.</item>
    ///     <item>Group ids are sparse. They run 0-44 then 64-94 here, so a <c>0..count-1</c> walk
    ///     asks for nineteen groups that do not exist and never reaches the last thirty-one.</item>
    ///     <item>The <c>area</c> file is id <b>4</b> in 32 groups and id <b>0</b> in the other
    ///     seven, so even within a resolved group the file has to be found by name
    ///     (<c>Class278.java:508-509</c> fetches it as group <c>&lt;areaName&gt;</c>, file
    ///     <c>"area"</c>).</item>
    ///     </list>
    ///     <para>
    ///     The hash is over the <b>lower-cased</b> name, and this index is the cheapest proof of
    ///     that rule anywhere in the cache: 75 of the 76 groups are already lower case and resolve
    ///     either way, while the area whose details record spells it <c>ft3_zanaris_HQ</c> resolves
    ///     only once the name is folded.
    ///     </para>
    /// </remarks>
    public static class WorldMapNaming {
        /// <summary>Name of the group holding one details record per area.</summary>
        public const string DetailsGroup = "details";

        /// <summary>Name of the file holding an area's overview raster, within the area's group.</summary>
        public const string RasterFile = "area";

        /// <summary>Suffix on the group holding an area's fixed-position map elements.</summary>
        /// <remarks>
        ///     Three areas have no such group at all and the client tolerates it, falling back to an
        ///     empty element list at <c>Class181.java:86-100</c>. That is correct behaviour rather
        ///     than a defect, so nothing here may require the group to exist.
        /// </remarks>
        public const string StaticElementSuffix = "_staticelements";

        /// <summary>
        ///     The group a world-map name resolves to, or -1 when the index has no such group.
        /// </summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="name">The group name, case-insensitive.</param>
        /// <returns>The group id, or -1.</returns>
        public static int GroupIdFor(RSCache cache, string name) {
            return cache.GetReferenceTable(RSConstants.WORLD_MAP).GetArchiveId(name);
        }

        /// <summary>
        ///     The file a name resolves to within one group, or -1 when the group has no such file.
        /// </summary>
        /// <remarks>
        ///     A linear scan of the group's file entries rather than a hash table. The reference
        ///     table builds its open-addressed map over archive identifiers only, and no group on
        ///     this index holds more than five files, so a scan costs less than the table would.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="groupId">The group to search.</param>
        /// <param name="name">The file name, case-insensitive.</param>
        /// <returns>The file id, or -1.</returns>
        public static int FileIdFor(RSCache cache, int groupId, string name) {
            RSArchiveEntry? entry = cache.GetReferenceTable(RSConstants.WORLD_MAP).GetArchiveEntry(groupId);
            if (entry == null)
                return -1;

            int hash = NameHasher.GetNameHash(name);
            foreach (KeyValuePair<int, RSFileEntry> file in entry.GetFileEntries())
                if (file.Value.GetIdentifier() == hash)
                    return file.Key;

            return -1;
        }

        /// <summary>The group name holding an area's static elements.</summary>
        /// <param name="internalName">The area's internal name, as its details record spells it.</param>
        /// <returns>The group name.</returns>
        public static string StaticElementGroupFor(string internalName) {
            return internalName + StaticElementSuffix;
        }
    }
}
