using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Billboards;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Billboards
{
    /// <summary>
    ///     Decodes every billboard the index-29 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     The index is one group, so a billboard id is a file id within group 0. Its file count is
    ///     one of the six that differ between the two supported caches, which is exactly why nothing
    ///     here is written down: every population comes off the reference table and every assertion
    ///     is a relationship against it.
    ///     <para>
    ///     Order capture is the property under test. Opcode 1 is written last in every record, so an
    ///     encoder emitting ascending opcodes reproduces none of them, and the order is not
    ///     derivable from a rule either - both 4-then-5 and 5-then-4 occur.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheBillboardTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheBillboardTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Files the index-29 reference table declares.</summary>
        /// <remarks>
        ///     Read off the table on every run. This is one of the counts that moves between the two
        ///     supported caches, so a literal here would be a fact about whichever one it was
        ///     measured on rather than about the codec.
        /// </remarks>
        private int BillboardsDeclared => _fixture.DeclaredFiles(RSConstants.CONFIG_BILLBOARD);

        /// <summary>Groups the index-29 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.CONFIG_BILLBOARD);

        /// <summary>The billboard index bound to the production codec.</summary>
        /// <returns>A sweep over every declared billboard.</returns>
        private DefinitionSweep<BillboardDefinition> Sweep()
        {
            return new DefinitionSweep<BillboardDefinition>(_fixture, _output, RSConstants.CONFIG_BILLBOARD,
                new DefinitionCodec<BillboardDefinition>("billboard",
                    (id, stream) => new BillboardDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .AcrossEveryGroup();
        }

        /// <summary>Every declared billboard decodes and finishes on the last byte of its file.</summary>
        [RealCacheFact]
        public void EveryBillboard_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(BillboardsDeclared > 0, "index 29 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(BillboardsDeclared, swept.Records);
            Assert.Equal(BillboardsDeclared, swept.Passed);
        }

        /// <summary>Every declared billboard re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryBillboard_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(BillboardsDeclared > 0, "index 29 declares no files, so nothing was checked");
            Assert.Equal(BillboardsDeclared, swept.Records);
            Assert.Equal(BillboardsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryBillboard_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     Opcode 1 is the last opcode of every record, and no record's opcodes ascend.
        /// </summary>
        /// <remarks>
        ///     Stated as a property of each record rather than as a table of orderings and counts,
        ///     so it holds in any cache. It is the whole justification for recording the opcode
        ///     stream on this index: an ascending encoder would rewrite every file the user merely
        ///     opened, and the archive CRC covers those bytes.
        /// </remarks>
        [RealCacheFact]
        public void MaterialIsAlwaysTheLastOpcodeAndNoRecordAscends()
        {
            var orders = new SortedDictionary<string, int>();
            int ascending = 0;

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                string order = string.Join(",", opcodes);
                orders.TryGetValue(order, out int seen);
                orders[order] = seen + 1;

                Assert.True(opcodes.Length > 0, $"billboard {record.Id} carries no opcode at all");
                Assert.Equal(1, opcodes[opcodes.Length - 1]);

                if (opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                    ascending++;
            });

            foreach (KeyValuePair<string, int> order in orders)
                _output.WriteLine($"opcode order [{order.Key}]: {order.Value}");

            Assert.Equal(BillboardsDeclared, swept.Records);
            Assert.Equal(0, ascending);

            //More than one ordering, or the recording would be worth nothing over a fixed order.
            Assert.True(orders.Count > 1, "every record shares one opcode order, so nothing here " +
                                          "needs the recorded stream");
        }

        /// <summary>
        ///     A field stored at its own default cannot be told from an absent one by value, so the
        ///     recorded stream is the only thing that says which it was.
        /// </summary>
        /// <remarks>
        ///     Both cases occur: records store the combine mode at its default of 1, and records
        ///     store opcode 3 as 0. An encoder that emitted an opcode only when its field differed
        ///     from the default would shorten every one of them.
        /// </remarks>
        [RealCacheFact]
        public void FieldsStoredAtTheirDefaultAreKept()
        {
            int combineAtDefault = 0;
            int unusedByteZero = 0;
            int withoutCombine = 0;
            int withoutUnusedByte = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Opcodes.Has(5) && definition.CombineMode == BillboardDefinition.DefaultCombineMode)
                    combineAtDefault++;
                if (!definition.Opcodes.Has(5))
                    withoutCombine++;

                if (definition.Opcodes.Has(3) && definition.UnusedByte3 == 0)
                    unusedByteZero++;
                if (!definition.Opcodes.Has(3))
                    withoutUnusedByte++;
            });

            _output.WriteLine($"{combineAtDefault} records store the combine mode at its default, " +
                              $"{withoutCombine} omit it");
            _output.WriteLine($"{unusedByteZero} records store the discarded byte as 0, " +
                              $"{withoutUnusedByte} omit it");

            Assert.True(combineAtDefault > 0 && withoutCombine > 0,
                "the combine mode is either always stored or never stored at its default, so " +
                "absent-versus-default is not exercised here");
            Assert.True(unusedByteZero > 0 && withoutUnusedByte > 0,
                "the discarded byte is either always stored as 0 or never, so absent-versus-default " +
                "is not exercised here");
        }

        /// <summary>
        ///     Every material id a billboard names resolves against the material table in index 26.
        /// </summary>
        /// <remarks>
        ///     The join is checkable rather than plausible: <c>Class260.method11</c> indexes the
        ///     material table directly with this id, so an id past the end of that table is a record
        ///     the client cannot load. Counted as a relationship against the table's own declared
        ///     size, which differs between the two supported caches.
        ///     <para>
        ///     The client's bound is <c>if (i &gt; length) i = length - 1</c>
        ///     (Class260.java:233-240), which should be <c>&gt;=</c> - an id exactly equal to the
        ///     table length falls through and throws. That off-by-one is a defect, not a rule to
        ///     port, so the assertion here is the correct bound.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryMaterialIdIsAddressableInTheMaterialTable()
        {
            RSCache cache = _fixture.OpenCache();
            CacheAddressing materials = CacheAddressing.For(RSConstants.MATERIALS);
            int[] materialFiles = cache.GetFileIds(RSConstants.MATERIALS, materials.SingleGroupId);

            //Index 26 is a single columnar blob, so the count of materials is not the file count -
            //it is the first field of that blob, which the client reads before anything else.
            byte[] table = cache.ReadFileBytes(RSConstants.MATERIALS, materials.SingleGroupId, materialFiles[0]);
            int materialCount = new JagStream(table).ReadUnsignedShort();

            var ids = new SortedSet<int>();
            int withoutMaterial = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.MaterialId < 0)
                {
                    withoutMaterial++;
                    return;
                }

                ids.Add(definition.MaterialId);
                Assert.True(definition.MaterialId < materialCount,
                    $"billboard {record.Id} names material {definition.MaterialId}, past the " +
                    $"{materialCount} the material table declares");
            });

            Assert.True(ids.Count > 0, "no billboard names a material, so the join was not exercised");

            _output.WriteLine($"{ids.Count} distinct material ids, {ids.Min}..{ids.Max}, " +
                              $"against {materialCount} materials; {withoutMaterial} billboards name none");
        }
    }
}
