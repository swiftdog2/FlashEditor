using System;
using FlashEditor.Definitions.Particles;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     The fixed-point conventions the particle simulation works in.
    /// </summary>
    /// <remarks>
    ///     Collected here because they are shared across four files and because most of them are shift
    ///     counts the client writes as bare literals. A named constant is checkable against the client
    ///     line it came from; an inline <c>&gt;&gt; 23</c> is not.
    /// </remarks>
    public static class ParticleUnits
    {
        /// <summary>
        ///     The simulation's time step.
        /// </summary>
        /// <remarks>
        ///     A millisecond, not a 20 ms client cycle - the particle path is driven by elapsed
        ///     milliseconds throughout (<c>Particle_Sub9.java:221</c> scales the spawn rate by them and
        ///     <c>Particle_Sub4_Sub2_Sub1.java:36</c> subtracts them from the lifetime). Every rate and
        ///     lifetime in an index-27 record is per millisecond, which is why the emitter and the
        ///     animation player are driven by different units.
        /// </remarks>
        public const int MillisecondsPerStep = 1;

        /// <summary>Fractional bits in a particle's stored position.</summary>
        /// <remarks>
        ///     Twelfths of a model unit. A particle at speed moves a fraction of a unit per
        ///     millisecond, so integer positions would quantise slow drift to nothing.
        /// </remarks>
        public const int PositionFractionBits = 12;

        /// <summary>Fractional bits shifted off a particle's size when it is drawn.</summary>
        /// <remarks>
        ///     Not the same as the bits shifted <i>on</i> at load, which is
        ///     <see cref="ParticleEmitterDefinition.SizeShift"/> - 14. The difference is a net factor
        ///     of four between a stored size and its half extent in model units, and it is the
        ///     client's (<c>Class360.java:141</c>).
        /// </remarks>
        public const int SizeFractionBits = 12;

        /// <summary>Length a direction vector is normalised to.</summary>
        /// <remarks>
        ///     The largest value a signed short holds, so a direction fills its storage.
        ///     <c>Particle_Sub9.java:208-210</c>.
        /// </remarks>
        public const int DirectionScale = 32767;

        /// <summary>Bits a speed is shifted up by before it multiplies a direction.</summary>
        /// <remarks><c>Particle_Sub4_Sub2_Sub1.java:324</c>, <c>anInt6498 &lt;&lt; 2</c>.</remarks>
        public const int SpeedShift = 2;

        /// <summary>Bits the direction-times-speed product is shifted down by to give a velocity.</summary>
        /// <remarks><c>Particle_Sub4_Sub2_Sub1.java:324-326</c>.</remarks>
        public const int VelocityShift = 23;

        /// <summary>What one particle costs from the emitter's spawn accumulator.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:224-226</c>: the accumulator is compared against 64, shifted down
        ///     six for the count and masked back to the remainder. A spawn rate is therefore in
        ///     sixty-fourths of a particle per millisecond, and 64 means exactly one a millisecond.
        /// </remarks>
        public const int SpawnAccumulatorPerParticle = 64;

        /// <summary>Bits the linear drag term is shifted down by.</summary>
        /// <remarks><c>Particle_Sub4_Sub2_Sub1.java:118</c>.</remarks>
        public const int LinearDragShift = 18;

        /// <summary>Bits the quadratic drag term is shifted down by.</summary>
        /// <remarks>
        ///     <c>Particle_Sub4_Sub2_Sub1.java:126</c>. Ten more than
        ///     <see cref="LinearDragShift"/>, because the term it divides is a squared distance rather
        ///     than a distance.
        /// </remarks>
        public const int QuadraticDragShift = 28;

        /// <summary>Bits a stored effector cone half-angle is shifted up by.</summary>
        /// <remarks><c>Class66.java:245</c>.</remarks>
        public const int ConeAngleShift = 3;
    }

    /// <summary>
    ///     The pseudo-random source the simulation draws from.
    /// </summary>
    /// <remarks>
    ///     A 32-bit xorshift, deliberately <b>not</b> the client's <see cref="Random"/>. The client
    ///     uses <c>Math.random()</c> throughout the particle path, which is unseedable, so matching it
    ///     is not on the table; what is on the table is being <i>reproducible</i>. Two runs with the
    ///     same seed produce the same particles, which is what lets a preview be compared against
    ///     itself and lets a test assert a position rather than a range.
    ///     <para>
    ///     The distribution is what matters, not the sequence, and every draw here feeds either a
    ///     uniform pick between two bounds or a barycentric blend.
    ///     </para>
    /// </remarks>
    public sealed class ParticleRandom
    {
        /// <summary>The generator state, which is never allowed to be zero.</summary>
        private uint state;

        /// <summary>Creates a generator.</summary>
        /// <param name="seed">
        ///     The seed. A xorshift is stuck at zero forever, so a zero seed is replaced with the
        ///     golden-ratio constant rather than silently producing a stream of zeroes - which would
        ///     spawn every particle at one corner of its face with one colour.
        /// </param>
        public ParticleRandom(int seed)
        {
            state = seed == 0 ? 2654435769u : (uint)seed;
        }

        /// <summary>A uniform value in [0, 1).</summary>
        /// <returns>The value.</returns>
        public double NextFraction()
        {
            return NextUInt() / 4294967296.0;
        }

        /// <summary>A uniform integer in [0, range), or in (range, 0] when the range is negative.</summary>
        /// <remarks>
        ///     Signed ranges are load-bearing rather than defensive. Every caller computes the range as
        ///     a difference between two stored bounds, and a definition that fades from bright to dark
        ///     stores the bright value first - so a negative span is ordinary data and not a damaged
        ///     record.
        /// </remarks>
        /// <param name="range">The exclusive bound, of either sign.</param>
        /// <returns>The value.</returns>
        public int NextScaled(int range)
        {
            return (int)(NextFraction() * range);
        }

        /// <summary>Advances the state one xorshift step.</summary>
        /// <remarks>The 13/17/5 triple, which is the standard full-period choice for 32 bits.</remarks>
        /// <returns>The new state.</returns>
        private uint NextUInt()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }

    /// <summary>
    ///     One emitter definition with everything the client derives from it at load, derived once.
    /// </summary>
    /// <remarks>
    ///     <c>ParticleType.method897</c>, <c>ParticleType.java:669-755</c>. It exists for the same
    ///     reason the client's does: these values are functions of the record alone, several involve a
    ///     division, and they would otherwise be recomputed for every particle of every emitter on
    ///     every frame.
    ///     <para>
    ///     Keeping it apart from <see cref="ParticleEmitterDefinition"/> also keeps the definition a
    ///     faithful record of the bytes. The definition round-trips to the cache and must hold exactly
    ///     what was stored; this holds what the stored values <i>mean</i>, and the two would fight if
    ///     they shared a type.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEmitterRuntime
    {
        /// <summary>The record these values were derived from.</summary>
        public ParticleEmitterDefinition Definition { get; }

        /// <summary>Red at the near end of the spawn-colour range.</summary>
        public int RedBase { get; }

        /// <summary>Signed distance from <see cref="RedBase"/> to the far end.</summary>
        /// <remarks>
        ///     A difference and routinely negative, so nothing may treat it as a magnitude.
        ///     <c>ParticleType.java:679-693</c>.
        /// </remarks>
        public int RedSpan { get; }

        /// <summary>Green at the near end of the spawn-colour range.</summary>
        public int GreenBase { get; }

        /// <summary>Signed distance from <see cref="GreenBase"/> to the far end.</summary>
        public int GreenSpan { get; }

        /// <summary>Blue at the near end of the spawn-colour range.</summary>
        public int BlueBase { get; }

        /// <summary>Signed distance from <see cref="BlueBase"/> to the far end.</summary>
        public int BlueSpan { get; }

        /// <summary>Alpha at the near end of the spawn-colour range.</summary>
        public int AlphaBase { get; }

        /// <summary>Signed distance from <see cref="AlphaBase"/> to the far end.</summary>
        public int AlphaSpan { get; }

        /// <summary>
        ///     Whether either height plane has moved off its default, arming the plane test.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:673-676</c>. Derived here so a reader does not have to remember
        ///     that -2 rather than -1 is the "unset" value for these two. Nothing in this viewer acts
        ///     on it - the planes cull a particle against the scene, and a model previewed on its own
        ///     has no scene - but it is what a panel would need to say so.
        /// </remarks>
        public bool HasHeightBound { get; }

        /// <summary>Lower spawn size, shifted into the client's fixed point.</summary>
        public int SizeMin { get; }

        /// <summary>Upper spawn size, shifted into the client's fixed point.</summary>
        public int SizeMax { get; }

        /// <summary>
        ///     Whether the emitter has a colour ramp at all.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:695</c> gates the whole block on the packed fade colour being
        ///     nonzero, so zero means "no ramp" and <b>not</b> "fade to black". A flag rather than a
        ///     sentinel colour, because black with zero alpha is a value the field could legitimately
        ///     hold if the format allowed it.
        /// </remarks>
        public bool HasColourRamp { get; }

        /// <summary>How many milliseconds of a particle's life the RGB fade spans.</summary>
        public int ColourRampSteps { get; }

        /// <summary>How many milliseconds of a particle's life the alpha fade spans.</summary>
        /// <remarks>Independent of <see cref="ColourRampSteps"/> - they come from different opcodes.</remarks>
        public int AlphaRampSteps { get; }

        /// <summary>Red change per millisecond during the fade, in 1/256ths.</summary>
        public int RedRate { get; }

        /// <summary>Green change per millisecond during the fade, in 1/256ths.</summary>
        public int GreenRate { get; }

        /// <summary>Blue change per millisecond during the fade, in 1/256ths.</summary>
        public int BlueRate { get; }

        /// <summary>Alpha change per millisecond during the fade, in 1/256ths.</summary>
        public int AlphaRate { get; }

        /// <summary>Whether the emitter ramps its particles' size.</summary>
        /// <remarks>
        ///     A flag rather than a sentinel, because the guard at <c>ParticleType.java:733</c> is
        ///     against the <i>stored</i> -1 and the shifted value can never be -1.
        /// </remarks>
        public bool HasSizeRamp { get; }

        /// <summary>The size a particle ramps towards, shifted into fixed point.</summary>
        public int EndSize { get; }

        /// <summary>How many milliseconds of a particle's life the size ramp spans.</summary>
        public int SizeRampSteps { get; }

        /// <summary>Size change per millisecond during the ramp.</summary>
        public int SizeRate { get; }

        /// <summary>Whether the emitter ramps its particles' speed.</summary>
        public bool HasSpeedRamp { get; }

        /// <summary>How many milliseconds of a particle's life the speed ramp spans.</summary>
        public int SpeedRampSteps { get; }

        /// <summary>Speed change per millisecond during the ramp.</summary>
        public int SpeedRate { get; }

        /// <summary>Lower yaw bound of the spawn cone, shifted and narrowed to a short.</summary>
        public int YawStart { get; }

        /// <summary>Upper yaw bound of the spawn cone, shifted and narrowed to a short.</summary>
        public int YawEnd { get; }

        /// <summary>Lower pitch bound of the spawn cone, shifted and narrowed to a short.</summary>
        public int PitchStart { get; }

        /// <summary>Upper pitch bound of the spawn cone, shifted and narrowed to a short.</summary>
        public int PitchEnd { get; }

        /// <summary>Width of the yaw range a spawn direction is drawn from.</summary>
        public int YawSpread { get; }

        /// <summary>Width of the pitch range a spawn direction is drawn from.</summary>
        public int PitchSpread { get; }

        /// <summary>
        ///     Whether particles leave along a random direction in the cone or along the face normal.
        /// </summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:211</c> and <c>:231</c> test the two <b>end</b> bounds, not the
        ///     spreads - so an emitter with wide start bounds and zero end bounds still spawns straight
        ///     out of its face. Testing the spreads instead is the plausible reading and the wrong one.
        /// </remarks>
        public bool SpawnsAlongAnAngleRange { get; }

        /// <summary>Derives everything the simulation needs from one emitter record.</summary>
        /// <param name="definition">The record.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public ParticleEmitterRuntime(ParticleEmitterDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));

            RedBase = (definition.SpawnColourStart >> 16) & 0xFF;
            GreenBase = (definition.SpawnColourStart >> 8) & 0xFF;
            BlueBase = definition.SpawnColourStart & 0xFF;
            AlphaBase = (definition.SpawnColourStart >> 24) & 0xFF;

            RedSpan = ((definition.SpawnColourEnd >> 16) & 0xFF) - RedBase;
            GreenSpan = ((definition.SpawnColourEnd >> 8) & 0xFF) - GreenBase;
            BlueSpan = (definition.SpawnColourEnd & 0xFF) - BlueBase;
            AlphaSpan = ((definition.SpawnColourEnd >> 24) & 0xFF) - AlphaBase;

            HasHeightBound = definition.CeilingPlane > -2 || definition.FloorPlane > -2;

            SizeMin = definition.SizeMinStored << ParticleEmitterDefinition.SizeShift;
            SizeMax = definition.SizeMaxStored << ParticleEmitterDefinition.SizeShift;

            HasColourRamp = definition.FadeColour != 0;

            if (HasColourRamp)
            {
                ColourRampSteps = AtLeastOne(definition.FadeColourPercent * definition.LifetimeMax / 100);
                AlphaRampSteps = AtLeastOne(definition.LifetimeMax * definition.FadeAlphaPercent / 100);

                //Distance from the middle of the spawn range to the fade colour, per channel, shifted
                //up eight so the per-millisecond rate has fractional resolution. The middle rather
                //than the base, because a particle spawns anywhere in the range and the ramp is
                //derived once for all of them.
                RedRate = Bias(((definition.FadeColour >> 16) & 0xFF) - RedBase - RedSpan / 2 << 8, ColourRampSteps);
                GreenRate = Bias(((definition.FadeColour >> 8) & 0xFF) - GreenBase - GreenSpan / 2 << 8, ColourRampSteps);
                BlueRate = Bias((definition.FadeColour & 0xFF) - BlueBase - BlueSpan / 2 << 8, ColourRampSteps);
                AlphaRate = Bias(((definition.FadeColour >> 24) & 0xFF) - (AlphaSpan / 2 + AlphaBase) << 8,
                    AlphaRampSteps);
            }

            HasSizeRamp = definition.EndSizeStored != -1;

            if (HasSizeRamp)
            {
                EndSize = definition.EndSizeStored << ParticleEmitterDefinition.SizeShift;
                SizeRampSteps = AtLeastOne(definition.SizeRampPercent * definition.LifetimeMax / 100);
                SizeRate = (EndSize - SizeMin - (SizeMax - SizeMin) / 2) / SizeRampSteps;
            }

            HasSpeedRamp = definition.EndSpeed != -1;

            if (HasSpeedRamp)
            {
                SpeedRampSteps = AtLeastOne(definition.LifetimeMax * definition.SpeedRampPercent / 100);
                SpeedRate = (definition.EndSpeed - (definition.SpeedMax - definition.SpeedMin) / 2
                    - definition.SpeedMin) / SpeedRampSteps;
            }

            YawStart = Shifted(definition.YawStartStored);
            YawEnd = Shifted(definition.YawEndStored);
            PitchStart = Shifted(definition.PitchStartStored);
            PitchEnd = Shifted(definition.PitchEndStored);

            YawSpread = YawEnd - YawStart;
            PitchSpread = PitchEnd - PitchStart;

            SpawnsAlongAnAngleRange = YawEnd > 0 || PitchEnd > 0;
        }

        /// <summary>
        ///     Divides a ramp's total change by its duration and nudges the result away from zero.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:716-730</c>: four is added to a rate that is zero or negative and
        ///     subtracted from a positive one. That is not rounding - it deliberately <b>overshoots</b>
        ///     so the fade reaches its target and holds there instead of approaching it asymptotically
        ///     and dying part way. It therefore decides the colour a particle dies at, which is why it
        ///     is reproduced rather than tidied into a round.
        ///     <para>
        ///     Note the direction: the nudge makes a negative rate more negative and a positive rate
        ///     less positive. Applied the other way round, a fade downwards would stall.
        ///     </para>
        /// </remarks>
        /// <param name="numerator">Total change over the ramp, already shifted up eight.</param>
        /// <param name="steps">Ramp duration in milliseconds, never zero.</param>
        /// <returns>The per-millisecond rate.</returns>
        private static int Bias(int numerator, int steps)
        {
            int rate = numerator / steps;
            return rate + (rate <= 0 ? 4 : -4);
        }

        /// <summary>Floors a ramp duration at one millisecond.</summary>
        /// <remarks>
        ///     <c>ParticleType.java:699-701</c> and <c>:736-738</c>. It is a divisor, so zero would
        ///     throw; and a ramp declared as zero percent of the lifetime is asking to happen at once,
        ///     which one millisecond is.
        /// </remarks>
        /// <param name="steps">The computed duration.</param>
        /// <returns>The duration, at least one.</returns>
        private static int AtLeastOne(int steps)
        {
            return steps == 0 ? 1 : steps;
        }

        /// <summary>Shifts a stored angle bound into the client's angle unit, through a short.</summary>
        /// <remarks>
        ///     <c>ParticleType.java:527-533</c> assigns the shifted value back into a <c>short</c>
        ///     field, so a stored bound above 4095 <b>wraps</b>. That is why the definition keeps the
        ///     stored value rather than the shifted one, and it is what decides the spawn cone of any
        ///     emitter storing a large bound - dropping the narrowing would give those emitters a cone
        ///     the client never gives them.
        /// </remarks>
        /// <param name="stored">The stored bound.</param>
        /// <returns>The angle, narrowed to sixteen bits.</returns>
        private static int Shifted(int stored)
        {
            return (short)(stored << ParticleUnits.ConeAngleShift);
        }
    }

    /// <summary>
    ///     One effector definition with everything the client derives from it at load, derived once.
    /// </summary>
    /// <remarks><c>Class66.method685</c>, <c>Class66.java:241-281</c>.</remarks>
    public sealed class ParticleEffectorRuntime
    {
        /// <summary>The record these values were derived from.</summary>
        public ParticleEffectorDefinition Definition { get; }

        /// <summary>
        ///     Cosine of the cone half-angle, in <see cref="SkeletalTrig"/>'s fixed point.
        /// </summary>
        /// <remarks>
        ///     <c>Class66.java:245</c>. A particle is inside the cone when the cosine of its bearing
        ///     is at least this, which is a comparison rather than an inverse trig call per particle.
        /// </remarks>
        public int ConeCosine { get; }

        /// <summary>
        ///     Length of the force vector, negated when the effector pulls rather than pushes.
        /// </summary>
        /// <remarks>
        ///     The negation happens <b>after</b> <see cref="RadiusBound"/> is derived from it
        ///     (<c>Class66.java:256</c> then <c>:262-269</c> then <c>:271-275</c>). Doing it first
        ///     would give every pulling effector a negative radius and therefore no reach at all,
        ///     which would be silent - a pull that does nothing looks like a pull that is too weak.
        /// </remarks>
        public int Magnitude { get; }

        /// <summary>The falloff law's divisor, floored at one.</summary>
        public int Divisor { get; }

        /// <summary>
        ///     How far the effector reaches, to be compared against a <b>squared</b> distance.
        /// </summary>
        /// <remarks>
        ///     Squared under falloff mode 1 and left linear under mode 2 (<c>Class66.java:264-269</c>),
        ///     while the comparison against it is always a squared distance
        ///     (<c>Particle_Sub4_Sub2_Sub1.java:153</c>). So a mode-2 effector's real reach is the
        ///     square root of what the arithmetic reads as. That is the client's, the 639 data has no
        ///     opinion on it, and it stands - pinned by a test so a later reader does not "fix" it.
        ///     <para>
        ///     Mode 0 is unbounded, spelled as <see cref="int.MaxValue"/> rather than as a flag,
        ///     because the comparison is a double and no real squared distance approaches it.
        ///     </para>
        /// </remarks>
        public long RadiusBound { get; }

        /// <summary>Derives everything the simulation needs from one effector record.</summary>
        /// <param name="definition">The record.</param>
        /// <exception cref="ArgumentNullException"><paramref name="definition"/> is null.</exception>
        public ParticleEffectorRuntime(ParticleEffectorDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));

            ConeCosine = SkeletalTrig.Cos(definition.ConeAngleStored << ParticleUnits.ConeAngleShift);

            //Widened before squaring: the three components are each a full int in the format, and
            //their squares overflow one.
            long directionX = definition.DirectionX;
            long directionY = definition.DirectionY;
            long directionZ = definition.DirectionZ;

            int magnitude = (int)Math.Sqrt(
                directionX * directionX + directionY * directionY + directionZ * directionZ);

            //A stored zero is repaired rather than rejected, because it is a divisor.
            Divisor = definition.Strength == 0 ? 1 : definition.Strength;

            RadiusBound = definition.FalloffMode switch
            {
                1 => Square(8L * magnitude / Divisor),
                2 => 8L * magnitude / Divisor,
                _ => int.MaxValue,
            };

            Magnitude = definition.IsInverted ? -magnitude : magnitude;
        }

        /// <summary>Squares a value in 64 bits.</summary>
        /// <remarks>Named because the point of the call is that the multiplication does not overflow.</remarks>
        /// <param name="value">The value.</param>
        /// <returns>Its square.</returns>
        private static long Square(long value)
        {
            return value * value;
        }
    }
}
