using FlashEditor;
using FlashEditor.cache;
using FlashEditor.Utils;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Cache
{
    public class CodecTests
    {
        public CodecTests()
        {
            // Disable blocking debug prompts during test execution
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }
        [Fact]
        public void ReferenceTable_EncodeDecode_RoundTrips()
        {
            // Arrange - build a minimal reference table with one entry and one child
            var table = new RSReferenceTable
            {
                format = 6,
                version = 1,
                flags = 0
            };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(new int[] { 0 });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>
            {
                { 0, new RSFileEntry(0) }
            });

            table.PutArchiveEntry(0, entry);

            // Act
            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = ReferenceTableCodec.Encode(decoded);

            // Assert
            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        /// <summary>
        ///     File ids are delta-encoded from the ids themselves, so a sparse archive must
        ///     come back with the same ids it went in with. Emitting the ordinal position
        ///     instead renumbers every file to 0..n-1 and silently repoints the archive's
        ///     contents at the wrong ids.
        /// </summary>
        [Fact]
        public void ReferenceTable_SparseFileIds_SurviveRoundTrip()
        {
            var table = new RSReferenceTable { format = 6, version = 1, flags = 0 };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetValidFileIds(new[] { 0, 5, 9 });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>
            {
                { 0, new RSFileEntry(0) },
                { 5, new RSFileEntry(5) },
                { 9, new RSFileEntry(9) }
            });
            table.PutArchiveEntry(0, entry);

            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));

            Assert.Equal(new[] { 0, 5, 9 }, decoded.GetArchiveEntry(0).GetValidFileIds());
            Assert.Equal(new[] { 0, 5, 9 }, decoded.GetArchiveEntry(0).GetFileEntries().Keys);
            Assert.Equal(encoded.ToArray(), ReferenceTableCodec.Encode(decoded).ToArray());
        }

        /// <summary>
        ///     Format 7 carries one flags byte per archive, bit 0 being the XTEA marker.
        ///     Decoding reads it, so encoding has to write it back - re-encoding a format-7
        ///     table without it shifts every following field and corrupts the table. The
        ///     cache re-encodes the table on every edit, so this fires on the first save.
        /// </summary>
        [Fact]
        public void ReferenceTable_Format7_PreservesPerArchiveXteaFlag()
        {
            var table = new RSReferenceTable { format = 7, version = 1, flags = 0 };

            foreach (int archiveId in new[] { 0, 1 })
            {
                var entry = new RSArchiveEntry(archiveId);
                entry.SetVersion(1);
                entry.UsesXtea = archiveId == 1;
                entry.SetValidFileIds(new[] { 0 });
                entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { { 0, new RSFileEntry(0) } });
                table.PutArchiveEntry(archiveId, entry);
            }

            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));

            Assert.False(decoded.GetArchiveEntry(0).UsesXtea);
            Assert.True(decoded.GetArchiveEntry(1).UsesXtea);
            Assert.Equal(1, decoded.GetArchiveEntry(1).GetVersion());
            Assert.Equal(new[] { 0 }, decoded.GetArchiveEntry(1).GetValidFileIds());
            Assert.Equal(encoded.ToArray(), ReferenceTableCodec.Encode(decoded).ToArray());
        }

        /// <summary>
        ///     Only bit 0 of the format-7 archive-flags byte has a known meaning (XTEA), but
        ///     the byte has to round trip whole. Rebuilding it from a single bool zeroes every
        ///     other bit a real table carries, and the cache re-encodes the table on every
        ///     edit, so the loss is permanent after one save.
        /// </summary>
        /// <param name="archiveFlags">
        ///     0x05 sets XTEA plus an unknown bit; 0x80 sets the high bit with XTEA clear,
        ///     the case that would expose sign trouble in the byte round trip; 0xFF sets all.
        /// </param>
        [Theory]
        [InlineData(0x05)]
        [InlineData(0x80)]
        [InlineData(0xFF)]
        public void ReferenceTable_Format7_PreservesUnknownArchiveFlagBits(int archiveFlags)
        {
            // Hand-built wire bytes rather than an Encode round trip: the claim is that bits
            // this codec has no name for survive, and only external bytes can express that.
            var wire = new JagStream();
            wire.WriteByte(7);                  // format
            wire.WriteInteger(1);               // table version
            wire.WriteByte(0);                  // table flags
            wire.WriteShort(1);                 // archive count
            wire.WriteShort(3);                 // archive id delta - archive 3
            wire.WriteInteger(0x12345678);      // archive crc
            wire.WriteInteger(4);               // archive version
            wire.WriteByte(archiveFlags);       // archive flags
            wire.WriteShort(1);                 // file count
            wire.WriteShort(0);                 // file id delta - file 0

            byte[] original = wire.ToArray();
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(original));

            Assert.Equal((byte)archiveFlags, decoded.GetArchiveEntry(3).ArchiveFlags);
            Assert.Equal((archiveFlags & RSArchiveEntry.FLAG_XTEA) != 0, decoded.GetArchiveEntry(3).UsesXtea);
            Assert.Equal(original, ReferenceTableCodec.Encode(decoded).ToArray());
        }

        /// <summary>
        ///     Clearing the XTEA marker must clear bit 0 alone. The two representations are
        ///     one field, so setting either one has to leave the rest of the byte intact.
        /// </summary>
        [Fact]
        public void ArchiveEntry_SettingUsesXtea_LeavesOtherFlagBitsIntact()
        {
            var entry = new RSArchiveEntry(0) { ArchiveFlags = 0x85 };

            entry.UsesXtea = false;
            Assert.Equal((byte)0x84, entry.ArchiveFlags);

            entry.UsesXtea = true;
            Assert.Equal((byte)0x85, entry.ArchiveFlags);
        }

        /// <summary>
        ///     The four flag bools are views over <see cref="RSReferenceTable.flags"/>, not
        ///     stored copies of it. Encode writes the flags byte from the number but decides
        ///     what follows it from the bools, so a table whose two representations had drifted
        ///     apart would ship a flags byte that did not describe its own payload - and every
        ///     field after the disagreement shifts, the same failure the format-7 flags byte
        ///     had.
        /// </summary>
        [Fact]
        public void ReferenceTable_FlagBools_TrackTheFlagsByte()
        {
            var table = new RSReferenceTable
            {
                flags = RSReferenceTable.FLAG_IDENTIFIERS | RSReferenceTable.FLAG_SIZES
            };

            Assert.True(table.hasIdentifiers);
            Assert.True(table.sizes);
            Assert.False(table.usesWhirlpool);
            Assert.False(table.entryHashes);

            table.flags = RSReferenceTable.FLAG_WHIRLPOOL | RSReferenceTable.FLAG_HASH;

            Assert.False(table.hasIdentifiers);
            Assert.False(table.sizes);
            Assert.True(table.usesWhirlpool);
            Assert.True(table.entryHashes);
        }

        /// <summary>
        ///     With FLAG_HASH set the table carries a 32-bit hash per archive. It is read
        ///     off the wire and has to be written back verbatim; recomputing it from the
        ///     entry's own stream - which the codec never populates - discards the value
        ///     the cache was shipped with and writes zero in its place.
        /// </summary>
        [Fact]
        public void ReferenceTable_EntryHashes_SurviveRoundTrip()
        {
            var table = new RSReferenceTable
            {
                format = 6,
                version = 1,
                flags = RSReferenceTable.FLAG_HASH
            };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetHash(unchecked((int)0xDEADBEEF));
            entry.SetValidFileIds(new[] { 0 });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { { 0, new RSFileEntry(0) } });
            table.PutArchiveEntry(0, entry);

            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));

            Assert.Equal(unchecked((int)0xDEADBEEF), (int)decoded.GetArchiveEntry(0).GetHash());
            Assert.Equal(encoded.ToArray(), ReferenceTableCodec.Encode(decoded).ToArray());
        }

        /// <summary>
        ///     With FLAG_IDENTIFIERS set the table carries a name hash per archive and per
        ///     file. Both must round trip; decoding the per-file one into a different field
        ///     from the one encoding reads loses every file name on the first save.
        /// </summary>
        [Fact]
        public void ReferenceTable_Identifiers_SurviveRoundTripForArchivesAndFiles()
        {
            var table = new RSReferenceTable
            {
                format = 6,
                version = 1,
                flags = RSReferenceTable.FLAG_IDENTIFIERS
            };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(1);
            entry.SetIdentifier(0x11223344);
            entry.SetValidFileIds(new[] { 0 });

            var file = new RSFileEntry(0);
            file.SetIdentifier(0x55667788);
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry> { { 0, file } });
            table.PutArchiveEntry(0, entry);

            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));

            Assert.Equal(0x11223344, decoded.GetArchiveEntry(0).GetIdentifier());
            Assert.Equal(0x55667788, decoded.GetArchiveEntry(0).GetFileEntries()[0].GetIdentifier());
            Assert.Equal(encoded.ToArray(), ReferenceTableCodec.Encode(decoded).ToArray());
        }

        /// <summary>
        ///     With FLAG_SIZES set the table carries a compressed and an uncompressed size per
        ///     archive, written between the whirlpool digests and the archive versions. The
        ///     write path recomputes both on every edit, so the pair has to survive the round
        ///     trip - and has to sit where the decoder looks for it, or every field after it
        ///     shifts.
        /// </summary>
        [Fact]
        public void ReferenceTable_Sizes_SurviveRoundTrip()
        {
            var table = new RSReferenceTable
            {
                format = 6,
                version = 1,
                flags = RSReferenceTable.FLAG_SIZES
            };

            var entry = new RSArchiveEntry(0);
            entry.SetVersion(7);
            entry.compressed = 1234;
            entry.uncompressed = 56789;
            entry.SetValidFileIds(new[] { 0, 4 });
            entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>
            {
                { 0, new RSFileEntry(0) },
                { 4, new RSFileEntry(4) }
            });
            table.PutArchiveEntry(0, entry);

            JagStream encoded = ReferenceTableCodec.Encode(table);
            RSReferenceTable decoded = ReferenceTableCodec.Decode(new JagStream(encoded.ToArray()));

            Assert.Equal(1234, decoded.GetArchiveEntry(0).compressed);
            Assert.Equal(56789, decoded.GetArchiveEntry(0).uncompressed);

            //Everything written after the sizes must still land where it was
            Assert.Equal(7, decoded.GetArchiveEntry(0).GetVersion());
            Assert.Equal(new[] { 0, 4 }, decoded.GetArchiveEntry(0).GetFileEntries().Keys.ToArray());
            Assert.Equal(encoded.ToArray(), ReferenceTableCodec.Encode(decoded).ToArray());
        }

        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_EncodeDecode_RoundTrips(byte compression)
        {
            var payload = new JagStream();
            payload.Write(new byte[] {1, 2, 3}, 0, 3);
            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, payload, 1);

            JagStream encoded = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_MultiFile_RoundTrips(byte compression)
        {
            var archive = new RSArchive();
            archive.PutFile(0, new JagStream(new byte[] { 1, 2 }));
            archive.PutFile(1, new JagStream(new byte[] { 3, 4, 5 }));

            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, archive.Encode(), 1);

            JagStream encoded = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(encoded.ToArray()));
            JagStream reencoded = decoded.Encode();

            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        [Fact]
        public void Archive_EncodeDecode_RoundTrips()
        {
            // Arrange
            var archive = new RSArchive();
            archive.PutFile(0, new JagStream(new byte[] { 1, 2, 3 }));
            archive.PutFile(1, new JagStream(new byte[] { 4, 5 }));

            // Act
            JagStream encoded = archive.Encode();
            RSArchive decoded = RSArchive.Decode(new JagStream(encoded.ToArray()), new int[] { 0, 1 });
            JagStream reencoded = decoded.Encode();

            // Assert
            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        /// <summary>
        ///     A single-file archive carries no size table and no trailing chunk-count
        ///     byte - the client's own unpacker special-cases a file count of 1 and takes
        ///     the whole container payload verbatim. Encoding must therefore emit the
        ///     payload alone, or every save cycle appends one more byte to the file.
        /// </summary>
        [Fact]
        public void Archive_SingleFile_EncodesPayloadOnly()
        {
            var archive = new RSArchive();
            archive.PutFile(7, new JagStream(new byte[] { 1, 2, 3 }));

            Assert.Equal(new byte[] { 1, 2, 3 }, archive.Encode().ToArray());
        }

        /// <summary>
        ///     The companion round trip: a single-file archive must survive
        ///     encode/decode/encode with the payload unchanged in length and content.
        /// </summary>
        [Fact]
        public void Archive_SingleFile_RoundTripsWithoutGrowing()
        {
            var archive = new RSArchive();
            archive.PutFile(7, new JagStream(new byte[] { 1, 2, 3 }));

            JagStream encoded = archive.Encode();
            RSArchive decoded = RSArchive.Decode(new JagStream(encoded.ToArray()), new int[] { 7 });
            JagStream reencoded = decoded.Encode();

            Assert.Equal(new byte[] { 1, 2, 3 }, decoded.GetFile(7).ToArray());
            Assert.Equal(encoded.ToArray(), reencoded.ToArray());
        }

        /// <summary>
        ///     A single-file archive has no trailer, so its last byte is file data. Decoding
        ///     one must not adopt that byte as a chunk count - adding a second file to the
        ///     archive would then write that many copies of the size table and corrupt it.
        /// </summary>
        [Fact]
        public void Archive_FileAddedToDecodedSingleFileArchive_RoundTrips()
        {
            var original = new RSArchive();
            original.PutFile(7, new JagStream(new byte[] { 1, 2, 3 }));

            RSArchive decoded = RSArchive.Decode(new JagStream(original.Encode().ToArray()), new int[] { 7 });
            decoded.PutFile(8, new JagStream(new byte[] { 4, 5 }));

            RSArchive reloaded = RSArchive.Decode(new JagStream(decoded.Encode().ToArray()), new int[] { 7, 8 });

            Assert.Equal(new byte[] { 1, 2, 3 }, reloaded.GetFile(7).ToArray());
            Assert.Equal(new byte[] { 4, 5 }, reloaded.GetFile(8).ToArray());
        }

        /// <summary>
        ///     An index with no archives still gets a reference table, and a real 639 cache ships
        ///     one: a four byte format-5 stub. Sizing the identifier array from the highest
        ///     archive id threw on it, so the table describing an empty index took the whole
        ///     decode down rather than returning an empty table.
        /// </summary>
        [Fact]
        public void ReferenceTable_WithNoArchives_DecodesAndReEncodes()
        {
            //Format 5, so no version field follows the format byte
            byte[] wire = { 5, 0, 0, 0 };

            RSReferenceTable table = ReferenceTableCodec.Decode(new JagStream(wire));

            Assert.Equal(5, table.format);
            Assert.Empty(table.GetArchiveEntries());
            Assert.Equal(wire, ReferenceTableCodec.Encode(table).ToArray());
        }

        /// <summary>
        ///     Builds the wire bytes of a two file, three chunk archive by hand.
        /// </summary>
        /// <remarks>
        ///     A multi-chunk archive is stored chunk-major - chunk 0 of every file, then chunk 1
        ///     of every file - and the size table is delta-encoded across the files within a
        ///     chunk, restarting the running total on each chunk. File 0 is split 2/1/3 and file
        ///     1 is split 3/4/1, with byte values that make the interleaving visible.
        /// </remarks>
        private static byte[] ThreeChunkArchiveBytes()
        {
            var wire = new JagStream();

            //Payload, chunk-major
            wire.Write(new byte[] { 0x01, 0x02 }, 0, 2);              // chunk 0, file 0
            wire.Write(new byte[] { 0x11, 0x12, 0x13 }, 0, 3);        // chunk 0, file 1
            wire.Write(new byte[] { 0x03 }, 0, 1);                    // chunk 1, file 0
            wire.Write(new byte[] { 0x14, 0x15, 0x16, 0x17 }, 0, 4);  // chunk 1, file 1
            wire.Write(new byte[] { 0x04, 0x05, 0x06 }, 0, 3);        // chunk 2, file 0
            wire.Write(new byte[] { 0x18 }, 0, 1);                    // chunk 2, file 1

            //Size table: per chunk, the delta from the previous file's size in that chunk
            wire.WriteInteger(2); wire.WriteInteger(1);    // chunk 0 - sizes 2, 3
            wire.WriteInteger(1); wire.WriteInteger(3);    // chunk 1 - sizes 1, 4
            wire.WriteInteger(3); wire.WriteInteger(-2);   // chunk 2 - sizes 3, 1

            wire.WriteByte(3);                             // chunk count

            return wire.ToArray();
        }

        /// <summary>
        ///     Most multi-file archives in a real 639 cache are stored across three chunks, not
        ///     one. Decoding reassembles each file from its slices, so encoding has to put them
        ///     back the same way - laying the files out one after another instead reorders the
        ///     payload and writes a size table describing whole files rather than slices. Both
        ///     survive a decode by this codec, which is why only captured bytes catch it.
        /// </summary>
        [Fact]
        public void Archive_MultiChunk_ReEncodesToTheSameBytes()
        {
            byte[] original = ThreeChunkArchiveBytes();

            RSArchive decoded = RSArchive.Decode(new JagStream(original), new[] { 0, 1 });

            Assert.Equal(3, decoded.chunks);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, decoded.GetFile(0).ToArray());
            Assert.Equal(new byte[] { 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18 }, decoded.GetFile(1).ToArray());
            Assert.Equal(original, decoded.Encode().ToArray());
        }

        /// <summary>
        ///     Editing a file invalidates the chunk split it was decoded with, so the archive is
        ///     written back as a single chunk. That is a shape the client reads for any archive
        ///     whose trailer says one chunk, and it has to carry the edit plus every untouched
        ///     file intact - keeping the stale split would slice the files at offsets that no
        ///     longer exist.
        /// </summary>
        [Fact]
        public void Archive_MultiChunk_EditedArchiveFallsBackToASingleChunk()
        {
            RSArchive decoded = RSArchive.Decode(new JagStream(ThreeChunkArchiveBytes()), new[] { 0, 1 });

            decoded.PutFile(1, new JagStream(new byte[] { 0xAA, 0xBB }));

            Assert.Equal(1, decoded.chunks);

            RSArchive reloaded = RSArchive.Decode(new JagStream(decoded.Encode().ToArray()), new[] { 0, 1 });

            Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, reloaded.GetFile(0).ToArray());
            Assert.Equal(new byte[] { 0xAA, 0xBB }, reloaded.GetFile(1).ToArray());
        }

        /// <summary>
        ///     Ensures container headers remain byte‑accurate through multiple
        ///     encode/decode cycles for all compression methods and for
        ///     multi‑file archives.
        /// </summary>
        [Theory]
        [InlineData(RSConstants.NO_COMPRESSION)]
        [InlineData(RSConstants.BZIP2_COMPRESSION)]
        [InlineData(RSConstants.GZIP_COMPRESSION)]
        public void Container_RoundTrip_PreservesBytes(byte compression)
        {
            var payload = new JagStream(System.Text.Encoding.ASCII.GetBytes("hello"));
            var container = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 0,
                                            compression, payload, 1);

            JagStream initial = container.Encode();
            RSContainer decoded = RSContainer.Decode(new JagStream(initial.ToArray()));
            JagStream re = decoded.Encode();
            Assert.Equal(initial.ToArray(), re.ToArray());

            // Second cycle: decode the re-encoded bytes and verify they still produce identical output
            RSContainer again = RSContainer.Decode(new JagStream(re.ToArray()));
            JagStream re2 = again.Encode();
            Assert.Equal(initial.ToArray(), re2.ToArray());

            var archive = new RSArchive();
            archive.PutFile(0, payload);
            archive.PutFile(1, new JagStream(System.Text.Encoding.ASCII.GetBytes("bye")));

            var multi = new RSContainer(RSConstants.ITEM_DEFINITIONS_INDEX, 1,
                                        compression, archive.Encode(), 1);

            JagStream initArchive = multi.Encode();
            RSContainer decArchive = RSContainer.Decode(new JagStream(initArchive.ToArray()));
            JagStream reArchive = decArchive.Encode();
            Assert.Equal(initArchive.ToArray(), reArchive.ToArray());

            // Second cycle for multi-file archive
            RSContainer againArchive = RSContainer.Decode(new JagStream(reArchive.ToArray()));
            JagStream reArchive2 = againArchive.Encode();
            Assert.Equal(initArchive.ToArray(), reArchive2.ToArray());
        }
    }
}
