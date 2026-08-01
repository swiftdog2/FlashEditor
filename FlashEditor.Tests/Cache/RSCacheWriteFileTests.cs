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
    /// Covers <see cref="RSCache.WriteFile"/> against a real, synthetic cache directory:
    /// the archive is rehydrated from the container before it is edited, and the archive
    /// and its reference-table entry are reconciled over actual file ids rather than
    /// ordinal positions. Sparse archives are the interesting case - the reference-table
    /// encoder preserves sparse file ids, and the write path has to leave them intact for
    /// that to be observable end to end.
    /// </summary>
    public class RSCacheWriteFileTests : IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public RSCacheWriteFileTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-cache-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>
        /// Seeds a cache holding a single archive (index 0, archive 0) with the supplied
        /// files, plus the matching reference table in the meta index.
        /// </summary>
        /// <remarks>
        /// Index 1 exists only so <c>GetIndexCount</c> - which reports the highest non-meta
        /// index id rather than a count - returns 1 and lets index 0's reference table load.
        /// </remarks>
        private RSCache CreateCache(params (int fileId, byte[] data)[] files)
        {
            //Sector 0 is burned: allocation derives the next free sector from the data
            //length, and sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            foreach (int i in new[] { 0, 1, RSConstants.META_INDEX })
                File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + i), Array.Empty<byte>());

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            var archive = new RSArchive();
            foreach ((int fileId, byte[] data) in files)
                archive.PutFile(fileId, new JagStream(data));

            store.Write(0, 0, new RSContainer(0, 0, RSConstants.GZIP_COMPRESSION, archive.Encode(), 1337).Encode());
            store.Write(RSConstants.META_INDEX, 0, EncodeReferenceTable(files.Select(f => f.fileId).ToArray()));

            return new RSCache(store);
        }

        private static JagStream EncodeReferenceTable(int[] fileIds)
        {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(fileIds);
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>(
                fileIds.ToDictionary(id => id, id => new RSFileEntry(id))));
            table.PutArchiveEntry(0, entry);

            return new RSContainer(RSConstants.META_INDEX, 0, RSConstants.GZIP_COMPRESSION,
                                   ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        /// <summary>
        /// Commits the cache to a fresh directory and reopens it, so assertions run against
        /// bytes that made a full round trip through the file store rather than against
        /// in-memory state.
        /// </summary>
        private RSCache SaveAndReopen(RSCache cache)
        {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);
            return new RSCache(reopened);
        }

        private static int[] FileIdsOf(RSCache cache)
        {
            return cache.GetReferenceTable(0).GetArchiveEntry(0).GetFileEntries().Keys.ToArray();
        }

        // ===================================================================
        //  Sparse archives
        // ===================================================================

        /// <summary>
        /// The reference-table encoder deltas over actual file ids, so a sparse archive
        /// must still be sparse after an edit. Walking an ordinal counter over the archive
        /// instead threw on the first gap, and where it did not throw it re-registered
        /// every file under an ordinal id.
        /// </summary>
        [Fact]
        public void WriteFile_SparseArchive_KeepsFileIdsSparse()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));

            Assert.Equal(new[] { 0, 5, 9 }, FileIdsOf(cache));
        }

        [Fact]
        public void WriteFile_SparseArchive_SurvivesSaveAndReopen()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 0, 5, 9 }, FileIdsOf(reopened));
            Assert.Equal(new byte[] { 7, 8, 9, 10 }, reopened.ReadFile(0, 0, 5).ToArray());
        }

        /// <summary>
        /// Editing one file must not disturb its neighbours. This is the case the old loop
        /// could not express at all: it started from whatever the archive happened to hold
        /// and had no way to recover files it had never loaded.
        /// </summary>
        [Fact]
        public void WriteFile_SparseArchive_LeavesTheOtherFilesIntact()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 0).ToArray());
            Assert.Equal(new byte[] { 6 }, reopened.ReadFile(0, 0, 9).ToArray());
        }

        /// <summary>
        /// ReadFile releases the container's decoded archive as soon as it has handed the
        /// caller its file, so by the time an edit arrives the archive is routinely null.
        /// Editing from a blank archive would drop every other file in the group.
        /// </summary>
        [Fact]
        public void WriteFile_AfterReadFileReleasedTheArchive_StillKeepsEveryFile()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }), (9, new byte[] { 6 }));

            cache.ReadFile(0, 0, 5);   // decodes the archive, then releases it
            cache.WriteFile(0, 0, 5, new JagStream(new byte[] { 7, 8, 9, 10 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 0, 5, 9 }, FileIdsOf(reopened));
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 0).ToArray());
            Assert.Equal(new byte[] { 7, 8, 9, 10 }, reopened.ReadFile(0, 0, 5).ToArray());
            Assert.Equal(new byte[] { 6 }, reopened.ReadFile(0, 0, 9).ToArray());
        }

        // ===================================================================
        //  Adding files
        // ===================================================================

        [Fact]
        public void WriteFile_NewSparseFileId_IsAppendedWithoutRenumbering()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 12, new JagStream(new byte[] { 9, 9 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 0, 5, 12 }, FileIdsOf(reopened));
            Assert.Equal(new byte[] { 9, 9 }, reopened.ReadFile(0, 0, 12).ToArray());
            Assert.Equal(new byte[] { 1, 2, 3 }, reopened.ReadFile(0, 0, 0).ToArray());
        }

        /// <summary>
        /// The valid-file-id list is what drives archive decoding, so it has to track the
        /// file entries. Leaving it stale makes a reloaded container decode with the wrong
        /// ids the moment the in-memory archive is evicted.
        /// </summary>
        [Fact]
        public void WriteFile_NewFileId_IsReflectedInTheValidFileIdList()
        {
            RSCache cache = CreateCache((0, new byte[] { 1, 2, 3 }), (5, new byte[] { 4, 5 }));

            cache.WriteFile(0, 0, 12, new JagStream(new byte[] { 9, 9 }));

            Assert.Equal(new[] { 0, 5, 12 }, cache.GetReferenceTable(0).GetArchiveEntry(0).GetValidFileIds());
        }

        // ===================================================================
        //  Dense archives and single-file archives
        // ===================================================================

        [Fact]
        public void WriteFile_DenseArchive_RoundTripsEveryFile()
        {
            RSCache cache = CreateCache((0, new byte[] { 1 }), (1, new byte[] { 2, 2 }), (2, new byte[] { 3, 3, 3 }));

            cache.WriteFile(0, 0, 1, new JagStream(new byte[] { 8, 8, 8, 8 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 0, 1, 2 }, FileIdsOf(reopened));
            Assert.Equal(new byte[] { 1 }, reopened.ReadFile(0, 0, 0).ToArray());
            Assert.Equal(new byte[] { 8, 8, 8, 8 }, reopened.ReadFile(0, 0, 1).ToArray());
            Assert.Equal(new byte[] { 3, 3, 3 }, reopened.ReadFile(0, 0, 2).ToArray());
        }

        /// <summary>
        /// A single-file archive carries no size table, so overwriting its only file must
        /// leave it a bare payload rather than growing a trailer.
        /// </summary>
        [Fact]
        public void WriteFile_SingleFileArchive_DoesNotGrowATrailer()
        {
            RSCache cache = CreateCache((7, new byte[] { 1, 2, 3 }));

            cache.WriteFile(0, 0, 7, new JagStream(new byte[] { 4, 5, 6, 7 }));
            RSCache reopened = SaveAndReopen(cache);

            Assert.Equal(new[] { 7 }, FileIdsOf(reopened));
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, reopened.ReadFile(0, 0, 7).ToArray());
        }
    }
}
