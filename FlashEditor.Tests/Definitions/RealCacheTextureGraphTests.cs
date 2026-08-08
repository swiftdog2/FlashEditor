using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every procedural texture graph in the real revision-639 cache, requires exact
    ///     buffer consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 9 was the only content index in either cache with no byte-identity sweep behind
    ///     it, because it had no encoder at all - a texture could be read and never written.
    ///     <para>
    ///     Two things decide what this sweep can claim. Index 9's compression is mixed - roughly
    ///     half of its groups are stored uncompressed and the rest are GZip - and no GZip container
    ///     re-encodes byte-identically, so the comparison has to be against the decompressed
    ///     payload. <see cref="DefinitionSweep{T}"/> already does that, which is the reason this is
    ///     a descriptor rather than a fifth hand-written enumerate-decode-compare loop.
    ///     </para>
    ///     <para>
    ///     The second is what byte identity is evidence <em>of</em> here. The encoder replays each
    ///     opcode's stored payload span rather than re-deriving it from the decoded node, because
    ///     the format is non-canonical in five separate ways and every one of them would rewrite
    ///     untouched files. So this sweep is sharp about structure - the node count, each node's
    ///     opcode count, the child run width, the presence of the output indices and the trailer
    ///     are all re-derived and must come back the same - and says nothing about the payload
    ///     widths. Those are pinned by
    ///     <see cref="TextureGraphConformanceTests.EveryTextureGraph_ConsumesItsFileExactlyBarTheTrailer"/>
    ///     and by <see cref="EveryTextureGraph_DecodesAndConsumesItsBufferExactly"/> below, which
    ///     decodes against a sentinel-padded copy and so cannot be met by an over-read.
    ///     </para>
    ///     <para>
    ///     In the "RealCache" collection for the same reason
    ///     <see cref="TextureGraphConformanceTests"/> is: <c>TextureManager.Textures</c> is static
    ///     and <c>Clear</c> disposes every definition in it, so classes that touch it must not run
    ///     concurrently.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheTextureGraphTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheTextureGraphTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Graphs index 9's reference table declares, one per group.</summary>
        /// <remarks>
        ///     Read from the table rather than written down. Index 9 is one of the eleven the two
        ///     supported caches disagree on - the repack holds 946 graphs to the vanilla capture's
        ///     915 - so a literal here would pin the suite to one of them.
        /// </remarks>
        private int GraphsInCache => _fixture.DeclaredGroups(RSConstants.TEXTURES);

        /// <summary>
        ///     The texture index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group rather than the 250-group sample: the whole index decompresses to well
        ///     under a megabyte, and "every texture graph in the cache re-encodes to its stored
        ///     bytes" is not a claim a run over 250 of them can make.
        ///     <para>
        ///     <c>NotOpcodeTerminated</c> because a graph file has no terminator - it ends on a
        ///     fixed trailer the client never reads - so the terminator assertion would fail every
        ///     record for a reason the format does not have. It also switches off the per-byte
        ///     opcode-boundary trace, which on a format whose records are hundreds of bytes long
        ///     would turn a failing run into a very slow one.
        ///     </para>
        /// </remarks>
        /// <returns>A sweep over every texture graph the cache declares.</returns>
        private DefinitionSweep<Texture> Sweep()
        {
            return new DefinitionSweep<Texture>(_fixture, _output, RSConstants.TEXTURES,
                new DefinitionCodec<Texture>("texture graph",
                    (id, stream) => Texture.Decode(stream),
                    texture => texture.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>
        ///     Every graph decodes and finishes on the last byte of its file.
        /// </summary>
        /// <remarks>
        ///     The sharp half of the codec's evidence. Nothing in a graph file states how long an
        ///     opcode's payload is, so a single wrong width desynchronises every node after it, and
        ///     the harness decodes a sentinel-padded copy as well as the genuine bytes - a decoder
        ///     that ran off the end reads into the padding and overshoots instead of stopping on
        ///     the length and looking exact.
        /// </remarks>
        [RealCacheFact]
        public void EveryTextureGraph_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.Equal(GraphsInCache, swept.Groups);
            Assert.Equal(_fixture.DeclaredFiles(RSConstants.TEXTURES), swept.Records);
        }

        /// <summary>Every graph re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     The editor rewrites a whole index 9 file on any save, and the archive CRC covers the
        ///     stored bytes, so an encoder that dropped an opcode or normalised a count would
        ///     rewrite graphs nobody edited and drag in the reference-table entry with them. The
        ///     opcodes type 12 swallows are the case to watch: <c>Node_Sub10_Sub30</c> has no arm
        ///     for opcodes 2 and 4, the decoder recognises them while reading nothing, and an
        ///     encoder built from decoded state alone drops them and shortens exactly those files.
        /// </remarks>
        [RealCacheFact]
        public void EveryTextureGraph_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(_fixture.DeclaredFiles(RSConstants.TEXTURES), swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        /// <remarks>
        ///     Independent of byte identity against the cache: this is the property a save path
        ///     depends on once a graph has actually been edited, and no comparison with the stored
        ///     bytes reaches it.
        /// </remarks>
        [RealCacheFact]
        public void EveryTextureGraph_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     What index 9 actually contains, so the codec's coverage is stated rather than assumed.
        /// </summary>
        /// <remarks>
        ///     Two claims, both about the format rather than about either cache.
        ///     <list type="bullet">
        ///     <item>Every node type is one of the forty the client's factory can construct.
        ///     <c>PlayerAppearance.method3630</c> returns exactly forty distinct
        ///     <c>Node_Sub10_Sub*</c> instances, so a graph naming type 40 is one the client cannot
        ///     build - and this decoder would read no opcodes and no children for it and then
        ///     desynchronise.</item>
        ///     <item>Every graph carries the three output indices, which follows from every graph
        ///     declaring at least one node: the client only writes them when the node count is
        ///     non-zero. An empty graph is a shape the encoder handles and the data does not
        ///     contain, and this is what says so.</item>
        ///     </list>
        ///     The histograms are printed rather than asserted. A count of which node types occur
        ///     belongs to one cache, and index 9 is one of the eleven the two disagree on.
        /// </remarks>
        [RealCacheFact]
        public void TheTextureIndex_HoldsWhatTheCodecClaimsItDoes()
        {
            var nodeTypes = new SortedDictionary<int, int>();
            var zeroWidthOpcodes = new SortedDictionary<(int Type, int Opcode), int>();
            var outOfRange = new List<string>();
            int graphsWithoutOutputs = 0;
            int nodes = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, texture) =>
            {
                TextureGraphRecord graph = texture.Record;
                if (!graph.HasOutputIndices)
                    graphsWithoutOutputs++;

                foreach (TextureNodeRecord node in graph.Nodes)
                {
                    nodes++;
                    Count(nodeTypes, node.Type);
                    if (node.Type < 0 || node.Type > 39)
                        outOfRange.Add($"texture {record.Id} carries node type {node.Type}");

                    foreach (TextureOpcodeRecord opcode in node.Opcodes)
                        if (opcode.Payload.Length == 0)
                            Count(zeroWidthOpcodes, (node.Type, opcode.Opcode));
                }
            });

            _output.WriteLine($"{nodes} nodes across {swept.Records} graphs");
            _output.WriteLine("node types: " + string.Join(", ", nodeTypes.Select(e => $"{e.Key}={e.Value}")));
            _output.WriteLine("opcodes that consume no bytes: " +
                              string.Join(", ", zeroWidthOpcodes.Select(e =>
                                  $"type {e.Key.Type} opcode {e.Key.Opcode} x{e.Value}")));

            Assert.Empty(outOfRange);
            Assert.Equal(0, graphsWithoutOutputs);
        }

        private static void Count<TKey>(SortedDictionary<TKey, int> counts, TKey value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }
    }
}
