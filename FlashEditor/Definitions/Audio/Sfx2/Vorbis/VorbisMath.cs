using System;

namespace FlashEditor.Definitions.Audio.Sfx2.Vorbis {
    /// <summary>
    ///     The three integer helpers the client's Vorbis decoder is built on.
    /// </summary>
    /// <remarks>
    ///     All three live on unrelated obfuscated classes in the client and are gathered here
    ///     because they are pure functions of their arguments and nothing else. Each is transcribed
    ///     from the client rather than from the Vorbis specification: the specification states what
    ///     they compute, the client states what this cache was encoded against, and where a
    ///     specification edge case and the client's arithmetic could differ the client wins.
    /// </remarks>
    internal static class VorbisMath {
        /// <summary>
        ///     The number of bits needed to hold a value, which is how every field width in the
        ///     format is stated.
        /// </summary>
        /// <remarks>
        ///     <c>Class48_Sub2_Sub1.method474</c>. <c>Ilog(0)</c> is 0 and <c>Ilog(1)</c> is 1, so a
        ///     one-element table is addressed with zero bits and a two-element table with one - the
        ///     off-by-one that silently shifts every subsequent field if it is got wrong, because
        ///     nothing downstream can tell a mis-sized read from real data.
        /// </remarks>
        /// <param name="value">The value to size.</param>
        /// <returns>The bit width.</returns>
        internal static int Ilog(int value) {
            int bits = 0;

            /* The client's guard is `i < 0 || (i ^ 0xffffffff) <= -65537`, which is `i < 0 ||
               i >= 65536` once the complement is unfolded. A negative reaches it through the first
               arm and is then shifted unsigned, so this must be a logical shift and not an
               arithmetic one. */
            if (value < 0 || value >= 65536) {
                bits += 16;
                value = (int) ((uint) value >> 16);
            }

            if (value >= 256) {
                value >>= 8;
                bits += 8;
            }

            if (value >= 16) {
                value >>= 4;
                bits += 4;
            }

            if (value >= 4) {
                value >>= 2;
                bits += 2;
            }

            if (value >= 1) {
                value >>= 1;
                bits++;
            }

            return value + bits;
        }

        /// <summary>
        ///     Reverses the low <paramref name="bits"/> bits of a value.
        /// </summary>
        /// <remarks>
        ///     <c>Applet_Sub1.method81</c>, used once, to build the bit-reversal permutation the
        ///     inverse MDCT's butterfly stage indexes through.
        /// </remarks>
        /// <param name="bits">How many bits to reverse.</param>
        /// <param name="value">The value.</param>
        /// <returns>The reversed value.</returns>
        internal static int BitReverse(int bits, int value) {
            int reversed = 0;
            for (; bits > 0; bits--) {
                reversed = (reversed << 1) | (value & 1);
                value = (int) ((uint) value >> 1);
            }

            return reversed;
        }

        /// <summary>
        ///     Integer exponentiation by squaring, wrapping on overflow exactly as Java's does.
        /// </summary>
        /// <remarks>
        ///     <c>AccessMaskNode.method1662</c>. Used only by <see cref="Lookup1Values"/>, where the
        ///     search deliberately walks a candidate down until the power stops exceeding the entry
        ///     count, so an intermediate that overflows has to wrap rather than throw or the search
        ///     never terminates.
        /// </remarks>
        /// <param name="value">The base.</param>
        /// <param name="exponent">The exponent.</param>
        /// <returns>The power.</returns>
        internal static int Power(int value, int exponent) {
            unchecked {
                int result = 1;
                while (exponent > 1) {
                    if ((exponent & 1) != 0)
                        result *= value;
                    exponent >>= 1;
                    value *= value;
                }

                return exponent == 1 ? value * result : result;
            }
        }

        /// <summary>
        ///     The greatest integer whose <paramref name="dimensions"/>-th power does not exceed
        ///     <paramref name="entries"/>, which sizes a lookup-type-1 codebook's multiplicand table.
        /// </summary>
        /// <remarks>
        ///     <c>Class71.method713</c>. It starts one above the floating-point estimate and walks
        ///     down, so a rounding error in <c>Math.Pow</c> cannot make it answer too low - which
        ///     matters because the answer is a table length, and a table one entry short desynchronises
        ///     the whole setup header from that point on.
        /// </remarks>
        /// <param name="entries">The codebook's entry count.</param>
        /// <param name="dimensions">The codebook's dimension count.</param>
        /// <returns>The multiplicand count.</returns>
        internal static int Lookup1Values(int entries, int dimensions) {
            int candidate = (int) Math.Pow(entries, 1.0 / dimensions) + 1;
            while (Power(candidate, dimensions) > entries)
                candidate--;
            return candidate;
        }

        /// <summary>
        ///     Unpacks the format's own 32-bit float, which is not IEEE 754.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub13.method1139</c>: a 21-bit mantissa, a 10-bit exponent biased by 788 and
        ///     a sign bit, evaluated in double precision and narrowed once at the end. Reading these
        ///     four bytes as an IEEE float instead produces finite, plausible-looking values and a
        ///     codebook whose vectors are wrong by orders of magnitude.
        /// </remarks>
        /// <param name="packed">The stored 32 bits.</param>
        /// <returns>The value.</returns>
        internal static float Float32Unpack(int packed) {
            int mantissa = packed & 0x1fffff;
            bool negative = (packed & unchecked((int) 0x80000000)) != 0;
            int exponent = (packed & 0x7fe00000) >> 21;

            if (negative)
                mantissa = -mantissa;

            return (float) (mantissa * Math.Pow(2.0, exponent - 788));
        }
    }
}
