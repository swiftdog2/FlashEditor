using FlashEditor.cache;
using FlashEditor.Cache.Util;
using FlashEditor.Cache.Util.Crypto;
using FlashEditor.Utils;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Pins the codec against bytes taken from a real revision-639 cache and committed
    ///     alongside the tests.
    /// </summary>
    /// <remarks>
    ///     <see cref="RealCacheConformanceTests"/> is the exhaustive version of this and skips
    ///     when no cache is present. These fixtures are the part that always runs: a few hundred
    ///     bytes the client shipped, so a misreading of the wire format that this encoder and
    ///     this decoder happen to agree on is still caught with no cache on the machine.
    ///     <para>
    ///     Every expected value below was read off the cache the fixture came from - the archive
    ///     CRCs in particular were computed by whatever wrote the cache, not by this project.
    ///     </para>
    /// </remarks>
    public class CapturedCacheBytesTests
    {
        /// <summary>Silences the codec's debug logging, which otherwise blocks on prompts.</summary>
        public CapturedCacheBytesTests()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        /// <summary>Reads a committed fixture from the test output directory.</summary>
        private static byte[] Fixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", name);
            Assert.True(File.Exists(path), "missing captured fixture: " + path);
            return File.ReadAllBytes(path);
        }

        /// <summary>
        ///     Strips the version trailer, leaving the span an archive CRC covers.
        /// </summary>
        /// <remarks>
        ///     The trailer length is derived rather than assumed to be two, for the same reason
        ///     the production code derives it: both trailer lengths occur in a real cache, and
        ///     hardcoding one here would quietly re-assert the thing these tests exist to
        ///     disprove.
        /// </remarks>
        private static ReadOnlySpan<byte> CrcSpan(byte[] storedContainer)
        {
            int headerLength = storedContainer[0] == RSConstants.NO_COMPRESSION ? 5 : 9;
            int compressedLength = ReadInt(storedContainer, 1);
            int payloadEnd = headerLength + compressedLength;
            return storedContainer.AsSpan(0, payloadEnd);
        }

        /// <summary>Reads a big-endian 32-bit integer.</summary>
        private static int ReadInt(byte[] data, int offset)
        {
            return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
        }

        // ===================================================================
        //  Reference table
        // ===================================================================

        /// <summary>
        ///     The reference table for index 28, captured whole. It must decode to the archives
        ///     and file ids the cache actually holds, and re-encode to the same bytes - the table
        ///     is rewritten on every edit, so anything the codec drops is lost on the first save.
        /// </summary>
        [Fact]
        public void ReferenceTable_FromCapturedBytes_ReEncodesToTheSameBytes()
        {
            byte[] payload = Fixture("reftable-index28.payload.bin");

            RSReferenceTable table = ReferenceTableCodec.Decode(new JagStream(payload));

            Assert.Equal(6, table.format);
            Assert.Equal(new[] { 1, 3 }, table.GetArchiveEntries().Keys.ToArray());
            Assert.Equal(new[] { 0 }, table.GetArchiveEntry(1).GetValidFileIds());
            Assert.Equal(new[] { 0 }, table.GetArchiveEntry(3).GetValidFileIds());

            Assert.Equal(payload, ReferenceTableCodec.Encode(table).ToArray());
        }

        /// <summary>
        ///     The same table as it is actually stored - inside a container. Decoding the
        ///     container has to yield exactly the payload the table codec expects, so the header
        ///     layout is pinned to shipped bytes rather than to this encoder.
        /// </summary>
        /// <remarks>
        ///     This container carries no version trailer, and that is not an accident of the
        ///     fixture: in the reference cache every one of the 102,467 archive containers ends
        ///     in a two byte version, and every reference-table container in the meta index ends
        ///     without one. Both shapes are real, which is why the trailer length has to be
        ///     derived from the container rather than assumed to be two - the archive CRC and the
        ///     <c>FLAG_SIZES</c> compressed size are both taken over the span it defines.
        /// </remarks>
        [Fact]
        public void Container_FromCapturedBytes_YieldsTheStoredPayloadAndHasNoVersionTrailer()
        {
            byte[] stored = Fixture("reftable-index28.container.bin");
            byte[] expected = Fixture("reftable-index28.payload.bin");

            RSContainer container = RSContainer.Decode(new JagStream(stored));

            Assert.Equal(expected, container.GetStream().ToArray());
            Assert.Equal(-1, container.GetVersion());

            //An uncompressed container is a one byte type plus a four byte length, so the
            //payload accounting has to leave nothing over for a trailer.
            Assert.Equal(stored.Length, 5 + expected.Length);
        }

        /// <summary>
        ///     The archive containers, by contrast, do carry a two byte version trailer. Holding
        ///     both shapes in the suite is what stops the trailer length quietly becoming a
        ///     constant again.
        /// </summary>
        /// <param name="fixture">Captured stored-container fixture.</param>
        [Theory]
        [InlineData("archive-3chunk.container.bin")]
        [InlineData("archive-singlefile.container.bin")]
        public void ArchiveContainer_FromCapturedBytes_CarriesAVersionTrailer(string fixture)
        {
            byte[] stored = Fixture(fixture);

            RSContainer container = RSContainer.Decode(new JagStream(stored));

            Assert.NotEqual(-1, container.GetVersion());
        }

        // ===================================================================
        //  Archives
        // ===================================================================

        /// <summary>
        ///     A real three chunk archive - index 0, archive 99, two files. Most multi-file
        ///     archives in a 639 cache are stored this way. Decoding reassembles each file from
        ///     its slices and encoding has to lay them back out chunk-major; writing the files
        ///     end to end instead produces a payload of exactly the same length with the bytes in
        ///     the wrong order, which no round-trip test can see.
        /// </summary>
        [Fact]
        public void MultiChunkArchive_FromCapturedBytes_ReEncodesToTheSameBytes()
        {
            byte[] payload = Fixture("archive-3chunk.payload.bin");

            RSArchive archive = RSArchive.Decode(new JagStream(payload), new[] { 0, 1 });

            Assert.Equal(3, archive.chunks);
            Assert.Equal(2, archive.FileCount());
            Assert.Equal(payload, archive.Encode().ToArray());
        }

        /// <summary>
        ///     A real single-file archive - index 0, archive 2435. The whole payload is the file:
        ///     no size table, no chunk-count byte. This is the rule that was argued from the
        ///     client's unpacker rather than demonstrated, and getting it wrong grows the file by
        ///     five bytes on every save.
        /// </summary>
        /// <remarks>
        ///     Decoding and re-encoding proves nothing on its own here: the codec special-cases a
        ///     file count of one at both ends, so <c>Encode(Decode(x)) == x</c> holds for any
        ///     bytes at all, trailer or no trailer. The load-bearing assertion is that these
        ///     captured bytes are *not* consistent with a trailer - a one-file trailer would end
        ///     in a chunk count of 1 preceded by an int equal to the remaining length. Finding
        ///     that they are not is what makes the no-trailer reading the only one the bytes
        ///     support.
        /// </remarks>
        [Fact]
        public void SingleFileArchive_FromCapturedBytes_CarriesNoTrailer()
        {
            byte[] payload = Fixture("archive-singlefile.payload.bin");

            //If this held a one-file trailer, these two would both be true
            bool endsInAChunkCountOfOne = payload[payload.Length - 1] == 1;
            bool precededByItsOwnLength =
                ReadInt(payload, payload.Length - 5) == payload.Length - 5;
            Assert.False(endsInAChunkCountOfOne && precededByItsOwnLength,
                "the captured payload parses as a one-file trailer, so it cannot demonstrate the no-trailer rule");

            RSArchive archive = RSArchive.Decode(new JagStream(payload), new[] { 0 });

            Assert.Equal(1, archive.FileCount());
            Assert.Equal(payload, archive.GetFile(0).ToArray());
            Assert.Equal(payload, archive.Encode().ToArray());
        }

        // ===================================================================
        //  XTEA
        // ===================================================================

        /// <summary>
        ///     A real encrypted map archive - index 5, archive 1962, map square 9616 - decrypted
        ///     with the key revision 639 actually shipped with, taken from the OpenRS2 archive.
        ///     Neither the ciphertext nor the key originates here, so this is the first thing in
        ///     the suite that shows the XTEA path is right rather than merely self-consistent.
        /// </summary>
        /// <remarks>
        ///     The encrypted region begins after the compression type and compressed length and
        ///     runs to the end of the payload, which puts the uncompressed-length field inside
        ///     it. Reading that field before deciphering yields four bytes of ciphertext, and
        ///     deciphering only the bytes after it shifts every 8-byte block by four so nothing
        ///     decrypts at all. Against this cache that was the difference between 598 archives
        ///     decrypting and none of them.
        /// </remarks>
        [Fact]
        public void EncryptedArchive_FromCapturedBytes_DecryptsWithTheShippedKey()
        {
            byte[] stored = Fixture("archive-xtea.container.bin");
            byte[] expected = Fixture("archive-xtea.payload.bin");

            XTEAKeyTable table = XTEAKeyTable.LoadFromFile(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", "xtea-keys-openrs2.json"));
            int[] key = table.GetKey(RSConstants.MAPS_INDEX, 1962);
            Assert.NotNull(key);

            RSContainer container = RSContainer.Decode(new JagStream(stored), key);

            Assert.Equal(expected, container.GetStream().ToArray());
        }

        /// <summary>
        ///     Without the key the same bytes must not decode. This is what stops the test above
        ///     passing for the wrong reason - if the archive were not really encrypted, it would
        ///     decode either way and would prove nothing about XTEA.
        /// </summary>
        [Fact]
        public void EncryptedArchive_FromCapturedBytes_DoesNotDecodeWithoutTheKey()
        {
            byte[] stored = Fixture("archive-xtea.container.bin");

            Assert.ThrowsAny<Exception>(() => RSContainer.Decode(new JagStream(stored)));
        }

        /// <summary>
        ///     An encrypted container has to survive a save. Encode must encipher the same span
        ///     Decode deciphers, or an edited map archive is written back in a shape the client
        ///     cannot read - and, because the uncompressed-length field sits inside that span,
        ///     the mistake is invisible until something tries to decrypt it.
        /// </summary>
        [Fact]
        public void EncryptedContainer_ReEncodesToSomethingThatDecryptsBack()
        {
            byte[] expected = Fixture("archive-xtea.payload.bin");
            int[] key = { 829329687, 2060676264, 581836269, -714741378 };

            var container = new RSContainer(RSConstants.MAPS_INDEX, 1962,
                                            RSConstants.GZIP_COMPRESSION, new JagStream(expected), 1);

            byte[] encoded = container.Encode(key).ToArray();

            //The ciphertext must not be readable without the key, and must be with it
            Assert.ThrowsAny<Exception>(() => RSContainer.Decode(new JagStream(encoded)));
            Assert.Equal(expected, RSContainer.Decode(new JagStream(encoded), key).GetStream().ToArray());
        }

        /// <summary>
        ///     OpenRS2 key exports name the index "archive" and the archive id "group". Reading
        ///     "archive" as the archive id collapses an entire dump onto archive 5 of index 5,
        ///     which silently yields a table that holds one key and looks like it loaded fine.
        /// </summary>
        [Fact]
        public void XteaKeyTable_ReadsTheOpenRs2Shape()
        {
            XTEAKeyTable table = XTEAKeyTable.LoadFromFile(
                Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", "xtea-keys-openrs2.json"));

            Assert.Equal(1, table.Count);
            Assert.NotNull(table.GetKey(RSConstants.MAPS_INDEX, 1962));   // "group"
            Assert.Null(table.GetKey(RSConstants.MAPS_INDEX, 5));         // "archive", the index
        }

        // ===================================================================
        //  CRC span
        // ===================================================================

        /// <summary>
        ///     The CRCs these two archives carry in their reference table, checked against the
        ///     stored container bytes. Nothing here produced them, so they are an independent
        ///     statement that the checksum covers the whole stored container minus its version
        ///     trailer - the span the write path recomputes both the CRC and the
        ///     <c>FLAG_SIZES</c> compressed size over.
        /// </summary>
        /// <param name="fixture">Captured stored-container fixture.</param>
        /// <param name="expectedCrc">The CRC the cache's reference table carries for it.</param>
        [Theory]
        [InlineData("archive-3chunk.container.bin", 0xAC91686CU)]
        [InlineData("archive-singlefile.container.bin", 0xF2A9AE39U)]
        public void ArchiveCrc_FromCapturedBytes_CoversTheContainerWithoutItsVersionTrailer(
            string fixture, uint expectedCrc)
        {
            byte[] stored = Fixture(fixture);

            Assert.Equal(expectedCrc, CRC32Helper.ComputeCrc32(CrcSpan(stored)));

            //And the whole container is not the span - otherwise the rule above says nothing
            Assert.NotEqual(expectedCrc, CRC32Helper.ComputeCrc32(stored));
        }

        /// <summary>
        ///     The span a CRC covers is the stored one, so for an encrypted archive it is the
        ///     ciphertext. Encoding the container without its key checksums the plaintext
        ///     instead, which writes a CRC no client will ever agree with.
        /// </summary>
        [Fact]
        public void ApplyCrcAndVersion_EncryptedContainer_ChecksumsTheStoredCiphertext()
        {
            int[] key = { 829329687, 2060676264, 581836269, -714741378 };
            RSContainer container = RSContainer.Decode(new JagStream(Fixture("archive-xtea.container.bin")), key);

            var table = new RSReferenceTable { format = 7, version = 1 };
            table.PutArchiveEntry(0, new RSArchiveEntry(0));

            CRC32Helper.ApplyCrcAndVersion(container, table, 0, key);

            uint expected = CRC32Helper.ComputeCrc32(CrcSpan(container.Encode(key).ToArray()));
            Assert.Equal(expected, unchecked((uint) table.GetArchiveEntry(0).GetCrc()));
            Assert.True(table.GetArchiveEntry(0).UsesXtea);
        }

        /// <summary>
        ///     And with no key it must refuse. The helper hands its caller the encoded container
        ///     back, so quietly encoding an encrypted archive in the clear does not merely
        ///     mis-checksum it - it produces the plaintext that then gets stored over a map
        ///     square the client still expects to decipher.
        /// </summary>
        [Fact]
        public void ApplyCrcAndVersion_EncryptedContainerWithNoKey_Throws()
        {
            int[] key = { 829329687, 2060676264, 581836269, -714741378 };
            RSContainer container = RSContainer.Decode(new JagStream(Fixture("archive-xtea.container.bin")), key);

            var table = new RSReferenceTable { format = 7, version = 1 };
            table.PutArchiveEntry(0, new RSArchiveEntry(0));

            Assert.Throws<InvalidOperationException>(
                () => CRC32Helper.ApplyCrcAndVersion(container, table, 0, null));
        }
    }
}
