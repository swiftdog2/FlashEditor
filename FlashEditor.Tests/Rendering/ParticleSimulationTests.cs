using System;
using System.Collections.Generic;
using System.Numerics;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Particles;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins the particle simulation against numbers worked out by hand from the 637 client.
    /// </summary>
    /// <remarks>
    ///     Nothing in this suite can see the GL surface, so what is checked here is the arithmetic
    ///     and the bookkeeping: what a definition derives to, where an emitter sits, how many
    ///     particles a spawn rate produces, and what the cap does when it is reached. Every expected
    ///     value is derived in the test from the client's formula rather than produced by running the
    ///     code - a simulation compared against itself would agree with any mistake in it.
    /// </remarks>
    public class ParticleSimulationTests
    {
        /// <summary>
        ///     A spawn colour pair unpacks into a base and a signed span per channel.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:679-693</c>. The span is a difference and is routinely negative -
        ///     an emitter fading from bright to dark stores the bright colour first - so nothing here
        ///     may treat it as a magnitude.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_UnpacksBothSpawnColours()
        {
            var runtime = new ParticleEmitterRuntime(new ParticleEmitterDefinition
            {
                SpawnColourStart = unchecked((int)0x80FF8040),
                SpawnColourEnd = unchecked((int)0xFF008020)
            });

            Assert.Equal(0xFF, runtime.RedBase);
            Assert.Equal(0x00 - 0xFF, runtime.RedSpan);
            Assert.Equal(0x80, runtime.GreenBase);
            Assert.Equal(0, runtime.GreenSpan);
            Assert.Equal(0x40, runtime.BlueBase);
            Assert.Equal(0x20 - 0x40, runtime.BlueSpan);
            Assert.Equal(0x80, runtime.AlphaBase);
            Assert.Equal(0xFF - 0x80, runtime.AlphaSpan);
        }

        /// <summary>
        ///     A colour fade rate is the distance to the fade colour spread over its share of the
        ///     lifetime, nudged by four.
        /// </summary>
        /// <remarks>
        ///     The nudge is <c>ParticleType.java:716-730</c>: four is added to a rate that is zero or
        ///     negative and subtracted from a positive one. It is not rounding - it deliberately
        ///     overshoots so the fade finishes instead of asymptoting - and it decides the colour a
        ///     particle dies at, so it is reproduced rather than tidied away.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_DerivesTheColourFadeRateWithTheClientsNudge()
        {
            var runtime = new ParticleEmitterRuntime(new ParticleEmitterDefinition
            {
                SpawnColourStart = unchecked((int)0x00FF0000),
                SpawnColourEnd = 0x00000000,
                FadeColour = 0x40201008,
                FadeColourPercent = 50,
                LifetimeMax = 1000
            });

            Assert.True(runtime.HasColourRamp);
            Assert.Equal(500, runtime.ColourRampSteps);
            Assert.Equal(1000, runtime.AlphaRampSteps);

            //Red: base 255, span -255. (0x20 - 255 - (-255/2)) << 8, over 500 steps, then +4.
            int expected = ((0x20 - 255 - -255 / 2) << 8) / 500 + 4;
            Assert.Equal(expected, runtime.RedRate);
            Assert.True(runtime.RedRate < 0, "Red is fading downwards, so its rate must be negative.");
        }

        /// <summary>No fade colour means no colour ramp at all, rather than a fade to black.</summary>
        /// <remarks>
        ///     <c>ParticleType.java:695</c> gates the whole block on the packed value being nonzero,
        ///     so zero is the "no ramp" value and not the colour black.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_TreatsAZeroFadeColourAsNoRamp()
        {
            var runtime = new ParticleEmitterRuntime(new ParticleEmitterDefinition { LifetimeMax = 1000 });

            Assert.False(runtime.HasColourRamp);
            Assert.Equal(0, runtime.RedRate);
        }

        /// <summary>The size and speed ramps spread their distance over a share of the lifetime.</summary>
        [Fact]
        public void EmitterRuntime_DerivesTheSizeAndSpeedRamps()
        {
            var definition = new ParticleEmitterDefinition
            {
                LifetimeMax = 1000,
                SizeMinStored = 2,
                SizeMaxStored = 6,
                EndSizeStored = 10,
                SizeRampPercent = 25,
                SpeedMin = 10,
                SpeedMax = 30,
                EndSpeed = 100000,
                SpeedRampPercent = 100
            };

            var runtime = new ParticleEmitterRuntime(definition);

            Assert.Equal(2 << ParticleEmitterDefinition.SizeShift, runtime.SizeMin);
            Assert.Equal(6 << ParticleEmitterDefinition.SizeShift, runtime.SizeMax);

            Assert.True(runtime.HasSizeRamp);
            Assert.Equal(250, runtime.SizeRampSteps);
            Assert.Equal((runtime.EndSize - runtime.SizeMin - (runtime.SizeMax - runtime.SizeMin) / 2) / 250,
                runtime.SizeRate);

            Assert.True(runtime.HasSpeedRamp);
            Assert.Equal(1000, runtime.SpeedRampSteps);
            Assert.Equal((100000 - (30 - 10) / 2 - 10) / 1000, runtime.SpeedRate);
        }

        /// <summary>A stored -1 end size is "no ramp", not a size.</summary>
        /// <remarks>
        ///     The guard at <c>ParticleType.java:733</c> is against the stored value, and the shifted
        ///     value can never be -1 - which is why the flag is kept rather than a sentinel size.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_TreatsAStoredMinusOneEndSizeAsNoRamp()
        {
            var runtime = new ParticleEmitterRuntime(new ParticleEmitterDefinition { LifetimeMax = 100 });

            Assert.False(runtime.HasSizeRamp);
            Assert.False(runtime.HasSpeedRamp);
        }

        /// <summary>
        ///     A stored angle bound is shifted through a sixteen-bit field, so a large one wraps.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:527-533</c> assigns the shifted value back into a <c>short</c>.
        ///     That is why the definition keeps the stored value rather than the shifted one, and it
        ///     is what decides the spawn cone of any emitter storing a bound above 4095.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_NarrowsAShiftedAngleBoundToAShort()
        {
            var runtime = new ParticleEmitterRuntime(new ParticleEmitterDefinition
            {
                YawStartStored = 0,
                YawEndStored = 8192,
                PitchStartStored = 0,
                PitchEndStored = 100
            });

            //8192 << 3 is 65536, which is zero in sixteen bits.
            Assert.Equal(0, runtime.YawEnd);
            Assert.Equal(800, runtime.PitchEnd);
        }

        /// <summary>
        ///     Both end bounds at zero means every particle leaves along the face normal.
        /// </summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:211</c> and <c>:231</c> test the two <em>end</em> bounds, not the
        ///     spreads, so an emitter with wide start bounds and zero end bounds still spawns straight
        ///     out of its face.
        /// </remarks>
        [Fact]
        public void EmitterRuntime_UsesTheFaceNormalWhenBothEndBoundsAreZero()
        {
            Assert.False(new ParticleEmitterRuntime(new ParticleEmitterDefinition
            {
                YawStartStored = 900,
                PitchStartStored = 900
            }).SpawnsAlongAnAngleRange);

            Assert.True(new ParticleEmitterRuntime(new ParticleEmitterDefinition
            {
                YawEndStored = 1
            }).SpawnsAlongAnAngleRange);
        }

        /// <summary>Either height bound moving off its default arms the plane test.</summary>
        [Fact]
        public void EmitterRuntime_ArmsTheHeightTestFromEitherBound()
        {
            Assert.False(new ParticleEmitterRuntime(new ParticleEmitterDefinition()).HasHeightBound);
            Assert.True(new ParticleEmitterRuntime(new ParticleEmitterDefinition { CeilingPlane = -1 })
                .HasHeightBound);
            Assert.True(new ParticleEmitterRuntime(new ParticleEmitterDefinition { FloorPlane = 0 })
                .HasHeightBound);
        }

        /// <summary>
        ///     An effector's reach is derived from its force before the inversion flips the sign.
        /// </summary>
        /// <remarks>
        ///     <c>Class66.java:255-276</c> computes the magnitude, then the radius bound from it, and
        ///     only then negates the magnitude for an inverted effector. Doing the negation first
        ///     would give a pulling effector a negative radius and no reach at all.
        /// </remarks>
        [Fact]
        public void EffectorRuntime_SizesItsReachBeforeInvertingItsForce()
        {
            var upright = new ParticleEffectorRuntime(new ParticleEffectorDefinition
            {
                DirectionX = 3,
                DirectionY = 4,
                DirectionZ = 0,
                FalloffMode = 1,
                Strength = 1
            });

            var inverted = new ParticleEffectorDefinition
            {
                DirectionX = 3,
                DirectionY = 4,
                DirectionZ = 0,
                FalloffMode = 1,
                Strength = 1,
                IsInverted = true
            };

            Assert.Equal(5, upright.Magnitude);
            Assert.Equal(8L * 5 / 1 * (8L * 5 / 1), upright.RadiusBound);

            var pulled = new ParticleEffectorRuntime(inverted);
            Assert.Equal(-5, pulled.Magnitude);
            Assert.Equal(upright.RadiusBound, pulled.RadiusBound);
        }

        /// <summary>
        ///     The radius bound is squared under falloff mode 1 and not under mode 2.
        /// </summary>
        /// <remarks>
        ///     <c>Class66.java:264-269</c>, while the comparison against it
        ///     (<c>Particle_Sub4_Sub2_Sub1.java:153</c>) is always a squared distance. That makes a
        ///     mode-2 effector's real reach the square root of what the arithmetic reads as. It is
        ///     the client's, the data has no opinion on it, and it stands - pinned here so a later
        ///     reader does not "fix" it.
        /// </remarks>
        [Fact]
        public void EffectorRuntime_SquaresTheBoundForOneFalloffModeOnly()
        {
            ParticleEffectorDefinition Definition(int mode) => new ParticleEffectorDefinition
            {
                DirectionX = 3,
                DirectionY = 4,
                FalloffMode = mode,
                Strength = 1
            };

            Assert.Equal((long)int.MaxValue, new ParticleEffectorRuntime(Definition(0)).RadiusBound);
            Assert.Equal(1600L, new ParticleEffectorRuntime(Definition(1)).RadiusBound);
            Assert.Equal(40L, new ParticleEffectorRuntime(Definition(2)).RadiusBound);
        }

        /// <summary>A stored strength of zero becomes one, since it is a divisor.</summary>
        [Fact]
        public void EffectorRuntime_RepairsAZeroDivisor()
        {
            var runtime = new ParticleEffectorRuntime(new ParticleEffectorDefinition { Strength = 0 });

            Assert.Equal(1, runtime.Divisor);
        }

        /// <summary>
        ///     An emitter sits on its face's centre and an effector on its vertex.
        /// </summary>
        /// <remarks>
        ///     <b>The test this file exists for.</b> The two attachment lists sit next to each other
        ///     in the same tail block of a model file and store the same-looking pair of numbers, but
        ///     <c>Model.java:762-772</c> indexes the face arrays with one and
        ///     <c>Renderable_Sub1.java:1461-1472</c> indexes the vertex arrays with the other. The
        ///     model below is built so no face centre coincides with any vertex, which is what makes
        ///     a crossed pair produce a number that cannot be right.
        /// </remarks>
        [Fact]
        public void Attachments_PutTheEmitterOnAFaceAndTheEffectorOnAVertex()
        {
            ModelDefinition model = TwoTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(1, 1) };
            model.Effectors = new[] { new ModelParticleEffector(2, 4) };

            ParticleSystem system = Build(model);

            Assert.Equal(1, system.EmitterCount);
            Assert.Equal(1, system.EffectorCount);

            //Face 1 is vertices 3, 4 and 5: (600,0,0), (900,300,0) and (600,600,0).
            ParticleEmitterInstance emitter = system.Emitters[0];
            Assert.Equal((600 + 900 + 600) / 3, emitter.CentroidX);
            Assert.Equal((0 + 300 + 600) / 3, emitter.CentroidY);
            Assert.Equal(0, emitter.CentroidZ);

            //Vertex 4 is (900,300,0), which is not the centre of anything.
            ParticleEffectorInstance effector = system.Effectors[0];
            Assert.Equal(900, effector.X);
            Assert.Equal(300, effector.Y);
            Assert.Equal(0, effector.Z);
            Assert.NotEqual(emitter.CentroidX, effector.X);
        }

        /// <summary>
        ///     A pose drags both kinds of attachment with it.
        /// </summary>
        /// <remarks>
        ///     The join between the animation work and this. An emitter that stayed at the rest
        ///     position would spray particles out of thin air while the model waved somewhere else,
        ///     and the client explicitly rewrites both every time it transforms the model.
        /// </remarks>
        [Fact]
        public void Attachments_FollowThePose()
        {
            ModelDefinition model = TwoTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(1, 1) };
            model.Effectors = new[] { new ModelParticleEffector(2, 4) };

            ParticleSystem system = Build(model);
            int restCentroidX = system.Emitters[0].CentroidX;
            int restEffectorX = system.Effectors[0].X;

            PosedMesh pose = new SkinnedModel(model).CreatePose();
            pose.Reset();
            for (int v = 0; v < pose.VertexX.Length; v++)
                pose.VertexX[v] += 1000;

            system.ApplyPose(new List<PosedMesh> { pose });

            Assert.Equal(restCentroidX + 1000, system.Emitters[0].CentroidX);
            Assert.Equal(restEffectorX + 1000, system.Effectors[0].X);

            system.ApplyPose(null);
            Assert.Equal(restCentroidX, system.Emitters[0].CentroidX);
        }

        /// <summary>
        ///     A spawn rate of 64 sixty-fourths produces one particle a millisecond.
        /// </summary>
        /// <remarks>
        ///     <c>Particle_Sub9.java:221-227</c>: the rate accumulates per millisecond and one
        ///     particle costs 64. The emitter also starts with a random phase below 64
        ///     (<c>:110</c>), which is what stops several emitters sharing a definition from spawning
        ///     in lockstep - and which cannot change this count, because the phase is always less
        ///     than the cost of one particle.
        /// </remarks>
        [Fact]
        public void Spawning_ProducesOneParticlePerMillisecondAtSixtyFour()
        {
            ParticleSystem system = Build(SteadyEmitterModel());

            Assert.True(system.Advance(0.010));

            Assert.Equal(10, system.LiveParticleCount);
            Assert.Equal(10L, system.TotalSpawned);
            Assert.Equal(1, system.ActiveEmitterCount);
            Assert.Equal(0L, system.SpawnsRefusedByCap);
        }

        /// <summary>
        ///     Spawning stops at the cap and the refusals are counted rather than hidden.
        /// </summary>
        /// <remarks>
        ///     An uncapped system stalls the viewport, so the cap is not optional. The count is
        ///     public because a truncated effect and a working one look the same on a screen nobody
        ///     can capture, and a steadily rising refusal count is how a human knows which they are
        ///     looking at.
        /// </remarks>
        [Fact]
        public void Spawning_StopsAtTheCapAndSaysHowMuchItRefused()
        {
            ParticleSystem system = Build(SteadyEmitterModel(), maximumParticles: 4);

            Assert.True(system.Advance(0.010));

            Assert.Equal(4, system.LiveParticleCount);
            Assert.Equal(4, system.MaximumParticles);
            Assert.Equal(6L, system.SpawnsRefusedByCap);
            Assert.Contains("4/4 particles", system.Status);
        }

        /// <summary>The cap the editor defaults to is the client's own lowest-detail ceiling.</summary>
        [Fact]
        public void Cap_MatchesTheClientsLowestDetailCeiling()
        {
            Assert.Equal(ParticleSystem.DefaultMaximumParticles, ParticleSystem.ClientDetailCaps[0]);
            Assert.Equal(new[] { 2047, 16383, 65535 }, ParticleSystem.ClientDetailCaps);
        }

        /// <summary>A particle dies when its lifetime runs out.</summary>
        /// <remarks>
        ///     The rate is dropped to zero between the two advances so the second one has nothing new
        ///     to spawn, which is what makes the count after it a statement about expiry rather than
        ///     about the balance between spawning and dying.
        /// </remarks>
        [Fact]
        public void Particles_DieWhenTheirLifetimeRunsOut()
        {
            ModelDefinition model = SteadyEmitterModel();
            ParticleSystem system = Build(model);

            Assert.True(system.Advance(0.010));
            Assert.Equal(10, system.LiveParticleCount);
            Assert.Equal(100, system.ParticleAt(0).MaxLife);

            //Already 90: a particle spawned during an advance is stepped by that same advance, which
            //is what the client does - method3109 runs over every live particle including the ones
            //the emitter has just added.
            Assert.Equal(90, system.ParticleAt(0).Life);

            ParticleEmitterDefinition definition = system.Emitters[0].Runtime.Definition;
            definition.SpawnRateMin = definition.SpawnRateMax = 0;

            Assert.True(system.Advance(0.050));
            Assert.Equal(10, system.LiveParticleCount);
            Assert.Equal(40, system.ParticleAt(0).Life);

            Assert.True(system.Advance(0.060));
            Assert.Equal(0, system.LiveParticleCount);
            Assert.Equal(0, system.ActiveEmitterCount);
        }

        /// <summary>Particles spawn on the emitter's face, not at the model's origin.</summary>
        [Fact]
        public void Particles_SpawnInsideTheAttachedFace()
        {
            ParticleSystem system = Build(SteadyEmitterModel());
            Assert.True(system.Advance(0.010));

            for (int i = 0; i < system.LiveParticleCount; i++)
            {
                Particle particle = system.ParticleAt(i);
                int x = particle.X >> ParticleUnits.PositionFractionBits;
                int y = particle.Y >> ParticleUnits.PositionFractionBits;

                //One unit of slack each way: the barycentric blend is done in single precision and
                //truncated, so a point exactly on a corner can land a unit outside it.
                Assert.InRange(x, 599, 901);
                Assert.InRange(y, -1, 601);
            }
        }

        /// <summary>A step longer than the client's give-up window is clamped, and the loss reported.</summary>
        /// <remarks>
        ///     A UI thread that stalls - loading a model, say - must not come back and run ten
        ///     seconds of emission in one frame. At a realistic rate that is tens of thousands of
        ///     spawns the cap immediately throws away, and the frame it lands on is the one the user
        ///     sees stutter.
        /// </remarks>
        [Fact]
        public void Advance_ClampsALongStallAndReportsTheLoss()
        {
            ParticleSystem system = Build(SteadyEmitterModel(), maximumParticles: 64);

            Assert.True(system.Advance(10.0));

            Assert.Equal((long)ParticleSystem.MaximumStepMilliseconds, system.ElapsedMilliseconds);
            Assert.Equal(10000L - ParticleSystem.MaximumStepMilliseconds, system.DroppedMilliseconds);
        }

        /// <summary>Sub-millisecond remainders are carried, not dropped.</summary>
        /// <remarks>
        ///     A 30fps redraw is 33.33ms. Dropping the third of a millisecond each time would run
        ///     every effect one percent slow, which is invisible and wrong.
        /// </remarks>
        [Fact]
        public void Advance_CarriesTheSubMillisecondRemainder()
        {
            ParticleSystem system = Build(SteadyEmitterModel());

            for (int frame = 0; frame < 3; frame++)
                system.Advance(1.0 / 30.0);

            Assert.Equal(100L, system.ElapsedMilliseconds);
        }

        /// <summary>An attachment naming a definition the cache does not hold is counted, not thrown.</summary>
        [Fact]
        public void Attachments_CountWhatTheyCouldNotResolve()
        {
            ModelDefinition model = TwoTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(999, 0) };
            model.Effectors = new[] { new ModelParticleEffector(998, 0) };

            ParticleSystem system = Build(model);

            Assert.Equal(0, system.EmitterCount);
            Assert.Equal(1, system.MissingEmitterCount);
            Assert.Equal(1, system.MissingEffectorCount);
            Assert.Equal("no emitters attached", system.Status);
        }

        /// <summary>An attachment naming a face or vertex the model does not have is counted.</summary>
        [Fact]
        public void Attachments_CountWhatIsOutOfRange()
        {
            ModelDefinition model = TwoTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(1, 99) };
            model.Effectors = new[] { new ModelParticleEffector(2, 99) };

            ParticleSystem system = Build(model);

            Assert.Equal(0, system.EmitterCount);
            Assert.Equal(0, system.EffectorCount);
            Assert.Equal(2, system.OutOfRangeAttachmentCount);
        }

        /// <summary>
        ///     Nothing culls a particle against terrain here, and the system says so.
        /// </summary>
        /// <remarks>
        ///     Opcodes 12, 13 and 33 destroy a particle against the scene, and a model previewed on
        ///     its own has no scene. An effect that relies on a floor to stop its particles will look
        ///     wrong in the viewport and be right in the client - which is worth stating up front
        ///     rather than leaving someone to find.
        /// </remarks>
        [Fact]
        public void SceneBounds_AreNotSimulatedAndAreReported()
        {
            ParticleSystem system = Build(SteadyEmitterModel());

            Assert.False(system.SimulatesSceneBounds);
            Assert.Contains(system.Diagnostics,
                entry => entry.Key == "Scene bounds simulated" && entry.Value == "no");
        }

        /// <summary>
        ///     A billboard is a camera-facing square whose half extent is the size shifted down twelve.
        /// </summary>
        /// <remarks>
        ///     <c>Class360.java:141-150</c>. The corners come from the modelview matrix's first two
        ///     rows, which are the camera's right and up axes, so the quad faces the camera at every
        ///     angle without any per-particle rotation.
        /// </remarks>
        [Fact]
        public void Billboards_AreCameraFacingSquaresOfTheParticlesSize()
        {
            ParticleSystem system = Build(PointEmitterModel());
            Assert.True(system.Advance(0.001));
            Assert.Equal(1, system.LiveParticleCount);

            var buffer = new float[ParticleBillboards.FloatsPerParticle];
            int written = ParticleBillboards.Build(system, Vector3.UnitX, Vector3.UnitY,
                new Vector3(0f, 0f, 1f), buffer);

            Assert.Equal(1, written);

            //Stored size 4 shifts up 14 at load and down 12 at draw, so the half extent is 16 model
            //units, which is an eighth of a world unit.
            const float half = (4 << ParticleEmitterDefinition.SizeShift >>
                                ParticleUnits.SizeFractionBits) / RenderSpace.ModelUnitsPerWorldUnit;
            Assert.Equal(0.125, half, 5);

            Vector3 bottomLeft = Corner(buffer, 0);
            Vector3 bottomRight = Corner(buffer, 1);
            Vector3 topRight = Corner(buffer, 2);
            Vector3 topLeft = Corner(buffer, 3);

            Assert.Equal(2 * half, bottomRight.X - bottomLeft.X, 5);
            Assert.Equal(2 * half, topRight.Y - bottomRight.Y, 5);
            Assert.Equal(bottomLeft.X, topLeft.X, 5);
            Assert.Equal(bottomLeft.Z, topRight.Z, 5);

            //The face is at model x 1280, which is world x 10.
            Vector3 centre = (bottomLeft + topRight) / 2f;
            Assert.Equal(10.0, centre.X, 1);
        }

        /// <summary>
        ///     A particle's colour reaches the buffer pre-divided by the shader's lighting.
        /// </summary>
        /// <remarks>
        ///     The particles share the model shader, which lights what it draws. Writing the light
        ///     direction as the billboard's normal fixes the lighting at a known constant, and
        ///     dividing by it first is what makes the spawn colour the colour on screen.
        /// </remarks>
        [Fact]
        public void Billboards_CarryTheSpawnColourUnlit()
        {
            ParticleSystem system = Build(PointEmitterModel());
            Assert.True(system.Advance(0.001));

            var buffer = new float[ParticleBillboards.FloatsPerParticle];
            ParticleBillboards.Build(system, Vector3.UnitX, Vector3.UnitY, new Vector3(0f, 0f, 1f), buffer);

            Particle particle = system.ParticleAt(0);
            Assert.Equal(0x80, particle.Red);
            Assert.Equal(0x40, particle.Green);
            Assert.Equal(0x20, particle.Blue);
            Assert.Equal(0xFF, particle.Alpha);

            //Colour is at offset 9 and alpha at 8 in the twelve-float vertex.
            Assert.Equal(0x80 / 255f, buffer[9] * OverlayGeometry.FullIncidenceLighting, 4);
            Assert.Equal(0x40 / 255f, buffer[10] * OverlayGeometry.FullIncidenceLighting, 4);
            Assert.Equal(0x20 / 255f, buffer[11] * OverlayGeometry.FullIncidenceLighting, 4);
            Assert.Equal(1f, buffer[8], 4);
        }

        /// <summary>Two triangles per particle, sharing four corners.</summary>
        [Fact]
        public void Billboards_IndexTwoTrianglesPerParticle()
        {
            uint[] indices = ParticleBillboards.BuildIndices(2);

            Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 }, indices);
        }

        /// <summary>The same seed replays the same run, so a preview and a test are repeatable.</summary>
        [Fact]
        public void Simulation_IsDeterministicForASeed()
        {
            ParticleSystem first = Build(SteadyEmitterModel(), seed: 12345);
            ParticleSystem second = Build(SteadyEmitterModel(), seed: 12345);

            first.Advance(0.020);
            second.Advance(0.020);

            Assert.Equal(first.LiveParticleCount, second.LiveParticleCount);
            for (int i = 0; i < first.LiveParticleCount; i++)
            {
                Assert.Equal(first.ParticleAt(i).X, second.ParticleAt(i).X);
                Assert.Equal(first.ParticleAt(i).Life, second.ParticleAt(i).Life);
            }
        }

        /// <summary>Reads one billboard corner's position out of the interleaved buffer.</summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="corner">Which of the four corners.</param>
        /// <returns>The world position.</returns>
        private static Vector3 Corner(float[] buffer, int corner)
        {
            int o = corner * OverlayGeometry.FloatsPerVertex;
            return new Vector3(buffer[o], buffer[o + 1], buffer[o + 2]);
        }

        /// <summary>Builds a system over one model, with a source holding emitter 1 and effector 2.</summary>
        /// <param name="model">The model.</param>
        /// <param name="maximumParticles">The cap.</param>
        /// <param name="seed">The random seed.</param>
        /// <returns>The system, already attached.</returns>
        private static ParticleSystem Build(ModelDefinition model, int maximumParticles = 256,
            int seed = 0x5EED)
        {
            var source = new InMemoryParticleDataSource()
                .AddEmitter(1, SteadyEmitter())
                .AddEffector(2, new ParticleEffectorDefinition
                {
                    Id = 2,
                    DirectionX = 3,
                    DirectionY = 4,
                    FalloffMode = 1,
                    Strength = 1
                });

            var system = new ParticleSystem(source, maximumParticles, seed);
            system.SetModels(new[] { model });
            return system;
        }

        /// <summary>
        ///     An emitter that spawns exactly one particle a millisecond and never moves one.
        /// </summary>
        /// <remarks>
        ///     Speed zero and a single-valued lifetime, so a spawn count is a statement about the
        ///     spawn arithmetic alone and a position is a statement about the attachment alone.
        /// </remarks>
        /// <returns>The definition.</returns>
        private static ParticleEmitterDefinition SteadyEmitter() => new ParticleEmitterDefinition
        {
            Id = 1,
            SpawnRateMin = 64,
            SpawnRateMax = 64,
            LifetimeMin = 100,
            LifetimeMax = 100,
            SizeMinStored = 4,
            SizeMaxStored = 4,
            SpeedMin = 0,
            SpeedMax = 0,
            SpawnColourStart = unchecked((int)0xFF804020),
            SpawnColourEnd = unchecked((int)0xFF804020)
        };

        /// <summary>Two triangles, arranged so no face centre lands on a vertex.</summary>
        /// <returns>The model.</returns>
        private static ModelDefinition TwoTriangles() => new ModelDefinition
        {
            VertX = new[] { 0, 300, 0, 600, 900, 600 },
            VertY = new[] { 0, 300, 600, 0, 300, 600 },
            VertZ = new[] { 0, 0, 0, 0, 0, 0 },
            faceIndices1 = new[] { 0, 3 },
            faceIndices2 = new[] { 1, 4 },
            faceIndices3 = new[] { 2, 5 }
        };

        /// <summary>The same model with the steady emitter on face 1.</summary>
        /// <returns>The model.</returns>
        private static ModelDefinition SteadyEmitterModel()
        {
            ModelDefinition model = TwoTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(1, 1) };
            return model;
        }

        /// <summary>
        ///     A model whose emitter face is almost a point, so a spawn position is known to a unit.
        /// </summary>
        /// <remarks>
        ///     Almost, not exactly: a face whose three corners agree is degenerate, has no normal,
        ///     and the client stops the emitter dead rather than spawning from it
        ///     (<c>Particle_Sub9.java:367-375</c>).
        /// </remarks>
        /// <returns>The model.</returns>
        private static ModelDefinition PointEmitterModel() => new ModelDefinition
        {
            VertX = new[] { 1280, 1281, 1280 },
            VertY = new[] { 0, 0, 1 },
            VertZ = new[] { 0, 0, 0 },
            faceIndices1 = new[] { 0 },
            faceIndices2 = new[] { 1 },
            faceIndices3 = new[] { 2 },
            Emitters = new[] { new ModelParticleEmitter(1, 0) }
        };
    }
}
