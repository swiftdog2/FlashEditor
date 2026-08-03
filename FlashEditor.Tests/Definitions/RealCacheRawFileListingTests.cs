using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The raw index-3 listing the Interfaces tab shows, checked against the cache it claims to
    ///     describe.
    /// </summary>
    /// <remarks>
    ///     Index 3's record format is not reverse engineered, so the listing is deliberately not a
    ///     decode: it reports what the reference table addresses and how long each stored file is.
    ///     That is a claim about the cache and it is checkable, which is the whole reason a raw
    ///     listing is an honest deliverable rather than a placeholder.
    ///     <para>
    ///     This drives the descriptor exactly as <c>DefinitionListPanel</c> does - enumerate, read a
    ///     group whole, decode each address - so a defect in that sequence shows up here rather than
    ///     only in the UI, which nothing in the suite covers.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheRawFileListingTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _cache;
        private readonly ITestOutputHelper _output;

        public RealCacheRawFileListingTests(RealCacheFixture cache, ITestOutputHelper output)
        {
            _cache = cache;
            _output = output;
        }

        /// <summary>
        ///     The listing addresses exactly the files the reference table declares.
        /// </summary>
        /// <remarks>
        ///     Compared against <see cref="RSCache.EnumerateFiles"/> pair for pair rather than by
        ///     count. A count agrees with plenty of wrong enumerations; index 3's group ids are
        ///     sparse - the idx holds 772, 825 and 891 that the table never declares - so only the
        ///     ids themselves tell a table-driven walk from a dense one.
        /// </remarks>
        [RealCacheFact]
        public void TheListingAddressesTheFilesTheTableDeclares()
        {
            RSCache cache = _cache.OpenCache();
            var descriptor = new RawFileListDescriptor(RSConstants.INTERFACE_DEFINITIONS_INDEX, "interface file");

            var declared = cache.EnumerateFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX)
                .Select(pair => (pair.Group, pair.File))
                .ToList();
            var listed = descriptor.Enumerate(cache)
                .Select(address => (address.GroupId, address.FileId))
                .ToList();

            Assert.Equal(declared, listed);
            Assert.Equal(42256, listed.Count);
        }

        /// <summary>
        ///     Every row reports the length of the bytes actually stored for it.
        /// </summary>
        /// <remarks>
        ///     The size column is the one claim the listing makes that could be wrong on its own, so
        ///     it is checked against a second reader - <see cref="RSCache.ReadFileBytes"/>, which
        ///     takes the file-at-a-time path rather than the group-at-a-time one the panel uses.
        ///     Sampled, because reading 42,256 files singly is the slow path this exists to avoid.
        /// </remarks>
        [RealCacheFact]
        public void EveryRowReportsTheStoredLength()
        {
            RSCache cache = _cache.OpenCache();
            var descriptor = new RawFileListDescriptor(RSConstants.INTERFACE_DEFINITIONS_INDEX, "interface file");

            int checkedRows = 0;

            foreach (RawFileListing row in Load(cache, descriptor).Where((_, i) => i % 500 == 0))
            {
                byte[] stored = cache.ReadFileBytes(RSConstants.INTERFACE_DEFINITIONS_INDEX, row.GroupId, row.FileId);
                Assert.Equal(stored.Length, row.SizeBytes);
                checkedRows++;
            }

            _output.WriteLine($"checked {checkedRows} sampled rows against a second reader");
            Assert.True(checkedRows > 0, "no row was sampled, so nothing was checked");
        }

        /// <summary>
        ///     The name hashes are the table's own, and absence is reported as absence.
        /// </summary>
        /// <remarks>
        ///     Index 3 sets the identifiers flag, so a hash column that came back empty everywhere
        ///     would mean the identifier block was being read into the wrong field - which is a real
        ///     failure mode here, the codec having a separate <c>hash</c> field that looks just as
        ///     plausible a home for it. Measured in the reference cache: 1377 of the 42,256 rows sit
        ///     in a group the table leaves unnamed, and 1721 are unnamed files, the format spelling
        ///     "no name" as -1 rather than omitting the entry.
        /// </remarks>
        [RealCacheFact]
        public void TheNameHashesComeFromTheTable()
        {
            RSCache cache = _cache.OpenCache();
            var descriptor = new RawFileListDescriptor(RSConstants.INTERFACE_DEFINITIONS_INDEX, "interface file");
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.INTERFACE_DEFINITIONS_INDEX);

            Assert.True(table.hasIdentifiers, "index 3 carries an identifier block, so the hashes have somewhere to come from");

            List<RawFileListing> rows = Load(cache, descriptor);
            int unnamedGroups = 0;
            int unnamedFiles = 0;

            foreach (RawFileListing row in rows)
            {
                RSArchiveEntry group = table.GetArchiveEntry(row.GroupId);
                Assert.Equal(group.GetIdentifier(), row.GroupNameHash);
                Assert.Equal(group.GetFileEntry(row.FileId).GetIdentifier(), row.FileNameHash);

                if (row.GroupNameHash == -1)
                    unnamedGroups++;
                if (row.FileNameHash == -1)
                    unnamedFiles++;
            }

            _output.WriteLine($"{unnamedGroups} rows in unnamed groups, {unnamedFiles} unnamed files");

            Assert.Equal(1377, unnamedGroups);
            Assert.Equal(1721, unnamedFiles);
        }

        /// <summary>
        ///     A raw listing never claims a definition id, and never offers to write.
        /// </summary>
        /// <remarks>
        ///     Both follow from the same fact: nothing has established how an index-3 id relates to a
        ///     group and a file, and nothing has established the record format either. A listing that
        ///     quietly invented either would be worse than the empty tab it replaced.
        /// </remarks>
        [RealCacheFact]
        public void TheListingClaimsNeitherAnIdNorAnEncoder()
        {
            RSCache cache = _cache.OpenCache();
            var descriptor = new RawFileListDescriptor(RSConstants.INTERFACE_DEFINITIONS_INDEX, "interface file");

            Assert.False(descriptor.IsEditable);
            Assert.All(descriptor.Enumerate(cache), address => Assert.False(address.HasDefinitionId));
        }

        /// <summary>Loads the whole listing the way the panel does: one read per group.</summary>
        private static List<RawFileListing> Load(RSCache cache, RawFileListDescriptor descriptor)
        {
            var rows = new List<RawFileListing>();

            foreach (IGrouping<int, DefinitionAddress> group in
                     descriptor.Enumerate(cache).GroupBy(address => address.GroupId))
            {
                IReadOnlyDictionary<int, JagStream> files = cache.ReadGroup(descriptor.IndexId, group.Key);

                foreach (DefinitionAddress address in group)
                    if (files.TryGetValue(address.FileId, out JagStream payload))
                        rows.Add(descriptor.Decode(cache, address, payload));
            }

            return rows;
        }
    }
}
