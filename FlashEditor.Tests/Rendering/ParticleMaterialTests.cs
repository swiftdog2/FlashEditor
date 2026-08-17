using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Particles;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins the pixels a particle's material resolves to, which is what decides whether the
    ///     effect reads as smoke or as a box.
    /// </summary>
    /// <remarks>
    ///     A particle quad is a flat camera-facing rectangle, so every soft edge it has comes from
    ///     its material's alpha channel. Nothing in the suite can see the GL surface, and the
    ///     failure this guards against - a hard-edged opaque square where a soft orb belongs - is
    ///     entirely a property of the sampled texture rather than of the draw call. So it is
    ///     checkable here, before any of it reaches OpenGL: a texture whose alpha is uniform cannot
    ///     produce a soft orb whatever the renderer does with it.
    ///     <para>
    ///     In the "RealCache" collection because <c>TextureManager.Textures</c> is static and
    ///     <c>Clear</c> disposes every definition in it, so classes that touch it must not run
    ///     concurrently.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class ParticleMaterialTests : IClassFixture<RealCacheFixture>
    {
        /// <summary>The material the Dungeoneering master cape's emitter 157 names.</summary>
        private const int SmokeMaterialId = 812;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public ParticleMaterialTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     The smoke material's alpha falls off from an opaque centre to transparent corners.
        /// </summary>
        /// <remarks>
        ///     Three claims, and the third is the one that matters. The material resolves to a real
        ///     graph rather than to the representative-colour fallback; its colour output is a noise
        ///     field that is opaque everywhere, which is exactly the hard square the viewport was
        ///     drawing; and its <em>alpha</em> output is a radial falloff, which is the soft orb.
        ///     <para>
        ///     The two renders differ only in whether the alpha output node is sampled, so this also
        ///     pins which of the client's two entry points the particle path has to use -
        ///     <c>Node_Sub46_Sub19.method1633</c> (<c>:309-390</c>), not <c>method1631</c>
        ///     (<c>:218</c>). Getting that wrong is invisible in the colour channel and total in the
        ///     alpha channel.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void SmokeMaterial_HasARadialAlphaFalloff_OnlyThroughTheAlphaOutputNode()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            Assert.True(TextureManager.Textures.TryGetValue(SmokeMaterialId, out TextureDefinition def),
                $"material {SmokeMaterialId} is not in the texture table of the {_fixture.Profile} cache");
            Assert.NotNull(def.graph);

            const int Side = 64;

            //The colour path, which is what the material fell back to before the particle draw
            //learned to ask for alpha. Uniformly opaque, so every pixel of the quad is drawn.
            int[] colourOnly = TextureGraphEvaluator.RenderArgb(def.graph, Side, Side, cache, def.transposePixels,
                SmokeMaterialId);
            Assert.NotNull(colourOnly);
            Assert.Equal(1, DistinctAlphaCount(colourOnly));
            Assert.Equal(255, Alpha(colourOnly, Side, Side / 2, Side / 2));

            int[] withAlpha = TextureGraphEvaluator.RenderArgb(def.graph, Side, Side, cache, def.transposePixels,
                SmokeMaterialId, sampleAlphaOutput: true);
            Assert.NotNull(withAlpha);

            int distinct = DistinctAlphaCount(withAlpha);
            int centre = Alpha(withAlpha, Side, Side / 2, Side / 2);
            int corner = Alpha(withAlpha, Side, 0, 0);
            int transparent = withAlpha.Count(p => ((p >> 24) & 0xFF) == 0);

            _output.WriteLine($"{_fixture.Profile}: material {SmokeMaterialId} at {Side}x{Side} - "
                + $"{distinct} distinct alpha values, centre {centre}, corner {corner}, "
                + $"{transparent} fully transparent pixels of {withAlpha.Length}");

            //A gradient rather than a mask. Two values would be a hard-edged disc, which still
            //reads as a shape rather than as a haze.
            Assert.True(distinct > 32,
                $"material {SmokeMaterialId} has only {distinct} distinct alpha values, so it cannot fade out");
            Assert.Equal(255, centre);
            Assert.Equal(0, corner);

            //Monotonic on the way out from the centre along the centre row, allowing equal steps.
            //A falloff that rises again would be a tiled or wrapped sample rather than one orb.
            int previous = 256;
            for (int x = Side / 2; x < Side; x++)
            {
                int alpha = Alpha(withAlpha, Side, x, Side / 2);
                Assert.True(alpha <= previous,
                    $"alpha rises from {previous} to {alpha} at x={x}, so the falloff is not radial");
                previous = alpha;
            }
        }

        /// <summary>
        ///     A monochrome type 7 blend is evaluated rather than left at flat mid-grey.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub10_Sub7</c> is declared <c>super(2, false)</c> and so is a colour node
        ///     until opcode 1 flips it, and this evaluator had no monochrome arm for it at all - the
        ///     node fell to <see cref="TextureGraphEvaluator"/>'s unknown-mono default and returned
        ///     2040 for every pixel whatever its children held.
        ///     <para>
        ///     Census rather than a single case, because the defect's visibility depends entirely on
        ///     where the node sits: mid-grey in the colour channel of a noisy texture is invisible,
        ///     and mid-grey in an alpha channel is a fully opaque texture. The count is measured from
        ///     the loaded cache rather than written down, because index 9 differs between the two.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void MonochromeBlendNodes_AreEvaluated_NotLeftAtTheUnknownNodeDefault()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            var hosts = new List<int>();
            int monoBlends = 0;

            foreach (TextureDefinition def in TextureManager.Textures.Values)
            {
                if (def.graph?.Nodes == null)
                    continue;

                int here = def.graph.Nodes.Count(n => n != null && n.Type == 7 && n.MonoOverride == true);
                if (here == 0)
                    continue;

                monoBlends += here;
                hosts.Add(def.id);
            }

            _output.WriteLine($"{_fixture.Profile}: {monoBlends} monochrome type 7 blends across "
                + $"{hosts.Count} of {TextureManager.Textures.Count} textures");

            Assert.NotEmpty(hosts);
            Assert.Contains(SmokeMaterialId, hosts);

            //The proof that the arm runs: a node whose children vary must vary. The unknown-node
            //default is a constant, so a constant row on a host with varying children is the
            //defect returning. Sampled on the smoke material, whose alpha chain is two of them.
            TextureDefinition smoke = TextureManager.Textures[SmokeMaterialId];
            int savedColourOutput = smoke.graph.ColourOutputIndex;

            try
            {
                int blendIndex = Array.FindIndex(smoke.graph.Nodes,
                    n => n != null && n.Type == 7 && n.MonoOverride == true);
                smoke.graph.ColourOutputIndex = blendIndex;

                int[] pixels = TextureGraphEvaluator.RenderArgb(smoke.graph, 32, 32, cache, smoke.transposePixels,
                    SmokeMaterialId);
                Assert.NotNull(pixels);

                int lowest = pixels.Min(p => p & 0xFF);
                int highest = pixels.Max(p => p & 0xFF);
                _output.WriteLine($"  node[{blendIndex}] of material {SmokeMaterialId}: {lowest}..{highest}");

                Assert.True(highest - lowest > 8,
                    $"the monochrome blend at node {blendIndex} renders {lowest}..{highest}, which is flat - "
                    + "the mono arm is not running and the node fell to the unknown-node default");
            }
            finally
            {
                smoke.graph.ColourOutputIndex = savedColourOutput;
            }
        }

        /// <summary>
        ///     The smoke material selects the alpha-sampling rasterisation and the MODULATE combine.
        /// </summary>
        /// <remarks>
        ///     Both are per-material fields rather than properties of the particle feature, and both
        ///     were guessed at before they were read.
        ///     <para>
        ///     <b>Which rasterisation.</b> <c>Class364.method3931:121</c> takes the alpha-sampling
        ///     path when <c>anInt1818 == 2 || !Node_Sub10_Sub7.method1023(1, aByte1820)</c>, and
        ///     <c>method1023</c> (<c>:53-62</c>) is <c>i != 1 &amp;&amp; i != 7</c>. So the condition
        ///     is <c>alphaMode == 2</c> or <c>effectProgram</c> in {1, 7}. Material 812 satisfies the
        ///     first.
        ///     </para>
        ///     <para>
        ///     <b>Which combine mode.</b> Not a particle constant. <c>Class360.java:443</c> binds the
        ///     particle's material through <c>RenderType_Sub1.method1834</c> (<c>:2976</c>), which
        ///     reaches <c>method1897</c>, and there <c>:4437</c> reads <c>i_285_ =
        ///     class238.anInt1821</c> and <c>:4449</c> passes it to <c>method1896</c>. So every
        ///     textured draw takes the mode from the material it binds, and a particle is not
        ///     special. This asserts the census as well as 812's value, because the shader
        ///     implements one arm of five.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void SmokeMaterial_TakesTheAlphaRasterisationAndTheModulateCombine()
        {
            RSCache cache = _fixture.OpenCache();
            new TextureManager(cache).Load();

            TextureDefinition smoke = TextureManager.Textures[SmokeMaterialId];

            Assert.Equal(2, smoke.alphaMode);
            Assert.True(smoke.alphaMode == 2 || smoke.effectProgram == 1 || smoke.effectProgram == 7,
                $"material {SmokeMaterialId} would take the colour-derived alpha, which is uniform");

            //Mode 0 is method1896's second arm, method1899(8448, 8960, 8448), which sets
            //GL_COMBINE_RGB and GL_COMBINE_ALPHA both to GL_MODULATE - note method1899 writes its
            //third argument to GL_COMBINE_RGB (34161) and its first to GL_COMBINE_ALPHA (34162),
            //which is the reverse of the reading order. texture.frag already computes exactly that.
            Assert.Equal(0, smoke.combineMode);

            var modes = new SortedDictionary<int, int>();
            foreach (TextureDefinition def in TextureManager.Textures.Values)
                modes[def.combineMode] = modes.GetValueOrDefault(def.combineMode) + 1;

            foreach (var mode in modes)
                _output.WriteLine($"{_fixture.Profile}: combine mode {mode.Key} on {mode.Value} materials");

            //Recorded rather than required to be {0}. If a later cache does carry another mode the
            //shader has to grow an arm for it, and this is the line that says so.
            Assert.True(modes.ContainsKey(0), "no material takes the MODULATE combine");

            //The claim that matters is narrower than "every material is MODULATE", which is false.
            //It is that every material an emitter can put on a particle is, because that is the set
            //the overlay shader has to serve. Derived from index 27 rather than assumed.
            var nonModulate = new SortedSet<int>();

            foreach (TextureDefinition def in TextureManager.Textures.Values)
                if (def.combineMode != 0)
                    nonModulate.Add(def.id);

            var used = new SortedSet<int>();

            foreach (int emitterMaterial in EmitterMaterials(cache))
                if (nonModulate.Contains(emitterMaterial))
                    used.Add(emitterMaterial);

            _output.WriteLine($"  materials off MODULATE: [{string.Join(", ", nonModulate)}]; "
                + $"named by an emitter: [{string.Join(", ", used)}]");

            Assert.True(used.Count == 0,
                $"emitters name materials {string.Join(", ", used)}, which do not take the MODULATE "
                + "combine, so texture.frag draws them with the wrong texture environment");
        }

        /// <summary>Every material id declared by an index-27 emitter.</summary>
        /// <remarks>
        ///     Read through <see cref="RSCache.ReadGroup"/> rather than file by file, because
        ///     <c>ReadFile</c> re-decodes the whole group per file and the emitter family is one
        ///     group of a few hundred.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The material ids, including duplicates.</returns>
        private static IEnumerable<int> EmitterMaterials(RSCache cache)
        {
            foreach (KeyValuePair<int, JagStream> file in cache.ReadGroup(RSConstants.CONFIG_PARTICLES,
                ParticleEmitterDefinition.GroupId))
            {
                if (file.Value == null)
                    continue;

                file.Value.Seek0();
                ParticleEmitterDefinition emitter = new ParticleEmitterDefinition { Id = file.Key }.Decode(file.Value);

                if (emitter.MaterialId != ParticleEmitterDefinition.NoMaterial)
                    yield return emitter.MaterialId;
            }
        }

        /// <summary>How many distinct alpha values a rasterised texture holds.</summary>
        /// <param name="argb">Packed ARGB pixels.</param>
        /// <returns>The count.</returns>
        private static int DistinctAlphaCount(int[] argb) => argb.Select(p => (p >> 24) & 0xFF).Distinct().Count();

        /// <summary>The alpha at one pixel of a square raster.</summary>
        /// <param name="argb">Packed ARGB pixels.</param>
        /// <param name="stride">Row length.</param>
        /// <param name="x">Column.</param>
        /// <param name="y">Row.</param>
        /// <returns>Alpha, 0 to 255.</returns>
        private static int Alpha(int[] argb, int stride, int x, int y) => (argb[y * stride + x] >> 24) & 0xFF;
    }
}
