using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Definitions.Audio.Sfx2;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-14 codec against bytes committed alongside the tests, and against the one
    ///     rule in the format that no cache exercises.
    /// </summary>
    /// <remarks>
    ///     Two jobs, and they are different in kind.
    ///     <para>
    ///     The captured-bytes half is the part that runs on a machine with no cache: three real
    ///     groups from a revision-639 cache - the setup header, the shortest sample, and the
    ///     shortest looping sample - with every expected value read off those bytes rather than
    ///     produced by this project's decoder.
    ///     </para>
    ///     <para>
    ///     The synthetic half exists because <b>no packet in either supported cache reaches 255
    ///     bytes</b>, so the continuation byte of the base-255 length prefix is unreachable and a
    ///     byte-identity sweep over the whole index cannot tell a correct encoder from three
    ///     plausible wrong ones. <c>RealCacheSfx2Tests</c> measures and asserts that gap; this closes
    ///     it, against byte sequences written out by hand from <c>Node_Sub13.java:510-513</c>.
    ///     </para>
    /// </remarks>
    public class Sfx2CodecTests
    {
        /// <summary>Reads a committed fixture from the test output directory.</summary>
        /// <param name="name">The fixture file name.</param>
        /// <returns>Its bytes.</returns>
        private static byte[] Fixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "RealCache", name);
            Assert.True(File.Exists(path), "missing captured fixture: " + path);
            return File.ReadAllBytes(path);
        }

        // ===================================================================
        //  Captured bytes
        // ===================================================================

        /// <summary>
        ///     Group 0's leading fields parse to what the cache holds, and the group writes back
        ///     verbatim.
        /// </summary>
        /// <remarks>
        ///     The sync pattern is the load-bearing assertion. 0x564342 only assembles from bytes
        ///     2..4 under the client's LSB-first bit order, so finding it confirms the bit reader and
        ///     the claim that this group is a Vorbis setup header in one go. The client itself skips
        ///     those 24 bits without checking them (<c>Class71.java:44</c>), so nothing on the read
        ///     path would notice if they were wrong.
        /// </remarks>
        [Fact]
        public void CapturedSetupHeader_ParsesItsLeadingFieldsAndWritesBackVerbatim()
        {
            byte[] stored = Fixture("sfx2-group0-setup.payload.bin");

            var setup = new Sfx2SetupHeader { Id = 0 }.Decode(new JagStream(stored));

            Assert.Equal(1024, setup.Blocksize0);
            Assert.Equal(1024, setup.Blocksize1);
            Assert.Equal(23, setup.CodebookCount);
            Assert.Equal(Sfx2SetupHeader.VorbisCodebookSyncPattern, setup.FirstCodebookSync);
            Assert.True(setup.HasCodebookSyncPattern);

            Assert.Equal(stored, setup.Encode().ToArray());
        }

        /// <summary>
        ///     The shortest sample in the cache decodes to the values its bytes hold and re-encodes
        ///     to them.
        /// </summary>
        [Fact]
        public void CapturedSample_DecodesToTheValuesItsBytesHold()
        {
            byte[] stored = Fixture("sfx2-group2901-sample.payload.bin");

            var stream = new JagStream(stored);
            var sample = new Sfx2Sample { Id = 2901 }.Decode(stream);

            Assert.Equal(22050, sample.SampleRate);
            Assert.Equal(1103, sample.PcmByteCount);
            Assert.Equal(0, sample.LoopStart);
            Assert.Equal(1103, sample.LoopEnd);
            Assert.False(sample.IsLooping);
            Assert.Equal(new[] { 10, 12, 11, 1 }, sample.PacketLengths);

            //The four lengths plus their one-byte prefixes are the whole payload past the header.
            Assert.Equal(stored.Length, stream.Position);
            Assert.Equal(stored.Length - Sfx2Sample.HeaderBytes - sample.PacketCount, sample.PacketByteCount);

            Assert.Equal(stored, sample.Encode().ToArray());
        }

        /// <summary>
        ///     A looping sample stores the bitwise complement of its loop end, which is not its
        ///     negation.
        /// </summary>
        /// <remarks>
        ///     The two rules differ by exactly one, so an encoder using <c>-</c> would produce a file
        ///     of the right length whose loop point is one PCM byte out. This fixture settles it
        ///     against shipped bytes: the stored int32 is -23919 and the record's PCM length is
        ///     23918, which is the complement and not the negation.
        /// </remarks>
        [Fact]
        public void CapturedLoopingSample_ComplementsItsLoopEndRatherThanNegatingIt()
        {
            byte[] stored = Fixture("sfx2-group568-looping.payload.bin");
            int storedLoopEnd = BinaryPrimitives.ReadInt32BigEndian(stored.AsSpan(12));

            var sample = new Sfx2Sample { Id = 568 }.Decode(new JagStream(stored));

            Assert.True(storedLoopEnd < 0, "this fixture is meant to be a looping record");
            Assert.True(sample.IsLooping);
            Assert.Equal(~storedLoopEnd, sample.LoopEnd);
            Assert.NotEqual(-storedLoopEnd, sample.LoopEnd);

            //The complement lands on the record's own PCM length, which is what makes it the right
            //rule rather than merely a self-consistent one.
            Assert.Equal(sample.PcmByteCount, sample.LoopEnd);

            //A non-looping record's loop points are meaningful too, so this one keeps its start.
            Assert.Equal(19672, sample.LoopStart);

            Assert.Equal(stored, sample.Encode().ToArray());
        }

        /// <summary>
        ///     The entry point sends group 0 to the setup header and every other group to the sample
        ///     reader, as the client does.
        /// </summary>
        [Fact]
        public void EntryDecode_DispatchesGroupZeroToTheSetupHeader()
        {
            Sfx2Entry setup = Sfx2Entry.Decode(0, new JagStream(Fixture("sfx2-group0-setup.payload.bin")));
            Sfx2Entry sample = Sfx2Entry.Decode(2901, new JagStream(Fixture("sfx2-group2901-sample.payload.bin")));

            Assert.IsType<Sfx2SetupHeader>(setup);
            Assert.IsType<Sfx2Sample>(sample);
            Assert.Equal(0, setup.Id);
            Assert.Equal(2901, sample.Id);
        }

        // ===================================================================
        //  The rule no cache exercises
        // ===================================================================

        /// <summary>
        ///     The packet length prefix is base-255 and continues on 255, so 255 costs two bytes and
        ///     510 costs three.
        /// </summary>
        /// <remarks>
        ///     Every expected sequence below is what <c>Node_Sub13.java:510-513</c> reads back as the
        ///     stated length, written out by hand. Both directions are checked against those bytes,
        ///     so this cannot pass by the writer and the reader agreeing with each other about the
        ///     wrong answer.
        ///     <para>
        ///     254 and 255 are the pair that matters: they are one apart and the encoders that get
        ///     this wrong all agree below 255.
        ///     </para>
        /// </remarks>
        [Theory]
        [InlineData(0, new byte[] { 0x00 })]
        [InlineData(1, new byte[] { 0x01 })]
        [InlineData(254, new byte[] { 0xFE })]
        [InlineData(255, new byte[] { 0xFF, 0x00 })]
        [InlineData(256, new byte[] { 0xFF, 0x01 })]
        [InlineData(509, new byte[] { 0xFF, 0xFE })]
        [InlineData(510, new byte[] { 0xFF, 0xFF, 0x00 })]
        [InlineData(511, new byte[] { 0xFF, 0xFF, 0x01 })]
        [InlineData(1000, new byte[] { 0xFF, 0xFF, 0xFF, 0xEB })]
        public void PacketLengthPrefix_IsBase255AndContinuesOn255(int length, byte[] expected)
        {
            var written = new JagStream();
            Sfx2Sample.WritePacketLength(written, length);
            Assert.Equal(expected, written.ToArray());

            var read = new JagStream(expected);
            Assert.Equal(length, Sfx2Sample.ReadPacketLength(read));
            Assert.Equal(expected.Length, read.Position);
        }

        /// <summary>
        ///     A record whose packets need a continuation byte round-trips against bytes built by
        ///     hand.
        /// </summary>
        /// <remarks>
        ///     The whole point of the test. No such record exists in either supported cache, so this
        ///     is the only thing standing between the encoder and a record that is silently corrupt
        ///     the first time a user imports audio with a packet of 255 bytes or more. The expected
        ///     bytes are assembled from the client's reader rather than from
        ///     <see cref="Sfx2Sample.Encode"/>.
        /// </remarks>
        [Fact]
        public void ASampleWithLongPackets_RoundTripsAgainstHandBuiltBytes()
        {
            int[] lengths = { 254, 255, 510 };
            byte[] built = BuildRecord(sampleRate: 22050, pcmByteCount: 4321, loopStart: 7,
                storedLoopEnd: ~9999, lengths: lengths);

            var stream = new JagStream(built);
            var sample = new Sfx2Sample { Id = 1 }.Decode(stream);

            Assert.Equal(built.Length, stream.Position);
            Assert.Equal(22050, sample.SampleRate);
            Assert.Equal(4321, sample.PcmByteCount);
            Assert.Equal(7, sample.LoopStart);
            Assert.True(sample.IsLooping);
            Assert.Equal(9999, sample.LoopEnd);
            Assert.Equal(lengths, sample.PacketLengths);

            for (int i = 0; i < lengths.Length; i++)
            {
                byte[] packet = sample.Packet(i).ToArray();
                Assert.Equal(lengths[i], packet.Length);
                for (int b = 0; b < packet.Length; b++)
                    Assert.Equal(PacketByte(i, b), packet[b]);
            }

            Assert.Equal(built, sample.Encode().ToArray());
        }

        /// <summary>
        ///     Rebuilding a record from packets handed in produces the same bytes as reading it did.
        /// </summary>
        /// <remarks>
        ///     The import path in miniature: <see cref="Sfx2Sample.SetPackets"/> is how new audio
        ///     gets in, and it has to lay the length prefixes out the same way the decoder found
        ///     them. Long packets again, for the same reason.
        /// </remarks>
        [Fact]
        public void SetPackets_LaysOutTheSameBytesTheDecoderRead()
        {
            int[] lengths = { 254, 255, 510 };
            byte[] built = BuildRecord(sampleRate: 22050, pcmByteCount: 4321, loopStart: 7,
                storedLoopEnd: ~9999, lengths: lengths);

            var packets = new byte[lengths.Length][];
            for (int i = 0; i < lengths.Length; i++)
            {
                packets[i] = new byte[lengths[i]];
                for (int b = 0; b < lengths[i]; b++)
                    packets[i][b] = PacketByte(i, b);
            }

            var sample = new Sfx2Sample
            {
                Id = 1,
                SampleRate = 22050,
                PcmByteCount = 4321,
                LoopStart = 7,
                LoopEnd = 9999,
                IsLooping = true
            };
            sample.SetPackets(packets);

            Assert.Equal(built, sample.Encode().ToArray());
        }

        /// <summary>
        ///     A looping record with a negative loop end is refused rather than written as one that
        ///     does not loop.
        /// </summary>
        /// <remarks>
        ///     The flag lives in the sign of the stored int32, so complementing a negative end yields
        ///     a non-negative one and the record silently stops looping. Nothing in the cache can
        ///     produce that state - it only arrives through an edit - which is why it is asserted
        ///     here rather than swept for.
        /// </remarks>
        [Fact]
        public void Encode_RefusesALoopingRecordWithANegativeLoopEnd()
        {
            var sample = new Sfx2Sample { Id = 1, SampleRate = 22050, IsLooping = true, LoopEnd = -1 };

            Assert.Throws<InvalidOperationException>(() => sample.Encode());
        }

        /// <summary>
        ///     A packet count the remaining bytes cannot possibly hold is refused rather than
        ///     allocated for.
        /// </summary>
        /// <remarks>
        ///     Every packet costs at least the one byte of its own length prefix, so the count is
        ///     bounded by the bytes left. Without the bound a truncated or mis-addressed file reaches
        ///     the decoder as a request to allocate up to two billion entries.
        /// </remarks>
        [Fact]
        public void Decode_RefusesAPacketCountTheRecordCannotHold()
        {
            var header = new JagStream();
            header.WriteInteger(22050);
            header.WriteInteger(0);
            header.WriteInteger(0);
            header.WriteInteger(0);
            header.WriteInteger(int.MaxValue);

            Assert.Throws<InvalidDataException>(() =>
                new Sfx2Sample { Id = 1 }.Decode(new JagStream(header.ToArray())));
        }

        // ===================================================================
        //  The bit reader
        // ===================================================================

        /// <summary>
        ///     Bits fill each byte from its low end upward, and a field crossing a byte boundary
        ///     takes its low bits from the earlier byte.
        /// </summary>
        /// <remarks>
        ///     Stated synthetically as well as against group 0, because the cache exercises only one
        ///     sequence of field widths and a reader can be wrong about the boundary case without
        ///     that sequence noticing. <c>Read(3)</c> then <c>Read(8)</c> over <c>FF 01</c> is the
        ///     case: five bits from the first byte as the low end, three from the second as the high
        ///     end, giving 63. A big-endian reader gives 248.
        /// </remarks>
        [Fact]
        public void BitReader_FillsEachByteFromItsLowBitUpward()
        {
            var withinAByte = new Sfx2BitReader(new byte[] { 0x8D, 0x01 });
            Assert.Equal(0xD, withinAByte.Read(4));
            Assert.Equal(0x8, withinAByte.Read(4));
            Assert.Equal(0x01, withinAByte.Read(8));

            var acrossABoundary = new Sfx2BitReader(new byte[] { 0xFF, 0x01 });
            Assert.Equal(7, acrossABoundary.Read(3));
            Assert.Equal(63, acrossABoundary.Read(8));

            var bitByBit = new Sfx2BitReader(new byte[] { 0x05 });
            Assert.Equal(new[] { 1, 0, 1, 0, 0, 0, 0, 0 }, ReadBits(bitByBit, 8));
            Assert.Equal(8, bitByBit.BitPosition);
        }

        /// <summary>Reading past the end of the buffer is reported rather than indexed into.</summary>
        [Fact]
        public void BitReader_RefusesToReadPastTheBuffer()
        {
            var reader = new Sfx2BitReader(new byte[] { 0xFF });

            Assert.True(reader.CanRead(8));
            Assert.False(reader.CanRead(9));
            Assert.Equal(0xFF, reader.Read(8));
            Assert.Throws<EndOfStreamException>(() => reader.ReadBit());
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        ///     Assembles a sample record the way the client's reader would take one apart.
        /// </summary>
        /// <remarks>
        ///     Written out field by field with the length prefixes spelled out, so the expected bytes
        ///     owe nothing to <see cref="Sfx2Sample.Encode"/>.
        /// </remarks>
        /// <param name="sampleRate">The record's first int32.</param>
        /// <param name="pcmByteCount">The record's second int32.</param>
        /// <param name="loopStart">The record's third int32.</param>
        /// <param name="storedLoopEnd">The record's fourth int32, complemented when it loops.</param>
        /// <param name="lengths">The packet lengths, in stream order.</param>
        /// <returns>The record's bytes.</returns>
        private static byte[] BuildRecord(int sampleRate, int pcmByteCount, int loopStart,
            int storedLoopEnd, int[] lengths)
        {
            var record = new List<byte>();
            AppendInt(record, sampleRate);
            AppendInt(record, pcmByteCount);
            AppendInt(record, loopStart);
            AppendInt(record, storedLoopEnd);
            AppendInt(record, lengths.Length);

            for (int i = 0; i < lengths.Length; i++)
            {
                int remaining = lengths[i];
                while (remaining >= 255)
                {
                    record.Add(0xFF);
                    remaining -= 255;
                }
                record.Add((byte) remaining);

                for (int b = 0; b < lengths[i]; b++)
                    record.Add(PacketByte(i, b));
            }

            return record.ToArray();
        }

        /// <summary>A byte pattern that differs per packet, so a mis-sliced packet is visible.</summary>
        /// <param name="packet">The packet's position in the record.</param>
        /// <param name="offset">The byte's position in the packet.</param>
        /// <returns>The byte.</returns>
        private static byte PacketByte(int packet, int offset)
        {
            return (byte) (packet * 37 + offset * 7 + 1);
        }

        private static void AppendInt(List<byte> record, int value)
        {
            record.Add((byte) (value >> 24));
            record.Add((byte) (value >> 16));
            record.Add((byte) (value >> 8));
            record.Add((byte) value);
        }

        private static int[] ReadBits(Sfx2BitReader reader, int count)
        {
            var bits = new int[count];
            for (int i = 0; i < count; i++)
                bits[i] = reader.ReadBit();
            return bits;
        }
    }
}
