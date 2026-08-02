using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    /// The seeded tables two of the texture graph's generator nodes are built on.
    /// </summary>
    /// <remarks>
    /// Node types 15 and 34 both derive their permutation table from a Java
    /// <c>java.util.Random</c> stream, so reproducing the client's output needs that exact
    /// generator rather than <see cref="System.Random"/> - a different stream gives noise that
    /// is plausible but not the noise in the cache.
    /// </remarks>
    internal static class TextureNoise {
        /// <summary>
        /// <c>java.util.Random</c>: a 48-bit linear congruential generator. Ported rather than
        /// approximated because the permutation tables below are shuffled by it.
        /// </summary>
        internal sealed class JavaRandom {
            private const long Multiplier = 0x5DEECE66DL;
            private const long Addend = 0xBL;
            private const long Mask = (1L << 48) - 1;
            private long _seed;

            internal JavaRandom(long seed) => _seed = (seed ^ Multiplier) & Mask;

            private int Next(int bits) {
                _seed = (_seed * Multiplier + Addend) & Mask;
                return (int)((ulong)_seed >> (48 - bits));
            }

            internal int NextInt() => Next(32);
        }

        /// <summary>
        /// A uniform draw below <paramref name="bound"/>, matching <c>Class63.method546</c>.
        /// </summary>
        /// <remarks>
        /// Not <c>Random.nextInt(bound)</c>: the client takes a different path for power-of-two
        /// bounds and consumes whole 32-bit draws in the general case, so the number of draws
        /// consumed - and therefore every later value in the stream - depends on this being
        /// exact.
        /// </remarks>
        internal static int NextBounded(int bound, JavaRandom random) {
            if (bound <= 0)
                throw new ArgumentOutOfRangeException(nameof(bound));

            //Power of two: take the top bits of a widened multiply.
            if (bound == (-bound & bound))
                return (int)(((random.NextInt() & 0xFFFFFFFFL) * bound) >> 32);

            int limit = int.MinValue - (int)(4294967296L % bound);
            int value;
            do {
                value = random.NextInt();
            } while (limit <= value);

            //Class198.method2678 - a floor-mod, so a negative draw still lands in range.
            int bias = (bound - 1) & (value >> 31);
            return (int)(((uint)value >> 31) + value) % bound + bias;
        }

        private static readonly Dictionary<int, byte[]> _permutations = new();
        private static readonly object _permLock = new();

        /// <summary>
        /// The 512-entry permutation table for a seed, matching <c>Class279.method3323</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately not a clean permutation of 0..255. The client's fill loop stops at 254,
        /// leaving entry 255 holding zero, and the mirror never writes entry 256. Both quirks
        /// change the noise, so they are reproduced rather than tidied up.
        /// </remarks>
        internal static byte[] Permutation(int seed) {
            lock (_permLock) {
                if (_permutations.TryGetValue(seed, out byte[] cached))
                    return cached;

                var table = new byte[512];
                for (int i = 0; i < 255; i++)
                    table[i] = (byte)i;

                var random = new JavaRandom(seed);
                for (int k = 0; k < 255; k++) {
                    int j = 255 - k;
                    int pick = NextBounded(j, random);
                    byte swap = table[pick];
                    table[pick] = table[j];
                    table[j] = table[511 - k] = swap;
                }

                _permutations[seed] = table;
                return table;
            }
        }

        /// <summary>
        /// Perlin's smootherstep <c>t^3(6t^2 - 15t + 10)</c> over the 12-bit range, the curve the
        /// client precomputes in <c>Class151_Sub8</c>.
        /// </summary>
        internal static readonly int[] Smooth = BuildSmooth();

        private static int[] BuildSmooth() {
            var table = new int[4096];
            for (int t = 0; t < 4096; t++) {
                int cube = t * (t * t >> 12) >> 12;
                int inner = t * 6 - 61440;
                int poly = (t * inner >> 12) + 40960;
                table[t] = cube * poly >> 12;
            }
            return table;
        }
    }
}
