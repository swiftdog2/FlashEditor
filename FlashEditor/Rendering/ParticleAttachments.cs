using System;
using FlashEditor.Definitions.Particles;

namespace FlashEditor.Rendering
{
    /// <summary>One live particle.</summary>
    /// <remarks>
    ///     A mutable struct held in a flat array and stepped by reference. That is unusual enough to
    ///     be worth stating: the cap is 2047 particles rewritten every millisecond of simulated time,
    ///     and a class would put every one of them on the heap and chase a pointer per field per step.
    ///     Nothing outside <see cref="ParticleSystem"/> mutates one - <see cref="ParticleSystem.ParticleAt"/>
    ///     hands out a copy.
    /// </remarks>
    public struct Particle
    {
        /// <summary>Position x, in twelfths of a model unit.</summary>
        public int X;

        /// <summary>Position y, in twelfths of a model unit.</summary>
        public int Y;

        /// <summary>Position z, in twelfths of a model unit.</summary>
        public int Z;

        /// <summary>Direction x, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        /// <remarks>
        ///     Short rather than int because the client stores it so
        ///     (<c>Particle_Sub4_Sub2_Sub1.java:319-321</c>), and the halving loop that keeps it in
        ///     range depends on the width - see <see cref="ParticleSystem"/>'s direction clamp.
        /// </remarks>
        public short DirectionX;

        /// <summary>Direction y, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        public short DirectionY;

        /// <summary>Direction z, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        public short DirectionZ;

        /// <summary>Speed along the direction. Doubles whenever the direction is halved.</summary>
        public int Speed;

        /// <summary>Half extent of the drawn quad, in <see cref="ParticleEmitterDefinition.SizeShift"/> fixed point.</summary>
        public int Size;

        /// <summary>Packed ARGB colour.</summary>
        public int Colour;

        /// <summary>
        ///     The fractional part of each channel of <see cref="Colour"/>, packed the same way.
        /// </summary>
        /// <remarks>
        ///     The fade rates are per millisecond and far below one unit of a channel, so without a
        ///     fraction to accumulate into, a slow fade would round to nothing every step and never
        ///     move at all. <c>Particle_Sub4_Sub2_Sub1.java:49-54</c> reassembles the two halves into
        ///     a 16-bit value per channel, adds the rate, and splits them again.
        /// </remarks>
        public int ColourFraction;

        /// <summary>Milliseconds left to live. The particle is removed when this reaches zero.</summary>
        public int Life;

        /// <summary>Milliseconds it was born with, so the ramps know how far through life it is.</summary>
        public int MaxLife;

        /// <summary>The index-26 material drawn on the quad, or -1 for untextured.</summary>
        public int MaterialId;

        /// <summary>
        ///     Which emitter of the system spawned it.
        /// </summary>
        /// <remarks>
        ///     A slot index rather than a reference, so the struct stays blittable and the array stays
        ///     free of pointers for the garbage collector to trace.
        /// </remarks>
        public int EmitterSlot;

        /// <summary>Red channel of <see cref="Colour"/>.</summary>
        public readonly int Red => (Colour >> 16) & 0xFF;

        /// <summary>Green channel of <see cref="Colour"/>.</summary>
        public readonly int Green => (Colour >> 8) & 0xFF;

        /// <summary>Blue channel of <see cref="Colour"/>.</summary>
        public readonly int Blue => Colour & 0xFF;

        /// <summary>Alpha channel of <see cref="Colour"/>.</summary>
        /// <remarks>Shifted unsigned, because the top bit of a packed ARGB is a real alpha value.</remarks>
        public readonly int Alpha => Colour >>> 24;
    }

