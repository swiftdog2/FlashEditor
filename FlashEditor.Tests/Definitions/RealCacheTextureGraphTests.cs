using System;
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
        ///     opcodes type 12 swallows are the case to watch: <c>Node_Sub10_Sub30</c> has arms for
        ///     opcodes 0, 1 and 3 only, the decoder recognises the rest while reading nothing, and an
        ///     encoder built from decoded state alone drops them and shortens exactly those files.
        ///     Which opcodes those are is printed by
        ///     <see cref="TheTextureIndex_HoldsWhatTheCodecClaimsItDoes"/> rather than written down
        ///     here - the set was recorded as "2 and 4" in three places at once, all of them copies
        ///     of one unmeasured sentence, and it is not.
        ///     <para>
        ///     What this sweep cannot see is the whole reason
        ///     <see cref="EveryOpcodePayload_IsTheWidthTheClientReads"/> exists: the encoder replays
        ///     the spans the decoder captured, so wrongly-sized spans still tile the file exactly and
        ///     still re-encode byte for byte.
        ///     </para>
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
        ///     Every opcode payload in the index is the width the 637 client reads for it, checked
        ///     one occurrence at a time.
        /// </summary>
        /// <remarks>
        ///     This is the sweep that closes the hole
        ///     <see cref="EveryTextureGraph_ReEncodesToTheCapturedBytes"/> leaves open. That one
        ///     compares an encoder that replays the decoder's own spans against the bytes those spans
        ///     were cut from, so a mis-sized payload is invisible to it: wrongly-sized spans
        ///     concatenate back into the original file just as exactly as correctly-sized ones.
        ///     <para>
        ///     Exact consumption was the only thing constraining the widths, and it constrains them
        ///     in aggregate - one equation per file, that every width in it sums with the structure
        ///     to the file's length. Two errors inside one node that cancel satisfy it, and so does
        ///     any other error vector in that equation's null space.
        ///     <see cref="TextureGraphWidthEvidenceTests"/> builds such a pair rather than arguing
        ///     about it. This sweep asserts one equality per <em>occurrence</em>, against a table
        ///     transcribed from the client's node classes with no reference to
        ///     <c>Texture.Decode</c>, so a single wrong width fails at the node and opcode that
        ///     carries it.
        ///     </para>
        ///     <para>
        ///     The whole structure is compared, not only the widths - node count, each node's type
        ///     and header bytes, the opcode sequence, the child run and where the client's parse ends
        ///     - because a width error and a child-count error look identical downstream and are
        ///     fixed in different places.
        ///     </para>
        ///     <para>
        ///     Measured rather than asserted: widening node type 12's opcode 0 from one byte to two
        ///     in <c>Texture.DecodeNodeOpcode</c> leaves every other index 9 test in the suite green
        ///     - byte identity, both exact-consumption sweeps, the encode fixed point and the
        ///     unhandled-opcode check - and fails only here, naming texture 275 node 16 and texture
        ///     742 node 3. Type 12 is the sharpest case because its arm returns true for every opcode
        ///     it does not handle, so the byte the widened payload swallows cannot even surface as an
        ///     unrecognised opcode.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryOpcodePayload_IsTheWidthTheClientReads()
        {
            var failures = new List<string>();
            var checkedPairs = new SortedDictionary<(int Type, int Opcode), int>();
            int opcodes = 0;
            int nodes = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, texture) =>
            {
                TextureGraphRecord ours = texture.Record;

                ClientGraphLayout theirs;
                try
                {
                    theirs = ClientTextureGraphReader.Read(record.Bytes);
                }
                catch (Exception ex)
                {
                    failures.Add($"texture {record.Id}: the client's own layout does not fit the " +
                                 $"{record.Bytes.Length} bytes stored - {ex.Message}");
                    return;
                }

                if (theirs.Nodes.Count != ours.Nodes.Count)
                {
                    failures.Add($"texture {record.Id}: the client reads {theirs.Nodes.Count} nodes, " +
                                 $"we read {ours.Nodes.Count}");
                    return;
                }

                if (theirs.BodyLength != ours.BodyLength)
                {
                    failures.Add($"texture {record.Id}: the client's parse ends at {theirs.BodyLength}, " +
                                 $"ours at {ours.BodyLength}, in a {record.Bytes.Length}-byte file");
                }

                if (theirs.HasOutputIndices != ours.HasOutputIndices)
                {
                    failures.Add($"texture {record.Id}: output indices present={theirs.HasOutputIndices} " +
                                 $"by the client, {ours.HasOutputIndices} by us");
                }

                for (int n = 0; n < theirs.Nodes.Count; n++)
                {
                    ClientNodeLayout thatNode = theirs.Nodes[n];
                    TextureNodeRecord thisNode = ours.Nodes[n];
                    nodes++;

                    if (thatNode.Type != thisNode.Type)
                    {
                        failures.Add($"texture {record.Id} node {n}: type {thatNode.Type} by the " +
                                     $"client, {thisNode.Type} by us");
                        break;
                    }

                    if (thatNode.ChildCount != thisNode.ChildIndices.Length)
                    {
                        failures.Add($"texture {record.Id} node {n} (type {thatNode.Type}): " +
                                     $"{thatNode.ChildCount} child bytes by the client, " +
                                     $"{thisNode.ChildIndices.Length} by us");
                    }

                    if (thatNode.Opcodes.Count != thisNode.Opcodes.Count)
                    {
                        failures.Add($"texture {record.Id} node {n} (type {thatNode.Type}): " +
                                     $"{thatNode.Opcodes.Count} opcodes by the client, " +
                                     $"{thisNode.Opcodes.Count} by us");
                        continue;
                    }

                    for (int op = 0; op < thatNode.Opcodes.Count; op++)
                    {
                        ClientOpcodeSpan expected = thatNode.Opcodes[op];
                        TextureOpcodeRecord actual = thisNode.Opcodes[op];
                        opcodes++;
                        Count(checkedPairs, (expected.NodeType, expected.Opcode));

                        if (expected.Opcode != actual.Opcode)
                        {
                            failures.Add($"texture {record.Id} {expected}: we read it as opcode " +
                                         $"{actual.Opcode}");
                            break;
                        }

                        if (expected.Width != actual.Payload.Length)
                        {
                            failures.Add($"texture {record.Id} {expected}: the client reads a " +
                                         $"{expected.Width}-byte payload at offset {expected.Offset}, " +
                                         $"we consumed {actual.Payload.Length} bytes");
                            break;
                        }
                    }
                }
            });

            _output.WriteLine($"{opcodes} opcode payloads across {nodes} nodes in {swept.Records} graphs " +
                              "were measured against the client's own widths");
            _output.WriteLine($"{checkedPairs.Count} distinct node-type/opcode pairs occur in this cache, " +
                              "so only those widths are pinned by data: " +
                              string.Join(", ", checkedPairs.Select(e =>
                                  $"{e.Key.Type}/{e.Key.Opcode}=x{e.Value}")));
            _output.WriteLine($"{swept.Records} files give the exact-consumption sweep {swept.Records} " +
                              $"equations to work with; this one asserts {opcodes} equalities over the " +
                              "same widths");

            Assert.True(opcodes > 0, "no opcode payload was measured, so nothing was checked");
            AssertNoFailures(failures);
        }

        /// <summary>
        ///     Perturbs every opcode width the cache exercises, one at a time, and measures which
        ///     layer of the evidence notices.
        /// </summary>
        /// <remarks>
        ///     A mutation gate rather than an assertion about the format: it wrongs one width by a
        ///     byte and asks what would have gone red, over the whole index, for each of the widths
        ///     this cache actually puts to the test. Two layers are compared - the exact-consumption
        ///     sweep, which sees a perturbation only when it moves some file's total, and the
        ///     per-occurrence check, which sees it at the opcode.
        ///     <para>
        ///     The assertion is the second one, because that is the property the new sweep is for: a
        ///     single wrong width must be visible at every occurrence of it, with no pair skipped.
        ///     A width table that quietly passed unknown pairs, or a sweep that compared totals
        ///     rather than spans, fails here rather than sitting green and testing nothing.
        ///     </para>
        ///     <para>
        ///     How many perturbations exact consumption misses is printed rather than asserted, since
        ///     it is a property of the two caches and not of the format. The general statement -
        ///     that consumption balancing cannot pin an individual width, because two errors inside
        ///     one node cancel - is made by construction in
        ///     <see cref="TextureGraphWidthEvidenceTests"/>, which needs no cache to hold.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void PerturbingOneOpcodeWidth_IsSeenAtTheOpcodeEvenWhenTheFileTotalSurvives()
        {
            var files = new List<byte[]>();
            Sweep().ForEachDecoded((record, texture) => files.Add(record.Bytes));

            var pairs = new SortedSet<(int Type, int Opcode)>();
            var bodyLengths = new List<int>();
            foreach (byte[] file in files)
            {
                ClientGraphLayout layout = ClientTextureGraphReader.Read(file);
                bodyLengths.Add(layout.BodyLength);
                foreach (ClientOpcodeSpan span in layout.Opcodes)
                    pairs.Add((span.NodeType, span.Opcode));
            }

            var unseenByConsumption = new List<string>();
            var unseenAtTheOpcode = new List<string>();
            int perturbations = 0;

            foreach ((int Type, int Opcode) pair in pairs)
            {
                foreach (int delta in new[] { -1, 1 })
                {
                    int perturbed = ClientTextureGraphReader.DeclaredWidth(pair.Type, pair.Opcode) + delta;
                    if (perturbed < 0)
                        continue;

                    perturbations++;
                    int filesWithADifferentTotal = 0;
                    int occurrencesOfADifferentWidth = 0;

                    for (int f = 0; f < files.Count; f++)
                    {
                        ClientGraphLayout wrong;
                        try
                        {
                            wrong = ClientTextureGraphReader.Read(files[f],
                                (type, opcode) => type == pair.Type && opcode == pair.Opcode
                                    ? perturbed
                                    : ClientTextureGraphReader.DeclaredWidth(type, opcode));
                        }
                        catch (InvalidOperationException)
                        {
                            //Ran off the end of the file, which is a consumption failure too.
                            filesWithADifferentTotal++;
                            occurrencesOfADifferentWidth++;
                            continue;
                        }

                        if (wrong.BodyLength != bodyLengths[f])
                            filesWithADifferentTotal++;

                        foreach (ClientOpcodeSpan span in wrong.Opcodes)
                            if (span.NodeType == pair.Type && span.Opcode == pair.Opcode &&
                                span.Width != ClientTextureGraphReader.DeclaredWidth(pair.Type, pair.Opcode))
                                occurrencesOfADifferentWidth++;
                    }

                    string named = $"type {pair.Type} opcode {pair.Opcode} at " +
                                   $"{ClientTextureGraphReader.DeclaredWidth(pair.Type, pair.Opcode) + delta} " +
                                   $"bytes instead of " +
                                   $"{ClientTextureGraphReader.DeclaredWidth(pair.Type, pair.Opcode)}";

                    if (filesWithADifferentTotal == 0)
                        unseenByConsumption.Add(named);
                    if (occurrencesOfADifferentWidth == 0)
                        unseenAtTheOpcode.Add($"{named}: no occurrence of it changed width");
                }
            }

            _output.WriteLine($"{perturbations} single-opcode width perturbations were tried across the " +
                              $"{pairs.Count} node-type/opcode pairs this cache exercises");
            _output.WriteLine($"{unseenByConsumption.Count} of them move no file's total, so the " +
                              "exact-consumption sweep would stay green" +
                              (unseenByConsumption.Count == 0
                                  ? ""
                                  : ": " + string.Join(", ", unseenByConsumption.Take(10))));
            _output.WriteLine($"{unseenAtTheOpcode.Count} of them are invisible at the opcode, which is " +
                              "what this test asserts is none");

            Assert.True(perturbations > 0, "no width was perturbed, so nothing was gated");
            AssertNoFailures(unseenAtTheOpcode);
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

        /// <summary>
        ///     Fails with every disagreement listed, truncated so a whole-format mismatch stays
        ///     readable.
        /// </summary>
        /// <remarks>
        ///     The count is reported before the detail because "3 opcodes disagree" and "19,000
        ///     opcodes disagree" want completely different first moves, and the truncated list cannot
        ///     tell them apart on its own.
        /// </remarks>
        /// <param name="failures">Every disagreement found, in the order it was found.</param>
        private static void AssertNoFailures(List<string> failures)
        {
            if (failures.Count == 0)
                return;

            const int reported = 20;
            string detail = string.Join(Environment.NewLine, failures.Take(reported));
            if (failures.Count > reported)
                detail += $"{Environment.NewLine}... and {failures.Count - reported} more";

            Assert.Fail($"{failures.Count} texture graph fields disagree with the 637 client:" +
                        Environment.NewLine + detail);
        }
    }
}
