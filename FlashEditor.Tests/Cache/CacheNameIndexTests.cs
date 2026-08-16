using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     The per-group and per-file name lookup, against tables built here rather than read.
    /// </summary>
    /// <remarks>
    ///     Synthetic on purpose. The interesting cases are the ones the shipped cache does not
    ///     contain - an entry marked unnamed, a file legitimately named the empty string, a table
    ///     with no identifiers block at all - and a table built by hand is the only way to put all
    ///     three next to each other.
    /// </remarks>
    public class CacheNameIndexTests
    {
        /// <summary>Builds a table carrying whatever names the case needs.</summary>
        /// <param name="indexId">The index the table describes.</param>
        /// <param name="withIdentifiers">Whether the identifiers flag is set.</param>
        /// <returns>The table.</returns>
        private static RSReferenceTable Table(int indexId, bool withIdentifiers)
        {
            var table = new RSReferenceTable
            {
                format = 6,
                flags = withIdentifiers ? RSReferenceTable.FLAG_IDENTIFIERS : 0,
                indexId = indexId
            };

            return table;
        }

        /// <summary>Adds one group with the files named as given.</summary>
        /// <param name="table">The table to add to.</param>
        /// <param name="groupId">The group id.</param>
        /// <param name="groupName">The group name, or null for an entry marked unnamed.</param>
        /// <param name="fileNames">The file names by file id, a null meaning unnamed.</param>
        private static void AddGroup(RSReferenceTable table, int groupId, string groupName,
            params (int FileId, string Name)[] fileNames)
        {
            var entry = new RSArchiveEntry(groupId);
            entry.SetIdentifier(groupName == null ? CacheNameIndex.Unnamed : NameHasher.GetNameHash(groupName));

            var ids = new List<int>();
            foreach ((int fileId, string name) in fileNames)
            {
                var file = new RSFileEntry(fileId);
                file.SetIdentifier(name == null ? CacheNameIndex.Unnamed : NameHasher.GetNameHash(name));
                entry.PutFileEntry(fileId, file);
                ids.Add(fileId);
            }

            entry.SetValidFileIds(ids.ToArray());
            table.PutArchiveEntry(groupId, entry);
        }

        [Fact]
        public void AFileNameResolvesInsideItsOwnGroup()
        {
            RSReferenceTable table = Table(RSConstants.GRAPHICS_SHADERS, withIdentifiers: true);
            AddGroup(table, 1, "gl", (0, "uw_ground_lit"), (6, "environment_mapped_water_v"));
            AddGroup(table, 3, "dx", (0, "uw_ground_lit"), (6, "environment_mapped_water_v"));

            CacheNameIndex names = table.Names;

            Assert.Equal(1, names.GroupId("gl"));
            Assert.Equal(3, names.GroupId("dx"));
            Assert.Equal(6, names.FileId(1, "environment_mapped_water_v"));
            Assert.Equal(0, names.FileId(3, "uw_ground_lit"));

            //Both backends carry the same seven names, so a flat file-name map would be ambiguous.
            //The count says the lookup is nested under a group rather than shared across the index.
            Assert.Equal(4, names.NamedFileCount);
        }

        [Fact]
        public void TheClientsTwoPartAddressResolvesBothHalves()
        {
            RSReferenceTable table = Table(RSConstants.GRAPHICS_SHADERS, withIdentifiers: true);
            AddGroup(table, 1, "gl", (2, "transparent_water"));

            Assert.True(table.Names.TryResolve("gl", "transparent_water", out int group, out int file));
            Assert.Equal(1, group);
            Assert.Equal(2, file);
        }

        [Fact]
        public void TheLookupIsCaseInsensitiveBecauseTheClientLowerCasesFirst()
        {
            RSReferenceTable table = Table(RSConstants.GRAPHICS_SHADERS, withIdentifiers: true);
            AddGroup(table, 1, "gl", (2, "transparent_water"));

            Assert.True(table.Names.TryResolve("GL", "Transparent_Water", out int group, out int file));
            Assert.Equal(1, group);
            Assert.Equal(2, file);
        }

        /// <summary>
        ///     The empty string is a name here, not the absence of one.
        /// </summary>
        /// <remarks>
        ///     Every index-30 group holds a single file called <c>""</c>, hash 0, and the client
        ///     passes that empty string explicitly. An implementation keyed by array position would
        ///     leave undeclared slots holding 0 and answer this lookup with padding.
        /// </remarks>
        [Fact]
        public void TheEmptyStringIsARealFileName()
        {
            RSReferenceTable table = Table(RSConstants.NATIVE_LIBRARIES, withIdentifiers: true);
            AddGroup(table, 2, "windows/x86/jaggl.dll", (0, ""));

            Assert.Equal(0, NameHasher.GetNameHash(""));
            Assert.True(table.Names.TryResolve("windows/x86/jaggl.dll", "", out int group, out int file));
            Assert.Equal(2, group);
            Assert.Equal(0, file);
        }

        /// <summary>
        ///     An entry the format marks unnamed contributes nothing, and does not shadow a real name.
        /// </summary>
        /// <remarks>
        ///     Index 3 carries identifiers and still leaves entries at -1, so this is the common case
        ///     there rather than a hypothetical.
        /// </remarks>
        [Fact]
        public void AnUnnamedEntryIsNotAddressable()
        {
            RSReferenceTable table = Table(RSConstants.INTERFACE_DEFINITIONS_INDEX, withIdentifiers: true);
            AddGroup(table, 4, null, (0, null), (1, "real"));

            CacheNameIndex names = table.Names;

            Assert.Equal(-1, names.GroupIdOfHash(CacheNameIndex.Unnamed));
            Assert.Equal(-1, names.FileIdOfHash(4, CacheNameIndex.Unnamed));
            Assert.Equal(1, names.FileId(4, "real"));
            Assert.Equal(0, names.NamedGroupCount);
            Assert.Equal(1, names.NamedFileCount);
        }

        /// <summary>
        ///     Index 2 is the case this refuses honestly for.
        /// </summary>
        /// <remarks>
        ///     A table with no identifiers block cannot answer any name, and the failure mode worth
        ///     avoiding is answering -1 in silence: that reads identically to a name that is merely
        ///     absent, and sends someone hunting for a group that was never named.
        /// </remarks>
        [Fact]
        public void ATableWithNoIdentifiersSaysSoRatherThanAnsweringMinusOneInSilence()
        {
            RSReferenceTable table = Table(RSConstants.CONFIG, withIdentifiers: false);
            AddGroup(table, 1, "underlay", (0, "anything"));

            CacheNameIndex names = table.Names;

            Assert.False(names.CarriesNames);
            Assert.NotNull(names.NameLookupRefusal);
            Assert.Contains("index " + RSConstants.CONFIG, names.NameLookupRefusal);
            Assert.Equal(-1, names.GroupId("underlay"));
            Assert.Equal(-1, names.FileId(1, "anything"));
            Assert.False(names.TryResolve("underlay", "anything", out _, out _));
        }

        [Fact]
        public void ANamedTableThatSimplyLacksTheNameOffersNoRefusal()
        {
            RSReferenceTable table = Table(RSConstants.GRAPHICS_SHADERS, withIdentifiers: true);
            AddGroup(table, 1, "gl", (0, "transparent_water"));

            CacheNameIndex names = table.Names;

            //A lookup that failed, not a lookup that was never possible. Only the second is worth
            //putting on screen.
            Assert.True(names.CarriesNames);
            Assert.Null(names.NameLookupRefusal);
            Assert.Equal(-1, names.GroupId("vulkan"));
        }

        /// <summary>
        ///     A write that adds a group has to be visible to the next lookup.
        /// </summary>
        /// <remarks>
        ///     The lookup is built once and memoised, so the invalidation is the part that can be
        ///     wrong: a stale index would keep reporting that a group the user just created does not
        ///     exist.
        /// </remarks>
        [Fact]
        public void AddingAGroupRebuildsTheLookup()
        {
            RSReferenceTable table = Table(RSConstants.GRAPHICS_SHADERS, withIdentifiers: true);
            AddGroup(table, 1, "gl", (0, "transparent_water"));

            Assert.Equal(-1, table.Names.GroupId("dx"));

            AddGroup(table, 3, "dx", (0, "transparent_water"));

            Assert.Equal(3, table.Names.GroupId("dx"));
        }
    }
}