    /// <summary>
    ///     One emitter definition attached to one face of one model.
    /// </summary>
    /// <remarks>
    ///     <b>An emitter anchors to a face, and that is not interchangeable with a vertex.</b> The
    ///     client reads the attachment's second value as a face index and expands it into that face's
    ///     three vertices at load (<c>Model.java:755-773</c>). It needs all three: a particle spawns
    ///     at a random barycentric point <i>inside</i> the triangle, and the face's normal is the
    ///     direction it leaves along. Neither is available from a single vertex, which is why the two
    ///     attachment kinds are not the same shape despite storing the same-looking pair of numbers.
    ///     <para>
    ///     The instance keeps its own spawn phase and accumulator, so several emitters sharing one
    ///     definition behave independently.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEmitterInstance
    {
        /// <summary>The face's three corners as of the last <see cref="SetFace"/>.</summary>
        private readonly int[] currentX = new int[3];

        /// <summary>The face's three corners as of the last <see cref="SetFace"/>.</summary>
        private readonly int[] currentY = new int[3];

        /// <summary>The face's three corners as of the last <see cref="SetFace"/>.</summary>
        private readonly int[] currentZ = new int[3];

        /// <summary>Where the face was before that, so spawns can be spread along its path.</summary>
        private readonly int[] previousX = new int[3];

        /// <summary>Where the face was before that, so spawns can be spread along its path.</summary>
        private readonly int[] previousY = new int[3];

        /// <summary>Where the face was before that, so spawns can be spread along its path.</summary>
        private readonly int[] previousZ = new int[3];

        /// <summary>
        ///     Fractional particles owed, in <see cref="ParticleUnits.SpawnAccumulatorPerParticle"/>ths.
        /// </summary>
        /// <remarks>
        ///     Carried across steps rather than rounded per step, which is what lets a rate below one
        ///     particle a millisecond produce anything at all.
        /// </remarks>
        private int accumulator;

        /// <summary>Face centre, used by the drag law as the point to measure distance from.</summary>
        private int centroidX;

        /// <summary>Face centre.</summary>
        private int centroidY;

        /// <summary>Face centre.</summary>
        private int centroidZ;

        /// <summary>Whether <see cref="SetFace"/> has ever run, so the first call is not a movement.</summary>
        private bool centroidValid;

        /// <summary>Middle of the yaw range a spawn direction is drawn from.</summary>
        private int yawBase;

        /// <summary>Middle of the pitch range a spawn direction is drawn from.</summary>
        private int pitchBase;

        /// <summary>The definition with its derived values.</summary>
        public ParticleEmitterRuntime Runtime { get; }

        /// <summary>The emitter id the model's attachment named.</summary>
        public int EmitterId { get; }

        /// <summary>Which model of the set it is attached to.</summary>
        public int ModelIndex { get; }

        /// <summary>Which <b>face</b> of that model it rides.</summary>
        public int FaceIndex { get; }

        /// <summary>The shared random source.</summary>
        /// <remarks>
        ///     Shared rather than per emitter on purpose. One stream across the whole system is what
        ///     makes a seeded run reproducible as a whole; per-emitter streams would be reproducible
        ///     individually and would reorder against each other whenever an emitter was added.
        /// </remarks>
        public ParticleRandom Random { get; }

        /// <summary>Whether the priming steps have already been taken.</summary>
        public bool Primed { get; private set; }

        /// <summary>
        ///     Whether the attached face has collapsed to a point.
        /// </summary>
        /// <remarks>
        ///     It then has no normal and no interior to spawn in, and the client stops the emitter dead
        ///     rather than spawning from it (<c>Particle_Sub9.java:367-375</c> sets the flag and
        ///     <c>:151</c> refuses to emit while it is set). Index 7 does hold such faces.
        /// </remarks>
        public bool FaceIsDegenerate { get; private set; }

        /// <summary>Face normal x, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        public int NormalX { get; private set; }

        /// <summary>Face normal y, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        public int NormalY { get; private set; }

        /// <summary>Face normal z, scaled to <see cref="ParticleUnits.DirectionScale"/>.</summary>
        public int NormalZ { get; private set; }

        /// <summary>Face centre x, in model units.</summary>
        public int CentroidX => centroidX;

        /// <summary>Face centre y, in model units.</summary>
        public int CentroidY => centroidY;

        /// <summary>Face centre z, in model units.</summary>
        public int CentroidZ => centroidZ;

        /// <summary>Attaches an emitter to a face.</summary>
        /// <param name="runtime">The definition with its derived values.</param>
        /// <param name="emitterId">The emitter id the attachment named.</param>
        /// <param name="modelIndex">Which model of the set.</param>
        /// <param name="faceIndex">Which face of it.</param>
        /// <param name="random">The system's shared random source.</param>
        /// <exception cref="ArgumentNullException"><paramref name="runtime"/> or <paramref name="random"/> is null.</exception>
        public ParticleEmitterInstance(ParticleEmitterRuntime runtime, int emitterId, int modelIndex, int faceIndex,
            ParticleRandom random)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            EmitterId = emitterId;
            ModelIndex = modelIndex;
            FaceIndex = faceIndex;

            //A random starting phase below the cost of one particle (Particle_Sub9.java:110). It is
            //what stops several emitters sharing a definition from spawning in lockstep, and it
            //cannot change how many particles a given interval produces, because it is always less
            //than one particle's worth.
            accumulator = random.NextScaled(ParticleUnits.SpawnAccumulatorPerParticle);
        }

