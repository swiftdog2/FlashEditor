using System;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     The client's fixed-point sine and cosine tables, in the client's own angle unit.
    /// </summary>
    /// <remarks>
    ///     A transcription of <c>Class284_Sub2_Sub2.java:15-23</c>, kept rather than replaced by
    ///     <see cref="Math"/> because the rotation arm of the skeletal transform is integer arithmetic
    ///     that <b>truncates</b>. <c>Renderable_Sub2.java:2864-2896</c> multiplies a coordinate by a
    ///     table entry, adds <see cref="ShiftBias"/> and shifts right by <see cref="FractionBits"/>,
    ///     and the result depends on the exact integer that came out of the table. Recomputing the
    ///     sine in double precision and rounding differently moves a vertex by a whole model unit on
    ///     some angles, which is a visible seam on a joint.
    ///     <para>
    ///     Two tables of 16,384 ints is 128 KB, built once. That is the reason the client builds them
    ///     and it is the reason to keep them: they are also faster than the alternative, but
    ///     correctness is what settles it.
    ///     </para>
    /// </remarks>
    public static class SkeletalTrig
    {
        /// <summary>Steps in a full turn. The client's angle unit, not radians or degrees.</summary>
        /// <remarks>
        ///     Also the table length, and the mask - an angle is reduced with <c>&amp; 0x3FFF</c>
        ///     rather than a modulus, which is why the count is a power of two.
        /// </remarks>
        public const int AngleSteps = 16384;

        /// <summary>Fractional bits in a table entry. A result is shifted right by this.</summary>
        public const int FractionBits = 14;

        /// <summary>The fixed-point representation of 1.0, which is <c>1 &lt;&lt; FractionBits</c>.</summary>
        public const int One = 1 << FractionBits;

        /// <summary>
        ///     Added before the right shift so the truncation lands on the nearer integer.
        /// </summary>
        /// <remarks>
        ///     <c>One - 1</c>. The client writes the literal <c>16383</c> inline at every one of the
        ///     nine places it shifts a rotated coordinate down
        ///     (<c>Renderable_Sub2.java:2864-2896</c>). It is not a rounding bias in the usual sense -
        ///     it biases the truncation upwards by almost a whole unit rather than by half - but it is
        ///     what the client does and the pose has to match it.
        /// </remarks>
        public const int ShiftBias = One - 1;

        /// <summary>Radians per angle step. <c>2 * pi / AngleSteps</c>, written as the client's literal.</summary>
        /// <remarks>
        ///     Transcribed from <c>Class284_Sub2_Sub2.java:18</c> rather than computed from
        ///     <see cref="Math.PI"/>, so that the last bit of every table entry is the client's.
        /// </remarks>
        private const double RadiansPerStep = 3.834951969714103E-4;

        /// <summary>Sine of every angle step, scaled by <see cref="One"/>.</summary>
        private static readonly int[] SinTable = Build(Math.Sin);

        /// <summary>Cosine of every angle step, scaled by <see cref="One"/>.</summary>
        private static readonly int[] CosTable = Build(Math.Cos);

        /// <summary>Fixed-point sine of an angle in client steps.</summary>
        /// <remarks>
        ///     The angle is masked rather than validated. Callers hand over values that were never
        ///     reduced - the frame decoder's <c>value &lt;&lt; 2</c> for a rotation
        ///     (<c>Class7.java:91-95</c>) masks, but the emitter's spawn-cone arithmetic adds a random
        ///     spread to a base derived from <c>atan2</c> and does not - so the table has to.
        /// </remarks>
        /// <param name="angle">Angle in steps; any integer, reduced into range here.</param>
        /// <returns>Sine scaled by <see cref="One"/>.</returns>
        public static int Sin(int angle)
        {
            return SinTable[angle & (AngleSteps - 1)];
        }

        /// <summary>Fixed-point cosine of an angle in client steps.</summary>
        /// <param name="angle">Angle in steps; any integer, reduced into range here.</param>
        /// <returns>Cosine scaled by <see cref="One"/>.</returns>
        public static int Cos(int angle)
        {
            return CosTable[angle & (AngleSteps - 1)];
        }

        /// <summary>Builds one table the way the client's static initialiser does.</summary>
        /// <remarks>
        ///     The cast to <c>int</c> truncates toward zero, which is what Java's does, so the entries
        ///     are the client's bit for bit. Rounding to nearest here would be defensible arithmetic
        ///     and the wrong answer.
        /// </remarks>
        /// <param name="function">
        ///     <see cref="Math.Sin"/> or <see cref="Math.Cos"/>. The client writes the two loops out;
        ///     one function taking the other as an argument says they are the same loop.
        /// </param>
        /// <returns>The table.</returns>
        private static int[] Build(Func<double, double> function)
        {
            int[] table = new int[AngleSteps];
            for (int step = 0; step < AngleSteps; step++)
            {
                table[step] = (int)(One * function(step * RadiansPerStep));
            }
            return table;
        }
    }
}
