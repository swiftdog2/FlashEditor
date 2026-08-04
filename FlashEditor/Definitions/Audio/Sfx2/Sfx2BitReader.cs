using System;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     The LSB-first bit reader index 14's group 0 is packed with.
    /// </summary>
    /// <remarks>
    ///     Ported from <c>Node_Sub13.method1138</c> (Node_Sub13.java:83-102) for a run of bits and
    ///     <c>method1134</c> (:37-44) for a single bit. Bit order is the whole point of the type and
    ///     is not the one a big-endian cache invites you to assume: bits fill from the low end of
    ///     each byte upwards, and a field spanning a byte boundary takes its <b>low</b> bits from the
    ///     earlier byte. Reading it the other way round yields plausible-looking small integers and a
    ///     setup header that parses into nonsense.
    ///     <para>
    ///     The data settles it rather than the port alone: bytes 2..4 of group 0 assemble to
    ///     0x564342 under this rule and to nothing recognisable under any other, and 0x564342 is the
    ///     Vorbis codebook sync pattern that <c>Class71.java:44</c> skips with a bare
    ///     <c>read(24)</c>.
    ///     </para>
    ///     <para>
    ///     Java reads the backing array as signed bytes and this reads them as unsigned, which is not
    ///     a divergence: both mask the shifted byte down to the bits being taken, so the sign
    ///     extension Java performs is masked off before it can reach the result.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2BitReader {
        /// <summary>Widest field this reader will assemble, being the width of the accumulator.</summary>
        /// <remarks>
        ///     The client's own reader accumulates into a signed 32-bit int and its widest call is
        ///     <c>read(24)</c>, so nothing in the format needs more. Refusing 32 rather than
        ///     silently producing a negative value keeps a mis-transcribed field width visible.
        /// </remarks>
        public const int MaximumFieldBits = 31;

        private readonly byte[] data;
        private int byteIndex;
        private int bitIndex;

        /// <summary>Reads bits out of a packed buffer, starting at its first bit.</summary>
        /// <param name="data">The buffer to read.</param>
        /// <exception cref="ArgumentNullException">The buffer is null.</exception>
        public Sfx2BitReader(byte[] data) {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
        }

        /// <summary>How many bits have been consumed so far.</summary>
        public int BitPosition => byteIndex * 8 + bitIndex;

        /// <summary>Whether at least <paramref name="bits"/> more bits are available.</summary>
        /// <param name="bits">The field width being considered.</param>
        /// <returns>Whether the read would stay inside the buffer.</returns>
        public bool CanRead(int bits) {
            return bits >= 0 && BitPosition + bits <= data.Length * 8;
        }

        /// <summary>
        ///     Reads one bit, as the client does for a mode's block flag.
        /// </summary>
        /// <returns>The bit, 0 or 1.</returns>
        /// <exception cref="System.IO.EndOfStreamException">The buffer holds no more bits.</exception>
        public int ReadBit() {
            Require(1);
            int bit = (data[byteIndex] >> bitIndex) & 1;
            bitIndex++;
            byteIndex += bitIndex >> 3;
            bitIndex &= 7;
            return bit;
        }

        /// <summary>
        ///     Reads a field of <paramref name="bits"/> bits.
        /// </summary>
        /// <param name="bits">The field width, 0..<see cref="MaximumFieldBits"/>.</param>
        /// <returns>The field's value.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The width is negative or too wide to accumulate.</exception>
        /// <exception cref="System.IO.EndOfStreamException">The buffer holds fewer bits than that.</exception>
        public int Read(int bits) {
            if (bits < 0 || bits > MaximumFieldBits)
                throw new ArgumentOutOfRangeException(nameof(bits), bits,
                    "A field is 0.." + MaximumFieldBits + " bits wide; the client's widest is 24.");

            Require(bits);

            int value = 0;
            int shift = 0;

            /* Whole-byte remainders first, exactly as the client loops: each pass takes every bit
               left in the current byte, and those bits are the LESS significant end of the result. */
            while (bits >= 8 - bitIndex) {
                int width = 8 - bitIndex;
                value += ((data[byteIndex] >> bitIndex) & ((1 << width) - 1)) << shift;
                bitIndex = 0;
                byteIndex++;
                shift += width;
                bits -= width;
            }

            if (bits > 0) {
                value += ((data[byteIndex] >> bitIndex) & ((1 << bits) - 1)) << shift;
                bitIndex += bits;
            }

            return value;
        }

        /// <summary>Fails before an out-of-range index rather than after it.</summary>
        /// <remarks>
        ///     The client indexes the array unguarded, so a truncated setup header reaches it as an
        ///     <c>ArrayIndexOutOfBoundsException</c> from deep inside a codebook. Naming the buffer
        ///     end here is not a divergence in what is read, only in how a short buffer is reported.
        /// </remarks>
        /// <param name="bits">The field width about to be read.</param>
        private void Require(int bits) {
            if (!CanRead(bits))
                throw new System.IO.EndOfStreamException(
                    "Wanted " + bits + " bits at bit " + BitPosition + " of a " + (data.Length * 8) +
                    "-bit buffer.");
        }
    }
}