        /// <summary>Moves the emitter onto a new position of its face.</summary>
        /// <remarks>
        ///     Called whenever the pose changes. It keeps the previous position as well as the new one
        ///     so <see cref="Spawn"/> can place a particle anywhere along the path the face swept -
        ///     without which a fast-moving emitter produces a puff per frame instead of a trail.
        ///     <para>
        ///     The normal is only recomputed when the centroid actually moves, which is the client's
        ///     guard at <c>Particle_Sub9.java:183-187</c>. It costs a square root and two
        ///     <c>atan2</c>s, and a static model calls this on every frame.
        ///     </para>
        /// </remarks>
        /// <param name="ax">First corner x, in model units.</param>
        /// <param name="ay">First corner y.</param>
        /// <param name="az">First corner z.</param>
        /// <param name="bx">Second corner x.</param>
        /// <param name="by">Second corner y.</param>
        /// <param name="bz">Second corner z.</param>
        /// <param name="cx">Third corner x.</param>
        /// <param name="cy">Third corner y.</param>
        /// <param name="cz">Third corner z.</param>
        public void SetFace(int ax, int ay, int az, int bx, int by, int bz, int cx, int cy, int cz)
        {
            Array.Copy(currentX, previousX, 3);
            Array.Copy(currentY, previousY, 3);
            Array.Copy(currentZ, previousZ, 3);

            currentX[0] = ax;
            currentY[0] = ay;
            currentZ[0] = az;
            currentX[1] = bx;
            currentY[1] = by;
            currentZ[1] = bz;
            currentX[2] = cx;
            currentY[2] = cy;
            currentZ[2] = cz;

            //On the first call the previous face was all zeroes, and interpolating from the origin
            //would spray the first frame's particles across the whole model.
            if (!centroidValid)
            {
                Array.Copy(currentX, previousX, 3);
                Array.Copy(currentY, previousY, 3);
                Array.Copy(currentZ, previousZ, 3);
            }

            FaceIsDegenerate = ax == bx && bx == cx && ay == by && by == cy && az == bz && bz == cz;

            int newCentroidX = (ax + bx + cx) / 3;
            int newCentroidY = (ay + by + cy) / 3;
            int newCentroidZ = (az + bz + cz) / 3;

            if (!centroidValid || newCentroidX != centroidX || newCentroidY != centroidY || newCentroidZ != centroidZ)
            {
                centroidX = newCentroidX;
                centroidY = newCentroidY;
                centroidZ = newCentroidZ;
                centroidValid = true;
                RecomputeNormal();
            }
        }

