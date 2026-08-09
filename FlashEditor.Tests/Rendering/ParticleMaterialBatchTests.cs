using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Particles;
using FlashEditor.Rendering;
using Xunit;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins the material batching that decides which texture each particle is drawn with.
    /// </summary>
    /// <remarks>
    ///     The overlay pass binds one texture per batch, so a batch boundary in the wrong place
    ///     silently draws one emitter's particles with another emitter's material. That is invisible
    ///     with a single emitter attached, which is the common case and the one the smoke on the
    ///     Dungeoneering cape happens to be, so it needs a test with two.
    ///     <para>
    ///     No GL here. What is checked is the run list <see cref="ParticleBillboards.Build"/>
    ///     produces: that it covers every quad exactly once, in order, and names the right material
    ///     for each.
    ///     </para>
    /// </remarks>
    public class ParticleMaterialBatchTests
    {
        /// <summary>
        ///     Every quad belongs to exactly one run, and the runs name the emitters' materials.
        /// </summary>
        /// <remarks>
        ///     Two emitters on two faces, with different materials, so the run list has to have more
        ///     than one entry and the boundary has to fall where the material changes. The
        ///     partition check is the sharp one: runs that overlap or leave a gap would still draw
        ///     something, and a caller reading only the count would not notice.
        /// </remarks>
        [Fact]
        public void EveryQuadFallsInExactlyOneMaterialRun()
        {
            const int FirstMaterial = 812;
            const int SecondMaterial = 47;

            ParticleSystem system = SystemWithTwoEmitters(FirstMaterial, SecondMaterial);

            Assert.True(system.Advance(ParticleUnits.SecondsPerCycle * 20));
            Assert.True(system.LiveParticleCount > 1,
                "the two emitters produced " + system.LiveParticleCount + " particles, so nothing is batched");

            float[] buffer = new float[system.LiveParticleCount * ParticleBillboards.FloatsPerParticle];
            List<ParticleMaterialRun> runs = new List<ParticleMaterialRun>();

            int quads = ParticleBillboards.Build(system, Vector3.UnitX, Vector3.UnitY, Vector3.UnitY, buffer, runs);

            Assert.Equal(system.LiveParticleCount, quads);
            Assert.NotEmpty(runs);

            //A partition: the runs must tile [0, quads) with no overlap and no hole, in order.
            int next = 0;
            foreach (ParticleMaterialRun run in runs)
            {
                Assert.Equal(next, run.FirstQuad);
                Assert.True(run.QuadCount > 0, "run at quad " + run.FirstQuad + " is empty");
                next += run.QuadCount;
            }

            Assert.Equal(quads, next);

            //Every quad in a run must carry that run's material, which is what makes binding one
            //texture for the whole run correct.
            foreach (ParticleMaterialRun run in runs)
                for (int quad = run.FirstQuad; quad < run.FirstQuad + run.QuadCount; quad++)
                    Assert.Equal(run.MaterialId, system.ParticleAt(quad).MaterialId);

            Assert.Equal(new[] { FirstMaterial, SecondMaterial },
                runs.Select(run => run.MaterialId).Distinct().OrderByDescending(id => id).ToArray());
        }

        /// <summary>One emitter collapses to a single run, so the common case costs one bind.</summary>
        [Fact]
        public void OneMaterialProducesOneRun()
        {
            ParticleSystem system = SystemWithTwoEmitters(812, 812);

            Assert.True(system.Advance(ParticleUnits.SecondsPerCycle * 20));

            float[] buffer = new float[system.LiveParticleCount * ParticleBillboards.FloatsPerParticle];
            List<ParticleMaterialRun> runs = new List<ParticleMaterialRun>();

            int quads = ParticleBillboards.Build(system, Vector3.UnitX, Vector3.UnitY, Vector3.UnitY, buffer, runs);

            ParticleMaterialRun only = Assert.Single(runs);
            Assert.Equal(812, only.MaterialId);
            Assert.Equal(0, only.FirstQuad);
            Assert.Equal(quads, only.QuadCount);
        }

        /// <summary>A system with nothing alive produces no runs rather than one empty one.</summary>
        /// <remarks>
        ///     A zero-length run would issue a draw call for no indices, which is harmless, and would
        ///     also make the partition check above pass vacuously on a broken build. Pinned so it
        ///     cannot.
        /// </remarks>
        [Fact]
        public void NoLiveParticlesProducesNoRuns()
        {
            ParticleSystem system = SystemWithTwoEmitters(812, 47);
            List<ParticleMaterialRun> runs = new List<ParticleMaterialRun> { new ParticleMaterialRun(1, 2, 3) };

            int quads = ParticleBillboards.Build(system, Vector3.UnitX, Vector3.UnitY, Vector3.UnitY,
                new float[0], runs);

            Assert.Equal(0, quads);
            Assert.Empty(runs);
        }

        /// <summary>
        ///     The materials to prewarm are readable from the emitters before anything has spawned.
        /// </summary>
        /// <remarks>
        ///     This is what keeps the texture-graph evaluation off the paint path: the renderer knows
        ///     which materials it will need the moment the models are set, rather than on the frame
        ///     the first particle appears.
        /// </remarks>
        [Fact]
        public void AttachedMaterialsAreKnownBeforeAnyParticleSpawns()
        {
            ParticleSystem system = SystemWithTwoEmitters(812, 47);

            Assert.Equal(0, system.LiveParticleCount);
            Assert.Equal(new[] { 47, 812 }, system.AttachedMaterialIds().OrderBy(id => id).ToArray());
        }

        /// <summary>An emitter with no opcode 15 contributes no material to prewarm.</summary>
        [Fact]
        public void AnEmitterWithNoMaterialIsNotPrewarmed()
        {
            ParticleSystem system = SystemWithTwoEmitters(ParticleEmitterDefinition.NoMaterial, 47);

            Assert.Equal(new[] { 47 }, system.AttachedMaterialIds().ToArray());
        }

        /// <summary>
        ///     A model with two emitters on two faces, each naming its own material.
        /// </summary>
        /// <param name="firstMaterial">Material for the emitter on face 0.</param>
        /// <param name="secondMaterial">Material for the emitter on face 1.</param>
        /// <returns>The system, with the model already set and nothing advanced.</returns>
        private static ParticleSystem SystemWithTwoEmitters(int firstMaterial, int secondMaterial)
        {
            var source = new InMemoryParticleDataSource()
                .AddEmitter(1, Emitter(firstMaterial))
                .AddEmitter(2, Emitter(secondMaterial));

            var system = new ParticleSystem(source, 2047, 0x5EED);
            system.SetModels(new[] { TwoFacedModel() });
            return system;
        }

        /// <summary>An emitter that spawns steadily and lives long enough to accumulate.</summary>
        /// <param name="materialId">The material it puts on its particles.</param>
        /// <returns>The definition.</returns>
        private static ParticleEmitterDefinition Emitter(int materialId) => new ParticleEmitterDefinition
        {
            SpeedMin = 209715,
            SpeedMax = 314572,
            SizeMinStored = 8,
            SizeMaxStored = 9,
            EndSizeStored = 1,
            SpawnColourStart = unchecked((int)0x96000000),
            SpawnColourEnd = unchecked((int)0xC8000000),
            LifetimeMin = 200,
            LifetimeMax = 200,
            SpawnRateMin = ParticleUnits.SpawnAccumulatorPerParticle,
            SpawnRateMax = ParticleUnits.SpawnAccumulatorPerParticle,
            MaterialId = materialId,
            CycleThreshold = 32767,
            CyclePeriod = 32767
        };

        /// <summary>Two triangles, so two emitters can attach to two different faces.</summary>
        /// <returns>The model.</returns>
        private static ModelDefinition TwoFacedModel() => new ModelDefinition
        {
            VertX = new[] { 0, 20, 0, 40, 60, 40 },
            VertY = new[] { 0, 0, 0, 0, 0, 0 },
            VertZ = new[] { 0, 0, 20, 0, 0, 20 },
            faceIndices1 = new[] { 0, 3 },
            faceIndices2 = new[] { 1, 4 },
            faceIndices3 = new[] { 2, 5 },
            Emitters = new[]
            {
                new ModelParticleEmitter(1, 0),
                new ModelParticleEmitter(2, 1)
            }
        };
    }
}
