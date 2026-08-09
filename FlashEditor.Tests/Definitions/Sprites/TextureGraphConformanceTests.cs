using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Exact-consumption sweep over every texture graph in the TEXTURES index.
    /// </summary>
    /// <remarks>
    ///     The same idea as the definition byte-identity sweeps: a graph is a chain of
    ///     variable-length records, so a single wrong field width or child count silently
    ///     corrupts everything after it and the only reliable detector is where the read head
    ///     ends up. Nothing else in the suite catches a desync - the evaluator will happily
    ///     render a graph parsed out of garbage, and the renderer has no test coverage at all.
    ///
    ///     Three separate decoder defects were live when these tests were written, and all
    ///     three show up here as a consumption failure.
    ///
    ///     In the "RealCache" collection because <c>TextureManager.Textures</c> is a static
    ///     dictionary and <c>Clear</c> disposes every definition in it. Two other classes call
    ///     <c>Clear</c> - <c>TextureDefinitionTests</c> and <c>RealCacheMapIconTests</c> - so left in
    ///     their own collections xunit runs them concurrently and one of them disposes this sweep's
    ///     definitions mid-render. That surfaced as 884 of 1408 textures reporting no bitmap, in a
    ///     test that passes in isolation. The collection is the serialisation, and all three classes
    ///     must name the same one for it to hold.
    /// </remarks>
    [Collection("RealCache")]
    public class TextureGraphConformanceTests
    {
        /// <summary>
        ///     Bytes every graph file carries after the three output-node indices.
        /// </summary>
        /// <remarks>
        ///     The 637 client stops reading at the output indices and never looks at these, so
        ///     they are 639-era data it was never built to see - the cache being two builds
        ///     ahead of the client, in the usual way. They are uniform across every graph in
        ///     both supported caches, which is what makes them a trailer rather than a decode
        ///     failure. Index 9 is one of the eleven the two disagree on, the repack holding 946
        ///     graphs to the vanilla capture's 915, and the trailer is the same width in both.
        /// </remarks>
        private const int TrailerBytes = 10;

        private sealed class GraphResult
        {
            public int ArchiveId;
            public long Consumed;
            public long Length;
            public int Trailer;
            public int UnhandledNodeType;
            public int UnhandledOpcode;
        }

        private static List<GraphResult> SweepGraphs()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            var results = new List<GraphResult>();
            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            RSReferenceTable table = cache.GetReferenceTable(RSConstants.TEXTURES);

            foreach (var (archiveId, entry) in table.GetArchiveEntries())
            {
                int[] fileIds = entry.GetValidFileIds();
                if (fileIds.Length == 0)
                    continue;

                RSContainer container = cache.GetContainer(RSConstants.TEXTURES, archiveId);
                if (container?.GetStream() == null)
                    continue;

                RSArchive archive = RSCache.GetArchive(container, fileIds);
                foreach (int fileId in fileIds)
                {
                    JagStream stream = archive.GetFile(fileId);
                    if (stream == null)
                        continue;

                    stream.Seek0();
                    long length = stream.Length;
                    Texture texture = Texture.Decode(stream);
                    results.Add(new GraphResult
                    {
                        ArchiveId = archiveId,
                        //The decoder now consumes the trailer as well, so the read head lands on
                        //the end of the file whatever the parse did. The claim below is about
                        //where the graph proper stopped, which is what the record states.
                        Consumed = texture.Record.BodyLength,
                        Length = length,
                        Trailer = texture.Record.Trailer.Length,
                        UnhandledNodeType = texture.UnhandledNodeType,
                        UnhandledOpcode = texture.UnhandledOpcode,
                    });
                }

                container.ReleaseData();
            }

            return results;
        }

        /// <summary>
        ///     Every graph must consume all of its file bar the fixed trailer.
        /// </summary>
        /// <remarks>
        ///     This is the measurement the <c>Texture.TrailerBytes</c> constant rests on, so it
        ///     checks both halves of it: the graph proper ends this far from the end of every file,
        ///     and the trailer the decoder captured is that many bytes wide. Asserting only the
        ///     second would pass on a decoder that read the width it was told to read whatever the
        ///     file held.
        /// </remarks>
        [RealCacheFact]
        public void EveryTextureGraph_ConsumesItsFileExactlyBarTheTrailer()
        {
            List<GraphResult> results = SweepGraphs();
            Assert.NotEmpty(results);

            var wrong = results
                .Where(r => r.Length - r.Consumed != TrailerBytes || r.Trailer != TrailerBytes)
                .Take(10)
                .Select(r => $"archive {r.ArchiveId}: consumed {r.Consumed} of {r.Length}, " +
                             $"captured a {r.Trailer}-byte trailer")
                .ToList();

            Assert.True(wrong.Count == 0,
                $"{results.Count(r => r.Length - r.Consumed != TrailerBytes || r.Trailer != TrailerBytes)} " +
                $"of {results.Count} texture graphs did not leave exactly {TrailerBytes} trailing bytes. " +
                $"First few: {string.Join(" | ", wrong)}");
        }

        /// <summary>
        ///     No graph may contain an opcode this decoder has no case for.
        /// </summary>
        /// <remarks>
        ///     An unhandled opcode reads nothing, matching the client's empty
        ///     <c>Node_Sub10.method991</c>, so it cannot desync on its own. It is still a gap in
        ///     the opcode tables, and if the client did read bytes for it the consumption test
        ///     above would be the thing that fails - so this is the earlier, more specific
        ///     signal.
        /// </remarks>
        [RealCacheFact]
        public void NoTextureGraph_ContainsAnUnhandledOpcode()
        {
            List<GraphResult> results = SweepGraphs();
            Assert.NotEmpty(results);

            var unhandled = results
                .Where(r => r.UnhandledNodeType >= 0)
                .GroupBy(r => (r.UnhandledNodeType, r.UnhandledOpcode))
                .Select(g => $"node type {g.Key.UnhandledNodeType} opcode {g.Key.UnhandledOpcode} x{g.Count()}")
                .ToList();

            Assert.True(unhandled.Count == 0,
                $"Texture graphs carry opcodes the decoder does not handle: {string.Join(", ", unhandled)}");
        }

        /// <summary>
        ///     Type 36 nodes must resolve to a texture that exists, since they render it.
        /// </summary>
        [RealCacheFact]
        public void EveryComposedTextureReference_ResolvesToALoadedTexture()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            new TextureManager(cache).Load();

            var dangling = new List<string>();
            int references = 0;
            foreach (TextureDefinition def in TextureManager.Textures.Values)
            {
                if (def.graph?.Nodes == null)
                    continue;

                foreach (TextureNode node in def.graph.Nodes)
                {
                    if (node == null || node.Type != 36 || node.NestedTextureId < 0)
                        continue;

                    references++;
                    if (!TextureManager.Textures.ContainsKey(node.NestedTextureId))
                        dangling.Add($"texture {def.id} -> {node.NestedTextureId}");
                }
            }

            Assert.True(references > 0, "No composed texture references found - the sweep read nothing.");
            Assert.True(dangling.Count == 0,
                $"{dangling.Count} composed texture references point at textures that do not exist: " +
                string.Join(", ", dangling.Take(10)));
        }

        /// <summary>
        ///     Every declared texture must produce a bitmap, whether or not it has a graph.
        /// </summary>
        /// <remarks>
        ///     The materials index declares more textures than index 9 holds graphs for, and the
        ///     remainder can only ever be shown as their declared colour. They used to come out as
        ///     a numbered grey placeholder.
        ///     <para>
        ///     How many there are is a fact about one cache: the repack declares 1,408 textures
        ///     against 946 graphs, so 462 are flat, while the vanilla b639 capture declares 915
        ///     against 915 and has <b>none</b>. That is why the flat-colour check is preceded by a
        ///     count rather than left to iterate however many it finds - a loop over an empty set
        ///     passes silently, and on the vanilla capture that is exactly what it does. What is
        ///     asserted instead is the join: every graph index 9 declares is attached to a
        ///     texture, and the flat ones are precisely the declared textures left over.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryTexture_ProducesAThumbnail()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            new TextureManager(cache).Load();

            List<TextureDefinition> defs = TextureManager.Textures.Values.ToList();
            Assert.NotEmpty(defs);
            System.Threading.Tasks.Parallel.ForEach(defs, d => TextureManager.EnsureRendered(d));

            List<int> blank = defs.Where(d => d.thumb == null).Select(d => d.id).ToList();
            Assert.True(blank.Count == 0,
                $"{blank.Count} textures produced no bitmap: {string.Join(", ", blank.Take(20))}");

            //Every graph the index declares reached a texture. Without this the flat set below is
            //just "whatever did not load", and a manager that dropped half the graphs would look
            //like a cache with more flat colours in it.
            List<TextureDefinition> flat = defs.Where(d => d.graph == null).ToList();
            int declaredGraphs = cache.GetReferenceTable(RSConstants.TEXTURES).GetArchiveCount();
            Assert.Equal(declaredGraphs, defs.Count - flat.Count);

            //The ones with no graph must be exactly their declared colour, flat.
            foreach (TextureDefinition def in flat)
            {
                int expected = TextureManager.RepresentativeRgb(def) & 0xFFFFFF;
                Color actual = def.thumb.GetPixel(0, 0);
                Assert.Equal(expected, (actual.R << 16) | (actual.G << 8) | actual.B);
            }
        }

        /// <summary>
        ///     A texture's declared colour must agree with what its graph actually renders.
        /// </summary>
        /// <remarks>
        ///     This is what establishes that <see cref="TextureDefinition.field1831"/> is the
        ///     texture's colour rather than the timing value the field tables call it, and it is
        ///     the only check in the suite that scores the evaluator's output against something
        ///     independent - every other test compares the decoder to itself. The control makes
        ///     it meaningful: measured against a random other texture's colour the error is
        ///     roughly three times larger, so the threshold cannot be met by accident.
        /// </remarks>
        [RealCacheFact]
        public void DeclaredTextureColour_MatchesWhatTheGraphRenders()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            new TextureManager(cache).Load();

            var errors = new List<double>();
            var control = new List<double>();
            List<TextureDefinition> graphed = TextureManager.Textures.Values
                .Where(d => d.graph != null).ToList();
            Assert.NotEmpty(graphed);

            //Fixed seed: the control has to be reproducible or the threshold below drifts.
            var random = new Random(1);

            foreach (TextureDefinition def in graphed)
            {
                int[] pixels = TextureGraphEvaluator.RenderArgb(def.graph, 32, 32, cache, def.field1824, def.id);
                if (pixels == null)
                    continue;

                long r = 0, g = 0, b = 0;
                foreach (int p in pixels)
                {
                    r += (p >> 16) & 0xFF;
                    g += (p >> 8) & 0xFF;
                    b += p & 0xFF;
                }
                int mr = (int)(r / pixels.Length), mg = (int)(g / pixels.Length), mb = (int)(b / pixels.Length);

                errors.Add(ChannelError(mr, mg, mb, TextureManager.RepresentativeRgb(def)));
                control.Add(ChannelError(mr, mg, mb,
                    TextureManager.RepresentativeRgb(graphed[random.Next(graphed.Count)])));
            }

            errors.Sort();
            control.Sort();
            double median = errors[errors.Count / 2];
            double controlMedian = control[control.Count / 2];

            Assert.True(median < 30,
                $"Median channel error between a texture's declared colour and its render is {median:F1}, " +
                $"which means either the colour field or the evaluator is wrong (control {controlMedian:F1}).");
            Assert.True(median < controlMedian / 2,
                $"Declared colour ({median:F1}) is not clearly better than an unrelated texture's " +
                $"colour ({controlMedian:F1}), so the agreement is not meaningful.");
        }

        /// <summary>
        ///     The one type 25 node in the cache must tint texture 911, not pass it through.
        /// </summary>
        /// <remarks>
        ///     Type 25 is <c>Node_Sub10_Sub14</c>, a colour-key scale: where a pixel matches the
        ///     key colour within <c>anInt5604</c> on all three channels, <c>method997</c> scales
        ///     R, G and B by <c>anInt5605</c>, <c>anInt5607</c> and <c>anInt5611</c>. The
        ///     evaluator had no colour arm for it and fell through to the pass-through default,
        ///     so the scale never happened.
        ///
        ///     Exactly one type 25 node exists in this cache, in texture 911, and its child is a
        ///     monochrome chain - so with the node inert the texture renders grey. The anchor is
        ///     the cache's own declared colour for the texture, which the evaluator plays no part
        ///     in producing.
        ///
        ///     <para>
        ///     Both assertions are on the <em>ratios</em> between the channels rather than on their
        ///     absolute values, and that is a deliberate re-anchoring. This test used to require the
        ///     render's mean to sit within 8 of the declared colour, which held while a monochrome
        ///     type 7 blend evaluated to a flat mid-grey and stopped holding the moment that arm was
        ///     implemented: 911 carries two of them, in modes 5 and 7, both brighteners, and its
        ///     mean moved from (78, 64, 46) to (97, 83, 61) against a declared (72, 63, 48). The
        ///     brightness anchor was therefore calibrated against a defect.
        ///     </para>
        ///     <para>
        ///     Dropping it costs nothing this test exists to buy, because absolute brightness is not
        ///     what type 25 does. It scales the three channels <em>apart</em>, so what proves it ran
        ///     is the shape of the result: normalised against its own strongest channel the render
        ///     is (1.000, 0.856, 0.629) against the declared (1.000, 0.875, 0.667), a worst-channel
        ///     deviation of 0.038 - and under the old flat-blend render it was 0.077, so the
        ///     assertion is sharper now as well as still true. A pass-through renders grey, whose
        ///     ratios are (1, 1, 1) and whose deviation is 0.333, so the defect this was written for
        ///     still fails it by a factor of nine.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void ColourKeyNode_TintsTexture911TowardItsDeclaredColour()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            new TextureManager(cache).Load();

            //If a later cache or decoder change moves the type 25 population, the anchor below
            //stops meaning what this test says it means - so assert the census, not just 911.
            List<int> hosts = TextureManager.Textures.Values
                .Where(d => d.graph?.Nodes != null && d.graph.Nodes.Any(n => n != null && n.Type == 25))
                .Select(d => d.id)
                .OrderBy(id => id)
                .ToList();
            Assert.Equal(new[] { 911 }, hosts);

            TextureDefinition def = TextureManager.Textures[911];
            TextureNode keyNode = def.graph.Nodes.Single(n => n != null && n.Type == 25);

            //Tolerance, then the blue, green and red scales - the order method991 assigns them.
            Assert.Equal(4096, keyNode.IntParam0);
            Assert.Equal(409, keyNode.IntParam1);
            Assert.Equal(1638, keyNode.IntParam2);
            Assert.Equal(2867, keyNode.IntParam3);

            int[] pixels = TextureGraphEvaluator.RenderArgb(def.graph, 64, 64, cache, def.field1824, def.id);
            Assert.NotNull(pixels);

            long r = 0, g = 0, b = 0;
            foreach (int p in pixels)
            {
                r += (p >> 16) & 0xFF;
                g += (p >> 8) & 0xFF;
                b += p & 0xFF;
            }
            int mr = (int)(r / pixels.Length), mg = (int)(g / pixels.Length), mb = (int)(b / pixels.Length);

            int declared = TextureManager.RepresentativeRgb(def);
            double deviation = ChannelRatioDeviation(mr, mg, mb, declared);
            Assert.True(deviation < 0.10,
                $"Texture 911 renders ({mr}, {mg}, {mb}), whose channel ratios deviate by " +
                $"{deviation:F3} from its declared colour's. The type 25 colour-key scale is not " +
                "scaling the channels into the declared proportions.");

            int chroma = Math.Max(mr, Math.Max(mg, mb)) - Math.Min(mr, Math.Min(mg, mb));
            Assert.True(chroma >= 20,
                $"Texture 911 renders ({mr}, {mg}, {mb}), a chroma of {chroma}. Its colour output " +
                "is a monochrome chain, so the only thing that can give it a colour is the type " +
                "25 node scaling the channels apart.");
        }

        /// <summary>
        ///     How far a render's channel proportions sit from a declared colour's.
        /// </summary>
        /// <remarks>
        ///     Each triple is normalised against its own strongest channel before they are compared,
        ///     so this measures hue and saturation and ignores brightness entirely. That is the
        ///     right shape for anything asking whether a node scaled the channels apart, and the
        ///     wrong one for anything asking how bright a texture is - <see cref="ChannelError"/>
        ///     is still the measure for that, and the cache-wide sweep above uses it.
        /// </remarks>
        /// <param name="r">Rendered mean red.</param>
        /// <param name="g">Rendered mean green.</param>
        /// <param name="b">Rendered mean blue.</param>
        /// <param name="rgb">The declared colour, packed.</param>
        /// <returns>The largest per-channel difference between the two normalised triples.</returns>
        private static double ChannelRatioDeviation(int r, int g, int b, int rgb)
        {
            int dr = (rgb >> 16) & 0xFF, dg = (rgb >> 8) & 0xFF, db = rgb & 0xFF;

            //A black render or a black declared colour has no proportions at all, so it cannot
            //agree with anything. Reported as total disagreement rather than as a division by zero.
            double renderPeak = Math.Max(r, Math.Max(g, b));
            double declaredPeak = Math.Max(dr, Math.Max(dg, db));
            if (renderPeak <= 0 || declaredPeak <= 0)
                return 1.0;

            return Math.Max(Math.Abs(r / renderPeak - dr / declaredPeak),
                Math.Max(Math.Abs(g / renderPeak - dg / declaredPeak),
                    Math.Abs(b / renderPeak - db / declaredPeak)));
        }

        private static double ChannelError(int r, int g, int b, int rgb) =>
            (Math.Abs(r - ((rgb >> 16) & 0xFF)) +
             Math.Abs(g - ((rgb >> 8) & 0xFF)) +
             Math.Abs(b - (rgb & 0xFF))) / 3.0;

        /// <summary>
        ///     Rendering the same composed texture on many threads must give one answer.
        /// </summary>
        /// <remarks>
        ///     Evaluation writes row caches into the graph's nodes, so a graph reached through a
        ///     type 36 reference is shared mutable state the moment two textures compose the
        ///     same third one - and the editor renders on 20 threads. The failure is a torn
        ///     scanline rather than an exception, so it would not surface as a crash.
        /// </remarks>
        [RealCacheFact]
        public void ComposedTextures_RenderIdenticallyUnderConcurrency()
        {
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;

            using var store = new RSFileStore(RealCacheLocator.Directory);
            var cache = new RSCache(store);
            new TextureManager(cache).Load();

            //Pick the textures that compose something, so the shared path is the one exercised.
            List<TextureDefinition> composing = TextureManager.Textures.Values
                .Where(d => d.graph?.Nodes != null &&
                            d.graph.Nodes.Any(n => n != null && n.Type == 36 && n.NestedTextureId >= 0))
                .Take(8)
                .ToList();
            Assert.NotEmpty(composing);

            foreach (TextureDefinition def in composing)
            {
                int[] expected = TextureGraphEvaluator.RenderArgb(def.graph, 64, 64, cache, def.field1824, def.id);
                if (expected == null)
                    continue;

                var results = new int[16][];
                System.Threading.Tasks.Parallel.For(0, results.Length, i =>
                {
                    results[i] = TextureGraphEvaluator.RenderArgb(def.graph, 64, 64, cache, def.field1824, def.id);
                });

                //Null rather than different is the shape a render that ran out of its time budget
                //takes, and SequenceEqual throws on it rather than reporting it, so it is checked
                //here instead of surfacing as an ArgumentNullException from inside LINQ.
                for (int i = 0; i < results.Length; i++)
                    Assert.True(results[i] != null && expected.SequenceEqual(results[i]),
                        $"Texture {def.id} rendered differently on thread {i} than it did serially.");
            }
        }
    }
}