        /// <summary>Whether the emitter is inside its duty cycle and should spawn.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:147-174</c>. Four independent reasons not to emit, in the client's
        ///     order: a collapsed face, a non-repeating cycle that has already run, and the two arms
        ///     of the threshold test - which is a phase within the cycle, with
        ///     <see cref="ParticleEmitterDefinition.EmitsBeforeThreshold"/> choosing which side of it
        ///     emits. A period of -1 means "always on" and skips all of it.
        /// </remarks>
        /// <param name="elapsedMilliseconds">How long the system has been running.</param>
        /// <returns>Whether to run the spawn arithmetic this step.</returns>
        public bool IsOn(long elapsedMilliseconds)
        {
            if (FaceIsDegenerate)
            {
                return false;
            }

            ParticleEmitterDefinition definition = Runtime.Definition;

            if (definition.CyclePeriod == -1)
            {
                return true;
            }

            long intoCycle = elapsedMilliseconds;

            if (!definition.CycleRepeats && intoCycle > definition.CyclePeriod)
            {
                return false;
            }

            intoCycle %= definition.CyclePeriod;

            if (!definition.EmitsBeforeThreshold && intoCycle < definition.CycleThreshold)
            {
                return false;
            }

            if (definition.EmitsBeforeThreshold && definition.CycleThreshold <= intoCycle)
            {
                return false;
            }

            return true;
        }

        /// <summary>Accumulates the spawn rate over a number of milliseconds and returns whole particles.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:221-227</c>. The rate is drawn fresh from between its two bounds
        ///     each call rather than once per particle, so an emitter with a wide rate range flickers
        ///     rather than settling on an average.
        /// </remarks>
        /// <param name="steps">Milliseconds elapsed.</param>
        /// <returns>How many particles to spawn now.</returns>
        public int Emit(int steps)
        {
            ParticleEmitterDefinition definition = Runtime.Definition;

            accumulator += (int)(steps * (Random.NextFraction()
                * (definition.SpawnRateMax - definition.SpawnRateMin) + definition.SpawnRateMin));

            if (accumulator < ParticleUnits.SpawnAccumulatorPerParticle)
            {
                return 0;
            }

            int whole = accumulator >> 6;
            accumulator &= ParticleUnits.SpawnAccumulatorPerParticle - 1;
            return whole;
        }

        /// <summary>How many extra spawn steps to run before the first real one, once.</summary>
        /// <remarks>
        ///     An effect that has been running for a while looks different from one that has just
        ///     started - a fire has a column of smoke above it. Priming runs the emitter forward so it
        ///     appears mid-flow the moment it becomes visible, which is what stops every effect in the
        ///     scene from visibly starting when the camera reaches it.
        /// </remarks>
        /// <returns>The priming step count on the first call, and zero afterwards.</returns>
        public int TakePrimingSteps()
        {
            if (Primed)
            {
                return 0;
            }

            Primed = true;
            return Runtime.Definition.PrimeSteps;
        }

        /// <summary>Creates one particle.</summary>
        /// <remarks><c>Particle_Sub9.java:228-314</c>, in the client's draw order.</remarks>
        /// <returns>The particle, with everything but its emitter slot filled in.</returns>
        public Particle Spawn()
        {
            ParticleEmitterDefinition definition = Runtime.Definition;
            Particle particle = default;

            PickDirection(out int directionX, out int directionY, out int directionZ);
            PickPosition(out int positionX, out int positionY, out int positionZ);

            particle.X = positionX << ParticleUnits.PositionFractionBits;
            particle.Y = positionY << ParticleUnits.PositionFractionBits;
            particle.Z = positionZ << ParticleUnits.PositionFractionBits;
            particle.DirectionX = (short)directionX;
            particle.DirectionY = (short)directionY;
            particle.DirectionZ = (short)directionZ;
            particle.Speed = Random.NextScaled(definition.SpeedMax - definition.SpeedMin) + definition.SpeedMin;

            //Narrowed to a short before it is stored, matching the client's field width - a lifetime
            //above 32767 milliseconds wraps rather than being clamped.
            particle.Life = particle.MaxLife =
                (short)(Random.NextScaled(definition.LifetimeMax - definition.LifetimeMin) + definition.LifetimeMin);

            particle.Size = Runtime.SizeMin + Random.NextScaled(Runtime.SizeMax - Runtime.SizeMin);
            particle.Colour = PickColour();
            particle.ColourFraction = 0;
            particle.MaterialId = definition.MaterialId;

            return particle;
        }

