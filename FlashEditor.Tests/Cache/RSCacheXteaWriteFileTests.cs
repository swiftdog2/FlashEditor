using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    /// Covers what <see cref="RSCache.WriteFile"/> does to an XTEA encrypted archive - the map
    /// index, in practice. The archive is decrypted on read, so it has to be encrypted again on
    /// the way out or the map square is written back as plaintext and destroyed: the client
    /// deciphers it regardless and gets noise, and nothing here reports a problem because the
    /// CRC and the reference table are both rewritten to agree with the corrupted bytes.
    /// </summary>
    /// <remarks>
    /// A revision 639 reference table is format 6 and carries no per-archive encryption flag, so
    /// there is nothing on disk to consult. The key table is not a substitute: the reference
    /// cache holds 1,587 keys for 659 encrypted archives, so "a key exists for this archive" and
    /// "this archive is encrypted" are different statements, and the plaintext case below is the
    /// one that punishes treating them as the same.
    /// </remarks>
    public class RSCacheXteaWriteFileTests : IDisposable
    {
        private const int SectorSize = 520;   // RSSector.SIZE is static readonly, unusable in a const context

        /// <summary>The map square seeded below. Any id works - the key file is written to match.</summary>
        private const int ArchiveId = 0;

        /// <summary>
        /// The key revision 639 shipped for index 5, archive 1962, taken from the OpenRS2 archive.
        /// It came from outside this project, as did the ciphertext it opens.
        /// </summary>
        private static readonly int[] FixtureKey = { 829329687, 2060676264, 581836269, -714741378 };

        private readonly string _dir;
        private readonly List<RSFileStore> _stores = new List<RSFileStore>();

        public RSCacheXteaWriteFileTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
            _dir = Path.Combine(Path.GetTempPath(), "fe-xtea-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>Reads a committed fixture from the test output directory.</summary>
        private static byte[] Fixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", name);
            Assert.True(File.Exists(path), "missing captured fixture: " + path);
            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// Seeds a cache whose map index holds a single archive, stored as the raw bytes given.
        /// </summary>
        /// <remarks>
        /// The map index is the only content index. A padding index above it used to be needed,
        /// because the store reported the highest non-meta index id under the name
        /// <c>GetIndexCount</c> and RSCache consumed it as a count, which put the highest index
        /// one past the end; the bound and the enumeration are separate members now.
        /// <para>
        /// The meta index is pre-sized to five empty records, because the reference table for
        /// index 5 lands at record 5 and <see cref="RSFileStore.Write"/> only ever appends
        /// contiguously. The empty records read back as absent reference tables, which is what a
        /// cache with unused indexes looks like anyway.
        /// </para>
        /// </remarks>
        /// <param name="storedContainer">The container bytes to place on disk verbatim.</param>
        private RSCache CreateCache(byte[] storedContainer)
        {
            //Sector 0 is burned: allocation derives the next free sector from the data
            //length, and sector id 0 is the end-of-chain marker.
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.dat2"), new byte[SectorSize]);
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.MAPS_INDEX), Array.Empty<byte>());
            File.WriteAllBytes(Path.Combine(_dir, "main_file_cache.idx" + RSConstants.META_INDEX),
                               new byte[RSConstants.MAPS_INDEX * RSIndex.SIZE]);

            var store = new RSFileStore(_dir);
            _stores.Add(store);

            store.Write(RSConstants.MAPS_INDEX, ArchiveId, new JagStream(storedContainer));
            store.Write(RSConstants.META_INDEX, RSConstants.MAPS_INDEX, EncodeReferenceTable());

            return new RSCache(store);
        }

        /// <summary>
        /// The map index's reference table: format 6, one archive, one file. Format 6 is the
        /// point - it has no per-archive flags byte, so it cannot record that the archive is
        /// encrypted even if something wanted to.
        /// </summary>
        private static JagStream EncodeReferenceTable()
        {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(ArchiveId);
            entry.SetVersion(1);
            entry.SetValidFileIds(new[] { 0 });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { { 0, new RSFileEntry(0) } });
            table.PutArchiveEntry(ArchiveId, entry);

            return new RSContainer(RSConstants.META_INDEX, RSConstants.MAPS_INDEX,
                                   RSConstants.GZIP_COMPRESSION, ReferenceTableCodec.Encode(table), 1337).Encode();
        }

        /// <summary>
        /// Writes a key file naming the seeded archive, and returns its path.
        /// </summary>
        /// <remarks>
        /// The committed OpenRS2 fixture names archive 1962, which cannot be seeded here without
        /// 1,961 archives in front of it, so the same key words are re-pointed at the archive
        /// this cache actually holds. The key is the part that has to be real.
        /// </remarks>
        private string WriteKeyFile()
        {
            string path = Path.Combine(_dir, "xteas.json");
            File.WriteAllText(path,
                "[ { \"index\": " + RSConstants.MAPS_INDEX + ", \"archive\": " + ArchiveId + ", \"keys\": [" +
                string.Join(", ", FixtureKey) + "] } ]");
            return path;
        }

        /// <summary>
        /// Commits the cache to a fresh directory and reopens it, so assertions run against bytes
        /// that made a full round trip through the file store.
        /// </summary>
        /// <param name="withKeys">
        /// Whether the reopened cache is given the key file. Reopening without it is how the
        /// tests below tell ciphertext from plaintext: only one of the two decodes.
        /// </param>
        private RSCache SaveAndReopen(RSCache cache, bool withKeys)
        {
            string outDir = Path.Combine(_dir, "out-" + Guid.NewGuid().ToString("N"));
            cache.WriteCache(outDir);

            var reopened = new RSFileStore(outDir);
            _stores.Add(reopened);

            var reopenedCache = new RSCache(reopened);
            if (withKeys)
                reopenedCache.LoadXTEAKeys(WriteKeyFile());
            return reopenedCache;
        }

        /// <summary>The bytes written over the map square by the tests that edit it.</summary>
        private static byte[] EditedPayload()
        {
            return Enumerable.Range(0, 300).Select(i => (byte) (i * 7)).ToArray();
        }

        // ===================================================================
        //  Encrypted archives
        // ===================================================================

        /// <summary>
        /// The seed itself: real ciphertext from a real cache, decrypting to the payload the
        /// cache actually holds. Without this the tests below could pass over bytes that were
        /// never encrypted in the first place.
        /// </summary>
        [Fact]
        public void SeededEncryptedArchive_ReadsBackAsThePlaintextPayload()
        {
            RSCache cache = CreateCache(Fixture("archive-xtea.container.bin"));
            cache.LoadXTEAKeys(WriteKeyFile());

            Assert.Equal(Fixture("archive-xtea.payload.bin"), cache.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0).ToArray());
        }

        /// <summary>
        /// Decrypt, edit, save, decrypt again. An edited map archive has to come back out under
        /// the key it went in under - writing the plaintext instead leaves a cache that looks
        /// entirely healthy here and is unreadable at the client.
        /// </summary>
        [Fact]
        public void WriteFile_EncryptedArchive_RoundTripsThroughItsKey()
        {
            RSCache cache = CreateCache(Fixture("archive-xtea.container.bin"));
            cache.LoadXTEAKeys(WriteKeyFile());
            byte[] edited = EditedPayload();

            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(edited));
            RSCache reopened = SaveAndReopen(cache, withKeys: true);

            Assert.Equal(edited, reopened.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0).ToArray());
        }

        /// <summary>
        /// The other half of the round trip, and the half that actually catches the defect: an
        /// archive written back as plaintext reads perfectly well without a key, so the test
        /// above passes on its own either way.
        /// </summary>
        [Fact]
        public void WriteFile_EncryptedArchive_IsNotReadableWithoutItsKey()
        {
            RSCache cache = CreateCache(Fixture("archive-xtea.container.bin"));
            cache.LoadXTEAKeys(WriteKeyFile());

            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(EditedPayload()));
            RSCache reopened = SaveAndReopen(cache, withKeys: false);

            Assert.ThrowsAny<Exception>(() => reopened.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0));
        }

        /// <summary>
        /// The CRC in a reference table covers the STORED bytes. For an encrypted archive that is
        /// the ciphertext, so a CRC taken over the plaintext is wrong even when the payload
        /// itself was written correctly - and it is wrong in a way only the client notices.
        /// </summary>
        [Fact]
        public void WriteFile_EncryptedArchive_ChecksumsTheStoredCiphertext()
        {
            RSCache cache = CreateCache(Fixture("archive-xtea.container.bin"));
            cache.LoadXTEAKeys(WriteKeyFile());

            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(EditedPayload()));
            RSCache reopened = SaveAndReopen(cache, withKeys: true);

            byte[] stored = reopened.LoadContainer(RSConstants.MAPS_INDEX, ArchiveId).ToArray();
            int trailer = reopened.GetContainer(RSConstants.MAPS_INDEX, ArchiveId).GetVersion() != -1 ? 2 : 0;
            uint expected = CRC32Helper.ComputeCrc32(stored.AsSpan(0, stored.Length - trailer));

            Assert.Equal(expected,
                unchecked((uint) reopened.GetReferenceTable(RSConstants.MAPS_INDEX).GetArchiveEntry(ArchiveId).GetCrc()));
        }

        /// <summary>
        /// Saving a decrypted archive with no key to re-encrypt it has to fail, loudly. The
        /// alternative is a save that reports success and silently destroys the map square,
        /// which is the whole failure mode being guarded against - so a guess in the safe-looking
        /// direction is not an improvement on a guess in the other one.
        /// </summary>
        [Fact]
        public void WriteFile_DecryptedArchiveWithNoKeyAvailable_ThrowsRatherThanWritingPlaintext()
        {
            //A cache with no key table at all, holding a container that was decrypted elsewhere
            RSCache cache = CreateCache(Fixture("archive-xtea.container.bin"));
            RSContainer decrypted = RSContainer.Decode(new JagStream(Fixture("archive-xtea.container.bin")), FixtureKey);
            cache.UpdateRSContainer(RSConstants.MAPS_INDEX, ArchiveId, decrypted);

            var ex = Assert.Throws<InvalidOperationException>(
                () => cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(EditedPayload())));

            Assert.Contains("plaintext", ex.Message);

            //And it threw before staging anything - a half-written archive here is the same
            //corruption arriving by another route.
            Assert.Equal(Fixture("archive-xtea.container.bin"),
                         cache.LoadContainer(RSConstants.MAPS_INDEX, ArchiveId).ToArray());
        }

        /// <summary>
        /// Saving an encrypted archive without editing it must leave the ciphertext exactly where
        /// it was. Re-encryption is deterministic, so this would survive a re-encode of the
        /// payload - but the container around it is gzip, and deflate is not canonical, so a
        /// re-encode produces different stored bytes and therefore a different CRC for a map
        /// square nobody touched.
        /// </summary>
        [Fact]
        public void WriteFile_UnmodifiedEncryptedArchive_StaysEncryptedAndByteIdentical()
        {
            byte[] stored = Fixture("archive-xtea.container.bin");
            RSCache cache = CreateCache(stored);
            cache.LoadXTEAKeys(WriteKeyFile());

            byte[] tableBefore = cache.LoadContainer(RSConstants.META_INDEX, RSConstants.MAPS_INDEX).ToArray();

            byte[] unchanged = cache.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0).ToArray();
            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(unchanged));

            Assert.Equal(stored, cache.LoadContainer(RSConstants.MAPS_INDEX, ArchiveId).ToArray());
            Assert.Equal(tableBefore, cache.LoadContainer(RSConstants.META_INDEX, RSConstants.MAPS_INDEX).ToArray());

            //And it is still ciphertext, not plaintext that happens to be the same length
            RSCache reopened = SaveAndReopen(cache, withKeys: false);
            Assert.ThrowsAny<Exception>(() => reopened.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0));
        }

        /// <summary>
        /// Reusing the stored bytes sidesteps encryption altogether, so an archive that was
        /// decrypted elsewhere and saved unedited no longer needs a key at all - where an edit to
        /// the same archive still refuses to write, because that genuinely cannot be done without
        /// one. The two mechanisms compose rather than compete: <c>ResolveWriteKey</c> guards the
        /// encode, and an unchanged save never reaches an encode.
        /// </summary>
        [Fact]
        public void WriteFile_UnmodifiedDecryptedArchiveWithNoKeyAvailable_WritesNothingRatherThanThrowing()
        {
            byte[] stored = Fixture("archive-xtea.container.bin");

            //A cache with no key table at all, holding a container that was decrypted elsewhere
            RSCache cache = CreateCache(stored);
            RSContainer decrypted = RSContainer.Decode(new JagStream(stored), FixtureKey);
            cache.UpdateRSContainer(RSConstants.MAPS_INDEX, ArchiveId, decrypted);

            byte[] tableBefore = cache.LoadContainer(RSConstants.META_INDEX, RSConstants.MAPS_INDEX).ToArray();

            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0,
                            new JagStream(Fixture("archive-xtea.payload.bin")));

            Assert.Equal(stored, cache.LoadContainer(RSConstants.MAPS_INDEX, ArchiveId).ToArray());
            Assert.Equal(tableBefore, cache.LoadContainer(RSConstants.META_INDEX, RSConstants.MAPS_INDEX).ToArray());
        }

        // ===================================================================
        //  Plaintext archives
        // ===================================================================

        /// <summary>
        /// An archive that was stored plaintext stays plaintext, even though the key table names
        /// it. Key dumps cover a whole build while a cache may have had archives decrypted in
        /// place, so encrypting on the strength of a key existing destroys exactly as much data
        /// as failing to encrypt does.
        /// </summary>
        [Fact]
        public void WriteFile_PlaintextArchiveWithAKeyInTheTable_StaysPlaintext()
        {
            byte[] payload = Fixture("archive-xtea.payload.bin");
            RSCache cache = CreateCache(new RSContainer(RSConstants.MAPS_INDEX, ArchiveId,
                                                        RSConstants.GZIP_COMPRESSION, new JagStream(payload), 1337)
                                        .Encode().ToArray());
            cache.LoadXTEAKeys(WriteKeyFile());
            byte[] edited = EditedPayload();

            //The read path tries the key, finds it does not fit, and falls back to plaintext
            Assert.False(cache.GetContainer(RSConstants.MAPS_INDEX, ArchiveId).StoredEncrypted);

            cache.WriteFile(RSConstants.MAPS_INDEX, ArchiveId, 0, new JagStream(edited));
            RSCache reopened = SaveAndReopen(cache, withKeys: false);

            Assert.Equal(edited, reopened.ReadFile(RSConstants.MAPS_INDEX, ArchiveId, 0).ToArray());
        }
    }
}
