using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.IO;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache {
    /// <summary>
    ///     Covers <see cref="RSCache.CreateGroup"/>, which is the only way to add a group an index
    ///     did not already hold.
    /// </summary>
    /// <remarks>
    ///     <see cref="RSCache.WriteGroup"/> cannot do this and is not meant to. It reads the stored
    ///     container so it can carry across the files it was not asked to change and so it can
    ///     decline a write that changes nothing, and a group being created has neither - which is
    ///     why it used to fail here as a <see cref="FileNotFoundException"/> out of a container
    ///     lookup rather than as a refusal saying the idx file has no record for that group.
    ///     <para>
    ///     Most of what is asserted is read out of a <b>reopened</b> store. A read through the cache
    ///     that wrote it returns the staged bytes whether or not they were ever committed, so it
    ///     cannot tell a create that reached the file store from one that did not.
    ///     </para>
    ///     <para>
    ///     <b>The seeded group is stored uncompressed on purpose</b>, so an untouched neighbour can
    ///     be compared on its stored bytes rather than on a decompressed payload. A GZip re-encode
    ///     is never byte-identical, so the weaker comparison would pass over a neighbour that had
    ///     been rewritten.
    ///     </para>
    /// </remarks>
    public class RSCacheCreateGroupTests : IDisposable {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        /// <summary>The only content index the fixture seeds.</summary>
        private const int Index = 0;

        /// <summary>The group the fixture seeds, so the append slot is 1.</summary>
        private const int Existing = 0;

        /// <summary>The name the seeded group carries, for the collision case.</summary>
        private const string ExistingName = "existing";

        /// <summary>The reference table version seeded, so a bump away from it is visible.</summary>
        private const int SeededTableVersion = 7;

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public RSCacheCreateGroupTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-create-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose() {
            //Each store holds an exclusive handle on its dat2 and must be released before the temp
            //directory can be removed.
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        // ===================================================================
        //  Seeding
        // ===================================================================

        /// <summary>
        ///     Seeds a cache holding one named group, plus the table that declares it.
        /// </summary>
        /// <param name="tableFlags">The table's flags byte.</param>
        /// <returns>The cache.</returns>
        private RSCache CreateCache(int tableFlags = RSReferenceTable.FLAG_IDENTIFIERS) {
            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            foreach (int i in new[] { Index, RSConstants.META_INDEX })
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + i), Array.Empty<byte>());

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            store.Write(Index, Existing, StoredContainer(SeededPayload()));
            store.Write(RSConstants.META_INDEX, Index, EncodeReferenceTable(tableFlags));

            return new RSCache(store);
        }

        private static byte[] SeededPayload() {
            var archive = new RSArchive();
            archive.PutFile(0, new JagStream(new byte[] { 10, 20, 30 }));
            archive.PutFile(1, new JagStream(new byte[] { 40, 50 }));
            return archive.Encode().ToArray();
        }

        private static JagStream StoredContainer(byte[] payload) {
            return new RSContainer(Index, Existing, RSConstants.NO_COMPRESSION,
                new JagStream(payload), 1337).Encode();
        }

        /// <summary>
        ///     The table for the seeded group, carrying a CRC that genuinely describes the stored
        ///     container.
        /// </summary>
        /// <remarks>
        ///     Seeding the CRC honestly is what lets a group left alone be told from one that was
        ///     rewritten to the same contents: an entry seeded with a wrong CRC would look
        ///     rewritten either way.
        /// </remarks>
        private static JagStream EncodeReferenceTable(int tableFlags) {
            var table = new RSReferenceTable { format = 6, version = SeededTableVersion, flags = tableFlags };
            byte[] stored = StoredContainer(SeededPayload()).ToArray();

            var entry = new RSArchiveEntry(Existing);
            entry.SetVersion(1);
            entry.SetIdentifier(tableFlags == 0 ? RSGroupFile.Unnamed : NameHasher.GetNameHash(ExistingName));
            entry.SetCrc(unchecked((int) CRC32Helper.ComputeCrc32(stored.AsSpan(0, stored.Length - 2))));
            entry.SetValidFileIds(new[] { 0, 1 });

            var fileEntries = new SortedDictionary<int, RSFileEntry>();
            foreach (int fileId in new[] { 0, 1 })
                fileEntries[fileId] = new RSFileEntry(fileId);

            entry.SetFileEntries(fileEntries);
            table.PutArchiveEntry(Existing, entry);

            return new RSContainer(RSConstants.META_INDEX, Index, RSConstants.GZIP_COMPRESSION,
                ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        private static RSGroupFile[] OneFile(params byte[] data) {
            return new[] { new RSGroupFile(0, new JagStream(data)) };
        }

        // ===================================================================
        //  Reading what was staged
        // ===================================================================

        /// <summary>
        ///     Everything a create would touch: the seeded container, the table, both sets of index
        ///     records, and the length of the dat2 they live in.
        /// </summary>
        /// <remarks>
        ///     Compared rather than <c>HasUnsavedChanges</c>, which is already true from seeding. It
        ///     is the stronger statement anyway, because it catches a rewrite that produced the same
        ///     container bytes at a different sector.
        /// </remarks>
        private sealed class StoreSnapshot {
            public byte[] Archive = Array.Empty<byte>();
            public byte[] Table = Array.Empty<byte>();
            public byte[] IndexRecords = Array.Empty<byte>();
            public byte[] MetaIndexRecords = Array.Empty<byte>();
            public long DataLength;
        }

        private static StoreSnapshot Snapshot(RSCache cache) {
            return new StoreSnapshot {
                Archive = cache.LoadContainer(Index, Existing).ToArray(),
                Table = cache.LoadContainer(RSConstants.META_INDEX, Index).ToArray(),
                IndexRecords = cache.GetStore().GetIndexEntry(Index).GetStream().ToArray(),
                MetaIndexRecords = cache.GetStore().GetIndexEntry(RSConstants.META_INDEX).GetStream().ToArray(),
                DataLength = cache.GetStore().dataChannel.Length
            };
        }

        private static void AssertNothingWasWritten(StoreSnapshot before, RSCache cache) {
            StoreSnapshot after = Snapshot(cache);
            Assert.Equal(before.Archive, after.Archive);
            Assert.Equal(before.Table, after.Table);
            Assert.Equal(before.IndexRecords, after.IndexRecords);
            Assert.Equal(before.MetaIndexRecords, after.MetaIndexRecords);
            Assert.Equal(before.DataLength, after.DataLength);
        }

        /// <summary>
        ///     Commits the cache to a fresh directory and reopens it, so assertions run against
        ///     bytes that made a full round trip through the file store.
        /// </summary>
        private RSCache SaveAndReopen(RSCache cache) {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        // ===================================================================
        //  Creating
        // ===================================================================

        /// <summary>
        ///     The append slot is the id past the last idx record, not the id past the last declared
        ///     group.
        /// </summary>
        /// <remarks>
        ///     The two differ on any index carrying an undeclared group, which the repack does on
        ///     eight, so deriving the id from the table would pick one the store refuses.
        /// </remarks>
        [Fact]
        public void NextCreatableGroupId_IsOnePastTheLastIdxRecord() {
            RSCache cache = CreateCache();

            Assert.Equal(1, cache.NextCreatableGroupId(Index));
        }

        [Fact]
        public void CreateGroup_DeclaresTheGroupAndStoresItsFiles() {
            RSCache cache = CreateCache();
            int created = cache.NextCreatableGroupId(Index);

            Assert.True(cache.CreateGroup(Index, created, new[] {
                new RSGroupFile(0, new JagStream(new byte[] { 1, 2, 3 })),
                new RSGroupFile(1, new JagStream(new byte[] { 4 }))
            }, NameHasher.GetNameHash("brand_new")));

            RSCache reopened = SaveAndReopen(cache);

            RSArchiveEntry entry = reopened.GetReferenceTable(Index).GetArchiveEntry(created);
            Assert.NotNull(entry);
            Assert.Equal(new[] { 0, 1 }, entry.GetValidFileIds());

            IReadOnlyDictionary<int, JagStream> files = reopened.ReadGroup(Index, created);
            Assert.Equal(new byte[] { 1, 2, 3 }, files[0].ToArray());
            Assert.Equal(new byte[] { 4 }, files[1].ToArray());
        }

        /// <summary>
        ///     The idx file has to grow by exactly one record.
        /// </summary>
        /// <remarks>
        ///     A record is six bytes at a fixed offset, so growing by anything else means every
        ///     group after the new one is addressed at the wrong offset - which reads as a cache
        ///     full of corrupt containers rather than as a bad create.
        /// </remarks>
        [Fact]
        public void CreateGroup_GrowsTheIndexByExactlyOneRecord() {
            RSCache cache = CreateCache();
            int before = cache.NextCreatableGroupId(Index);

            cache.CreateGroup(Index, before, OneFile(9));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(before + 1, reopened.NextCreatableGroupId(Index));
        }

        [Fact]
        public void CreateGroup_CarriesTheGroupNameThroughAReopen() {
            RSCache cache = CreateCache();
            int created = cache.NextCreatableGroupId(Index);
            int hash = NameHasher.GetNameHash("brand_new");

            cache.CreateGroup(Index, created, OneFile(9), hash);

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(hash, reopened.GetReferenceTable(Index).GetArchiveEntry(created).GetIdentifier());
            Assert.Equal(created, reopened.GetReferenceTable(Index).GetArchiveId("brand_new"));
        }

        /// <summary>
        ///     A group with no name is normal and must survive as one.
        /// </summary>
        /// <remarks>
        ///     -1 rather than 0, because 0 is the hash of the empty string and would be indexed as a
        ///     real name. Eleven of index 3's groups carry -1.
        /// </remarks>
        [Fact]
        public void CreateGroup_WithNoName_StoresTheUnnamedSentinel() {
            RSCache cache = CreateCache();
            int created = cache.NextCreatableGroupId(Index);

            cache.CreateGroup(Index, created, OneFile(9));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(RSGroupFile.Unnamed,
                reopened.GetReferenceTable(Index).GetArchiveEntry(created).GetIdentifier());
        }

        /// <summary>
        ///     Creating a group must not disturb the groups already in the index.
        /// </summary>
        /// <remarks>
        ///     Compared on the stored container bytes rather than on the decoded files, because a
        ///     neighbour that was re-encoded to the same contents would pass the weaker comparison
        ///     while having moved in the dat2 and changed its CRC.
        /// </remarks>
        [Fact]
        public void CreateGroup_LeavesTheExistingGroupsStoredBytesAlone() {
            RSCache cache = CreateCache();
            byte[] before = cache.LoadContainer(Index, Existing).ToArray();
            int crcBefore = cache.GetReferenceTable(Index).GetArchiveEntry(Existing).GetCrc();

            cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(9));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(before, reopened.LoadContainer(Index, Existing).ToArray());
            Assert.Equal(crcBefore, reopened.GetReferenceTable(Index).GetArchiveEntry(Existing).GetCrc());
        }

        /// <summary>
        ///     The container's version trailer and the table entry's version must agree.
        /// </summary>
        /// <remarks>
        ///     Every group on disk in both supported caches satisfies this, so a created group that
        ///     did not would be the only one in the cache that disagreed with itself. It holds only
        ///     because the container is seeded at 1 to meet the entry's 0 + 1 - the container is
        ///     encoded before the entry version is bumped.
        /// </remarks>
        [Fact]
        public void CreateGroup_LeavesTheContainerTrailerAgreeingWithTheEntryVersion() {
            RSCache cache = CreateCache();
            int created = cache.NextCreatableGroupId(Index);

            cache.CreateGroup(Index, created, OneFile(9));

            RSCache reopened = SaveAndReopen(cache);
            byte[] stored = reopened.LoadContainer(Index, created).ToArray();
            int trailer = (stored[stored.Length - 2] << 8) | stored[stored.Length - 1];

            Assert.Equal(1, reopened.GetReferenceTable(Index).GetArchiveEntry(created).GetVersion());
            Assert.Equal(1, trailer);
        }

        /// <summary>
        ///     Everything that answers a question about the table has to answer it the same way
        ///     immediately after a create.
        /// </summary>
        /// <remarks>
        ///     Asserted together rather than in three tests, because the failure being guarded
        ///     against is exactly that one of them was refreshed and another was not: the count and
        ///     both name lookups were three separate caches over one dictionary, and two of them
        ///     were only ever populated at decode.
        /// </remarks>
        [Fact]
        public void CreateGroup_LeavesTheCountAndBothNameLookupsAgreeingInTheSameSession() {
            RSCache cache = CreateCache();
            RSReferenceTable table = cache.GetReferenceTable(Index);
            int created = cache.NextCreatableGroupId(Index);

            //Read before the create so every lookup is already built and memoised.
            Assert.Equal(1, table.validArchivesCount);
            Assert.Equal(-1, table.GetArchiveId("brand_new"));
            Assert.Equal(-1, table.Names.GroupId("brand_new"));

            cache.CreateGroup(Index, created, OneFile(9), NameHasher.GetNameHash("brand_new"));

            Assert.Equal(2, table.validArchivesCount);
            Assert.Equal(new[] { Existing, created }, table.validArchiveIds);
            Assert.Equal(created, table.GetArchiveId("brand_new"));
            Assert.Equal(created, table.Names.GroupId("brand_new"));
        }

        // ===================================================================
        //  Refusals
        // ===================================================================

        [Fact]
        public void CreateGroup_AtAnIdTheTableAlreadyDeclares_IsRefused() {
            RSCache cache = CreateCache();
            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => cache.CreateGroup(Index, Existing, OneFile(9)));

            Assert.Contains("already declares group", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     A group the idx holds and the table omits stays out of the table.
        /// </summary>
        /// <remarks>
        ///     This is the repack residue rule, and the create path has to enforce it for the same
        ///     reason the rewrite path does: adopting one would promote bytes nobody declared into
        ///     the table as though the editor had made them. Checked before the append-slot rule so
        ///     the message names the real problem - an orphan is never at the append slot, so the
        ///     other refusal would fire first and say something true but useless.
        /// </remarks>
        [Fact]
        public void CreateGroup_AtAnIdHoldingUndeclaredBytes_IsRefusedRatherThanAdopted() {
            RSCache cache = CreateCache();

            //Straight into the store, so the table never learns about it.
            cache.GetStore().Write(Index, 1, new RSContainer(Index, 1, RSConstants.NO_COMPRESSION,
                new JagStream(new byte[] { 7, 7, 7 }), 1337).Encode());

            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => cache.CreateGroup(Index, 1, OneFile(9)));

            Assert.Contains("does not declare", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        [Fact]
        public void CreateGroup_PastTheAppendSlot_IsRefusedAndNamesTheIdThatWouldWork() {
            RSCache cache = CreateCache();
            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<ArgumentOutOfRangeException>(
                () => cache.CreateGroup(Index, 40, OneFile(9)));

            Assert.Contains("the only group id it can accept is 1", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        [Fact]
        public void CreateGroup_WithNoFiles_IsRefused() {
            RSCache cache = CreateCache();
            StoreSnapshot before = Snapshot(cache);

            Assert.Throws<ArgumentException>(
                () => cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), Array.Empty<RSGroupFile>()));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     A name on a table with no identifiers block is refused rather than dropped.
        /// </summary>
        /// <remarks>
        ///     The encoder writes the block only when the flag is set, so the name would otherwise
        ///     vanish on the way out with nothing anywhere reporting it - and the group would then
        ///     be unnameable for good, because index 3 recovers names by re-hashing candidates
        ///     rather than by storing them.
        /// </remarks>
        [Fact]
        public void CreateGroup_NamingAGroupOnATableThatCannotStoreNames_IsRefused() {
            RSCache cache = CreateCache(tableFlags: 0);
            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<ArgumentException>(
                () => cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(9),
                    NameHasher.GetNameHash("brand_new")));

            Assert.Contains("no identifiers flag", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     An unnamed group on such a table is fine, because -1 is the absence of a name.
        /// </summary>
        [Fact]
        public void CreateGroup_UnnamedOnATableThatCannotStoreNames_IsAllowed() {
            RSCache cache = CreateCache(tableFlags: 0);

            Assert.True(cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(9)));
        }

        [Fact]
        public void CreateGroup_WithANameAnotherGroupAlreadyCarries_IsRefused() {
            RSCache cache = CreateCache();
            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(9),
                    NameHasher.GetNameHash(ExistingName)));

            Assert.Contains("already carries the name hash", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     Two unnamed groups do not collide, because they share the absence of a name.
        /// </summary>
        [Fact]
        public void CreateGroup_UnnamedBesideAnotherUnnamedGroup_IsAllowed() {
            RSCache cache = CreateCache();

            Assert.True(cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(9)));
            Assert.True(cache.CreateGroup(Index, cache.NextCreatableGroupId(Index), OneFile(8)));
        }

        [Fact]
        public void CreateGroup_OnTheMetaIndex_IsRefused() {
            RSCache cache = CreateCache();

            Assert.Throws<IOException>(() => cache.CreateGroup(RSConstants.META_INDEX, 9, OneFile(9)));
        }

        // ===================================================================
        //  What WriteGroup still refuses
        // ===================================================================

        /// <summary>
        ///     A rewrite of a group that exists nowhere is refused, and says which operation would
        ///     have worked.
        /// </summary>
        /// <remarks>
        ///     It used to escape as <c>FileNotFoundException("Could not find container for index
        ///     0")</c> from a container lookup, which names neither the group nor the reason and
        ///     reads as a corrupt cache rather than as the wrong call.
        /// </remarks>
        [Fact]
        public void WriteGroup_ForAGroupThatIsNeitherDeclaredNorStored_IsRefusedAndNamesCreateGroup() {
            RSCache cache = CreateCache();
            StoreSnapshot before = Snapshot(cache);

            var thrown = Assert.Throws<InvalidOperationException>(
                () => cache.WriteGroup(Index, 1, OneFile(9)));

            Assert.Contains("CreateGroup", thrown.Message);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     Renaming a group is a change, even though nothing else about it moves.
        /// </summary>
        /// <remarks>
        ///     The no-op guard compares the file ids, the per-file names and the payload, and a
        ///     rename moves none of them. Without the group's own name in that comparison the write
        ///     is discarded as changing nothing and the group silently keeps its old name.
        /// </remarks>
        [Fact]
        public void WriteGroup_RenamingAGroupAndNothingElse_IsNotDiscardedAsANoOp() {
            RSCache cache = CreateCache();
            int renamed = NameHasher.GetNameHash("renamed");

            RSGroupFile[] asStored = cache.ReadGroup(Index, Existing)
                .OrderBy(pair => pair.Key)
                .Select(pair => {
                    pair.Value.Seek0();
                    return new RSGroupFile(pair.Key, new JagStream(pair.Value.ToArray()));
                })
                .ToArray();

            //The same contents, so only the name can be what makes this a write.
            Assert.False(cache.WriteGroup(Index, Existing, asStored));
            Assert.True(cache.WriteGroup(Index, Existing, asStored, renamed));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(renamed, reopened.GetReferenceTable(Index).GetArchiveEntry(Existing).GetIdentifier());
        }
    }
}
