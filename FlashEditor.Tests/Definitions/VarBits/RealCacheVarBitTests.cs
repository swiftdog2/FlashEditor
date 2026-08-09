using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.VarBits;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.VarBits
{
    /// <summary>
    ///     Decodes every varbit the index-22 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     The format is as small as one in this cache gets - one opcode carrying three fields - so
    ///     what these sweeps really defend is the absent-versus-default split. A quarter of the
    ///     declared files hold nothing but the terminator, and they decode to exactly the same
    ///     all-zero varbit as a stored record of zeroes would. An encoder that wrote six bytes for a
    ///     default-valued varbit rewrites every one of them the first time a group is saved.
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheVarBitTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheVarBitTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-22 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.SCRIPT_CONFIGS);

        /// <summary>Files the index-22 reference table declares across every group.</summary>
        private int VarBitsDeclared => _fixture.DeclaredFiles(RSConstants.SCRIPT_CONFIGS);

        /// <summary>The varbit index bound to the production codec.</summary>
        /// <returns>A sweep over every declared varbit.</returns>
        private DefinitionSweep<VarBitDefinition> Sweep()
        {
            return new DefinitionSweep<VarBitDefinition>(_fixture, _output, RSConstants.SCRIPT_CONFIGS,
                new DefinitionCodec<VarBitDefinition>("varbit",
                    (id, stream) => new VarBitDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .AcrossEveryGroup();
        }

        /// <summary>Every declared varbit decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryVarBit_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(VarBitsDeclared > 0, "index 22 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(VarBitsDeclared, swept.Records);
            Assert.Equal(VarBitsDeclared, swept.Passed);
        }

        /// <summary>Every declared varbit re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryVarBit_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(VarBitsDeclared > 0, "index 22 declares no files, so nothing was checked");
            Assert.Equal(VarBitsDeclared, swept.Records);
            Assert.Equal(VarBitsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryVarBit_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     Stored records and bare terminators partition the declared population, and the two
        ///     are distinguishable after decoding.
        /// </summary>
        /// <remarks>
        ///     The partition is the assertion, not either count: what matters is that no third state
        ///     exists and that both are populated, so neither branch of the encoder is dead. The
        ///     bit-range bound is checked alongside because the client indexes a 32-entry mask table
        ///     with the range width and does not bounds-check it.
        /// </remarks>
        [RealCacheFact]
        public void StoredRecordsAndBareTerminatorsPartitionTheIndex()
        {
            int stored = 0;
            int bare = 0;
            int beyondTheClientMaskTable = 0;
            int inverted = 0;
            int highestVarp = -1;
            var widths = new SortedDictionary<int, int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                if (!definition.IsStored)
                {
                    bare++;

                    //A file that stored nothing must decode to the all-zero record, or the decoder
                    //is carrying state between definitions.
                    Assert.Equal(0, definition.VarpId);
                    Assert.Equal(0, definition.FromBit);
                    Assert.Equal(0, definition.ToBit);
                    return;
                }

                stored++;
                Count(widths, definition.BitWidth);
                highestVarp = Math.Max(highestVarp, definition.VarpId);

                if (!definition.FitsTheClientMaskTable)
                    beyondTheClientMaskTable++;
                if (definition.ToBit < definition.FromBit)
                    inverted++;
            });

            _output.WriteLine($"{stored} stored records, {bare} bare terminators, highest varp {highestVarp}");
            _output.WriteLine("bit range widths: " + Histogram(widths));

            Assert.Equal(VarBitsDeclared, swept.Records);
            Assert.Equal(VarBitsDeclared, stored + bare);
            Assert.True(stored > 0, "no varbit record was stored, so the opcode-1 path is untested");
            Assert.True(bare > 0, "no bare terminator was seen, so the absent-versus-default path is untested");

            //Every shipped record fits the client's mask table, so the editor may treat a range it
            //cannot express as invalid input rather than as something the cache contains.
            Assert.Equal(0, beyondTheClientMaskTable);
            Assert.Equal(0, inverted);
        }

        /// <summary>
        ///     A bare terminator survives the write path as one byte rather than growing to six.
        /// </summary>
        /// <remarks>
        ///     The byte-identity sweep already covers this across the whole index, but only while a
        ///     bare terminator exists in the cache. Stating it against a picked record makes the
        ///     requirement legible in isolation, and names the group and file it was taken from so
        ///     the failure is actionable.
        /// </remarks>
        [RealCacheFact]
        public void ADefaultValuedVarBitReEncodesToItsSingleTerminatorByte()
        {
            RSCache cache = _fixture.OpenCache();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.SCRIPT_CONFIGS);

            /* Read a group at a time rather than a file at a time. ReadFile resolves and unpacks the
               whole group for each call, so a per-file walk over 1024 files re-inflates the same
               BZip2 container 1024 times to find a one-byte payload in it. */
            int group = -1;
            int file = -1;
            byte[] bytes = null;
            foreach (int candidate in cache.GetReferenceTable(RSConstants.SCRIPT_CONFIGS)
                         .GetArchiveEntries().Keys)
            {
                foreach (KeyValuePair<int, JagStream> entry in
                         cache.ReadGroup(RSConstants.SCRIPT_CONFIGS, candidate).OrderBy(pair => pair.Key))
                {
                    byte[] payload = entry.Value.ToArray();
                    if (payload.Length != 1 || payload[0] != 0)
                        continue;

                    group = candidate;
                    file = entry.Key;
                    bytes = payload;
                    break;
                }

                if (bytes != null)
                    break;
            }

            Assert.True(bytes != null, "index 22 holds no bare-terminator file to check");

            int id = addressing.DefinitionId(group, file);
            _output.WriteLine($"varbit {id} (group {group} file {file}) is a bare terminator");

            var definition = new VarBitDefinition { Id = id }.Decode(new JagStream(bytes));

            Assert.False(definition.IsStored);
            Assert.Equal(new byte[] { 0 }, definition.Encode().ToArray());
        }

        private static void Count(SortedDictionary<int, int> counts, int value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(entry => entry.Key + "=" + entry.Value));
        }
    }
}
