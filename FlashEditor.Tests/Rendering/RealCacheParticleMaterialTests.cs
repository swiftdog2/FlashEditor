using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Particles;
using FlashEditor.IO;
using FlashEditor.Rendering;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Rendering {
    /// <summary>
    ///     That a particle's material can be turned into pixels, which is the half of the render
    ///     path a test can reach.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing here touches GL, and that is the point.</b> The particle preview drew plain
    ///     white squares because no material texture was ever bound; the fix has two halves, a
    ///     CPU rasterise and a per-context upload, and only the first is testable. The upload and
    ///     what appears on screen need the monitor, per the viewer checklist.
    ///     <para>
    ///     Worth having anyway: if emitters named no materials, or if those materials would not
    ///     rasterise, then binding a resolver would change nothing and the squares would stay. That
    ///     is exactly the failure this pins, and it is invisible from a screenshot because a
    ///     white square is also what a correctly-bound-but-unrasterised material looks like.
    ///     </para>
    /// </remarks>
    public sealed class RealCacheParticleMaterialTests : IClassFixture<RealCacheFixture> {
        private readonly RealCacheFixture fixture;
        private readonly ITestOutputHelper output;

        /// <summary>Binds the shared open cache and the output sink.</summary>
        public RealCacheParticleMaterialTests(RealCacheFixture fixture, ITestOutputHelper output) {
            this.fixture = fixture;
            this.output = output;
        }

        /// <summary>
        ///     Emitters name materials, and those materials rasterise to pixels.
        /// </summary>
        /// <remarks>
        ///     Asserted as a relationship rather than a count, because the number of index-27
        ///     emitters differs between the two supported caches and a written figure would belong
        ///     to whichever produced it. What holds for both is that a material a particle names is
        ///     one the texture cache can warm.
        /// </remarks>
        [RealCacheFact]
        public void AnEmittersMaterialRasterisesToPixels() {
            RSCache cache = fixture.OpenCache();
            var textures = new GLTextureCache(cache);

            int emitters = 0;
            int named = 0;
            int warmed = 0;
            var refused = new List<int>();

            foreach (int group in cache.EnumerateGroups(RSConstants.CONFIG_PARTICLES)) {
                IReadOnlyDictionary<int, JagStream> files;
                try {
                    files = cache.ReadGroup(RSConstants.CONFIG_PARTICLES, group);
                }
                catch {
                    continue;
                }

                foreach (KeyValuePair<int, JagStream> file in files) {
                    if (file.Value == null)
                        continue;

                    ParticleEmitterDefinition emitter;
                    try {
                        emitter = new ParticleEmitterDefinition().Decode(file.Value);
                    }
                    catch {
                        continue;
                    }

                    emitters++;

                    int material = emitter.MaterialId;
                    if (material == ParticleEmitterDefinition.NoMaterial)
                        continue;

                    named++;

                    //No GL here. PrewarmParticleMaterial evaluates the index-9 graph into an int[]
                    //and stores it; the upload is a separate step on the paint path.
                    if (textures.PrewarmParticleMaterial(material))
                        warmed++;
                    else if (refused.Count < 12)
                        refused.Add(material);
                }
            }

            output.WriteLine(emitters + " emitters, " + named + " naming a material, " +
                warmed + " of those rasterised");

            Assert.True(emitters > 0, "No index-27 emitter decoded at all.");
            Assert.True(named > 0,
                "No emitter named a material, so binding a texture resolver would change nothing " +
                "and every particle would stay a flat white square.");
            Assert.True(warmed > 0,
                "No named material rasterised, so the resolver would hand back 0 for all of them " +
                "and the renderer would fall back to flat white. Refused: " +
                string.Join(", ", refused));
        }
    }
}
