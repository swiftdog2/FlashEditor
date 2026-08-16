using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.IO;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache {
    /// <summary>
    ///     Covers <see cref="RSCache.WriteGroup"/>, which is the only way to change WHICH files a
    ///     group holds.
    /// </summary>
    /// <remarks>
    ///     <see cref="RSCache.WriteFile"/> can replace a payload and add a file and has no way at
    ///     all to remove one, so a renumbering could be planned correctly and never applied. The
    ///     thing a group rewrite changes that a file write does not is the reference table's file
    ///     count and its delta-encoded per-file id list, which is why most of what is asserted here
    ///     is read back out of a reopened store rather than off the cache that wrote it - a read
    ///     through the writing cache returns the staged bytes whether or not they were committed.
    ///     <para>
    ///     <b>The fixture stores uncompressed on purpose.</b> A GZip re-encode is never
    ///     byte-identical, so an edit and its inverse could only ever be compared as decompressed
    ///     payloads there. With no compression the stored container is deterministic and the round
    ///     trip can be asserted on the stored bytes themselves, which is the stronger claim.
    ///     </para>
    /// </remarks>
    public class RSCacheWriteGroupTests : IDisposable {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        /// <summary>The only content index the fixture seeds.</summary>
        private const int Index = 0;

        /// <summary>The group every test rewrites.</summary>
        private const int Group = 0;

        /// <summary>The reference table version seeded, so a bump away from it is visible.</summary>
        private const int SeededTableVersion = 7;

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public RSCacheWriteGroupTests() {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-group-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>One seeded file: its id, its bytes and the name hash the table records for it.</summary>
        private readonly record struct Seed(int FileId, byte[] Data, int Identifier) {
            internal Seed(int fileId, byte[] data) : this(fileId, data, RSGroupFile.Unnamed) {
            }
        }

        /// <summary>
        ///     Seeds a cache holding one group, plus the reference table that declares it.
        /// </summary>
        /// <remarks>
        ///     The payload is placed rather than built where a test needs a multi-chunk layout,
        ///     because the chunk split survives only a decode - an archive assembled through
        ///     <see cref="RSArchive.PutFile"/> is always single-chunk.
        /// </remarks>
        private RSCache CreateCache(int tableFlags, byte[] payload, Seed[] files) {
            //Sector 0 is burned: allocation derives the next free sector from the data length, and
            //sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            foreach (int i in new[] { Index, RSConstants.META_INDEX })
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + i), Array.Empty<byte>());

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            store.Write(Index, Group, StoredContainer(payload));
            store.Write(RSConstants.META_INDEX, Index, EncodeReferenceTable(tableFlags, payload, files));

            return new RSCache(store);
        }

        private RSCache CreateCache(int tableFlags, params Seed[] files) {
            var archive = new RSArchive();
            foreach (Seed file in files)
                archive.PutFile(file.FileId, new JagStream(file.Data));

            return CreateCache(tableFlags, archive.Encode().ToArray(), files);
        }

        private static JagStream StoredContainer(byte[] payload) {
            return new RSContainer(Index, Group, RSConstants.NO_COMPRESSION, new JagStream(payload), 1337).Encode();
        }

        /// <summary>
        ///     The reference table for the seeded group, carrying a CRC that genuinely describes the
        ///     stored container.
        /// </summary>
        /// <remarks>
        ///     Seeding the CRC honestly is what lets a no-op be told from a recompute: an entry
        ///     seeded with a wrong CRC would look rewritten either way. An empty file list seeds a
        ///     table naming no group at all, which is how the undeclared-group case is expressed.
        /// </remarks>
        private static JagStream EncodeReferenceTable(int tableFlags, byte[] payload, Seed[] files) {
            var table = new RSReferenceTable { format = 6, version = SeededTableVersion, flags = tableFlags };

            if (files.Length > 0) {
                byte[] stored = StoredContainer(payload).ToArray();

                var entry = new RSArchiveEntry(Group);
                entry.SetVersion(1);
                entry.SetCrc(unchecked((int) FlashEditor.Cache.Util.CRC32Helper.ComputeCrc32(
                    stored.AsSpan(0, stored.Length - 2))));
                entry.SetValidFileIds(files.Select(f => f.FileId).ToArray());

                var fileEntries = new SortedDictionary<int, RSFileEntry>();
                foreach (Seed file in files) {
                    var child = new RSFileEntry(file.FileId);
                    child.SetIdentifier(file.Identifier);
                    fileEntries[file.FileId] = child;
                }

                entry.SetFileEntries(fileEntries);
                table.PutArchiveEntry(Group, entry);
            }

            return new RSContainer(RSConstants.META_INDEX, Index, RSConstants.GZIP_COMPRESSION,
                ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        // ===================================================================
        //  Reading what was staged
        // ===================================================================

        /// <summary>
        ///     Everything a write touches: the two stored containers, the index records pointing at
        ///     them, and the length of the dat2 they live in.
        /// </summary>
        /// <remarks>
        ///     Comparing what is staged rather than <c>HasUnsavedChanges</c>, for the reason the
        ///     unchanged-save fixture gives: seeding goes through the same staging writes, so the
        ///     store is already dirty before a test line runs. It is the stronger statement anyway,
        ///     because it catches a rewrite that produced the same container bytes and reallocated
        ///     its sectors - which still moves the dat2 under every archive after it.
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
                Archive = cache.LoadContainer(Index, Group).ToArray(),
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

        private static RSGroupFile[] Files(params Seed[] files) {
            return files.Select(f => new RSGroupFile(f.FileId, new JagStream(f.Data), f.Identifier)).ToArray();
        }

        private static int[] DeclaredIds(RSCache cache) {
            return cache.GetReferenceTable(Index).GetArchiveEntry(Group).GetValidFileIds();
        }

        // ===================================================================
        //  A rewrite that changes nothing
        // ===================================================================

        /// <summary>
        ///     The invariant a group rewrite is most likely to break, and the one that matters
        ///     most: restating a group exactly as it is stores nothing at all.
        /// </summary>
        /// <remarks>
        ///     It has to be decided before the container is re-encoded. Re-encoding rewrites the
        ///     archive CRC, which rewrites the entry that carries it, which rewrites the table
        ///     container every other entry in the index shares - so there is no detecting it
        ///     afterwards and putting it back.
        /// </remarks>
        [Fact]
        public void WriteGroup_RestatingTheStoredGroup_StagesNothing() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 }));

            StoreSnapshot before = Snapshot(cache);

            bool staged = cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 })));

            Assert.False(staged);
            Assert.Equal(SeededTableVersion, cache.GetReferenceTable(Index).version);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     Restating a group whose payload is stored across several chunks is a no-op too. The
        ///     split is part of the byte layout - the payload is chunk-major - so a rewrite that
        ///     dropped it would produce the same length with the bytes in a different order, which
        ///     is a changed archive and a changed CRC for an edit that changed nothing.
        /// </summary>
        [Fact]
        public void WriteGroup_RestatingAMultiChunkGroup_StagesNothing() {
            RSCache cache = CreateCache(0, MultiChunkPayload(),
                new[] { new Seed(0, new byte[] { 1, 2, 3, 4 }), new Seed(1, new byte[] { 5, 6 }) });

            //The seeded layout really is chunk-major, or this test proves nothing
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, cache.ReadFile(Index, Group, 0).ToArray());
            Assert.Equal(new byte[] { 5, 6 }, cache.ReadFile(Index, Group, 1).ToArray());

            StoreSnapshot before = Snapshot(cache);

            bool staged = cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3, 4 }),
                new Seed(1, new byte[] { 5, 6 })));

            Assert.False(staged);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     Restating the identifiers a table already carries is free as well. Index 3 names both
        ///     levels, so a rewrite that had to re-state every name in order to keep them would make
        ///     every structural edit unconditional.
        /// </summary>
        [Fact]
        public void WriteGroup_RestatingTheIdentifiers_StagesNothing() {
            RSCache cache = CreateCache(RSReferenceTable.FLAG_IDENTIFIERS,
                new Seed(0, new byte[] { 1, 2, 3 }, 111),
                new Seed(1, new byte[] { 4, 5 }, 222));

            StoreSnapshot before = Snapshot(cache);

            bool staged = cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }, 111),
                new Seed(1, new byte[] { 4, 5 }, 222)));

            Assert.False(staged);
            AssertNothingWasWritten(before, cache);
        }

        // ===================================================================
        //  A rewrite that changes something
        // ===================================================================

        /// <summary>
        ///     <b>The case a payload comparison alone gets wrong.</b> File ids appear nowhere in a
        ///     group payload, so renumbering the same three files from ids 0, 1, 3 to 0, 1, 2
        ///     produces byte-identical bytes and an entirely different reference-table entry.
        ///     Comparing the payload alone would report it as a no-op and discard the edit.
        /// </summary>
        [Fact]
        public void WriteGroup_RenumberingWithoutChangingAByte_IsStillWritten() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(3, new byte[] { 6 }));

            byte[] payloadBefore = cache.LoadContainer(Index, Group).ToArray();

            bool staged = cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 })));

            Assert.True(staged);

            //The bytes really were identical, which is what makes the id list the only difference
            Assert.Equal(payloadBefore, cache.LoadContainer(Index, Group).ToArray());

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 0, 1, 2 }, DeclaredIds(reopened));
            Assert.Equal(new byte[] { 6 }, reopened.ReadFile(Index, Group, 2).ToArray());
        }

        /// <summary>
        ///     A deletion has to leave the group and the table agreeing. A file entry left behind
        ///     would go on declaring a file the payload no longer holds, and the next decode would
        ///     read the size table against the wrong file count.
        /// </summary>
        [Fact]
        public void WriteGroup_RemovingAFile_TakesItOutOfTheTableAsWellAsThePayload() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 }));

            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 6 }))));

            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 0, 1 }, DeclaredIds(reopened));
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(Index, Group, 0).ToArray());
            Assert.Equal(new byte[] { 6 }, reopened.ReadFile(Index, Group, 1).ToArray());
            Assert.Throws<FileNotFoundException>(() => reopened.ReadFile(Index, Group, 2));
        }

        /// <summary>
        ///     Deleting the last file of a group down to one leaves a single-file archive, which has
        ///     no trailer at all - no size table and no chunk-count byte. Writing one would hand
        ///     that byte back as file data on the next read.
        /// </summary>
        [Fact]
        public void WriteGroup_DownToOneFile_WritesNoTrailer() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }));

            Assert.True(cache.WriteGroup(Index, Group, Files(new Seed(0, new byte[] { 1, 2, 3 }))));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 0 }, DeclaredIds(reopened));
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(Index, Group, 0).ToArray());
        }

        /// <summary>
        ///     Inserting is the other half. The id list has to grow and the new file has to be
        ///     readable at the id it was given, not at the position it happens to occupy.
        /// </summary>
        [Fact]
        public void WriteGroup_InsertingAFile_DeclaresItAndStoresIt() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }));

            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 9, 9 }),
                new Seed(2, new byte[] { 4, 5 }))));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 0, 1, 2 }, DeclaredIds(reopened));
            Assert.Equal(new byte[] { 9, 9 }, reopened.ReadFile(Index, Group, 1).ToArray());
            Assert.Equal(new byte[] { 4, 5 }, reopened.ReadFile(Index, Group, 2).ToArray());
        }

        /// <summary>
        ///     Moving a name is a change even when nothing else is. A renumbering that left the
        ///     identifiers where they were would rename every component it moved, silently.
        /// </summary>
        [Fact]
        public void WriteGroup_MovingAnIdentifier_IsNotMistakenForANoOp() {
            RSCache cache = CreateCache(RSReferenceTable.FLAG_IDENTIFIERS,
                new Seed(0, new byte[] { 1 }, 111),
                new Seed(1, new byte[] { 1 }, 222));

            //The two payloads are identical, so the identifiers are the ONLY difference
            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1 }, 222),
                new Seed(1, new byte[] { 1 }, 111))));

            RSCache reopened = SaveAndReopen(cache);
            RSArchiveEntry entry = reopened.GetReferenceTable(Index).GetArchiveEntry(Group);
            Assert.Equal(222, entry.GetFileEntry(0).GetIdentifier());
            Assert.Equal(111, entry.GetFileEntry(1).GetIdentifier());
        }

        // ===================================================================
        //  Set and unset
        // ===================================================================

        /// <summary>
        ///     A structural edit and its inverse land on the bytes that were there to begin with.
        /// </summary>
        /// <remarks>
        ///     Asserted on the stored container rather than on the decoded files, which the
        ///     uncompressed fixture makes possible - a GZip re-encode would differ whatever the
        ///     payload, so nothing about a round trip could be claimed from it.
        ///     <para>
        ///     The archive VERSION is expected to have advanced by two, and that is not a defect
        ///     being tolerated: it is a counter the JS5 update protocol compares against what a
        ///     client already holds, and this group really was written twice. A version that went
        ///     back to where it started would tell a client its stale copy is current.
        ///     </para>
        /// </remarks>
        [Fact]
        public void WriteGroup_AnEditAndItsInverse_LandOnTheOriginalStoredBytes() {
            RSCache cache = CreateCache(RSReferenceTable.FLAG_IDENTIFIERS,
                new Seed(0, new byte[] { 1, 2, 3 }, 111),
                new Seed(1, new byte[] { 4, 5 }, 222),
                new Seed(2, new byte[] { 6 }, 333));

            byte[] storedBefore = cache.LoadContainer(Index, Group).ToArray();
            int versionBefore = cache.GetReferenceTable(Index).GetArchiveEntry(Group).GetVersion();
            int crcBefore = cache.GetReferenceTable(Index).GetArchiveEntry(Group).GetCrc();

            //Delete the middle component, which renumbers the one after it
            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }, 111),
                new Seed(1, new byte[] { 6 }, 333))));

            Assert.NotEqual(storedBefore, cache.LoadContainer(Index, Group).ToArray());

            //And put it back
            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }, 111),
                new Seed(1, new byte[] { 4, 5 }, 222),
                new Seed(2, new byte[] { 6 }, 333))));

            RSCache reopened = SaveAndReopen(cache);
            RSArchiveEntry entry = reopened.GetReferenceTable(Index).GetArchiveEntry(Group);

            Assert.Equal(storedBefore, reopened.LoadContainer(Index, Group).ToArray());
            Assert.Equal(new[] { 0, 1, 2 }, entry.GetValidFileIds());
            Assert.Equal(new[] { 111, 222, 333 },
                entry.GetFileEntries().Values.Select(f => f.GetIdentifier()).ToArray());
            Assert.Equal(crcBefore, entry.GetCrc());
            Assert.Equal(versionBefore + 2, entry.GetVersion());
        }

        /// <summary>
        ///     And once it is back, restating it is a no-op again - so the baseline the unchanged
        ///     path measures against followed the writes rather than being fixed at the bytes the
        ///     session opened with.
        /// </summary>
        [Fact]
        public void WriteGroup_AfterAnEditAndItsInverse_RestatingIsStillANoOp() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 }));

            cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 6 })));

            cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 })));

            StoreSnapshot before = Snapshot(cache);

            bool staged = cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, new byte[] { 6 })));

            Assert.False(staged);
            AssertNothingWasWritten(before, cache);
        }

        // ===================================================================
        //  What it refuses
        // ===================================================================

        /// <summary>
        ///     A group with bytes in the idx file and no entry in the reference table is repack
        ///     residue: the client resolves every group through the table, so it can never load one.
        ///     A rewrite that created the entry would promote it into the cache as though the editor
        ///     had made it.
        /// </summary>
        /// <remarks>
        ///     Deliberately unlike <see cref="RSCache.WriteFile"/>, which does write an undeclared
        ///     archive - there the missing thing is the entry for an archive the caller is editing
        ///     on purpose. Here the whole file set is being restated, and adopting a group nobody
        ///     named is a different change from the one asked for.
        /// </remarks>
        [Fact]
        public void WriteGroup_UndeclaredGroup_IsRefusedRatherThanAdopted() {
            var archive = new RSArchive();
            archive.PutFile(0, new JagStream(new byte[] { 1, 2, 3 }));

            RSCache cache = CreateCache(0, archive.Encode().ToArray(), Array.Empty<Seed>());
            Assert.Null(cache.GetReferenceTable(Index).GetArchiveEntry(Group));

            StoreSnapshot before = Snapshot(cache);

            Assert.Throws<InvalidOperationException>(() =>
                cache.WriteGroup(Index, Group, Files(new Seed(0, new byte[] { 1, 2, 3 }))));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     Rewriting a declared group leaves an undeclared one beside it exactly where it was.
        /// </summary>
        /// <remarks>
        ///     The other half of the orphan rule, and the half that was asserted only in a comment.
        ///     Refusing to <i>adopt</i> one is easy to see; not <i>destroying</i> one rests on a
        ///     claim about sector allocation - that it only ever appends or reuses what this
        ///     session freed, so an orphan's chain is not reachable from a write to a different
        ///     group. That is exactly the kind of claim that stops being true when the allocator is
        ///     touched, and nothing else in the suite would notice.
        ///     <para>
        ///     Read back through a reopened store rather than through the writing cache, so the
        ///     orphan is proved to have survived the commit as well as the write. Its bytes are
        ///     compared, not merely its readability: a chain that was partly overwritten and still
        ///     terminates would read back short rather than throw.
        ///     </para>
        /// </remarks>
        [Fact]
        public void WriteGroup_DoesNotDisturbAnUndeclaredGroupBesideIt() {
            RSCache cache = CreateCache(0,
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }));

            //Written straight to the store, so no reference-table entry describes it. That is what
            //makes it an orphan rather than content.
            var orphanArchive = new RSArchive();
            orphanArchive.PutFile(0, new JagStream(new byte[] { 7, 7, 7, 7, 7, 7, 7, 7 }));

            JagStream orphan = new RSContainer(Index, Group + 1, RSConstants.NO_COMPRESSION,
                new JagStream(orphanArchive.Encode().ToArray()), 1337).Encode();
            byte[] orphanBytes = orphan.ToArray();

            cache.GetStore().Write(Index, Group + 1, new JagStream(orphanBytes));
            Assert.Equal(new[] { Group + 1 }, cache.EnumerateOrphanGroups(Index));

            /* Big enough to push the rewritten group over several sectors, and that size is the
               whole point of the case. The orphan was allocated immediately after the group, so a
               growth of ten bytes would still fit inside the sector the group already held and
               this test would prove nothing at all - it would pass against an allocator that
               extends in place and eats whatever is next. */
            var grown = new byte[4 * SectorSize];
            Array.Fill(grown, (byte) 6);

            Assert.True(cache.WriteGroup(Index, Group, Files(
                new Seed(0, new byte[] { 1, 2, 3 }),
                new Seed(1, new byte[] { 4, 5 }),
                new Seed(2, grown))));

            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(orphanBytes, reopened.LoadContainer(Index, Group + 1).ToArray());
            Assert.Equal(new[] { Group + 1 }, reopened.EnumerateOrphanGroups(Index));
        }

        /// <summary>
        ///     A group with no files has no payload to store and cannot be addressed by the client,
        ///     so deleting a group's last component is refused here rather than producing an archive
        ///     the file store rejects with a message naming neither the group nor the reason.
        /// </summary>
        [Fact]
        public void WriteGroup_NoFilesAtAll_IsRefused() {
            RSCache cache = CreateCache(0, new Seed(0, new byte[] { 1, 2, 3 }));

            StoreSnapshot before = Snapshot(cache);

            Assert.Throws<ArgumentException>(() =>
                cache.WriteGroup(Index, Group, Array.Empty<RSGroupFile>()));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        ///     The table delta-encodes file ids as unsigned shorts, so an id that does not follow
        ///     the one before it encodes as zero or as a wrapped negative and reads back as a
        ///     different file entirely.
        /// </summary>
        [Theory]
        [InlineData(2, 1)]
        [InlineData(1, 1)]
        public void WriteGroup_IdsThatDoNotAscend_AreRefused(int first, int second) {
            RSCache cache = CreateCache(0, new Seed(0, new byte[] { 1, 2, 3 }));

            StoreSnapshot before = Snapshot(cache);

            Assert.Throws<ArgumentException>(() => cache.WriteGroup(Index, Group, Files(
                new Seed(first, new byte[] { 1 }),
                new Seed(second, new byte[] { 2 }))));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>Reference tables are the file store's to write, never a group rewrite's.</summary>
        [Fact]
        public void WriteGroup_MetaIndex_IsRefused() {
            RSCache cache = CreateCache(0, new Seed(0, new byte[] { 1, 2, 3 }));

            Assert.Throws<IOException>(() => cache.WriteGroup(RSConstants.META_INDEX, Index,
                Files(new Seed(0, new byte[] { 1 }))));
        }

        /// <summary>
        ///     Two files spread over two chunks, written the way the client stores them: chunk 0 of
        ///     every file, then chunk 1 of every file, then the delta-encoded size table and the
        ///     chunk count. File 0 is <c>1,2,3,4</c> and file 1 is <c>5,6</c>.
        /// </summary>
        private static byte[] MultiChunkPayload() {
            var stream = new JagStream();
            stream.Write(new byte[] { 1, 2, 5, 3, 4, 6 });   // chunk 0: 1,2 | 5   chunk 1: 3,4 | 6

            for (int chunk = 0; chunk < 2; chunk++) {
                stream.WriteInteger(2);    // file 0 contributes 2 bytes to this chunk
                stream.WriteInteger(-1);   // file 1 contributes 1, delta-encoded against file 0
            }

            stream.WriteByte(2);           // chunk count
            return stream.Flip().ToArray();
        }
    }
}
