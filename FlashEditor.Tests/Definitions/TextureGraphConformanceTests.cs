using FlashEditor.cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Definitions
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
    /// </remarks>
    public class TextureGraphConformanceTests
    {
        /// <summary>
        ///     Bytes every graph file carries after the three output-node indices.
        /// </summary>
        /// <remarks>
        ///     The 637 client stops reading at the output indices and never looks at these, so
        ///     they are 639-era data it was never built to see - the cache being two builds
        ///     ahead of the client, in the usual way. They are uniform across all 946 graphs,
        ///     which is what makes them a trailer rather than a decode failure.
        /// </remarks>
        private const int TrailerBytes = 10;

        private sealed class GraphResult
        {
            public int ArchiveId;
            public long Consumed;
            public long Length;
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
                        Consumed = stream.Position,
                        Length = length,
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
        [RealCacheFact]
        public void EveryTextureGraph_ConsumesItsFileExactlyBarTheTrailer()
        {
            List<GraphResult> results = SweepGraphs();
            Assert.NotEmpty(results);

            var wrong = results
                .Where(r => r.Length - r.Consumed != TrailerBytes)
                .Take(10)
                .Select(r => $"archive {r.ArchiveId}: consumed {r.Consumed} of {r.Length}")
                .ToList();

            Assert.True(wrong.Count == 0,
                $"{results.Count(r => r.Length - r.Consumed != TrailerBytes)} of {results.Count} " +
                $"texture graphs did not leave exactly {TrailerBytes} trailing bytes. " +
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
        ///     The materials index declares 1,408 textures but only 946 have a graph, so 462 of
        ///     them can only ever be shown as their declared colour. They used to come out as a
        ///     numbered grey placeholder.
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

            //The ones with no graph must be exactly their declared colour, flat.
            foreach (TextureDefinition def in defs.Where(d => d.graph == null).Take(50))
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

                for (int i = 0; i < results.Length; i++)
                    Assert.True(expected.SequenceEqual(results[i]),
                        $"Texture {def.id} rendered differently on thread {i} than it did serially.");
            }
        }
    }
}