        /// <summary>Chooses the direction a particle leaves in.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:231-247</c>. Either straight out along the face normal, or a
        ///     random bearing inside the emitter's cone - see
        ///     <see cref="ParticleEmitterRuntime.SpawnsAlongAnAngleRange"/> for which, and note that
        ///     the pitch is masked to thirteen bits rather than fourteen, so it covers half a turn
        ///     where the yaw covers a whole one.
        /// </remarks>
        /// <param name="x">Direction x.</param>
        /// <param name="y">Direction y.</param>
        /// <param name="z">Direction z.</param>
        private void PickDirection(out int x, out int y, out int z)
        {
            if (!Runtime.SpawnsAlongAnAngleRange)
            {
                x = NormalX;
                y = NormalY;
                z = NormalZ;
                return;
            }

            int yaw = (Random.NextScaled(Runtime.YawSpread) + yawBase) & 0x3FFF;
            int yawSin = SkeletalTrig.Sin(yaw);
            int yawCos = SkeletalTrig.Cos(yaw);

            int pitch = (Random.NextScaled(Runtime.PitchSpread) + pitchBase) & 0x1FFF;
            int pitchSin = SkeletalTrig.Sin(pitch);
            int pitchCos = SkeletalTrig.Cos(pitch);

            x = yawCos * pitchSin >> 13;
            y = (pitchCos << 1) * -1;
            z = yawSin * pitchSin >> 13;
        }

        /// <summary>Chooses where inside the swept face a particle appears.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:249-274</c>, in two stages.
        ///     <para>
        ///     <b>A uniform point in a triangle.</b> Two independent fractions whose sum is folded back
        ///     under one when it exceeds it (<c>:251-254</c>). Folding rather than redrawing is what
        ///     makes it uniform - the pair lands in the unit square, and reflecting the far half of it
        ///     through the diagonal maps the square onto the triangle without bunching the result
        ///     anywhere. Clamping instead would pile spawns along the hypotenuse.
        ///     </para>
        ///     <para>
        ///     <b>A point along the face's path.</b> The barycentric point is computed on the current
        ///     face and on the previous one and interpolated between them by a third fraction, so a
        ///     face that moved a long way in one frame leaves a continuous trail rather than a puff at
        ///     each end. The three axes draw their own fraction, which is the client's and means the
        ///     result is not strictly on the segment between the two points.
        ///     </para>
        /// </remarks>
        /// <param name="x">Spawn x, in model units.</param>
        /// <param name="y">Spawn y.</param>
        /// <param name="z">Spawn z.</param>
        private void PickPosition(out int x, out int y, out int z)
        {
            float weightA = (float)Random.NextFraction();
            float weightB = (float)Random.NextFraction();

            if (weightA + weightB > 1f)
            {
                weightA = 1f - weightA;
                weightB = 1f - weightB;
            }

            float weightC = 1f - (weightB + weightA);

            int onCurrentX = (int)(currentX[1] * weightB + currentX[0] * weightA + currentX[2] * weightC);
            int onCurrentY = (int)(weightC * currentY[2] + (currentY[0] * weightA + weightB * currentY[1]));
            int onCurrentZ = (int)(currentZ[2] * weightC + (currentZ[0] * weightA + currentZ[1] * weightB));

            int onPreviousX = (int)(previousX[0] * weightA + weightB * previousX[1] + weightC * previousX[2]);
            int onPreviousY = (int)(weightC * previousY[2] + (previousY[1] * weightB + previousY[0] * weightA));
            int onPreviousZ = (int)(weightB * previousZ[1] + weightA * previousZ[0] + previousZ[2] * weightC);

            x = (int)((onCurrentX - onPreviousX) * Random.NextFraction() + onPreviousX);
            y = (int)(onPreviousY + (onCurrentY - onPreviousY) * Random.NextFraction());
            z = (int)(onPreviousZ + Random.NextFraction() * (onCurrentZ - onPreviousZ));
        }

