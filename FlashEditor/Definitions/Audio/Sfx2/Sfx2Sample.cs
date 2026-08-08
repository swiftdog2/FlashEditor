using System;
using System.Collections.Generic;
using System.IO;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     One sound effect from index 14: a fixed header of five big-endian int32s, then a list of
    ///     raw Vorbis audio packets each prefixed by a base-255 length.
    /// </summary>
    /// <remarks>
    ///     Transcribed from <c>Node_Sub13.method1142</c> (Node_Sub13.java:494-518), which reads
    ///     sample rate (:497), PCM byte count (:498), loop start (:499), loop end (:500) and packet
    ///     count (:505), then walks the packets at :507-517. Every group on the index bar group 0
    ///     is one of these; group 0 is <see cref="Sfx2SetupHeader"/>.
    ///     <para>
    ///     One record is one mono effect. The client's output is 8-bit signed PCM -
    ///     <c>method1132</c> maps each float to <c>(int)(128 + f * 128)</c>, clamps it and writes
    ///     <c>byte(i - 128)</c> (:266-270) - wrapped as
    ///     <c>Node_Sub24_Sub1(sampleRate, pcm, loopStart, loopEnd, looping)</c> at :281, which is
    ///     what settles what each header field means.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2Sample : Sfx2Entry {
        /// <summary>Bytes of fixed header ahead of the packet list: five big-endian int32s.</summary>
        public const int HeaderBytes = 20;

        /// <summary>
        ///     The radix a packet length is written in, and the byte value that continues it.
        /// </summary>
        /// <remarks>
        ///     <c>do { b = readUnsignedByte(); length += b; } while (b &gt;= 255)</c>
        ///     (Node_Sub13.java:510-513). The continuation test is <c>&gt;=</c>, so 255 is two bytes
        ///     (<c>FF 00</c>) and 510 is three (<c>FF FF 00</c>); an encoder that stops at 254, or
        ///     one that continues only above 255, writes a length the client reads as a different
        ///     number and desynchronises the rest of the record.
        /// </remarks>
        public const int PacketLengthRadix = 255;

        private int[] packetLengths = Array.Empty<int>();
        private byte[] packetData = Array.Empty<byte>();

        /// <summary>
        ///     Where each packet starts in <see cref="packetData"/>, kept rather than recomputed.
        /// </summary>
        /// <remarks>
        ///     Derived state, and the reason it is stored is performance rather than fidelity:
        ///     without it, addressing packet <c>n</c> means summing <c>n</c> lengths, and a decoder
        ///     walking a record end to end turns into a quadratic scan over up to a couple of
        ///     thousand packets. Always rebuilt from the lengths, never read from the file.
        /// </remarks>
        private int[] packetOffsets = Array.Empty<int>();

        /// <summary>Playback rate in Hz, the record's first int32.</summary>
        /// <remarks>
        ///     Not to be confused with <c>method1132(new int[] { 22050 })</c> at
        ///     Class280.java:211, which looks like a rate and is a mutable PCM-byte budget for one
        ///     incremental decode call (<c>is[0] -= i - anInt3913</c>, Node_Sub13.java:273). Records
        ///     in both supported caches carry several distinct rates, so reading that literal as the
        ///     rate would be wrong on real data as well as in principle.
        /// </remarks>
        public int SampleRate { get; set; }

        /// <summary>
        ///     How many bytes of PCM the packets decode to, the record's second int32.
        /// </summary>
        /// <remarks>
        ///     Sizes the client's output buffer outright (<c>new byte[anInt3910]</c>,
        ///     Node_Sub13.java:250) and bounds every incremental decode against it (:262-263), so it
        ///     is a stored statement about the payload rather than a free field. Nothing here
        ///     recomputes it, because recomputing it would need the Vorbis decoder this codec
        ///     deliberately does not have.
        /// </remarks>
        public int PcmByteCount { get; set; }

        /// <summary>Loop point in PCM bytes, the record's third int32.</summary>
        /// <remarks>
        ///     Meaningful whether or not the record loops - non-looping records carry non-zero loop
        ///     points throughout both caches - so it must be written back as found rather than
        ///     zeroed when <see cref="IsLooping"/> is clear.
        /// </remarks>
        public int LoopStart { get; set; }

        /// <summary>
        ///     Loop end in PCM bytes, decoded from the record's fourth int32.
        /// </summary>
        /// <remarks>
        ///     Stored complemented when the record loops, so the stored int32 carries both this and
        ///     <see cref="IsLooping"/>. See <see cref="IsLooping"/> for why the pair is lossless.
        /// </remarks>
        public int LoopEnd { get; set; }

        /// <summary>
        ///     Whether playback loops, carried as the sign bit of the stored loop end.
        /// </summary>
        /// <remarks>
        ///     <c>if (loopEnd &lt; 0) { loopEnd = loopEnd ^ 0xffffffff; looping = true }</c>
        ///     (Node_Sub13.java:501-504). That is a bitwise complement, not a negation, so the
        ///     re-encode is <c>~LoopEnd</c> and not <c>-LoopEnd</c>.
        ///     <para>
        ///     The split is a bijection rather than a lossy normalisation, which is why the stored
        ///     int32 does not have to be kept alongside it: a stored negative always decodes to
        ///     (looping, a non-negative end) and complements back to itself, and a stored
        ///     non-negative always decodes to (not looping, itself). What that bijection does
        ///     <i>not</i> survive is an edit setting a negative <see cref="LoopEnd"/> on a looping
        ///     record, which <see cref="Encode"/> refuses rather than writing bytes that would read
        ///     back as a non-looping record.
        ///     </para>
        /// </remarks>
        public bool IsLooping { get; set; }

        /// <summary>How many Vorbis packets the record holds, the record's fifth int32.</summary>
        public int PacketCount => packetLengths.Length;

        /// <summary>Each packet's length in bytes, in stream order.</summary>
        public IReadOnlyList<int> PacketLengths => packetLengths;

        /// <summary>Total packet payload, excluding the length prefixes.</summary>
        public int PacketByteCount => packetData.Length;

        /// <summary>
        ///     How many bytes this record occupies once written back.
        /// </summary>
        /// <remarks>
        ///     Here rather than in the caller that wants to display it. A second implementation of
        ///     "header plus prefixes plus audio" would be a restatement of the encoder's own layout
        ///     rule, and since no packet in either cache reaches
        ///     <see cref="PacketLengthRadix"/> bytes, a byte-identity sweep over the whole index
        ///     cannot see the two disagree - every prefix in shipped data is one byte, which is what
        ///     both a correct and a wrong rule produce.
        ///     <para>
        ///     <see cref="Encode"/> sizes its buffer from this, so the encoder and the count are the
        ///     same statement rather than two that happen to agree, and
        ///     <c>Sfx2CodecTests.StoredByteCount_MatchesWhatEncodeWrites</c> pins them together over
        ///     hand-built packets that do cross the continuation boundary.
        ///     </para>
        /// </remarks>
        public int StoredByteCount {
            get {
                int prefixes = 0;
                foreach (int length in packetLengths)
                    prefixes += PacketLengthPrefixBytes(length);

                return HeaderBytes + prefixes + packetData.Length;
            }
        }

        /// <summary>
        ///     One packet's bytes, as a view over the record's contiguous payload.
        /// </summary>
        /// <remarks>
        ///     The packets are held as one buffer plus a length table rather than as an array of
        ///     arrays. That is the shape the file already has, and it matters at index scale: the
        ///     two caches hold over four hundred thousand packets between them, and one small array
        ///     each would cost more in object headers than the audio costs in bytes.
        /// </remarks>
        /// <param name="index">The packet's position in the record.</param>
        /// <returns>The packet's bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException">There is no packet at that position.</exception>
        public ReadOnlySpan<byte> Packet(int index) {
            if (index < 0 || index >= packetLengths.Length)
                throw new ArgumentOutOfRangeException(nameof(index), index,
                    "This record holds " + packetLengths.Length + " packets.");

            return packetData.AsSpan(packetOffsets[index], packetLengths[index]);
        }

        /// <summary>Copies every packet out as its own array, for a caller that wants them apart.</summary>
        /// <returns>The packets, in stream order.</returns>
        public byte[][] ToPacketArrays() {
            var packets = new byte[packetLengths.Length][];
            for (int i = 0; i < packetLengths.Length; i++)
                packets[i] = packetData.AsSpan(packetOffsets[i], packetLengths[i]).ToArray();
            return packets;
        }

        /// <summary>
        ///     Replaces the record's audio with a new packet list.
        /// </summary>
        /// <remarks>
        ///     Leaves <see cref="PcmByteCount"/>, <see cref="LoopStart"/> and <see cref="LoopEnd"/>
        ///     alone deliberately: they describe the decoded PCM, which only a Vorbis decoder can
        ///     relate to these packets. A caller importing audio has to set them, and this cannot do
        ///     it for them without inventing numbers.
        /// </remarks>
        /// <param name="packets">The packets to store, in playback order.</param>
        /// <exception cref="ArgumentNullException">The list or one of its packets is null.</exception>
        public void SetPackets(IReadOnlyList<byte[]> packets) {
            if (packets == null)
                throw new ArgumentNullException(nameof(packets));

            var lengths = new int[packets.Count];
            var offsets = new int[packets.Count];
            int total = 0;
            for (int i = 0; i < packets.Count; i++) {
                byte[] packet = packets[i] ?? throw new ArgumentNullException(nameof(packets),
                    "Packet " + i + " is null; an empty packet is a zero-length array.");
                lengths[i] = packet.Length;
                offsets[i] = total;
                total += packet.Length;
            }

            byte[] payload = new byte[total];
            for (int i = 0; i < packets.Count; i++)
                packets[i].CopyTo(payload, offsets[i]);

            packetLengths = lengths;
            packetOffsets = offsets;
            packetData = payload;
        }

        /// <summary>Reads one sample record from its file.</summary>
        /// <param name="stream">The group's single file, positioned at its start.</param>
        /// <returns>This record.</returns>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        /// <exception cref="InvalidDataException">The packet count cannot describe the bytes present.</exception>
        /// <exception cref="EndOfStreamException">The record is truncated.</exception>
        public Sfx2Sample Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            SampleRate = stream.ReadInt();
            PcmByteCount = stream.ReadInt();
            LoopStart = stream.ReadInt();

            int storedLoopEnd = stream.ReadInt();
            IsLooping = storedLoopEnd < 0;
            LoopEnd = IsLooping ? ~storedLoopEnd : storedLoopEnd;

            int count = stream.ReadInt();

            /* Every packet costs at least the one byte of its length prefix, so a count past the
               bytes remaining cannot be honoured. Rejecting it here keeps a corrupt or
               mis-addressed file from being answered with a multi-gigabyte allocation. */
            if (count < 0 || count > stream.Remaining())
                throw new InvalidDataException(
                    "Packet count " + count + " cannot be held by the " + stream.Remaining() +
                    " bytes left in the record.");

            var lengths = new int[count];
            var offsets = new int[count];
            byte[] payload = new byte[stream.Remaining()];
            int written = 0;

            for (int i = 0; i < count; i++) {
                int length = ReadPacketLength(stream);
                lengths[i] = length;
                offsets[i] = written;
                if (length == 0)
                    continue;

                if (written + length > payload.Length || stream.Read(payload, written, length) != length)
                    throw new EndOfStreamException(
                        "Packet " + i + " claims " + length + " bytes and the record does not hold them.");
                written += length;
            }

            if (written != payload.Length)
                Array.Resize(ref payload, written);

            packetLengths = lengths;
            packetOffsets = offsets;
            packetData = payload;
            return this;
        }

        /// <summary>Writes this record back to the bytes the group should store.</summary>
        /// <remarks>
        ///     Byte-identical to what was read for an unedited record. Nothing in the layout is
        ///     ambiguous: the header is five fixed int32s, and the base-255 length is canonical -
        ///     there is exactly one byte sequence per length, so there is no stored encoding to
        ///     remember alongside the value.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">A looping record carries a negative loop end.</exception>
        public override JagStream Encode() {
            if (IsLooping && LoopEnd < 0)
                throw new InvalidOperationException(
                    "A looping record stores ~LoopEnd, so a negative LoopEnd would be written as a " +
                    "non-negative int32 and read back as a record that does not loop. Loop end is a " +
                    "PCM byte offset and cannot be negative.");

            /* Sized from StoredByteCount rather than from a per-packet over-estimate, so the
               encoder and anything asking how big the record is are the same rule with two callers.
               It was HeaderBytes + count * 2 + payload, which is only an upper bound and left the
               real width stated in two places. */
            var buffer = new JagStream(StoredByteCount);
            buffer.WriteInteger(SampleRate);
            buffer.WriteInteger(PcmByteCount);
            buffer.WriteInteger(LoopStart);
            buffer.WriteInteger(IsLooping ? ~LoopEnd : LoopEnd);
            buffer.WriteInteger(packetLengths.Length);

            int offset = 0;
            foreach (int length in packetLengths) {
                WritePacketLength(buffer, length);
                if (length > 0)
                    buffer.Write(packetData, offset, length);
                offset += length;
            }

            return buffer.Flip();
        }

        /// <summary>
        ///     Writes a packet length in the client's base-255 form.
        /// </summary>
        /// <remarks>
        ///     <b>No group in either supported cache reaches the continuation branch</b> - the
        ///     longest packet in both is well under 255 bytes - so the byte-identity sweep cannot
        ///     defend this method past its first byte. That is exactly the shape of defect a sweep
        ///     hides: any of "stop at 254", "continue above 255" or "write two bytes big-endian"
        ///     would pass every group on the index and corrupt the first record anyone imported
        ///     longer audio into. It is pinned by a synthetic test against bytes built from the
        ///     client's reader instead.
        /// </remarks>
        /// <param name="buffer">Where to write.</param>
        /// <param name="length">The packet length in bytes.</param>
        /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
        public static void WritePacketLength(JagStream buffer, int length) {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "A packet length is non-negative.");

            /* Driven by PacketLengthPrefixBytes rather than by its own `while (length >= radix)`,
               so the number of bytes this writes and the number anything else predicts cannot
               drift apart: they are now one expression with two callers. The loop body is
               unchanged. */
            int prefixBytes = PacketLengthPrefixBytes(length);
            for (int i = 1; i < prefixBytes; i++) {
                buffer.WriteByte((byte) PacketLengthRadix);
                length -= PacketLengthRadix;
            }

            buffer.WriteByte((byte) length);
        }

        /// <summary>
        ///     How many bytes the length prefix of a packet of this size costs.
        /// </summary>
        /// <remarks>
        ///     <b>The single statement of the prefix width.</b> The client's reader continues on
        ///     <c>&gt;= 255</c> (Node_Sub13.java:510-513), so a length of exactly the radix costs two
        ///     bytes and not one - which is the off-by-one every wrong implementation of this shares,
        ///     and which no group in either cache would expose, the longest packet in both being 147
        ///     bytes.
        ///     <para>
        ///     Exposed rather than kept private because the editor shows the prefix width per packet,
        ///     and a display that restated the rule would be a second copy of it that the sweeps
        ///     could not tell apart from the first.
        ///     </para>
        /// </remarks>
        /// <param name="length">The packet length in bytes.</param>
        /// <returns>The number of bytes the prefix occupies.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
        public static int PacketLengthPrefixBytes(int length) {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), length, "A packet length is non-negative.");

            return length / PacketLengthRadix + 1;
        }

        /// <summary>
        ///     Reads a packet length in the client's base-255 form.
        /// </summary>
        /// <remarks>
        ///     Exposed so a test can drive the reader on its own, over bytes built by hand from
        ///     Node_Sub13.java:510-513 rather than by this project's writer.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the length prefix.</param>
        /// <returns>The packet length in bytes.</returns>
        /// <exception cref="EndOfStreamException">The prefix runs off the end of the stream.</exception>
        public static int ReadPacketLength(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int length = 0;
            int part;
            do {
                part = stream.ReadUnsignedByte();
                length += part;
            } while (part >= PacketLengthRadix);

            return length;
        }
    }
}
