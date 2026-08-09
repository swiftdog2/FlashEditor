using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Particles;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Particles
{
    /// <summary>
    ///     Decodes every particle emitter and effector the index-27 reference table declares,
    ///     requires exact buffer consumption, and requires each to re-encode to the bytes it came
    ///     from.
    /// </summary>
    /// <remarks>
    ///     Index 27 holds two unrelated record families in two groups - emitters in group 0,
    ///     effectors in group 1 - so it gets two codecs and two sweeps, addressed per group the way
    ///     index 2 is. Its file count is one of the six that move between the two supported caches,
    ///     which is why every population here is read off the reference table on each run and every
    ///     assertion is a relationship against it.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheParticleTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheParticleTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Files the reference table declares in one of index 27's groups.</summary>
        /// <param name="groupId">The group to count.</param>
        /// <returns>The declared file count.</returns>
        private int DeclaredFiles(int groupId)
        {
            return _fixture.Table(RSConstants.CONFIG_PARTICLES).GetArchiveEntry(groupId)
                .GetValidFileIds().Length;
        }

        /// <summary>The emitter family, which is the whole of group 0.</summary>
        /// <returns>A sweep over every declared emitter.</returns>
        private DefinitionSweep<ParticleEmitterDefinition> Emitters()
        {
            return new DefinitionSweep<ParticleEmitterDefinition>(_fixture, _output,
                RSConstants.CONFIG_PARTICLES,
                new DefinitionCodec<ParticleEmitterDefinition>("particle emitter",
                    (id, stream) => new ParticleEmitterDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(ParticleEmitterDefinition.GroupId);
        }

        /// <summary>The effector family, which is the whole of group 1.</summary>
        /// <returns>A sweep over every declared effector.</returns>
        private DefinitionSweep<ParticleEffectorDefinition> Effectors()
        {
            return new DefinitionSweep<ParticleEffectorDefinition>(_fixture, _output,
                RSConstants.CONFIG_PARTICLES,
                new DefinitionCodec<ParticleEffectorDefinition>("particle effector",
                    (id, stream) => new ParticleEffectorDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(ParticleEffectorDefinition.GroupId);
        }

        /// <summary>
        ///     The reference table declares exactly the two groups the client reads from this index.
        /// </summary>
        /// <remarks>
        ///     <c>ParticleType.java:11</c> asks for group 0 and <c>Class21.java:51</c> for group 1,
        ///     both by literal, so a table that declared either differently would leave one family
        ///     unreachable. The counts are read rather than written down.
        /// </remarks>
        [RealCacheFact]
        public void TheIndexDeclaresTheTwoGroupsTheClientReads()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.CONFIG_PARTICLES);
            int[] groups = table.GetArchiveEntries().Keys.ToArray();

            _output.WriteLine("index 27 declares groups " + string.Join(", ", groups) +
                              $"; {DeclaredFiles(ParticleEmitterDefinition.GroupId)} emitters and " +
                              $"{DeclaredFiles(ParticleEffectorDefinition.GroupId)} effectors");

            Assert.Contains(ParticleEmitterDefinition.GroupId, groups);
            Assert.Contains(ParticleEffectorDefinition.GroupId, groups);
            Assert.True(DeclaredFiles(ParticleEmitterDefinition.GroupId) > 0,
                "index 27 group 0 declares no files, so nothing would be checked");
            Assert.True(DeclaredFiles(ParticleEffectorDefinition.GroupId) > 0,
                "index 27 group 1 declares no files, so nothing would be checked");

            Assert.Equal(DeclaredFiles(ParticleEmitterDefinition.GroupId) +
                         DeclaredFiles(ParticleEffectorDefinition.GroupId),
                _fixture.DeclaredFiles(RSConstants.CONFIG_PARTICLES));
        }

        /// <summary>Every declared emitter decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryEmitter_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Emitters().AssertExactConsumption();

            Assert.Equal(DeclaredFiles(ParticleEmitterDefinition.GroupId), swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
        }

        /// <summary>Every declared emitter re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryEmitter_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Emitters().AssertReEncodesToCapturedBytes();

            Assert.Equal(DeclaredFiles(ParticleEmitterDefinition.GroupId), swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The emitter encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryEmitter_EncodeIsAFixedPointOfDecode()
        {
            Emitters().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>Every declared effector decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryEffector_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Effectors().AssertExactConsumption();

            Assert.Equal(DeclaredFiles(ParticleEffectorDefinition.GroupId), swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
        }

        /// <summary>Every declared effector re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryEffector_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Effectors().AssertReEncodesToCapturedBytes();

            Assert.Equal(DeclaredFiles(ParticleEffectorDefinition.GroupId), swept.Records);
            Assert.Equal(swept.Records, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The effector encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryEffector_EncodeIsAFixedPointOfDecode()
        {
            Effectors().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     No record in either family stores its opcodes in ascending order, and no single order
        ///     accounts for them all.
        /// </summary>
        /// <remarks>
        ///     Stated as a property of each record rather than as a table of orderings and counts, so
        ///     it holds in any cache. It is the whole justification for recording the opcode stream on
        ///     this index: an encoder with an order of its own would rewrite every file the user
        ///     merely opened, and the archive CRC covers those bytes.
        /// </remarks>
        [RealCacheFact]
        public void NoParticleRecordStoresItsOpcodesInAscendingOrder()
        {
            var emitterOrders = new SortedDictionary<string, int>();
            var effectorOrders = new SortedDictionary<string, int>();
            int ascending = 0;

            DefinitionSweepResult emitters = Emitters().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                Assert.True(opcodes.Length > 0, $"emitter {record.Id} carries no opcode at all");

                string order = string.Join(",", opcodes);
                emitterOrders.TryGetValue(order, out int seen);
                emitterOrders[order] = seen + 1;

                if (opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                    ascending++;
            });

            DefinitionSweepResult effectors = Effectors().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                Assert.True(opcodes.Length > 0, $"effector {record.Id} carries no opcode at all");

                string order = string.Join(",", opcodes);
                effectorOrders.TryGetValue(order, out int seen);
                effectorOrders[order] = seen + 1;

                if (opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                    ascending++;
            });

            _output.WriteLine($"{emitterOrders.Count} distinct emitter opcode orders across " +
                              $"{emitters.Records} records, {effectorOrders.Count} distinct effector " +
                              $"orders across {effectors.Records}");

            Assert.Equal(0, ascending);
            Assert.True(emitterOrders.Count > 1 && effectorOrders.Count > 1,
                "one opcode order accounts for a whole family, so nothing here needs the recorded " +
                "stream");
        }

        /// <summary>
        ///     Both encodings of the size bounds occur, and each record uses exactly one of them.
        /// </summary>
        /// <remarks>
        ///     Opcodes 5 and 31 are aliases for the same pair of fields, so the decoded values cannot
        ///     say which was stored. This is the case <c>CLAUDE.md</c> calls aliased values, and it is
        ///     load-bearing rather than theoretical: normalising every record onto opcode 31 produces
        ///     a file two bytes longer than the one it replaced.
        /// </remarks>
        [RealCacheFact]
        public void EveryEmitterStoresItsSizeBoundsThroughExactlyOneOfTheTwoAliases()
        {
            int single = 0;
            int pair = 0;
            int neither = 0;

            DefinitionSweepResult swept = Emitters().ForEachDecoded((record, definition) =>
            {
                bool hasSingle = definition.Opcodes.Has(5);
                bool hasPair = definition.Opcodes.Has(31);

                Assert.False(hasSingle && hasPair,
                    $"emitter {record.Id} stores both size opcodes, so one silently overwrites the " +
                    "other and the encoder's choice of which to keep is not decided by this cache");

                if (hasSingle)
                    single++;
                else if (hasPair)
                    pair++;
                else
                    neither++;
            });

            _output.WriteLine($"{single} emitters store the size bounds as one value, {pair} as a " +
                              $"pair, {neither} store neither");

            //Figures of decoded content rather than of the reference table, so they belong to
            //whichever cache is loaded rather than to build 639.
            _fixture.Profile.AssertCensus(_output, "particles.emittersStoringOneSizeValue", single);
            _fixture.Profile.AssertCensus(_output, "particles.emittersStoringASizePair", pair);

            Assert.Equal(swept.Records, single + pair + neither);
            Assert.True(single > 0 && pair > 0,
                "only one of the two size encodings occurs, so the alias is not exercised here and " +
                "the recording that defends it is untested");
        }

        /// <summary>
        ///     Every effector id an emitter names resolves to a file the index-27 table declares.
        /// </summary>
        /// <remarks>
        ///     The join is checkable rather than plausible: <c>Class21.method263</c> fetches an
        ///     effector as <c>getChildFromFolder(1, id)</c>, so an id the table does not declare is a
        ///     record the client cannot load. Opcode 25 is deliberately excluded - its ids are model
        ///     attachment keys rather than effector ids (Particle_Sub5.java:207-211) - and neither
        ///     supported cache stores it anyway.
        /// </remarks>
        [RealCacheFact]
        public void EveryEffectorIdAnEmitterNamesIsADeclaredEffector()
        {
            var declared = new HashSet<int>(_fixture.Table(RSConstants.CONFIG_PARTICLES)
                .GetArchiveEntry(ParticleEffectorDefinition.GroupId).GetValidFileIds());

            var named = new SortedSet<int>();

            Emitters().ForEachDecoded((record, definition) =>
            {
                foreach (int id in Referenced(definition))
                {
                    named.Add(id);
                    Assert.True(declared.Contains(id),
                        $"emitter {record.Id} names effector {id}, which group 1 does not declare");
                }
            });

            _output.WriteLine($"{named.Count} distinct effector ids named, against " +
                              $"{declared.Count} declared");

            Assert.True(named.Count > 0, "no emitter names an effector, so the join was not exercised");
        }

        /// <summary>The effector ids one emitter names through opcodes 9 and 10.</summary>
        /// <param name="definition">The decoded emitter.</param>
        /// <returns>Every id, with repeats.</returns>
        private static IEnumerable<int> Referenced(ParticleEmitterDefinition definition)
        {
            if (definition.SceneEffectorIds != null)
                foreach (int id in definition.SceneEffectorIds)
                    yield return id;

            if (definition.GlobalEffectorIds != null)
                foreach (int id in definition.GlobalEffectorIds)
                    yield return id;
        }
    }
}
