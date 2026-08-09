using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Defaults;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.Defaults
{
    /// <summary>
    ///     Decodes both index-28 records out of the real cache, requires exact buffer consumption,
    ///     and requires each to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 28 is two unrelated config blobs rather than a record table, so it gets two codecs
    ///     and two sweeps of one record each. The group ids are 1 and 3, not 0 and 1: the idx file
    ///     has four slots and two of them are dead, so anything driven off <c>idx28.Length / 6</c>
    ///     asks for groups that do not exist. Both sweeps are therefore driven off the reference
    ///     table's declared group ids like every other.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheDefaultsTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheDefaultsTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>The scene-defaults record, which is the whole of group 1.</summary>
        /// <returns>A sweep over that one record.</returns>
        private DefinitionSweep<SceneDefaultsDefinition> SceneDefaults()
        {
            return new DefinitionSweep<SceneDefaultsDefinition>(_fixture, _output, RSConstants.DEFAULTS,
                new DefinitionCodec<SceneDefaultsDefinition>("scene defaults",
                    (id, stream) => new SceneDefaultsDefinition().Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(SceneDefaultsDefinition.GroupId);
        }

        /// <summary>The hitsplat-layout record, which is the whole of group 3.</summary>
        /// <returns>A sweep over that one record.</returns>
        private DefinitionSweep<HitsplatLayoutDefinition> HitsplatLayout()
        {
            return new DefinitionSweep<HitsplatLayoutDefinition>(_fixture, _output, RSConstants.DEFAULTS,
                new DefinitionCodec<HitsplatLayoutDefinition>("hitsplat layout",
                    (id, stream) => new HitsplatLayoutDefinition().Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .WithinGroup(HitsplatLayoutDefinition.GroupId);
        }

        /// <summary>
        ///     The reference table declares exactly the two groups the client reads, and nothing
        ///     else.
        /// </summary>
        /// <remarks>
        ///     Read off the table rather than written down. The claim is the relationship the client
        ///     depends on: both ids it asks for by literal are declared, each holds exactly one
        ///     file, and no group is left that nothing would ever read - <c>method2733</c> throws
        ///     when a group's file count is not 1 (JS5Archive.java:612), so a second file in either
        ///     crashes the client at load.
        /// </remarks>
        [RealCacheFact]
        public void TheIndexDeclaresTheTwoGroupsTheClientReads()
        {
            RSReferenceTable table = _fixture.Table(RSConstants.DEFAULTS);
            int[] groups = table.GetArchiveEntries().Keys.ToArray();

            _output.WriteLine("index 28 declares groups " + string.Join(", ", groups));

            Assert.Contains(SceneDefaultsDefinition.GroupId, groups);
            Assert.Contains(HitsplatLayoutDefinition.GroupId, groups);

            foreach (int group in groups)
                Assert.Single(table.GetArchiveEntry(group).GetValidFileIds());

            Assert.Equal(groups.Length, _fixture.DeclaredFiles(RSConstants.DEFAULTS));
        }

        /// <summary>The scene defaults record decodes, consumes exactly, and re-encodes.</summary>
        [RealCacheFact]
        public void TheSceneDefaultsRecord_RoundTripsToItsStoredBytes()
        {
            DefinitionSweep<SceneDefaultsDefinition> sweep = SceneDefaults();

            DefinitionSweepResult consumed = sweep.AssertExactConsumption();
            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();

            Assert.Equal(1, consumed.Records);
            Assert.Equal(1, swept.Records);
            Assert.Equal(1, swept.Passed);
            Assert.Equal(0, swept.Reordered);

            sweep.AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>The hitsplat layout record decodes, consumes exactly, and re-encodes.</summary>
        [RealCacheFact]
        public void TheHitsplatLayoutRecord_RoundTripsToItsStoredBytes()
        {
            DefinitionSweep<HitsplatLayoutDefinition> sweep = HitsplatLayout();

            DefinitionSweepResult consumed = sweep.AssertExactConsumption();
            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();

            Assert.Equal(1, consumed.Records);
            Assert.Equal(1, swept.Records);
            Assert.Equal(1, swept.Passed);
            Assert.Equal(0, swept.Reordered);

            sweep.AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     The slot count is stored ahead of the offsets it sizes.
        /// </summary>
        /// <remarks>
        ///     Load-bearing rather than merely non-canonical: opcode 3 allocates the arrays opcode 1
        ///     fills, so the two are not interchangeable and an encoder emitting them in ascending
        ///     order would produce a file the client mis-reads. Asserted as an ordering rather than
        ///     against a literal byte sequence, so it says why it matters.
        /// </remarks>
        [RealCacheFact]
        public void TheSlotCountIsStoredBeforeTheOffsetsItSizes()
        {
            HitsplatLayout().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                _output.WriteLine("hitsplat layout opcodes: " + string.Join(", ", opcodes));

                int count = Array.IndexOf(opcodes, 3);
                int offsets = Array.IndexOf(opcodes, 1);

                Assert.True(count >= 0, "the record does not store a slot count");
                Assert.True(offsets >= 0, "the record does not store the offsets");
                Assert.True(count < offsets,
                    "the slot count is stored after the offsets it sizes, so the client would read " +
                    "the wrong number of pairs and then discard them");

                Assert.Equal(definition.SlotCount, definition.OffsetX.Length);
                Assert.Equal(definition.SlotCount, definition.OffsetY.Length);
            });
        }

        /// <summary>
        ///     The committed codec fixtures are still the bytes the cache stores.
        /// </summary>
        /// <remarks>
        ///     Without this the offline codec tests pin the codec to literals nobody can check,
        ///     which is the shape a hand-built test takes when it asserts a bug rather than catching
        ///     one. Both supported caches store these exact bytes, so a failure here means a cache
        ///     this project has not measured rather than a regression - re-read the record and
        ///     update the fixture.
        /// </remarks>
        [RealCacheFact]
        public void TheCommittedFixturesAreStillWhatTheCacheStores()
        {
            RSCache cache = _fixture.OpenCache();

            foreach (KeyValuePair<int, byte[]> expected in new Dictionary<int, byte[]>
                     {
                         [SceneDefaultsDefinition.GroupId] = DefaultsDefinitionCodecTests.SceneDefaultsBytes,
                         [HitsplatLayoutDefinition.GroupId] = DefaultsDefinitionCodecTests.HitsplatLayoutBytes
                     })
            {
                int[] files = cache.GetFileIds(RSConstants.DEFAULTS, expected.Key);
                Assert.Single(files);
                Assert.Equal(expected.Value,
                    cache.ReadFileBytes(RSConstants.DEFAULTS, expected.Key, files[0]));
            }
        }
    }
}
