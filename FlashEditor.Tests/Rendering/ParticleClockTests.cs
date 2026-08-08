using System;
using FlashEditor.cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Particles;
using FlashEditor.Rendering;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins what unit the particle simulation's step is in, by what a particle looks like on the
    ///     frame it is born.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing in the suite can see the GL surface</b>, so a step in the wrong unit is invisible
    ///     to every other test here: the arithmetic in <see cref="ParticleSimulationTests"/> is correct
    ///     per step whatever a step turns out to be, and the cache-backed sweep only asks that
    ///     <i>something</i> is alive. What that leaves undefended is the one number neither states -
    ///     how much simulated time one redraw is worth - and getting it wrong scales every rate and
    ///     every lifetime at once.
    ///     <para>
    ///     The claim asserted here is therefore geometric and per particle rather than aggregate: on
    ///     the frame a particle is first drawn it must still be near its birth colour and size, and
    ///     its quad must still cover the face that emitted it. Those hold under the client's clock and
    ///     fail under any step short enough to age a particle through most of its life before the
    ///     first redraw. <b>An aggregate would not have caught this</b> - the effect kept the right
    ///     particle count, the right spawn rate and the right emitter, and only the clock was wrong.
    ///     </para>
    ///     <para>
    ///     The emitter modelled is the Dungeoneering master cape's smoke, index 27 emitter 157, whose
    ///     defect was reported from the viewport by eye.
    ///     <see cref="TheModelledSmokeEmitterStillMatchesTheCacheRecord"/> is what keeps the
    ///     transcription below honest, so these stay runnable with no cache present without becoming
    ///     a test of an emitter that no longer exists.
    ///     </para>
    /// </remarks>
    public class ParticleClockTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The cape's smoke emitter, index 27 group 0 file 157.</summary>
        private const int SmokeEmitterId = 157;

        /// <summary>One of the two cape models carrying <see cref="SmokeEmitterId"/>.</summary>
        /// <remarks>
        ///     59885 and 59887 hold the same five attachments - 157 on face 715, 158 on 716 and 718,
        ///     159 on 717 and 719 - so either settles the question and the pair is not a variable.
        /// </remarks>
        private const int CapeModelId = 59885;

        /// <summary>The face <see cref="SmokeEmitterId"/> rides on <see cref="CapeModelId"/>.</summary>
        private const int CapeSmokeFace = 715;

        /// <summary>
        ///     How much of its life a particle may burn before it is first drawn.
        /// </summary>
        /// <remarks>
        ///     A twentieth. Not a tolerance on an arithmetic result - it is the statement that a
        ///     redraw is short next to a particle's life, which is what makes a trail continuous
        ///     rather than a row of separated puffs. At the client's clock the youngest drawn particle
        ///     is one or two steps old out of fifty to sixty.
        /// </remarks>
        private const double MaximumAgeShareAtFirstDraw = 0.05;

        /// <summary>The shared open cache, for the two assertions taken against the real cape.</summary>
        private readonly RealCacheFixture fixture;

        /// <summary>Takes the shared cache fixture.</summary>
        /// <param name="fixture">The fixture, which skips the cache-backed facts when none is present.</param>
        public ParticleClockTests(RealCacheFixture fixture)
        {
            this.fixture = fixture;
        }

        /// <summary>
        ///     The simulation, run at the viewport's own redraw rate, with the youngest particle
        ///     ever drawn described.
        /// </summary>
        /// <remarks>
        ///     A struct rather than four out parameters because every assertion here wants the same
        ///     sample, and taking it once is what makes the failure messages describe one particle
        ///     instead of four unrelated ones.
        /// </remarks>
        private readonly struct YoungestDraw
        {
            /// <summary>Steps of life already spent when it was first drawn.</summary>
            public int Age { get; init; }

            /// <summary>The lifetime it was born with, in the same unit.</summary>
            public int MaxLife { get; init; }

            /// <summary>Its alpha channel when drawn, 0 to 255.</summary>
            public int Alpha { get; init; }

            /// <summary>Its quad's half extent when drawn, in model units.</summary>
            public int HalfExtent { get; init; }

            /// <summary>How far its centre was from the emitter face's centre, in model units.</summary>
            public double DistanceFromFace { get; init; }

            /// <summary>One line naming every measurement, for a failure message.</summary>
            public override string ToString()
            {
                return "age " + Age + "/" + MaxLife + " steps, alpha " + Alpha + "/255, half extent "
                    + HalfExtent + " model units, " + DistanceFromFace.ToString("F1")
                    + " model units from the emitter face";
            }
        }

        /// <summary>
        ///     A particle is drawn while it is still young.
        /// </summary>
        /// <remarks>
        ///     The root claim the other three are consequences of. One redraw at
        ///     <see cref="AnimationPlayer.RenderFramesPerSecond"/> is 33.3 ms, which is between one
        ///     and two of the client's 20 ms cycles - so a particle whose lifetime is fifty to sixty
        ///     cycles is one or two steps old the first time anything can see it. Reading the step as
        ///     a millisecond instead makes that same redraw 33 steps and the particle is already most
        ///     of the way through its life before it is drawn once.
        /// </remarks>
        [Fact]
        public void AParticleIsDrawnWhileItIsStillYoung()
        {
            YoungestDraw sample = RunAtTheViewportsFrameRate();

            Assert.True(sample.Age <= sample.MaxLife * MaximumAgeShareAtFirstDraw,
                "The youngest particle ever drawn was " + sample
                + ". A redraw must be short next to a particle's life, or the trail is drawn as "
                + "separated puffs of whatever the particle had already decayed to.");
        }

        /// <summary>
        ///     A particle is drawn at close to the alpha it was born with.
        /// </summary>
        /// <remarks>
        ///     Emitter 157 spawns between alpha 150 and 199 and fades at -742 per step in 1/256ths,
        ///     which is 2.9 alpha a step. One cycle of that is invisible; thirty-three of it takes the
        ///     smoke to roughly 79 and is the faintness that was reported.
        /// </remarks>
        [Fact]
        public void AParticleIsDrawnAtCloseToItsBirthAlpha()
        {
            var runtime = new ParticleEmitterRuntime(SmokeEmitter());
            YoungestDraw sample = RunAtTheViewportsFrameRate();

            //The dimmest a particle of this emitter can be born, less five percent.
            int floor = (int)(runtime.AlphaBase * 0.95);

            Assert.True(sample.Alpha >= floor,
                "The youngest particle ever drawn was " + sample + ", and this emitter spawns between "
                + runtime.AlphaBase + " and " + (runtime.AlphaBase + runtime.AlphaSpan)
                + ". Anything below " + floor + " means the fade ran before the particle was drawn.");
        }

        /// <summary>
        ///     A particle is drawn at close to the size it was born with.
        /// </summary>
        /// <remarks>
        ///     The same failure on the other ramp. Emitter 157 spawns at a half extent of 32 to 35
        ///     model units and shrinks by half a unit a step, so a step is imperceptible and
        ///     thirty-three of them halve the quad - which is why the smoke read as sparse as well as
        ///     faint.
        /// </remarks>
        [Fact]
        public void AParticleIsDrawnAtCloseToItsBirthSize()
        {
            var runtime = new ParticleEmitterRuntime(SmokeEmitter());
            YoungestDraw sample = RunAtTheViewportsFrameRate();

            int birthFloor = runtime.SizeMin >> ParticleUnits.SizeFractionBits;
            int floor = (int)(birthFloor * 0.95);

            Assert.True(sample.HalfExtent >= floor,
                "The youngest particle ever drawn was " + sample + ", and this emitter spawns between "
                + birthFloor + " and " + (runtime.SizeMax >> ParticleUnits.SizeFractionBits)
                + " model units. Anything below " + floor
                + " means the size ramp ran before the particle was drawn.");
        }

        /// <summary>
        ///     A particle's first quad still covers the face that emitted it.
        /// </summary>
        /// <remarks>
        ///     Compared against the particle's own half extent rather than an invented distance, which
        ///     is what makes this a statement about the picture: a trail whose first quad overlaps its
        ///     emitter face is rooted at the hem, and one whose first quad has already cleared the
        ///     face by three times its own width is the detached smudge that was reported. Both terms
        ///     move the wrong way at once under a short step, which is why the gap is wide.
        /// </remarks>
        [Fact]
        public void AParticlesFirstQuadStillCoversTheFaceThatEmittedIt()
        {
            YoungestDraw sample = RunAtTheViewportsFrameRate();

            Assert.True(sample.DistanceFromFace <= sample.HalfExtent,
                "The youngest particle ever drawn was " + sample
                + ". Its quad has to reach back to its own emitter face, or the trail is drawn "
                + "detached from the model.");
        }

        /// <summary>
        ///     A particle survives enough redraws to be a trail rather than a flash.
        /// </summary>
        /// <remarks>
        ///     Counted by stopping the emitter and draining what is already alive, so the number is
        ///     about how long a particle lasts and not about the balance between spawning and dying.
        ///     Fifty to sixty cycles at 33.3 ms a redraw is thirty to thirty-six frames; at a step of
        ///     a millisecond it is two.
        /// </remarks>
        [Fact]
        public void AParticleIsDrawnOverManyRedrawsRatherThanOne()
        {
            ParticleSystem system = Build(SmokeModel());

            while (system.LiveParticleCount == 0)
            {
                Assert.True(system.Advance(AnimationPlayer.RenderFrameSeconds),
                    "The emitter never produced a particle at all.");
            }

            ParticleEmitterDefinition definition = system.Emitters[0].Runtime.Definition;
            definition.SpawnRateMin = definition.SpawnRateMax = 0;

            int frames = 0;

            while (system.LiveParticleCount > 0 && frames < 1000)
            {
                system.Advance(AnimationPlayer.RenderFrameSeconds);
                frames++;
            }

            //Fifty is the shortest lifetime this emitter draws, so twenty-five frames is half of the
            //worst case rather than a number picked to pass.
            Assert.True(frames >= 25,
                "The last particle alive was drawn " + frames + " times before it expired. At "
                + AnimationPlayer.RenderFramesPerSecond + " fps a lifetime of "
                + definition.LifetimeMin + " to " + definition.LifetimeMax
                + " client cycles is thirty to thirty-six redraws.");
        }

        /// <summary>
        ///     The emitter transcribed into this file is still the one the cache holds.
        /// </summary>
        /// <remarks>
        ///     Only the derived values are compared, because they are what the assertions above turn
        ///     on and because comparing every stored field would fail on any edit that cannot change
        ///     the picture. Both supported caches hold emitter 157 with these values, so nothing here
        ///     is scoped to one of them.
        /// </remarks>
        [RealCacheFact]
        public void TheModelledSmokeEmitterStillMatchesTheCacheRecord()
        {
            RSCache cache = fixture.OpenCache();
            var source = new CacheParticleDataSource(cache);

            ParticleEmitterDefinition stored = source.GetEmitter(SmokeEmitterId);
            Assert.NotNull(stored);

            var fromCache = new ParticleEmitterRuntime(stored);
            var modelled = new ParticleEmitterRuntime(SmokeEmitter());

            Assert.Equal(fromCache.AlphaBase, modelled.AlphaBase);
            Assert.Equal(fromCache.AlphaSpan, modelled.AlphaSpan);
            Assert.Equal(fromCache.AlphaRate, modelled.AlphaRate);
            Assert.Equal(fromCache.AlphaRampSteps, modelled.AlphaRampSteps);
            Assert.Equal(fromCache.SizeMin, modelled.SizeMin);
            Assert.Equal(fromCache.SizeMax, modelled.SizeMax);
            Assert.Equal(fromCache.SizeRate, modelled.SizeRate);
            Assert.Equal(fromCache.SizeRampSteps, modelled.SizeRampSteps);
            Assert.Equal(fromCache.SpeedRate, modelled.SpeedRate);
            Assert.Equal(stored.LifetimeMin, SmokeEmitter().LifetimeMin);
            Assert.Equal(stored.LifetimeMax, SmokeEmitter().LifetimeMax);
            Assert.Equal(stored.MaterialId, SmokeEmitter().MaterialId);
        }

        /// <summary>
        ///     The same claim against the real cape, which is where the defect was seen.
        /// </summary>
        /// <remarks>
        ///     The synthetic face above is a triangle chosen for arithmetic; this one is the cape's
        ///     own hem, at its own scale and orientation, so it is the only version of the assertion
        ///     that cannot be satisfied by a face built to suit it.
        /// </remarks>
        [RealCacheFact]
        public void TheCapesSmokeIsDrawnYoungAndAtItsHem()
        {
            RSCache cache = fixture.OpenCache();
            ModelDefinition cape = cache.GetModelDefinition(CapeModelId, 0);

            //Only the smoke emitter is kept. The cape carries five, and a sample taken across all of
            //them would say nothing about any one.
            cape.Emitters = new[] { new ModelParticleEmitter(SmokeEmitterId, CapeSmokeFace) };

            var system = new ParticleSystem(new CacheParticleDataSource(cache), 2047, 0x5EED);
            system.SetModels(new[] { cape });

            Assert.Equal(1, system.EmitterCount);

            YoungestDraw sample = RunAtTheViewportsFrameRate(system);

            Assert.True(sample.Age <= sample.MaxLife * MaximumAgeShareAtFirstDraw,
                "The cape's youngest drawn smoke particle was " + sample + ".");
            Assert.True(sample.DistanceFromFace <= sample.HalfExtent,
                "The cape's youngest drawn smoke particle was " + sample
                + ", so its trail does not reach back to the hem.");
        }

        /// <summary>Runs the modelled emitter for a second of redraws and describes the youngest draw.</summary>
        /// <returns>The sample.</returns>
        private static YoungestDraw RunAtTheViewportsFrameRate()
        {
            return RunAtTheViewportsFrameRate(Build(SmokeModel()));
        }

        /// <summary>
        ///     Runs a system for a second of redraws and describes the youngest particle ever drawn.
        /// </summary>
        /// <remarks>
        ///     Every live particle is examined on every frame rather than only the newest, because
        ///     which particle is youngest is not knowable in advance - a spawn rate below one particle
        ///     per step means some frames add nothing at all, and the youngest particle on those
        ///     frames is one carried over.
        /// </remarks>
        /// <param name="system">The system, already attached.</param>
        /// <returns>The sample.</returns>
        private static YoungestDraw RunAtTheViewportsFrameRate(ParticleSystem system)
        {
            ParticleEmitterInstance emitter = system.Emitters[0];
            var sample = new YoungestDraw { Age = int.MaxValue };
            bool sampled = false;

            for (int frame = 0; frame < AnimationPlayer.RenderFramesPerSecond; frame++)
            {
                system.Advance(AnimationPlayer.RenderFrameSeconds);

                for (int index = 0; index < system.LiveParticleCount; index++)
                {
                    Particle particle = system.ParticleAt(index);
                    int age = particle.MaxLife - particle.Life;

                    if (sampled && age >= sample.Age)
                    {
                        continue;
                    }

                    int x = particle.X >> ParticleUnits.PositionFractionBits;
                    int y = particle.Y >> ParticleUnits.PositionFractionBits;
                    int z = particle.Z >> ParticleUnits.PositionFractionBits;

                    sample = new YoungestDraw
                    {
                        Age = age,
                        MaxLife = particle.MaxLife,
                        Alpha = particle.Alpha,
                        HalfExtent = particle.Size >> ParticleUnits.SizeFractionBits,
                        DistanceFromFace = Distance(x - emitter.CentroidX, y - emitter.CentroidY,
                            z - emitter.CentroidZ)
                    };
                    sampled = true;
                }
            }

            Assert.True(sampled, "The emitter produced no particle in a second of redraws.");
            return sample;
        }

        /// <summary>Length of a model-space offset.</summary>
        /// <param name="x">Offset x.</param>
        /// <param name="y">Offset y.</param>
        /// <param name="z">Offset z.</param>
        /// <returns>The length, in model units.</returns>
        private static double Distance(int x, int y, int z)
        {
            return Math.Sqrt((double)x * x + (double)y * y + (double)z * z);
        }

        /// <summary>Builds a system over one model holding only the smoke emitter.</summary>
        /// <param name="model">The model.</param>
        /// <returns>The system, already attached.</returns>
        private static ParticleSystem Build(ModelDefinition model)
        {
            var source = new InMemoryParticleDataSource().AddEmitter(SmokeEmitterId, SmokeEmitter());
            var system = new ParticleSystem(source, 2047, 0x5EED);
            system.SetModels(new[] { model });
            return system;
        }

        /// <summary>
        ///     Index 27 emitter 157, the Dungeoneering master cape's smoke, field for field.
        /// </summary>
        /// <remarks>
        ///     Transcribed rather than read, so the assertions above run with no cache present.
        ///     <see cref="TheModelledSmokeEmitterStillMatchesTheCacheRecord"/> compares it against the
        ///     bytes.
        /// </remarks>
        /// <returns>The definition.</returns>
        private static ParticleEmitterDefinition SmokeEmitter() => new ParticleEmitterDefinition
        {
            Id = SmokeEmitterId,
            YawStartStored = 0,
            YawEndStored = 8,
            PitchStartStored = 511,
            PitchEndStored = 493,
            SpeedMin = 209715,
            SpeedMax = 314572,
            EndSpeed = 734003,
            SizeMinStored = 8,
            SizeMaxStored = 9,
            EndSizeStored = 1,
            SpawnColourStart = unchecked((int)0x96000000),
            SpawnColourEnd = unchecked((int)0xC8000000),
            FadeColour = 0x00111111,
            LifetimeMin = 50,
            LifetimeMax = 60,
            SpawnRateMin = 19,
            SpawnRateMax = 32,
            MaterialId = 812,
            CycleFlagStored = 1,
            CycleThreshold = 32767,
            CyclePeriod = 32767,
            CycleRepeatsStored = 1
        };

        /// <summary>
        ///     A face for the modelled emitter to ride, at the scale of the cape's own.
        /// </summary>
        /// <remarks>
        ///     Twenty model units across, which is under a third of the particle's half extent - so a
        ///     particle sitting on the face is well inside its own quad and the distance assertion is
        ///     decided by how far the particle has travelled rather than by how large the face is.
        /// </remarks>
        /// <returns>The model.</returns>
        private static ModelDefinition SmokeModel() => new ModelDefinition
        {
            VertX = new[] { 0, 20, 0 },
            VertY = new[] { 0, 0, 0 },
            VertZ = new[] { 0, 0, 20 },
            faceIndices1 = new[] { 0 },
            faceIndices2 = new[] { 1 },
            faceIndices3 = new[] { 2 },
            Emitters = new[] { new ModelParticleEmitter(SmokeEmitterId, 0) }
        };
    }
}
