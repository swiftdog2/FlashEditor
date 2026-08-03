using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Config;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     Decodes every record of the index 2 config families this editor models, requires exact
    ///     buffer consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 2 is thirty-five unrelated families in one index, so each is swept as a single group
    ///     with the file id as the definition id. File ids come from the reference table and never
    ///     from a count: eight of the groups have holes in the middle of their id range.
    ///     <para>
    ///     Ordering is the dominant hazard here and it is worse than anywhere else in the cache.
    ///     <b>Not one</b> of group 36's 1,051 files is in ascending opcode order, across 16 distinct
    ///     orders, and neither is any of group 46's 28. An encoder that walked opcodes 1..n would
    ///     reproduce nothing, and the byte-identity sweep is what says the stored order survived.
    ///     </para>
    ///     <para>See <c>reference/index-architect-02.md</c>.</para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheIndex2ConfigTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheIndex2ConfigTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     A sweep over one index 2 family.
        /// </summary>
        /// <remarks>
        ///     Every family here derives from <see cref="ConfigDefinition"/>, so the decode, encode
        ///     and opcode-listing halves of the codec description are the same three lines each time
        ///     and only the group and the record type change.
        /// </remarks>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="label">Singular noun for one record, used in every failure line.</param>
        /// <param name="groupId">The group holding the family.</param>
        /// <returns>A sweep over the whole family.</returns>
        private DefinitionSweep<T> Family<T>(string label, int groupId) where T : ConfigDefinition, new()
        {
            return new DefinitionSweep<T>(_fixture, _output, RSConstants.CONFIG,
                new DefinitionCodec<T>(label,
                    (id, stream) =>
                    {
                        var definition = new T { Id = id };
                        definition.Decode(stream);
                        return definition;
                    },
                    definition => definition.Encode(),
                    definition => definition.DecodedOpcodes.Select(entry => entry.Opcode)))
                .WithinGroup(groupId);
        }

        /// <summary>Runs both sweeps over a family and reports how many records it held.</summary>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="label">Singular noun for one record.</param>
        /// <param name="groupId">The group holding the family.</param>
        /// <param name="expectedFiles">The file count the reference table declares for it.</param>
        private void AssertFamilyRoundTrips<T>(string label, int groupId, int expectedFiles)
            where T : ConfigDefinition, new()
        {
            DefinitionSweep<T> sweep = Family<T>(label, groupId);

            sweep.AssertExactConsumption();
            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();

            Assert.Equal(expectedFiles, swept.Records);
            Assert.Equal(expectedFiles, swept.Passed);
        }

        /// <summary>
        ///     Group 36, the world map elements. 1,051 files.
        /// </summary>
        /// <remarks>
        ///     The priority family: object definition opcode 107 is a file id in this group, and
        ///     modelling it is what the world map on index 23 waits on.
        /// </remarks>
        [RealCacheFact]
        public void EveryMapElementDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<MapElementDefinition>("map element", ConfigGroup.MapElement, 1051);
        }

        /// <summary>Group 11, the parameter types every opcode 249 block keys off. 1,330 files.</summary>
        [RealCacheFact]
        public void EveryParameterTypeDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<ParameterTypeDefinition>("parameter type", ConfigGroup.ParameterType, 1330);
        }

        /// <summary>Group 5, the item containers. 609 files.</summary>
        [RealCacheFact]
        public void EveryContainerDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<ContainerDefinition>("container", ConfigGroup.Container, 609);
        }

        /// <summary>Group 16, the player variables. 2,002 files, 9 of them non-empty.</summary>
        [RealCacheFact]
        public void EveryVarPlayerDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<VarPlayerDefinition>("varplayer", ConfigGroup.VarPlayer, 2002);
        }

        /// <summary>Group 19, the client variables. 1,445 files.</summary>
        [RealCacheFact]
        public void EveryClientVariableDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<ClientVariableDefinition>("client variable", ConfigGroup.ClientVariable, 1445);
        }

        /// <summary>Group 33, the cursors. 175 files.</summary>
        [RealCacheFact]
        public void EveryCursorDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<CursorDefinition>("cursor", ConfigGroup.Cursor, 175);
        }

        /// <summary>Group 46, the damage marks. 28 files.</summary>
        [RealCacheFact]
        public void EveryDamageMarkDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<DamageMarkDefinition>("damage mark", ConfigGroup.DamageMark, 28);
        }

        /// <summary>
        ///     Group 15, the client strings, every one of which is a bare terminator. 345 files.
        /// </summary>
        /// <remarks>
        ///     The assertion that earns its keep is that the group is <i>still</i> empty. The codec
        ///     throws on any opcode, so a cache that put real bytes in here would fail this test
        ///     rather than pass it with a decoder that guessed.
        /// </remarks>
        [RealCacheFact]
        public void EveryClientStringRecordIsStillEmpty()
        {
            AssertFamilyRoundTrips<EmptyConfigDefinition>("client string", ConfigGroup.ClientString, 345);

            int withOpcodes = 0;
            Family<EmptyConfigDefinition>("client string", ConfigGroup.ClientString)
                .ForEachDecoded((record, definition) =>
                {
                    if (definition.DecodedOpcodes.Count > 0 || record.Bytes.Length != 1)
                        withOpcodes++;
                });

            Assert.Equal(0, withOpcodes);
        }

        /// <summary>
        ///     No map element in the cache stores its opcodes in ascending order.
        /// </summary>
        /// <remarks>
        ///     This is what makes the order-preserving encoder load-bearing rather than a nicety:
        ///     0 of 1,051 files would survive an encoder that walked opcodes 1..n. Measuring it here
        ///     means the byte-identity sweep above cannot be passing for a duller reason than it
        ///     appears to be.
        /// </remarks>
        [RealCacheFact]
        public void NoMapElementStoresItsOpcodesInAscendingOrder()
        {
            int ascending = 0;
            int total = 0;
            var orders = new HashSet<string>();

            Family<MapElementDefinition>("map element", ConfigGroup.MapElement)
                .ForEachDecoded((record, definition) =>
                {
                    total++;
                    int[] opcodes = definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray();
                    orders.Add(string.Join(",", opcodes));

                    bool sorted = true;
                    for (int i = 1; i < opcodes.Length; i++)
                        if (opcodes[i] < opcodes[i - 1])
                            sorted = false;
                    if (sorted)
                        ascending++;
                });

            _output.WriteLine($"{ascending} of {total} map elements are in ascending opcode order, " +
                              $"across {orders.Count} distinct orders");

            Assert.Equal(1051, total);
            Assert.Equal(0, ascending);
        }

        /// <summary>
        ///     Map elements 779 and 780 each store opcode 22 twice, with different values.
        /// </summary>
        /// <remarks>
        ///     The same shape as floor overlay 94's doubled opcode 11, and the only two records in
        ///     this group that exercise it. A decoder that kept only the winning value would write a
        ///     file of the right length and the wrong contents, which the archive CRC covers, so it
        ///     would be a silent corruption rather than an error.
        /// </remarks>
        [RealCacheFact]
        public void TheTwoMapElementsThatRepeatAnOpcodeKeepBothOccurrences()
        {
            RSCache cache = _fixture.OpenCache();

            foreach (int id in new[] { 779, 780 })
            {
                var definition = new MapElementDefinition { Id = id };
                definition.Decode(cache.ReadFile(RSConstants.CONFIG, ConfigGroup.MapElement, id));

                int outlines = definition.DecodedOpcodes.Count(entry => entry.Opcode == 22);
                Assert.Equal(2, outlines);

                //Both occurrences are four-byte payloads and they differ, which is the whole point.
                byte[][] payloads = definition.DecodedOpcodes
                    .Where(entry => entry.Opcode == 22)
                    .Select(entry => entry.Payload)
                    .ToArray();
                Assert.Equal(4, payloads[0].Length);
                Assert.False(payloads[0].AsSpan().SequenceEqual(payloads[1]),
                    "map element " + id + " repeats opcode 22 with the same bytes, so it no longer " +
                    "pins the repeated-opcode path");
            }
        }

        /// <summary>
        ///     Every parameter type letter in the cache decodes, and the string flag agrees with it.
        /// </summary>
        /// <remarks>
        ///     Group 11 is the table CS2 opcode 6804 keys a param block against, and
        ///     <c>Class149.isString</c> is exactly "the type letter is 's'". This checks the half of
        ///     that join which needs no other group: that the letter is recoverable from the stored
        ///     byte at all, which the cp1252 remap makes a real question - one record here stores
        ///     0x80.
        /// </remarks>
        [RealCacheFact]
        public void EveryParameterTypeLetterDecodes()
        {
            var letters = new SortedDictionary<char, int>();
            int stringTypes = 0;
            int aboveAscii = 0;

            Family<ParameterTypeDefinition>("parameter type", ConfigGroup.ParameterType)
                .ForEachDecoded((record, definition) =>
                {
                    if (!definition.Has(1))
                        return;

                    letters.TryGetValue(definition.TypeLetter, out int seen);
                    letters[definition.TypeLetter] = seen + 1;

                    if (definition.IsString)
                        stringTypes++;
                    if (definition.TypeLetterByte > 127)
                        aboveAscii++;
                });

            _output.WriteLine("parameter type letters: " +
                              string.Join(", ", letters.Select(entry => $"{entry.Key}={entry.Value}")));

            //Measured: 442 records carry opcode 1, 59 of them typed 's', and exactly one stores a
            //byte above 127. A remap that silently mangled the high band would move that last count.
            Assert.Equal(59, stringTypes);
            Assert.Equal(1, aboveAscii);
        }

        /// <summary>
        ///     26 damage marks carry the bare <c>"%1"</c> template and one carries an empty string.
        /// </summary>
        /// <remarks>
        ///     <c>gjstr2</c> is the only string form in these families that carries a leading version
        ///     byte, and dropping it is a one-byte error the exact-consumption sweep would catch but
        ///     nothing else would explain. 27 of the 28 records carry opcode 8; file 8 carries none.
        ///     <para>
        ///     File 22's template is <b>empty</b>, so its opcode 8 payload is the two bytes
        ///     <c>00 00</c> - version byte then bare terminator. That record is the one that makes
        ///     this test sharp rather than decorative: it is the only place in the group where the
        ///     version byte is not followed by text, so a reader that skipped the version byte would
        ///     still produce <c>"%1"</c> for the other 26 and would run off the end here. Asserting a
        ///     uniform <c>"%1"</c> across all 27 would therefore assert away the single record that
        ///     tests the encoding.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryDamageMarkTemplateIsTheBareSubstitutionExceptOneEmptyOne()
        {
            var templatesById = new SortedDictionary<int, string>();

            Family<DamageMarkDefinition>("damage mark", ConfigGroup.DamageMark)
                .ForEachDecoded((record, definition) =>
                {
                    if (definition.Has(8))
                        templatesById[definition.Id] = definition.NumberTemplate;
                });

            _output.WriteLine("damage mark templates: " + string.Join(", ",
                templatesById.Select(entry => $"{entry.Key}={entry.Value}")));

            Assert.Equal(27, templatesById.Count);
            Assert.DoesNotContain(8, templatesById.Keys);
            Assert.Equal("", templatesById[22]);
            Assert.Equal(26, templatesById.Count(entry => entry.Value == "%1"));
            Assert.All(templatesById.Where(entry => entry.Key != 22),
                entry => Assert.Equal("%1", entry.Value));
        }
    }
}
