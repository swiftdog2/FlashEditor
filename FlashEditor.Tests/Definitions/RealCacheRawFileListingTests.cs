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
    ///     <see cref="RawFileListDescriptor"/>, checked against the cache it claims to describe.
    /// </summary>
    /// <remarks>
    ///     A raw listing is deliberately not a decode: it reports what the reference table addresses
    ///     and how long each stored file is, which is a claim about the cache and is checkable. That
    ///     is what makes it an honest deliverable for an index whose record format nobody has
    ///     established yet.
    ///     <para>
    ///     Index 3 is the subject here for continuity rather than because it still needs one - its
    ///     format <i>has</i> since been reverse engineered and the Interfaces tab now shows a decoded
    ///     component list instead. The descriptor kept its tests because it is the reusable answer for
    ///     the next such index, and index 3 is the only one it has ever been measured against; the
    ///     figures below would all have to be re-measured to move it.
    ///     </para>
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
        ///     count. A count agrees with plenty of wrong enumerations, and in the repack index 3's
        ///     idx additionally holds 772, 825 and 891 that the table never declares, so only the
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
            Assert.Equal(_cache.DeclaredFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX), listed.Count);
            Assert.True(listed.Count > 0, "the listing addressed nothing, so nothing was checked");
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
        ///     plausible a home for it. That is what the "at least one real name hash" assertion
        ///     below catches, and it is stated separately from the unnamed counts because those do
        ///     not transfer between caches: the repack leaves 1377 of its 42,256 rows in an unnamed
        ///     group and 1721 files unnamed, while every group and file of the vanilla capture's
        ///     index 3 is named, so a count of zero there would otherwise read as the block being
        ///     missed entirely.
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
            int namedFiles = 0;

            foreach (RawFileListing row in rows)
            {
                RSArchiveEntry group = table.GetArchiveEntry(row.GroupId);
                Assert.Equal(group.GetIdentifier(), row.GroupNameHash);
                Assert.Equal(group.GetFileEntry(row.FileId).GetIdentifier(), row.FileNameHash);

                if (row.GroupNameHash == -1)
                    unnamedGroups++;
                if (row.FileNameHash == -1)
                    unnamedFiles++;
                else
                    namedFiles++;
            }

            _output.WriteLine($"{rows.Count} rows, {unnamedGroups} in unnamed groups, " +
                              $"{unnamedFiles} unnamed files, {namedFiles} named files");

            //The failure this exists to catch is the identifier block landing in the wrong field,
            //which shows up as every hash coming back absent. A count of unnamed rows cannot say
            //that on its own, because a cache where nothing is unnamed would report zero either
            //way; what settles it is that real names came through.
            Assert.True(rows.Count > 0, "the listing produced no rows, so nothing was checked");
            Assert.True(namedFiles > 0,
                "every index-3 file reports no name, so the identifier block is being read into " +
                "the wrong field rather than into the file name hash");

            _cache.Profile.AssertCensus(_output, "interface.rowsInUnnamedGroups", unnamedGroups);
            _cache.Profile.AssertCensus(_output, "interface.unnamedFiles", unnamedFiles);
        }

        /// <summary>
        ///     A raw listing never offers to write, and its ids are the client's own fold.
        /// </summary>
        /// <remarks>
        ///     Not offering to write is the standing property of a raw listing: it reports addresses
        ///     and stored lengths and has no encoder, so <see cref="RawFileListDescriptor"/> can be
        ///     pointed at any index without risking a save built on a format nobody has established.
        ///     <para>
        ///     This test asserted the opposite of the id half until index 3's split was settled from
        ///     the client. It required <c>HasDefinitionId</c> to be false everywhere, standing in for
        ///     "nothing has established how an index-3 id relates to a group and a file". That is no
        ///     longer true and the assertion had to move rather than be dropped:
        ///     <c>EntityEnumType.java:46</c> builds a component id as
        ///     <c>ID_TAG = (parent &lt;&lt; 16) + childIndex</c> and <c>Class247.java:413-414</c>
        ///     takes it apart again as <c>stack &gt;&gt; 16</c> and <c>stack &amp; 0xFFFF</c>, so the
        ///     fold is stated in both directions. Checking every row against that arithmetic is a
        ///     stronger claim than the absence it replaced, and it is what would catch a page size
        ///     changed to 8 or 256 - which no byte-identity sweep would notice, the id never being
        ///     stored.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheListingOffersNoEncoderAndFoldsIdsTheWayTheClientDoes()
        {
            RSCache cache = _cache.OpenCache();
            var descriptor = new RawFileListDescriptor(RSConstants.INTERFACE_DEFINITIONS_INDEX, "interface file");

            Assert.False(descriptor.IsEditable);

            int rows = 0;

            foreach (DefinitionAddress address in descriptor.Enumerate(cache))
            {
                Assert.True(address.HasDefinitionId);
                Assert.Equal((address.GroupId << 16) | address.FileId, address.DefinitionId);
                rows++;
            }

            Assert.True(rows > 0, "the listing enumerated nothing, so the fold was never checked");
            Assert.Equal(_cache.DeclaredFiles(RSConstants.INTERFACE_DEFINITIONS_INDEX), rows);
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
