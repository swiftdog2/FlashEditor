using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    /// Covers the one thing <see cref="RSCache.WriteFile"/> has to do when nothing changed:
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Deflate is not canonical. Jagex compressed with Java's <c>Deflater</c> and this project
    /// uses SharpZipLib, so re-encoding an untouched payload produces different - equally valid -
    /// stored bytes. The archive CRC in a reference table covers the STORED bytes, so a save that
    /// re-encodes an unmodified archive rewrites that CRC, the entry that carries it, and, because
    /// the whole table is re-encoded and rewritten alongside, the stored bytes behind every other
    /// archive entry in the same index. Opening an item and saving it unedited therefore used to
    /// churn the dat2 and the reference table of everything packed with it.
    /// <para>
    /// The comparison that prevents this is over the archive PAYLOAD, never the compressed
    /// container: comparing compressed output would never match and the check would never fire.
    /// </para>
    /// </remarks>
    public class RSCacheUnchangedSaveTests : IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public RSCacheUnchangedSaveTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-noop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            // Each store holds an exclusive handle on its dat2 and must be released
            // before the temp directory can be removed.
            foreach (RSFileStore store in _stores)
                store.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        // ===================================================================
        //  Seeding
        // ===================================================================

        /// <summary>The stale sizes seeded into a <see cref="RSReferenceTable.FLAG_SIZES"/> table.</summary>
        private const int StaleCompressed = 999999;
        private const int StaleUncompressed = 888888;

        /// <summary>The reference table version seeded, so a bump to 1337 is visible.</summary>
        private const int SeededTableVersion = 7;

        /// <summary>
        /// Seeds a cache holding one archive (index 0, archive 0) whose payload is
        /// <paramref name="payload"/> verbatim, compressed with <paramref name="compression"/>,
        /// plus a matching reference table naming <paramref name="fileIds"/>.
        /// </summary>
        /// <remarks>
        /// The payload is placed rather than built, so a multi-chunk archive - the layout most
        /// multi-file archives in a real cache use - can be seeded at all. Nothing in this project
        /// emits one from scratch: the chunk split survives only a decode, so an archive built
        /// through <see cref="RSArchive.PutFile"/> is always single-chunk.
        /// <para>
        /// Index 0 is the only content index. A padding index above it used to be needed, because
        /// the store reported the highest non-meta index id under the name <c>GetIndexCount</c>
        /// and RSCache consumed it as a count, which put the highest index one past the end; the
        /// bound and the enumeration are separate members now, so index 0 loads on its own.
        /// </para>
        /// </remarks>
        private RSCache CreateCache(byte compression, byte[] payload, int[] fileIds, int tableFlags = 0)
        {
            //Sector 0 is burned: allocation derives the next free sector from the data
            //length, and sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            foreach (int i in new[] { 0, RSConstants.META_INDEX })
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + i), Array.Empty<byte>());

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            store.Write(0, 0, new RSContainer(0, 0, compression, new JagStream(payload), 1337).Encode());
            store.Write(RSConstants.META_INDEX, 0, EncodeReferenceTable(tableFlags, fileIds, payload, compression));

            return new RSCache(store);
        }

        /// <summary>
        /// Seeds a cache whose single archive holds the given files, laid out as one chunk.
        /// </summary>
        private RSCache CreateCache(byte compression, int tableFlags, params (int fileId, byte[] data)[] files)
        {
            var archive = new RSArchive();
            foreach ((int fileId, byte[] data) in files)
                archive.PutFile(fileId, new JagStream(data));

            return CreateCache(compression, archive.Encode().ToArray(),
                               files.Select(f => f.fileId).ToArray(), tableFlags);
        }

        /// <summary>
        /// The reference table for the seeded archive, carrying a CRC and sizes that genuinely
        /// describe the stored container.
        /// </summary>
        /// <remarks>
        /// Seeding the CRC honestly is the point of this fixture: a no-op save has to leave a
        /// correct entry correct, and an entry seeded with a wrong CRC could not tell "left alone"
        /// from "recomputed". The <see cref="RSReferenceTable.FLAG_SIZES"/> pair is seeded stale on
        /// purpose in the other direction - see
        /// <see cref="WriteFile_Unchanged_LeavesEvenStaleSizesAlone"/>.
        /// </remarks>
        private static JagStream EncodeReferenceTable(int tableFlags, int[] fileIds, byte[] payload, byte compression)
        {
            var table = new RSReferenceTable { format = 6, version = SeededTableVersion, flags = tableFlags };

            //An empty file id list seeds a table that names no archives at all, which is how the
            //"the entry itself is what is missing" case below is expressed
            if (fileIds.Length > 0)
            {
                byte[] stored = new RSContainer(0, 0, compression, new JagStream(payload), 1337).Encode().ToArray();

                var entry = new RSArchiveEntry(0);
                entry.SetVersion(1);
                entry.SetCrc(unchecked((int) FlashEditor.Cache.Util.CRC32Helper.ComputeCrc32(
                    stored.AsSpan(0, stored.Length - 2))));
                entry.SetValidFileIds(fileIds);
                entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>(
                    fileIds.ToDictionary(id => id, id => new RSFileEntry(id))));
                entry.compressed = StaleCompressed;
                entry.uncompressed = StaleUncompressed;
                table.PutArchiveEntry(0, entry);
            }

            return new RSContainer(RSConstants.META_INDEX, 0, RSConstants.GZIP_COMPRESSION,
                                   ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        /// <summary>The stored container bytes for index 0, archive 0.</summary>
        private static byte[] StoredArchive(RSCache cache)
        {
            return cache.LoadContainer(0, 0).ToArray();
        }

        /// <summary>
        /// The stored bytes of index 0's reference table - the whole table, because rewriting one
        /// entry rewrites the container every other entry shares.
        /// </summary>
        private static byte[] StoredTable(RSCache cache)
        {
            return cache.LoadContainer(RSConstants.META_INDEX, 0).ToArray();
        }

        /// <summary>
        /// Everything a write touches: the two stored containers, the index records that point at
        /// them, and the length of the dat2 they live in.
        /// </summary>
        /// <remarks>
        /// <c>RSCache.HasUnsavedChanges</c> is no use as the "nothing was written" signal here,
        /// because seeding the fixture goes through the same staging writes and leaves the store
        /// dirty before a single test line runs. Comparing what is actually staged is the stronger
        /// statement anyway: it catches a rewrite that happened to produce the same container
        /// bytes but reallocated its sectors, which would still move the dat2 under every archive
        /// after it.
        /// </remarks>
        private sealed class StoreSnapshot
        {
            public byte[] Archive;
            public byte[] Table;
            public byte[] IndexRecords;
            public byte[] MetaIndexRecords;
            public long DataLength;
        }

        private static StoreSnapshot Snapshot(RSCache cache)
        {
            return new StoreSnapshot
            {
                Archive = StoredArchive(cache),
                Table = StoredTable(cache),
                IndexRecords = cache.GetStore().GetIndexEntry(0).GetStream().ToArray(),
                MetaIndexRecords = cache.GetStore().GetIndexEntry(RSConstants.META_INDEX).GetStream().ToArray(),
                DataLength = cache.GetStore().dataChannel.Length
            };
        }

        private static void AssertNothingWasWritten(StoreSnapshot before, RSCache cache)
        {
            StoreSnapshot after = Snapshot(cache);
            Assert.Equal(before.Archive, after.Archive);
            Assert.Equal(before.Table, after.Table);
            Assert.Equal(before.IndexRecords, after.IndexRecords);
            Assert.Equal(before.MetaIndexRecords, after.MetaIndexRecords);
            Assert.Equal(before.DataLength, after.DataLength);
        }

        private static RSArchiveEntry EntryOf(RSCache cache)
        {
            return cache.GetReferenceTable(0).GetArchiveEntry(0);
        }

        // ===================================================================
        //  A save that changes nothing
        // ===================================================================

        /// <summary>
        /// The headline case: read a file, write the identical bytes back, and neither the dat2
        /// nor the reference table may move. Both are compared as stored bytes rather than as
        /// decoded values, because "the CRC happens to come out the same" is not the claim - the
        /// claim is that nothing was written.
        /// </summary>
        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void WriteFile_UnmodifiedPayload_LeavesTheStoredBytesAndTheTableAlone(int compression)
        {
            RSCache cache = CreateCache((byte) compression, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            StoreSnapshot before = Snapshot(cache);

            byte[] unchanged = cache.ReadFile(0, 0, 5).ToArray();
            cache.WriteFile(0, 0, 5, new JagStream(unchanged));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        /// A single-file archive has no size table, so its payload is the bare file. It is the
        /// case where an encoder that appends a trailer, or a comparison made against the
        /// compressed container, shows up immediately.
        /// </summary>
        [Fact]
        public void WriteFile_UnmodifiedSingleFileArchive_LeavesTheStoredBytesAlone()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0, (7, new byte[] { 1, 2, 3 }));

            StoreSnapshot before = Snapshot(cache);

            cache.WriteFile(0, 0, 7, new JagStream(new byte[] { 1, 2, 3 }));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        /// Most multi-file archives in a real 639 cache are stored across three chunks, and the
        /// split is part of the byte layout: the payload is chunk-major, so re-laying it out as a
        /// single chunk yields the same length with the bytes in a different order. Dropping the
        /// split on every <see cref="RSArchive.PutFile"/> therefore rewrote every multi-chunk
        /// archive the moment it was saved, edit or no edit.
        /// </summary>
        [Fact]
        public void WriteFile_UnmodifiedMultiChunkArchive_KeepsTheChunkLayoutAndTheStoredBytes()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, MultiChunkPayload(), new[] { 0, 1 });

            StoreSnapshot before = Snapshot(cache);

            //The seeded layout really is chunk-major, or this test proves nothing
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, cache.ReadFile(0, 0, 0).ToArray());
            Assert.Equal(new byte[] { 5, 6 }, cache.ReadFile(0, 0, 1).ToArray());

            cache.WriteFile(0, 0, 1, new JagStream(new byte[] { 5, 6 }));

            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        /// Two files spread over two chunks, written the way the client stores them: chunk 0 of
        /// every file, then chunk 1 of every file, then the delta-encoded size table and the
        /// chunk count. File 0 is <c>1,2,3,4</c> and file 1 is <c>5,6</c>.
        /// </summary>
        private static byte[] MultiChunkPayload()
        {
            var stream = new JagStream();
            stream.Write(new byte[] { 1, 2, 5, 3, 4, 6 });   // chunk 0: 1,2 | 5   chunk 1: 3,4 | 6

            for (int chunk = 0; chunk < 2; chunk++)
            {
                stream.WriteInteger(2);    // file 0 contributes 2 bytes to this chunk
                stream.WriteInteger(-1);   // file 1 contributes 1, delta-encoded against file 0
            }

            stream.WriteByte(2);           // chunk count
            return stream.Flip().ToArray();
        }

        /// <summary>
        /// The FLAG_SIZES pair is seeded wrong on purpose. A no-op save must not "fix" it: the
        /// point of the unchanged path is that the entry is not touched at all, and an entry that
        /// is partly rewritten is a rewritten entry.
        /// </summary>
        [Fact]
        public void WriteFile_Unchanged_LeavesEvenStaleSizesAlone()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, RSReferenceTable.FLAG_SIZES,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            StoreSnapshot before = Snapshot(cache);

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 4, 5 }));

            Assert.Equal(StaleCompressed, EntryOf(cache).compressed);
            Assert.Equal(StaleUncompressed, EntryOf(cache).uncompressed);
            Assert.Equal(SeededTableVersion, cache.GetReferenceTable(0).version);
            AssertNothingWasWritten(before, cache);
        }

        /// <summary>
        /// Writing the same bytes to a different file in the same archive is still a no-op for the
        /// archive as a whole - the payload is what is compared, not the file that was handed in.
        /// </summary>
        [Fact]
        public void WriteFile_UnchangedFileInAMultiFileArchive_DoesNotDisturbItsNeighbours()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            StoreSnapshot before = Snapshot(cache);

            cache.WriteFile(0, 0, 0, new JagStream(new byte[] { 1, 2, 3 }));
            cache.WriteFile(0, 0, 9, new JagStream(new byte[] { 6 }));

            AssertNothingWasWritten(before, cache);
        }

        // ===================================================================
        //  A save that changes something
        // ===================================================================

        /// <summary>
        /// The other half of the claim. Skipping unchanged writes is only safe if changed ones
        /// still go through, CRC and all.
        /// </summary>
        [Fact]
        public void WriteFile_EditedPayload_StillWritesAndUpdatesTheCrc()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            byte[] archiveBefore = StoredArchive(cache);
            int crcBefore = EntryOf(cache).GetCrc();

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));

            Assert.NotEqual(archiveBefore, StoredArchive(cache));
            Assert.NotEqual(crcBefore, EntryOf(cache).GetCrc());

            //And the CRC that was written describes the bytes that were written
            byte[] stored = StoredArchive(cache);
            uint expected = FlashEditor.Cache.Util.CRC32Helper.ComputeCrc32(stored.AsSpan(0, stored.Length - 2));
            Assert.Equal(expected, unchecked((uint) EntryOf(cache).GetCrc()));
        }

        /// <summary>
        /// An edit of the same length is still an edit. A comparison that only looked at lengths -
        /// or a chunk split kept without re-slicing the new bytes - would drop it silently, which
        /// is the worst possible failure for this feature.
        /// </summary>
        [Fact]
        public void WriteFile_SameLengthEdit_IsNotMistakenForANoOp()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, MultiChunkPayload(), new[] { 0, 1 });

            byte[] before = StoredArchive(cache);

            cache.WriteFile(0, 0, 0, new JagStream(new byte[] { 9, 9, 9, 9 }));

            Assert.NotEqual(before, StoredArchive(cache));
            Assert.Equal(new byte[] { 9, 9, 9, 9 }, cache.ReadFile(0, 0, 0).ToArray());
            Assert.Equal(new byte[] { 5, 6 }, cache.ReadFile(0, 0, 1).ToArray());
        }

        /// <summary>
        /// After a real edit, the archive that was just stored becomes the new baseline: repeating
        /// the same edit writes nothing. Without that the flag would have to be cleared for good
        /// on the first write, and every later save would re-encode.
        /// </summary>
        [Fact]
        public void WriteFile_RepeatingAnEdit_WritesOnlyOnce()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));

            byte[] archiveAfterEdit = StoredArchive(cache);
            byte[] tableAfterEdit = StoredTable(cache);

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));

            Assert.Equal(archiveAfterEdit, StoredArchive(cache));
            Assert.Equal(tableAfterEdit, StoredTable(cache));
        }

        /// <summary>
        /// And a genuine edit after a no-op save is not swallowed by it. This is the sequence that
        /// catches a baseline left describing the wrong payload.
        /// </summary>
        [Fact]
        public void WriteFile_EditAfterANoOpSave_StillWrites()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 4, 5 }));
            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new byte[] { 7, 8, 9, 10 }, reopened.ReadFile(0, 0, 5).ToArray());
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 0).ToArray());
        }

        /// <summary>
        /// Reverting an edit within one session has to write, not skip. The payload matches what
        /// the cache was opened with, but the store no longer holds those bytes - the earlier edit
        /// replaced them - so the baseline is the bytes currently stored, never the bytes the
        /// session started from.
        /// </summary>
        [Fact]
        public void WriteFile_RevertingAnEditWithinTheSession_WritesTheOriginalBack()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));
            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 4, 5 }));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new byte[] { 4, 5 }, reopened.ReadFile(0, 0, 5).ToArray());
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 0).ToArray());
        }

        /// <summary>
        /// An archive the reference table has never heard of has to be written even when its
        /// payload already matches the bytes on disk: the missing thing is the entry, not the
        /// payload. Skipping on payload equality alone would leave the archive unreachable.
        /// </summary>
        [Fact]
        public void WriteFile_ArchiveMissingFromTheTable_IsWrittenEvenWhenThePayloadMatches()
        {
            //A cache whose archive 0 is stored but whose reference table names no archives at all
            var archive = new RSArchive();
            archive.PutFile(7, new JagStream(new byte[] { 1, 2, 3 }));
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, archive.Encode().ToArray(), Array.Empty<int>());

            Assert.Null(cache.GetReferenceTable(0).GetArchiveEntry(0));

            cache.WriteFile(0, 0, 7, new JagStream(new byte[] { 1, 2, 3 }));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 7 }, reopened.GetReferenceTable(0).GetArchiveEntry(0).GetValidFileIds());
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 7).ToArray());
        }

        /// <summary>
        /// Adding a file to an archive is never a no-op, however small the file.
        /// </summary>
        [Fact]
        public void WriteFile_NewFileId_IsAlwaysWritten()
        {
            RSCache cache = CreateCache(RSConstants.GZIP_COMPRESSION, 0,
                (0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 12, new JagStream(new byte[] { 9 }));

            RSCache reopened = SaveAndReopen(cache);
            Assert.Equal(new[] { 0, 5, 12 }, reopened.GetReferenceTable(0).GetArchiveEntry(0).GetValidFileIds());
            Assert.Equal(new byte[] { 9 }, reopened.ReadFile(0, 0, 12).ToArray());
        }

        /// <summary>
        /// Commits the cache to a fresh directory and reopens it, so assertions run against bytes
        /// that made a full round trip through the file store.
        /// </summary>
        private RSCache SaveAndReopen(RSCache cache)
        {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }
    }
}
