using System;

namespace FlashEditor.Cache.Region
{
    /// <summary>
    ///     Reproduces the procedural terrain height generator the 637 client falls back to when a
    ///     tile carries no explicit height byte.
    /// </summary>
    /// <remarks>
    ///     Ported from <c>Projectile.method3082</c> (Projectile.java:46-77), which the client calls
    ///     from <c>Class305.java:1943-1945</c> for opcode-0 tiles on plane 0. Three octaves of value
    ///     noise at frequencies 4, 2 and 1 with amplitudes 1, 1/2 and 1/4, scaled by 0.3, biased by
    ///     35 and clamped to 10..60.
    ///
    ///     See <c>reference/hydra-637-maps/02-terrain-m.md</c> section 4.
    /// </remarks>
    public static class HeightCalc
    {
        /// <summary>
        ///     Entries in the cosine table. The client's table is 16384 entries at amplitude 16384
        ///     (Class284_Sub2_Sub2.java:16-22), indexed <c>COS[8192 * frac / frequency]</c>
        ///     (ClientScript.java:142) and taken <c>&gt;&gt; 16</c>.
        /// </summary>
        private const int JAGEX_CIRCULAR_ANGLE = 16384;

        /// <summary>Amplitude the cosine table is scaled to. Matches the entry count here.</summary>
        private const int COS_AMPLITUDE = 16384;

        private static readonly int[] COS = BuildCosineTable();

        /// <summary>
        ///     Builds the cosine table.
        /// </summary>
        /// <remarks>
        ///     A static initialiser rather than a public <c>Precalculate()</c>. The previous form
        ///     had to be called explicitly and never was - nothing in the solution referenced it -
        ///     so the table stayed all-zero, <see cref="Interpolate"/> returned a constant 32768,
        ///     and every procedural height in the editor was the same fixed 50/50 blend.
        /// </remarks>
        /// <returns>The populated table.</returns>
        private static int[] BuildCosineTable()
        {
            int[] table = new int[JAGEX_CIRCULAR_ANGLE];
            double step = 2 * Math.PI / JAGEX_CIRCULAR_ANGLE;
            for (int i = 0; i < JAGEX_CIRCULAR_ANGLE; i++)
                table[i] = (int) (Math.Cos(i * step) * COS_AMPLITUDE);
            return table;
        }

        /// <summary>
        ///     Approximates the terrain height for a tile with no explicit height byte.
        /// </summary>
        /// <remarks>
        ///     The coordinates are absolute world tile coordinates with no shift. The client
        ///     supplies <c>64 * regionX + x</c> (Class42.java:22-23) and adds the two magic offsets
        ///     at Class305.java:1944. An earlier version of this method shifted the region base
        ///     right by 3, which collapsed eight adjacent regions onto the same noise input, and
        ///     used offsets that were off by 93 and 48.
        /// </remarks>
        /// <param name="baseX">Absolute world X of the map square's western edge.</param>
        /// <param name="baseY">Absolute world Y of the map square's southern edge.</param>
        /// <param name="x">Tile X within the map square, 0..63.</param>
        /// <param name="y">Tile Y within the map square, 0..63.</param>
        /// <returns>A height in the range 10..60, in raw height-byte units.</returns>
        public static int Calculate(int baseX, int baseY, int x, int y)
        {
            int xc = baseX + x + 932731;
            int yc = baseY + y + 556238;

            int n = InterpolateNoise(xc + 45365, yc + 91923, 4) - 128
                  + ((InterpolateNoise(xc + 10294, yc + 37821, 2) - 128) >> 1)
                  + ((InterpolateNoise(xc, yc, 1) - 128) >> 2);

            n = 35 + (int) (n * 0.3D);

            if (n < 10)
                return 10;
            return n > 60 ? 60 : n;
        }

        private static int InterpolateNoise(int x, int y, int frequency)
        {
            int intX = x / frequency;
            int fracX = x & (frequency - 1);
            int intY = y / frequency;
            int fracY = y & (frequency - 1);

            int v1 = SmoothedNoise1(intX, intY);
            int v2 = SmoothedNoise1(intX + 1, intY);
            int v3 = SmoothedNoise1(intX, intY + 1);
            int v4 = SmoothedNoise1(intX + 1, intY + 1);

            int i1 = Interpolate(v1, v2, fracX, frequency);
            int i2 = Interpolate(v3, v4, fracX, frequency);
            return Interpolate(i1, i2, fracY, frequency);
        }

        private static int SmoothedNoise1(int x, int y)
        {
            int corners = Noise(x - 1, y - 1) + Noise(x + 1, y - 1) + Noise(x - 1, y + 1) + Noise(x + 1, y + 1);
            int sides = Noise(x - 1, y) + Noise(x + 1, y) + Noise(x, y - 1) + Noise(x, y + 1);
            int center = Noise(x, y);
            return center / 4 + sides / 8 + corners / 16;
        }

        private static int Noise(int x, int y)
        {
            int n = x + y * 57;
            n ^= n << 13;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & int.MaxValue) >> 19 & 255;
        }

        /// <summary>
        ///     Blends two noise samples using the client's damped cosine weight.
        /// </summary>
        /// <remarks>
        ///     The weight this produces spans only 24576..40960 (0.375 to 0.625), not the full
        ///     0..65536 of a true cosine interpolation. That is genuinely what the client does:
        ///     the table is amplitude 16384 but the result is taken <c>&gt;&gt; 16</c>, a quarter of
        ///     the shift the table's other twenty-odd users apply. It looks like a bug and is not
        ///     one to fix - widening it changes every procedural height in the world.
        /// </remarks>
        private static int Interpolate(int a, int b, int x, int y)
        {
            //65536 is the fixed-point one the weights are expressed in, not the table amplitude.
            //With a table of amplitude 16384 this lands f in 24576..40960, and f plus its
            //complement is exactly 65536, so the blend is a partition.
            int f = (65536 - COS[8192 * x / y]) >> 1;
            return (f * b >> 16) + (a * (65536 - f) >> 16);
        }
    }
}