        /// <summary>Chooses a particle's spawn colour from between the two stored colours.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:284-300</c>. Two modes, and the difference is visible:
        ///     <b>independently</b> per channel gives a colour anywhere in the box between the two
        ///     stored colours, which for a red-to-blue pair includes purple and grey; one shared
        ///     fraction gives a colour on the <i>line</i> between them. Alpha is always drawn
        ///     independently in both.
        /// </remarks>
        /// <returns>The packed ARGB colour.</returns>
        private int PickColour()
        {
            ParticleEmitterRuntime runtime = Runtime;

            if (runtime.Definition.RandomisesColourChannelsIndependently)
            {
                int alpha = Random.NextScaled(runtime.AlphaSpan) + runtime.AlphaBase;
                int red = runtime.RedBase + Random.NextScaled(runtime.RedSpan);
                int green = Random.NextScaled(runtime.GreenSpan) + runtime.GreenBase;
                int blue = runtime.BlueBase + Random.NextScaled(runtime.BlueSpan);
                return (alpha << 24) | (red << 16) | (green << 8) | blue;
            }

            double along = Random.NextFraction();
            int sharedRed = (int)(runtime.RedBase + along * runtime.RedSpan);
            int sharedGreen = (int)(runtime.GreenSpan * along + runtime.GreenBase);
            int sharedBlue = (int)(along * runtime.BlueSpan + runtime.BlueBase);
            int sharedAlpha = Random.NextScaled(runtime.AlphaSpan) + runtime.AlphaBase;

            return (sharedAlpha << 24) | (sharedRed << 16) | (sharedGreen << 8) | sharedBlue;
        }

