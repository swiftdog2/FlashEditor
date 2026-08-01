using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Utils;
using System;
using System.IO;
using System.Threading;
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
        /// An empty payload allocates no sectors, so it is rejected up front rather than
        /// mutating the index record and then failing when verification indexes an empty
        /// sector list.
        /// </summary>
        [Fact]
        public void Write_EmptyPayload_ThrowsBeforeMutatingAnything()
        {
            var store = CreateStore();

            Assert.Throws<ArgumentException>(() => store.Write(0, 0, new JagStream(Array.Empty<byte>())));

            Assert.Equal(0, store.GetFileCount(0));
            Assert.False(store.IsDirty);
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
        /// The index stream is seeked on both the new-archive and overwrite branches, so a new
        /// archive written after an overwrite lands at its own offset rather than wherever the
        /// preceding ReadContainerHeader left the position.
        /// </summary>
        [Fact]
        public void Write_NewArchiveAfterOverwrite_WritesRecordAtCorrectOffset()
        {
            var store = CreateStore();

            store.Write(0, 0, Payload(4));
            store.Write(0, 1, Payload(8));
            store.Write(0, 0, Payload(12));     // overwrite: leaves the stream mid-file
            store.Write(0, 2, Payload(16));     // must still land at offset 12

            var index = store.GetIndexEntry(0);

            index.ReadContainerHeader(0);
            Assert.Equal(12, index.GetSize());

            index.ReadContainerHeader(1);
            Assert.Equal(8, index.GetSize());   // untouched by archive 2's write

            index.ReadContainerHeader(2);
            Assert.Equal(16, index.GetSize());

            Assert.Equal(3, store.GetFileCount(0));
        }

        // ===================================================================
        //  Persistence
        // ===================================================================

        /// <summary>
        /// Nothing reaches the source cache until SaveTo runs, so disposing without saving
        /// leaves the dat2 byte for byte as it was found. This is the core guarantee of
        /// staging: an edit can never half-update the cache on disk.
        /// </summary>
        [Fact]
        public void Dispose_WithoutSave_LeavesSourceCacheUntouched()
        {
            string dat2 = Path.Combine(_dir, "main_file_cache.dat2");
            var store = CreateStore();
            byte[] before = File.ReadAllBytes(dat2);

            store.Write(0, 0, Payload(600));
            Assert.True(store.IsDirty);

            store.Dispose();
            _store = null;

            Assert.Equal(before, File.ReadAllBytes(dat2));
            Assert.Equal(0, new FileInfo(Path.Combine(_dir, "main_file_cache.idx0")).Length);

            _store = new RSFileStore(_dir);
            Assert.Equal(0, _store.GetFileCount(0));
        }

        // ===================================================================
        //  SaveTo - the commit path
        // ===================================================================

        [Fact]
        public void SaveTo_PersistsSectorsAndIndexRecordsTogether()
        {
            var store = CreateStore();
            var payload = Payload(600);
            byte[] expected = payload.ToArray();
            store.Write(0, 0, payload);

            string outDir = Path.Combine(_dir, "out");
            store.SaveTo(outDir);
            store.Dispose();
            _store = null;

            using var reopened = new RSFileStore(outDir);
            Assert.Equal(1, reopened.GetFileCount(0));

            var index = reopened.GetIndexEntry(0);
            index.ReadContainerHeader(0);
            Assert.Equal(600, index.GetSize());
            Assert.Equal(1, index.GetSectorID());

            var first = RSSector.Decode(new JagStream(reopened.dataChannel.ReadBytes(SectorSize, SectorSize)));
            Assert.Equal(expected[..SectorData], first.GetData());
        }

        [Fact]
        public void SaveTo_OverTheSourceDirectory_ReplacesFilesAndStaysUsable()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(600));
            store.SaveTo(_dir);

            Assert.False(store.IsDirty);

            // The channel closed and reopened over the replaced file; further edits still work.
            store.Write(0, 1, Payload(4));
            store.SaveTo(_dir);
            store.Dispose();
            _store = null;

            using var reopened = new RSFileStore(_dir);
            Assert.Equal(2, reopened.GetFileCount(0));
        }

        [Fact]
        public void SaveTo_ToAnotherDirectory_LeavesTheSourceUntouched()
        {
            string dat2 = Path.Combine(_dir, "main_file_cache.dat2");
            var store = CreateStore();
            byte[] before = File.ReadAllBytes(dat2);

            store.Write(0, 0, Payload(600));
            store.SaveTo(Path.Combine(_dir, "copy"));

            Assert.Equal(before, File.ReadAllBytes(dat2));
        }

        [Fact]
        public void SaveTo_WritesExactlyTheStagedLength()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(600));           // dummy + two sectors

            string outDir = Path.Combine(_dir, "out");
            store.SaveTo(outDir);

            Assert.Equal(SectorSize * 3, new FileInfo(Path.Combine(outDir, "main_file_cache.dat2")).Length);
        }

        [Fact]
        public void SaveTo_RemovesItsStagingDirectory()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(4));
            store.SaveTo(_dir);

            Assert.Empty(Directory.GetDirectories(_dir));
        }

        [Fact]
        public void IsDirty_FalseUntilAWrite_ThenClearedBySave()
        {
            var store = CreateStore();
            Assert.False(store.IsDirty);

            store.Write(0, 0, Payload(4));
            Assert.True(store.IsDirty);

            store.SaveTo(Path.Combine(_dir, "out"));
            Assert.False(store.IsDirty);
        }

        // ===================================================================
        //  Staging channel behaviour
        // ===================================================================

        [Fact]
        public void ReadBytes_ReturnsStagedBytesBeforeAnySave()
        {
            var store = CreateStore();
            var payload = Payload(4);
            byte[] expected = payload.ToArray();
            store.Write(0, 0, payload);

            // Read-through matters: Write's own chain verification depends on it.
            byte[] raw = store.dataChannel.ReadBytes(SectorSize, SectorSize);
            Assert.Equal(expected, raw[8..12]);
        }

        [Fact]
        public void ReadBytes_StartingPastTheDataLength_Throws()
        {
            var store = CreateStore();

            // Only the dummy sector exists, so sector 1 was never allocated.
            Assert.Throws<ArgumentOutOfRangeException>(() => store.dataChannel.ReadBytes(SectorSize, SectorSize));
        }

        [Fact]
        public void ReadBytes_NegativeOffset_Throws()
        {
            var store = CreateStore();
            Assert.Throws<ArgumentOutOfRangeException>(() => store.dataChannel.ReadBytes(-SectorSize, SectorSize));
        }

        /// <summary>
        /// Definitions and textures load on background threads, so reads genuinely race writes.
        /// A Dictionary read concurrent with an insert is undefined behaviour, hence the lock
        /// inside the channel; without it this fails non-deterministically.
        /// </summary>
        [Fact]
        public void ConcurrentReadsDuringWrites_DoNotCorruptTheOverlay()
        {
            var store = CreateStore();
            store.Write(0, 0, Payload(4));

            Exception? failure = null;
            var reader = new Thread(() => {
                try
                {
                    for (int i = 0; i < 3000; i++)
                        store.dataChannel.ReadBytes(SectorSize, SectorSize);
                }
                catch (Exception ex) { failure = ex; }
            });

            reader.Start();
            for (int archive = 1; archive < 200; archive++)
                store.Write(0, archive, Payload(4, seed: archive));
            reader.Join();

            Assert.Null(failure);
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
