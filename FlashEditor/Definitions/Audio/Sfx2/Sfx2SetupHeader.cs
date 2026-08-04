using System;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     Index 14 group 0: the Vorbis setup header and codebooks shared by every sample on the
    ///     index.
    /// </summary>
    /// <remarks>
    ///     Read once by <c>Node_Sub13.method1133</c> (Node_Sub13.java:32) and parsed by
    ///     <c>method1143</c> (:134-210): two 4-bit blocksize exponents, then <c>read(8)+1</c>
    ///     codebooks (<c>Class71</c>), <c>read(6)+1</c> time-domain transforms that are read and
    ///     discarded (:184-186), floors (<c>Class56</c>), residues (<c>Class311</c>), mappings
    ///     (<c>Class371</c>) and modes (:205-210).
    ///     <para>
    ///     <b>The bytes are kept verbatim and only the leading fields are parsed.</b> Everything
    ///     past the codebook count is a bit-packed structure whose length is known only by decoding
    ///     it in full, and this project does not do that. Re-encoding therefore replays what was
    ///     read rather than rebuilding it - which is the only correct answer available, since a
    ///     partial parse cannot put back what it did not understand.
    ///     </para>
    ///     <para>
    ///     <b>It cannot be handed to a stock Vorbis library.</b> It is a hybrid: the two blocksize
    ///     nibbles that belong to the Vorbis identification header, prepended to a setup header with
    ///     no <c>\x01vorbis</c> or <c>\x05vorbis</c> magic, no channel count, no sample rate and no
    ///     framing bit. Channels are implicitly one, since <c>method1132</c> emits a single byte per
    ///     sample (:266-270). Any decoder has to be written against <c>Node_Sub13</c> and
    ///     <c>Class71</c> directly.
    ///     </para>
    ///     <para>
    ///     Both blocksizes are equal in both supported caches, so a decoder ported from a reference
    ///     implementation that assumes the short block is strictly shorter than the long one will
    ///     mis-window. The client builds the two window tables independently (:141-177) and does not
    ///     care.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2SetupHeader : Sfx2Entry {
        /// <summary>The one group on this index that holds a setup header rather than a sample.</summary>
        public const int SetupGroupId = 0;

        /// <summary>
        ///     The 24-bit sync pattern a Vorbis codebook opens with, 0x564342 - "BCV" little-endian.
        /// </summary>
        /// <remarks>
        ///     <c>Class71.java:44</c> skips it with a bare <c>read(24)</c> without checking it, so
        ///     this is a property of the format rather than something the client validates. It is
        ///     worth checking here because it is self-proving: it only assembles under the client's
        ///     bit order, and finding it confirms both the bit reader and the claim that group 0 is
        ///     a setup header.
        /// </remarks>
        public const int VorbisCodebookSyncPattern = 0x564342;

        /// <summary>
        ///     Bytes needed before the leading fields can be parsed: 4 + 4 + 8 + 24 bits.
        /// </summary>
        public const int MinimumHeaderBytes = 5;

        private byte[] rawBytes = Array.Empty<byte>();

        /// <summary>
        ///     The group's bytes exactly as stored, which is the whole of what is written back.
        /// </summary>
        /// <remarks>
        ///     Stored state, not derived. The parsed properties below are a read-only view over
        ///     these bytes and changing one would not change what is encoded.
        /// </remarks>
        public byte[] RawBytes => rawBytes;

        /// <summary>The short window size, <c>1 &lt;&lt; read(4)</c> (Node_Sub13.java:137).</summary>
        public int Blocksize0 { get; private set; }

        /// <summary>The long window size, <c>1 &lt;&lt; read(4)</c> (Node_Sub13.java:138).</summary>
        public int Blocksize1 { get; private set; }

        /// <summary>How many codebooks follow, <c>read(8)+1</c> (Node_Sub13.java:178).</summary>
        public int CodebookCount { get; private set; }

        /// <summary>
        ///     The 24 bits the first codebook opens with, or -1 when the group is too short to hold
        ///     them.
        /// </summary>
        public int FirstCodebookSync { get; private set; } = -1;

        /// <summary>
        ///     Whether the first codebook carries the Vorbis sync pattern, which is what identifies
        ///     these bytes as a setup header rather than a sample.
        /// </summary>
        public bool HasCodebookSyncPattern => FirstCodebookSync == VorbisCodebookSyncPattern;

        /// <summary>
        ///     Reads the group, keeping every byte and parsing the fields ahead of the first
        ///     codebook.
        /// </summary>
        /// <remarks>
        ///     Consumes the rest of the stream by definition: the record <i>is</i> the group's whole
        ///     file, and no field in it states a length. A sweep therefore cannot check this one for
        ///     exact consumption the way it checks a sample - consumption is true by construction
        ///     here, and <see cref="HasCodebookSyncPattern"/> is the claim worth asserting instead.
        /// </remarks>
        /// <param name="stream">The group's single file, positioned at its start.</param>
        /// <returns>This header.</returns>
        /// <exception cref="ArgumentNullException">The stream is null.</exception>
        public Sfx2SetupHeader Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            rawBytes = stream.ReadBytes(stream.Remaining());

            Blocksize0 = 0;
            Blocksize1 = 0;
            CodebookCount = 0;
            FirstCodebookSync = -1;

            if (rawBytes.Length < MinimumHeaderBytes)
                return this;

            var bits = new Sfx2BitReader(rawBytes);
            Blocksize0 = 1 << bits.Read(4);
            Blocksize1 = 1 << bits.Read(4);
            CodebookCount = bits.Read(8) + 1;
            FirstCodebookSync = bits.Read(24);
            return this;
        }

        /// <summary>Writes the group back byte for byte.</summary>
        /// <returns>The encoded file, positioned at 0.</returns>
        public override JagStream Encode() {
            return new JagStream((byte[]) rawBytes.Clone());
        }
    }
}