        /// <summary>Recomputes the face normal and, with it, the middle of the spawn cone.</summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:188-219</c>. The same halve-until-it-fits, normalise-to-a-length
        ///     shape as <see cref="PosedNormals"/>, but to
        ///     <see cref="ParticleUnits.DirectionScale"/> rather than 256, because this feeds a
        ///     direction rather than a lighting term.
        ///     <para>
        ///     The cone's centre is the <i>normal's own</i> bearing plus the definition's start bound,
        ///     less half its spread (<c>:212-218</c>). So an emitter's angle bounds are relative to the
        ///     face it is attached to and not to the world, which is what lets one definition be reused
        ///     across faces pointing in different directions. The 8192/pi constant converts a radian
        ///     bearing into the client's angle unit.
        ///     </para>
        /// </remarks>
        private void RecomputeNormal()
        {
            int abX = currentX[1] - currentX[0];
            int abY = currentY[1] - currentY[0];
            int abZ = currentZ[1] - currentZ[0];
            int acX = currentX[2] - currentX[0];
            int acY = currentY[2] - currentY[0];
            int acZ = currentZ[2] - currentZ[0];

            int crossZ = abX * acY - abY * acX;
            int crossX = abY * acZ - abZ * acY;
            int crossY = abZ * acX - abX * acZ;

            //Halved together so the direction survives, until each fits a signed short - which is the
            //width the particle's direction fields are stored at.
            while (crossX > ParticleUnits.DirectionScale || crossY > ParticleUnits.DirectionScale
                || crossZ > ParticleUnits.DirectionScale || crossX < -ParticleUnits.DirectionScale
                || crossY < -ParticleUnits.DirectionScale || crossZ < -ParticleUnits.DirectionScale)
            {
                crossX >>= 1;
                crossY >>= 1;
                crossZ >>= 1;
            }

            int length = (int)Math.Sqrt(
                (double)crossZ * crossZ + (double)crossX * crossX + (double)crossY * crossY);

            if (length <= 0)
            {
                length = 1;
            }

            NormalX = crossX * ParticleUnits.DirectionScale / length;
            NormalY = crossY * ParticleUnits.DirectionScale / length;
            NormalZ = crossZ * ParticleUnits.DirectionScale / length;

            if (!Runtime.SpawnsAlongAnAngleRange)
            {
                return;
            }

            //8192 / pi: half a turn of the client's 16384-step unit per pi radians.
            const double AngleStepsPerRadian = 8192.0 / Math.PI;

            int normalYaw = (int)(AngleStepsPerRadian * Math.Atan2(NormalZ, NormalX));
            int normalPitch = (int)(Math.Atan2(NormalY,
                Math.Sqrt((double)NormalZ * NormalZ + (double)NormalX * NormalX)) * AngleStepsPerRadian);

            yawBase = Runtime.YawStart + normalYaw - (Runtime.YawSpread >> 1);
            pitchBase = Runtime.PitchStart + normalPitch - (Runtime.PitchSpread >> 1);
        }
    }

    /// <summary>
    ///     One effector definition attached to one vertex of one model.
    /// </summary>
    /// <remarks>
    ///     <b>An effector anchors to a vertex, and that is not interchangeable with a face.</b> The
    ///     client indexes the vertex coordinate arrays with the attachment's second value
    ///     (<c>Renderable_Sub1.java:1461-1472</c>, using <c>Class35.anInt327</c> from
    ///     <c>Model.java:779-781</c>). It needs only a point, because an effector is a point source of
    ///     force with a direction of its own - it has no use for a normal or an interior, which is
    ///     exactly what an emitter does need.
    ///     <para>
    ///     So the two attachment lists sit next to each other in the same tail block, store the same
    ///     pair of numbers, and mean different things. Crossing them produces an effect that comes out
    ///     of the wrong part of the model, and nothing below this layer can detect it.
    ///     </para>
    /// </remarks>
    public sealed class ParticleEffectorInstance
    {
        /// <summary>The definition with its derived values.</summary>
        public ParticleEffectorRuntime Runtime { get; }

        /// <summary>The effector id the model's attachment named.</summary>
        public int EffectorId { get; }

        /// <summary>Which model of the set it is attached to.</summary>
        public int ModelIndex { get; }

        /// <summary>Which <b>vertex</b> of that model it rides.</summary>
        public int VertexIndex { get; }

        /// <summary>Current position x, in model units.</summary>
        public int X { get; private set; }

        /// <summary>Current position y, in model units.</summary>
        public int Y { get; private set; }

        /// <summary>Current position z, in model units.</summary>
        public int Z { get; private set; }

        /// <summary>Attaches an effector to a vertex.</summary>
        /// <param name="runtime">The definition with its derived values.</param>
        /// <param name="effectorId">The effector id the attachment named.</param>
        /// <param name="modelIndex">Which model of the set.</param>
        /// <param name="vertexIndex">Which vertex of it.</param>
        /// <exception cref="ArgumentNullException"><paramref name="runtime"/> is null.</exception>
        public ParticleEffectorInstance(ParticleEffectorRuntime runtime, int effectorId, int modelIndex,
            int vertexIndex)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            EffectorId = effectorId;
            ModelIndex = modelIndex;
            VertexIndex = vertexIndex;
        }

        /// <summary>Moves the effector onto a new position of its vertex.</summary>
        /// <remarks>
        ///     No previous position kept, unlike <see cref="ParticleEmitterInstance.SetFace"/>. An
        ///     effector is sampled where it is now when a particle is stepped, so there is nothing to
        ///     spread along a path.
        /// </remarks>
        /// <param name="x">Vertex x, in model units.</param>
        /// <param name="y">Vertex y.</param>
        /// <param name="z">Vertex z.</param>
        public void SetPosition(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
