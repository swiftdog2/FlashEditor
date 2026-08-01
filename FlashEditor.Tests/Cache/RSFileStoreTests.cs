using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.IO;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    /// Comprehensive coverage of the cache write path: sector encoding, index record
    /// maintenance, sector chaining, allocation and the error surface.
    ///
    /// Several tests are named *_DocumentsKnownDefect. Those pin CURRENT behaviour that is
    /// known to be wrong, so the defect is recorded and any future fix shows up as a
    /// deliberate, visible test change rather than a silent behaviour swap. They are not
    /// endorsements of the behaviour they assert.
    /// </summary>
    public class RSFileStoreTests : IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in const context
        private const int SectorData = 512;   // RSSector.DATA_LEN
        private const int RecordSize = 6;     // RSIndex.SIZE

        private readonly string _dir;
        private RSFileStore _store;

        public RSFileStoreTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            // The store holds an exclusive handle on dat2 via MemoryMappedFile.
            // It must be disposed before the directory can be removed.
            _store?.Dispose();
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>
        /// Seeds a synthetic cache. The dat2 gets one dummy sector so that sector 0 is
        /// burned: allocation derives the next free sector from the data length, and a
        /// zero-length dat2 would hand out sector 0, which the reader treats as EOF.
        /// </summary>
        private RSFileStore CreateStore(int dummySectors = 1, params int[] indexes)
        {
            if (indexes.Length == 0)
                indexes = new[] { 0 };

            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize * dummySectors]);
            foreach (int i in indexes)
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + i), Array.Empty<byte>());

            _store = new RSFileStore(_dir);
            return _store;
        }

        private static JagStream Payload(int length, int seed = 0)
        {
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
                bytes[i] = (byte) ((i + seed) & 0xFF);
            return new JagStream(bytes);
        }

        private static RSSector ReadSector(RSFileStore store, int sectorId)
        {
            byte[] raw = store.dataChannel.ReadBytes((long) sectorId * SectorSize, SectorSize);
            return RSSector.Decode(new JagStream(raw));
        }

        // ===================================================================
        //  RSSector - pure, no IO
        // ===================================================================

        [Fact]
        public void RSSector_Constants_MatchOnDiskLayout()
        {
            Assert.Equal(8, RSSector.HEADER_LEN);
            Assert.Equal(512, RSSector.DATA_LEN);
            Assert.Equal(520, RSSector.SIZE);
            Assert.Equal(6, RSIndex.SIZE);
        }

        /// <summary>
        /// The constructor takes (indexId, id, chunk, nextSector) but the wire order is
        /// id, chunk, nextSector, indexId. Pins that transposition so a future refactor
        /// cannot quietly swap them.
        /// </summary>
        [Fact]
        public void RSSector_Encode_WritesHeaderFieldsInWireOrder()
        {
            var data = new byte[SectorData];
            data[0] = 0xAB;

            byte[] bytes = new RSSector(indexId: 7, id: 0x0102, chunk: 0x0304, nextSector: 0x050607, data)
                .Encode().ToArray();

            Assert.Equal(SectorSize, bytes.Length);
            Assert.Equal(0x01, bytes[0]);               // id, unsigned short big-endian
            Assert.Equal(0x02, bytes[1]);
            Assert.Equal(0x03, bytes[2]);               // chunk
            Assert.Equal(0x04, bytes[3]);
            Assert.Equal(0x05, bytes[4]);               // nextSector, medium
            Assert.Equal(0x06, bytes[5]);
            Assert.Equal(0x07, bytes[6]);
            Assert.Equal(7, bytes[7]);                  // indexId, single byte, written last
            Assert.Equal(0xAB, bytes[8]);               // payload starts at HEADER_LEN
        }

        [Fact]
        public void RSSector_EncodeDecode_RoundTrips()
        {
            var data = new byte[SectorData];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte) (i & 0xFF);

            var decoded = RSSector.Decode(new JagStream(
                new RSSector(indexId: 3, id: 42, chunk: 5, nextSector: 99, data).Encode().ToArray()));

            Assert.Equal(3, decoded.GetIndexId());
            Assert.Equal(42, decoded.GetId());
            Assert.Equal(5, decoded.GetChunk());
            Assert.Equal(99, decoded.GetNextSector());
            Assert.Equal(data, decoded.GetData());
        }

        [Fact]
        public void RSSector_Decode_StreamShorterThanOneSector_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => RSSector.Decode(new JagStream(new byte[SectorSize - 1])));
            Assert.Contains("Invalid sector length", ex.Message);
        }

        // ===================================================================
        //  RSIndex
        // ===================================================================

        [Fact]
        public void RSIndex_BeforeReadingAnyHeader_ReportsSentinelValues()
        {
            var index = new RSIndex(new JagStream(new byte[0]));
            Assert.Equal(-1, index.GetSize());
            Assert.Equal(-1, index.GetSectorID());
        }

        [Fact]
        public void RSIndex_ReadContainerHeader_DecodesMediumPairAtArchiveOffset()
        {
            var stream = new JagStream(12);
            stream.WriteMedium(111); stream.WriteMedium(222);   // archive 0
            stream.WriteMedium(333); stream.WriteMedium(444);   // archive 1
            stream.Flip();

            var index = new RSIndex(stream);

            index.ReadContainerHeader(0);
            Assert.Equal(111, index.GetSize());
            Assert.Equal(222, index.GetSectorID());

            index.ReadContainerHeader(1);
            Assert.Equal(333, index.GetSize());
            Assert.Equal(444, index.GetSectorID());
        }

        // ===================================================================
        //  Construction
        // ===================================================================

        [Fact]
        public void Constructor_CreatesDat2WhenAbsent()
        {
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx0"), Array.Empty<byte>());

            _store = new RSFileStore(_dir);

            Assert.True(File.Exists(Path.Combine(_dir, "main_file_cache.dat2")));
            Assert.Equal(0, _store.dataChannel.Length);
        }

        [Fact]
        public void Constructor_SilentlySkipsAbsentIndexFiles()
        {
            var store = CreateStore(dummySectors: 1, indexes: new[] { 0, 5 });

            Assert.Equal(0, store.GetFileCount(0));
            Assert.Equal(0, store.GetFileCount(5));
            Assert.Throws<FileNotFoundException>(() => store.GetFileCount(1));
        }

        [Fact]
        public void Constructor_LoadsMetaIndex255()
        {
            var store = CreateStore(dummySectors: 1, indexes: new[] { 0, RSConstants.META_INDEX });

            Assert.Equal(0, store.GetFileCount(RSConstants.META_INDEX));
        }

        [Fact]
        public void Constructor_MissingDirectory_Throws()
        {
            string missing = Path.Combine(_dir, "does-not-exist");
            Assert.ThrowsAny<IOException>(() => new RSFileStore(missing));
        }

        // ===================================================================
        //  Index accessors
        // ===================================================================

        [Fact]
        public void GetFileCount_CountsSixByteRecords()
        {
            var store = CreateStore();
            Assert.Equal(0, store.GetFileCount(0));

            store.Write(0, 0, Payload(4));
            Assert.Equal(1, store.GetFileCount(0));

            store.Write(0, 1, Payload(4));
            Assert.Equal(2, store.GetFileCount(0));
        }

        [Fact]
        public void GetIndexEntry_UnknownIndex_Throws()
        {
            var store = CreateStore();
            Assert.Throws<FileNotFoundException>(() => store.GetIndexEntry(9));
        }

        /// <summary>
        /// DEFECT: GetIndexCount returns the highest non-meta index id, not a count. Callers
        /// in RSCache use it as a count and loop `indexId &lt; GetIndexCount()`, so the highest
        /// index present is never loaded, and a single-index cache yields zero.
        /// </summary>
        [Fact]
        public void GetIndexCount_ReturnsHighestIdNotACount_DocumentsKnownDefect()
        {
            var store = CreateStore(dummySectors: 1, indexes: new[] { 0, 1, 4 });

            // Three indexes are loaded, but the highest id is 4, so this reports 4 rather
            // than 3. A `for (i = 0; i < GetIndexCount(); i++)` loop therefore skips index 4.
            Assert.Equal(4, store.GetIndexCount());
            Assert.Equal(0, store.GetFileCount(4));   // index 4 IS loaded, just unreachable by that loop
        }

        [Fact]
        public void GetIndexCount_SingleIndexZero_ReturnsZero_DocumentsKnownDefect()
        {
            var store = CreateStore(dummySectors: 1, indexes: new[] { 0 });

            // One index is loaded but this reports 0, so RSCache allocates a zero-length
            // reference table array and never loads anything.
            Assert.Equal(0, store.GetIndexCount());
        }

        // ===================================================================
        //  Write - happy paths
        // ===================================================================

        [Fact]
        public void Write_SinglePartialSector_WritesSectorHeaderPayloadAndIndexRecord()
        {
            var store = CreateStore();
            var payload = Payload(4);
            byte[] expected = payload.ToArray();

            store.Write(indexId: 0, archiveId: 0, data: payload);

            // Sector 0 is the dummy, so allocation starts at sector 1.
            var sector = ReadSector(store, 1);
            Assert.Equal(0, sector.GetIndexId());
            Assert.Equal(0, sector.GetId());
            Assert.Equal(0, sector.GetChunk());
            Assert.Equal(0, sector.GetNextSector());          // EOF marker on the tail sector
            Assert.Equal(expected, sector.GetData()[..4]);
            Assert.All(sector.GetData()[4..], b => Assert.Equal(0, b));   // zero padded to 512

            var index = store.GetIndexEntry(0);
            index.ReadContainerHeader(0);
            Assert.Equal(4, index.GetSize());
            Assert.Equal(1, index.GetSectorID());
        }

        [Fact]
        public void Write_ExactlyOneFullSector_DoesNotAllocateASecond()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(SectorData));

            Assert.Equal(0, ReadSector(store, 1).GetNextSector());
            Assert.Equal(SectorSize * 2, store.dataChannel.Length);   // dummy + one sector
        }

        [Fact]
        public void Write_MultiSectorPayload_ChainsSectorsWithSequentialChunks()
        {
            var store = CreateStore();
            var payload = Payload(600);
            byte[] expected = payload.ToArray();

            store.Write(0, 0, payload);

            var first = ReadSector(store, 1);
            var second = ReadSector(store, 2);

            Assert.Equal(0, first.GetChunk());
            Assert.Equal(2, first.GetNextSector());               // points at the continuation
            Assert.Equal(1, second.GetChunk());                   // chunk increments per sector
            Assert.Equal(0, second.GetNextSector());              // EOF

            Assert.Equal(expected[..SectorData], first.GetData());
            Assert.Equal(expected[SectorData..], second.GetData()[..(600 - SectorData)]);

            // Both sectors carry the owning index and archive for integrity checking.
            Assert.Equal(0, second.GetIndexId());
            Assert.Equal(0, second.GetId());
        }

        [Fact]
        public void Write_SequentialArchiveIds_AppendRecordsAndAllocateFreshSectors()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(4, seed: 1));
            store.Write(0, 1, Payload(4, seed: 2));
            store.Write(0, 2, Payload(4, seed: 3));

            Assert.Equal(3, store.GetFileCount(0));

            var index = store.GetIndexEntry(0);
            for (int archive = 0; archive < 3; archive++)
            {
                index.ReadContainerHeader(archive);
                Assert.Equal(4, index.GetSize());
                Assert.Equal(archive + 1, index.GetSectorID());   // sector 0 is the dummy

                var sector = ReadSector(store, archive + 1);
                Assert.Equal(archive, sector.GetId());
            }
        }

        [Fact]
        public void Write_GrowingAnExistingArchive_ReusesHeadSectorAndAppendsTheRest()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(4));
            store.Write(0, 0, Payload(600));

            var index = store.GetIndexEntry(0);
            index.ReadContainerHeader(0);
            Assert.Equal(600, index.GetSize());
            Assert.Equal(1, index.GetSectorID());       // head sector is reused, not reallocated

            Assert.Equal(2, ReadSector(store, 1).GetNextSector());
            Assert.Equal(0, ReadSector(store, 2).GetNextSector());
            Assert.Equal(1, store.GetFileCount(0));     // still one record, not a new one
        }

        /// <summary>
        /// DEFECT: allocation is append-only with no free list. Shrinking an archive
        /// rewrites the surplus sectors as zero-filled but leaves them chained from the
        /// head sector, so they are permanently orphaned. The archive still reads back
        /// correctly only because the index record's length field stops the reader early.
        /// </summary>
        [Fact]
        public void Write_ShrinkingAnExistingArchive_OrphansTrailingSectors_DocumentsKnownDefect()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(600));            // two sectors: 1 -> 2
            long lengthAfterGrow = store.dataChannel.Length;

            store.Write(0, 0, Payload(4));              // needs one sector

            var index = store.GetIndexEntry(0);
            index.ReadContainerHeader(0);
            Assert.Equal(4, index.GetSize());

            // The head sector STILL points at sector 2 even though only one sector is needed.
            // Nothing reclaims sector 2 and no free list exists, so the space is lost forever.
            Assert.Equal(2, ReadSector(store, 1).GetNextSector());
            Assert.Equal(lengthAfterGrow, store.dataChannel.Length);   // file never shrinks
        }

        // ===================================================================
        //  Write - error surface
        // ===================================================================

        [Fact]
        public void Write_UnknownIndex_Throws()
        {
            var store = CreateStore();
            Assert.Throws<FileNotFoundException>(() => store.Write(9, 0, Payload(4)));
        }

        [Fact]
        public void Write_NonContiguousArchiveId_Throws()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(4));

            // Archive 1 would be contiguous; archive 5 skips four records.
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Write(0, 5, Payload(4)));
        }

        [Fact]
        public void Write_ArchiveIdEqualToRecordCount_IsAllowedAsAnAppend()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(4));

            store.Write(0, 1, Payload(4));   // ptr == length, the append case

            Assert.Equal(2, store.GetFileCount(0));
        }

        /// <summary>
        /// DEFECT: a zero-length payload allocates no sectors, so the verification step
        /// indexes sectors[0] on an empty list. It throws ArgumentOutOfRangeException from
        /// the list indexer rather than a meaningful error, and only AFTER the index record
        /// has already been mutated, leaving the store partially updated.
        /// </summary>
        [Fact]
        public void Write_EmptyPayload_ThrowsAndLeavesRecordMutated_DocumentsKnownDefect()
        {
            var store = CreateStore();

            Assert.Throws<ArgumentOutOfRangeException>(() => store.Write(0, 0, new JagStream(Array.Empty<byte>())));

            // The record was already written before the throw: the store is now inconsistent,
            // reporting one archive of length zero that has no sector chain at all.
            Assert.Equal(1, store.GetFileCount(0));
            var index = store.GetIndexEntry(0);
            index.ReadContainerHeader(0);
            Assert.Equal(0, index.GetSize());
        }

        /// <summary>
        /// DEFECT: against a zero-length dat2 the allocator hands out sector 0, but both
        /// readers treat sector id 0 as the end-of-chain marker. The very first write to a
        /// fresh cache therefore always fails its own verification step.
        /// </summary>
        [Fact]
        public void Write_ToEmptyDataFile_AllocatesSectorZeroAndFailsVerification_DocumentsKnownDefect()
        {
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx0"), Array.Empty<byte>());
            _store = new RSFileStore(_dir);

            var ex = Assert.Throws<IOException>(() => _store.Write(0, 0, Payload(4)));
            Assert.Contains("Sector chain verification failed", ex.Message);
        }

        /// <summary>
        /// DEFECT: the seek that positions the index stream lives only on the
        /// existing-archive branch. After overwriting an archive the stream is left mid-file,
        /// so the next NEW archive writes its record at the stale position, silently
        /// overwriting a different archive's record.
        /// </summary>
        [Fact]
        public void Write_NewArchiveAfterOverwrite_CorruptsAnotherRecord_DocumentsKnownDefect()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(4));      // new: record 0, stream now at offset 6
            store.Write(0, 1, Payload(8));      // new: record 1, correct only by coincidence
            store.Write(0, 0, Payload(12));     // existing: seeks to 0, leaves stream at offset 6
            store.Write(0, 2, Payload(16));     // new: SHOULD write at 12, actually writes at 6

            var index = store.GetIndexEntry(0);

            // Archive 1's record now holds archive 2's size. Archive 1 is unrecoverable.
            index.ReadContainerHeader(1);
            Assert.Equal(16, index.GetSize());

            // And the record count never grew, so archive 2 has no record of its own.
            Assert.Equal(2, store.GetFileCount(0));
        }

        // ===================================================================
        //  Persistence
        // ===================================================================

        /// <summary>
        /// DEFECT: RSFileStore persists sector data to the memory-mapped dat2 but never
        /// writes the index files back. Reopening a cache recovers the payload bytes while
        /// the index records revert to their on-disk state, leaving the cache internally
        /// inconsistent the moment anything is edited.
        /// </summary>
        [Fact]
        public void Dispose_PersistsSectorsButNotIndexRecords_DocumentsKnownDefect()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(4));
            Assert.Equal(1, store.GetFileCount(0));

            store.Dispose();
            _store = null;

            // dat2 kept the sector.
            Assert.True(new FileInfo(Path.Combine(_dir, "main_file_cache.dat2")).Length >= SectorSize * 2);

            // idx0 is still the empty file we seeded: the record was only ever in memory.
            Assert.Equal(0, new FileInfo(Path.Combine(_dir, "main_file_cache.idx0")).Length);

            _store = new RSFileStore(_dir);
            Assert.Equal(0, _store.GetFileCount(0));   // the archive is gone on reopen
        }

        [Fact]
        public void Dispose_TruncatesDataFileToWrittenLength()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(600));           // dummy + two sectors

            store.Dispose();
            _store = null;

            Assert.Equal(SectorSize * 3, new FileInfo(Path.Combine(_dir, "main_file_cache.dat2")).Length);
        }

        [Fact]
        public void Dispose_IsIdempotent()
        {
            var store = CreateStore();
            store.Dispose();
            store.Dispose();
            _store = null;
        }
    }
}
