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
    ///     Sweeps the index 2 config families that had no codec until now: identity kits, structs,
    ///     light intensity curves, render animations, quests, and the nineteen groups no class in the
    ///     637 client opens.
    /// </summary>
    /// <remarks>
    ///     Every count comes from the reference table rather than from a literal. Index 2 declares the
    ///     same groups and files in both supported caches, but a literal would still be a number a
    ///     reader could mistake for a target, and the assertion that matters - "every file the table
    ///     declares was decoded and came back byte-identical" - does not need one.
    ///     <para>
    ///     Ordering is the dominant hazard in this index and it is worse in these five families than
    ///     in the ones already modelled: not one of the four light intensity curves is in ascending
    ///     opcode order, 184 of the 187 quests are not, and 579 of the 1,972 render animations are
    ///     not. The byte-identity sweep is what says the stored order survived.
    ///     </para>
    ///     <para>See <c>reference/index-architect-02.md</c>.</para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheIndex2RemainingConfigTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheIndex2RemainingConfigTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>How many files index 2's reference table declares for a group.</summary>
        /// <param name="groupId">The group id.</param>
        /// <returns>The declared file count.</returns>
        private int Declared(int groupId)
        {
            int[] fileIds = _fixture.Table(RSConstants.CONFIG).GetArchiveEntry(groupId)?.GetValidFileIds();
            return fileIds?.Length ?? 0;
        }

        /// <summary>A sweep over one index 2 family.</summary>
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

        /// <summary>
        ///     Runs both sweeps over a family and requires them to cover every declared file.
        /// </summary>
        /// <remarks>
        ///     The count comes from the reference table, and it is asserted to be above zero as well
        ///     as to match: a group the table declared as empty would otherwise let both sweeps pass
        ///     having examined nothing.
        /// </remarks>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="label">Singular noun for one record.</param>
        /// <param name="groupId">The group holding the family.</param>
        private void AssertFamilyRoundTrips<T>(string label, int groupId)
            where T : ConfigDefinition, new()
        {
            int declared = Declared(groupId);
            Assert.True(declared > 0,
                $"index 2 group {groupId} declares no files, so the {label} sweep would check nothing");

            DefinitionSweep<T> sweep = Family<T>(label, groupId);

            DefinitionSweepResult consumed = sweep.AssertExactConsumption();
            Assert.Equal(declared, consumed.Records);
            Assert.Equal(declared, consumed.Passed);

            DefinitionSweepResult swept = sweep.AssertReEncodesToCapturedBytes();
            Assert.Equal(declared, swept.Records);
            Assert.Equal(declared, swept.Passed);
        }

        /// <summary>
        ///     Group 3, the identity kits the player appearance is built from.
        /// </summary>
        /// <remarks>
        ///     Opcode 1's byte is read and discarded by the client, so it exists nowhere but in the
        ///     stored bytes, and it occurs on every record - a decoder that dropped it would fail this
        ///     sweep on every file in the group.
        /// </remarks>
        [RealCacheFact]
        public void EveryIdentityKitDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<IdentityKitDefinition>("identity kit", ConfigGroup.IdentityKit);
        }

        /// <summary>Group 26, the structs CS2 reads parameters out of.</summary>
        [RealCacheFact]
        public void EveryStructDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<StructDefinition>("struct", ConfigGroup.Struct);
        }

        /// <summary>Group 31, the light intensity curves.</summary>
        [RealCacheFact]
        public void EveryLightIntensityCurveDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<LightIntensityDefinition>("light curve", ConfigGroup.LightIntensity);
        }

        /// <summary>Group 32, the render animation sets. The largest of the five.</summary>
        [RealCacheFact]
        public void EveryRenderAnimationDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<RenderAnimationDefinition>("render animation", ConfigGroup.RenderAnimation);
        }

        /// <summary>Group 35, the quests.</summary>
        [RealCacheFact]
        public void EveryQuestDecodesAndRoundTrips()
        {
            AssertFamilyRoundTrips<QuestDefinition>("quest", ConfigGroup.Quest);
        }

        /// <summary>
        ///     The nineteen groups with no client provider hold nothing but bare terminators.
        /// </summary>
        /// <remarks>
        ///     The assertion that earns its keep is that they are <i>still</i> empty.
        ///     <see cref="EmptyConfigDefinition"/> throws on any opcode, so a cache that put real
        ///     bytes into one of these groups fails here rather than passing with a decoder that
        ///     guessed at a table - which is the only honest thing to assert about a group whose
        ///     format cannot be recovered from 639 data at all.
        /// </remarks>
        [RealCacheFact]
        public void EveryProviderlessGroupIsStillEmpty()
        {
            int groups = 0;
            int records = 0;
            int nonEmpty = 0;

            foreach (int group in ConfigGroup.EmptyProviderless)
            {
                AssertFamilyRoundTrips<EmptyConfigDefinition>("group " + group + " record", group);
                groups++;

                Family<EmptyConfigDefinition>("group " + group + " record", group)
                    .ForEachDecoded((record, definition) =>
                    {
                        records++;
                        if (definition.DecodedOpcodes.Count > 0 || record.Bytes.Length != 1)
                            nonEmpty++;
                    });
            }

            _output.WriteLine($"{records} records across {groups} provider-less groups, " +
                              $"{nonEmpty} of them carrying anything at all");

            Assert.Equal(ConfigGroup.EmptyProviderless.Count, groups);
            Assert.True(records > 0, "no provider-less record was read, so nothing was checked");
            Assert.Equal(0, nonEmpty);
        }

        /// <summary>
        ///     Every group index 2's reference table declares has a family, and no family names a
        ///     group the table does not declare.
        /// </summary>
        /// <remarks>
        ///     Both halves matter. A declared group with no family would silently fall to
        ///     <see cref="ConfigFamily.Unmodelled"/> and be reported as unreadable rather than as
        ///     missing; a family for a group the cache does not hold would be a codec nothing ever
        ///     runs, which is how groups 29 and 30 came to be listed as client providers in the first
        ///     place - two of the eighteen name groups absent from this cache.
        /// </remarks>
        [RealCacheFact]
        public void EveryDeclaredConfigGroupHasAFamily()
        {
            int[] declared = _fixture.Table(RSConstants.CONFIG).GetArchiveEntries().Keys
                .OrderBy(group => group).ToArray();
            int[] modelled = ConfigFamily.Modelled.Select(family => family.GroupId)
                .OrderBy(group => group).ToArray();

            _output.WriteLine("index 2 declares groups " + string.Join(", ", declared));

            Assert.NotEmpty(declared);
            Assert.Equal(declared, modelled);
            Assert.All(declared, group => Assert.True(ConfigFamily.For(group).IsModelled,
                "index 2 group " + group + " has no codec"));
        }

        /// <summary>
        ///     Not one light intensity curve stores its opcodes in ascending order.
        /// </summary>
        /// <remarks>
        ///     Four records, all four storing 3, 2, 4, 1. It is the smallest case in the cache that an
        ///     encoder walking opcodes 1..n would fail on, which makes it the cheapest statement that
        ///     the order-preserving encoder is load-bearing rather than decorative.
        /// </remarks>
        [RealCacheFact]
        public void NoLightIntensityCurveStoresItsOpcodesInAscendingOrder()
        {
            int ascending = 0;
            int total = 0;
            var orders = new SortedSet<string>();

            Family<LightIntensityDefinition>("light curve", ConfigGroup.LightIntensity)
                .ForEachDecoded((record, definition) =>
                {
                    total++;
                    int[] opcodes = definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray();
                    orders.Add(string.Join(",", opcodes));
                    if (IsAscending(opcodes))
                        ascending++;
                });

            _output.WriteLine($"{ascending} of {total} light curves are in ascending opcode order; " +
                              "orders seen: " + string.Join(" | ", orders));

            Assert.Equal(Declared(ConfigGroup.LightIntensity), total);
            Assert.Equal(0, ascending);
        }

        /// <summary>
        ///     The two render animations that repeat an opcode keep both occurrences.
        /// </summary>
        /// <remarks>
        ///     File 1205 stores opcode 6 twice and file 1799 stores opcodes 38 and 39 twice each,
        ///     interleaved. They are the only two records in the group that exercise repetition, so a
        ///     decoder that kept only the winning value would write files of the right length and the
        ///     wrong contents and the sweep would be the only thing that noticed.
        /// </remarks>
        [RealCacheFact]
        public void TheTwoRenderAnimationsThatRepeatAnOpcodeKeepBothOccurrences()
        {
            var repeated = new SortedDictionary<int, string>();

            Family<RenderAnimationDefinition>("render animation", ConfigGroup.RenderAnimation)
                .ForEachDecoded((record, definition) =>
                {
                    int[] opcodes = definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray();
                    if (opcodes.Length != opcodes.Distinct().Count())
                        repeated[definition.Id] = string.Join(",", opcodes);
                });

            _output.WriteLine("render animations repeating an opcode: " +
                              string.Join(" | ", repeated.Select(entry => entry.Key + " = " + entry.Value)));

            Assert.Equal(new[] { 1205, 1799 }, repeated.Keys.ToArray());
            Assert.Equal("38,39,40,41,42,6,6,8,9,1", repeated[1205]);
            Assert.Equal("38,39,38,39,40,41,42,6,1", repeated[1799]);
        }

        /// <summary>
        ///     Both halves of a render animation's opcode 1 use the 65535 sentinel, and it survives.
        /// </summary>
        /// <remarks>
        ///     A stored 65535 decodes to -1 in both shorts, and -1 has exactly one encoding, so the
        ///     alias only round-trips while the encoder writes the sentinel back rather than
        ///     truncating a -1 into the field. This measures that both halves are exercised, so the
        ///     byte-identity sweep above is defending the rule rather than never meeting it.
        /// </remarks>
        [RealCacheFact]
        public void BothHalvesOfARenderAnimationsFirstOpcodeUseTheSentinel()
        {
            int idleSentinels = 0;
            int moveSentinels = 0;
            int carrying = 0;

            Family<RenderAnimationDefinition>("render animation", ConfigGroup.RenderAnimation)
                .ForEachDecoded((record, definition) =>
                {
                    if (!definition.Has(1))
                        return;

                    carrying++;
                    if (definition.IdleAnimationId == -1)
                        idleSentinels++;
                    if (definition.MoveForwardAnimationId == -1)
                        moveSentinels++;
                });

            _output.WriteLine($"{carrying} render animations carry opcode 1; {idleSentinels} store the " +
                              $"sentinel in its first short and {moveSentinels} in its second");

            Assert.Equal(Declared(ConfigGroup.RenderAnimation), carrying);
            Assert.True(idleSentinels > 0, "no render animation stores 65535 in opcode 1's first short");
            Assert.True(moveSentinels > 0, "no render animation stores 65535 in opcode 1's second short");
        }

        /// <summary>
        ///     Six structs carry the same parameter key twice, and both entries survive.
        /// </summary>
        /// <remarks>
        ///     This is what makes <see cref="ConfigParameters"/>' ordered list load-bearing on real
        ///     data rather than only on a hand-built record. The client's own store keeps the
        ///     <i>first</i> occurrence (InterfaceConfig.java:125), so folding a block into a
        ///     dictionary would drop the loser and reorder the survivors - the file would come back
        ///     shorter, which the byte-identity sweep catches, but only this test says why.
        /// </remarks>
        [RealCacheFact]
        public void TheStructsThatRepeatAParameterKeyKeepBothEntries()
        {
            var duplicated = new SortedDictionary<int, string>();
            int blocks = 0;
            int entries = 0;

            Family<StructDefinition>("struct", ConfigGroup.Struct)
                .ForEachDecoded((record, definition) =>
                {
                    if (definition.Parameters.Count == 0)
                        return;

                    blocks++;
                    entries += definition.Parameters.Count;

                    int[] keys = definition.Parameters.Select(parameter => parameter.Key).ToArray();
                    if (keys.Length != keys.Distinct().Count())
                        duplicated[definition.Id] = string.Join(",",
                            keys.GroupBy(key => key).Where(group => group.Count() > 1)
                                .Select(group => group.Key).OrderBy(key => key));
                });

            _output.WriteLine($"{blocks} structs carry a parameter block holding {entries} entries; " +
                              "the ones with a repeated key: " +
                              string.Join(" | ", duplicated.Select(entry => entry.Key + " = " + entry.Value)));

            Assert.Equal(new[] { 951, 973, 1330, 1337, 1342, 1450 }, duplicated.Keys.ToArray());
            Assert.Equal("859", duplicated[951]);
            Assert.Equal("1296,1297", duplicated[1330]);
        }

        /// <summary>
        ///     Every struct parameter key is a live file id in the parameter type table, and the
        ///     per-entry string flag agrees with that record's type letter.
        /// </summary>
        /// <remarks>
        ///     A self-proving join rather than an aggregate that merely looks plausible: the flag is
        ///     redundant with the keyed type, so a wrong key space would show up as disagreements
        ///     rather than as a lower coverage figure. Both are asserted at zero, and the entry count
        ///     is asserted above zero so a run that read nothing cannot pass.
        /// </remarks>
        [RealCacheFact]
        public void EveryStructParameterKeyNamesAParameterTypeOfTheRightKind()
        {
            var isString = new Dictionary<int, bool>();

            Family<ParameterTypeDefinition>("parameter type", ConfigGroup.ParameterType)
                .ForEachDecoded((record, definition) => isString[definition.Id] = definition.IsString);

            int entries = 0;
            var keys = new SortedSet<int>();
            var unknownKeys = new SortedSet<int>();
            int disagreements = 0;

            Family<StructDefinition>("struct", ConfigGroup.Struct)
                .ForEachDecoded((record, definition) =>
                {
                    foreach (ConfigParameter parameter in definition.Parameters)
                    {
                        entries++;
                        keys.Add(parameter.Key);

                        if (!isString.TryGetValue(parameter.Key, out bool declaredString))
                            unknownKeys.Add(parameter.Key);
                        else if (declaredString != parameter.IsString)
                            disagreements++;
                    }
                });

            _output.WriteLine($"{entries} struct parameter entries under {keys.Count} distinct keys");

            Assert.True(entries > 0, "no struct parameter entry was read, so nothing was checked");
            Assert.Empty(unknownKeys);
            Assert.Equal(0, disagreements);
        }

        /// <summary>
        ///     Every quest carries a name, and the names decode to the quests they are.
        /// </summary>
        /// <remarks>
        ///     This is what settles the group's identity, and it settles it from the bytes rather than
        ///     from a name in a document: file 6 decodes to "Cook's Assistant" in both caches. It is
        ///     also the sharpest available test of the <c>gjstr2</c> reader in this family, because a
        ///     reader that skipped the leading version byte would produce a string one character
        ///     short of every one of these.
        /// </remarks>
        [RealCacheFact]
        public void EveryQuestIsNamedAndTheNamesAreQuestNames()
        {
            var named = new SortedDictionary<int, string>();

            Family<QuestDefinition>("quest", ConfigGroup.Quest)
                .ForEachDecoded((record, definition) =>
                {
                    if (definition.Has(1))
                        named[definition.Id] = definition.Name;
                });

            _output.WriteLine("first quests: " + string.Join(", ",
                named.Take(8).Select(entry => entry.Key + "=" + entry.Value)));

            Assert.Equal(Declared(ConfigGroup.Quest), named.Count);
            Assert.Equal("Cook's Assistant", named[6]);
            Assert.Equal("Witch's House", named[7]);
            Assert.All(named.Values, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        }

        /// <summary>
        ///     Every quest chat icon names a live sprite group in index 8.
        /// </summary>
        /// <remarks>
        ///     Opcode 17 is the one field of a quest the 637 client reads back, and it reads it as a
        ///     group id in index 8. Checking it against index 8's own reference table is what turns
        ///     "the values look like sprite ids" into a statement, and a mis-sized payload would move
        ///     the values well outside that range.
        /// </remarks>
        [RealCacheFact]
        public void EveryQuestChatIconNamesASpriteGroup()
        {
            var sprites = new SortedSet<int>(_fixture.Table(RSConstants.SPRITES_INDEX).GetArchiveEntries().Keys);
            var dangling = new SortedSet<int>();
            int carrying = 0;

            Family<QuestDefinition>("quest", ConfigGroup.Quest)
                .ForEachDecoded((record, definition) =>
                {
                    if (!definition.Has(17))
                        return;

                    carrying++;
                    if (!sprites.Contains(definition.IconSpriteId))
                        dangling.Add(definition.IconSpriteId);
                });

            _output.WriteLine($"{carrying} quests carry a chat icon, against index 8's {sprites.Count} groups");

            Assert.True(carrying > 0, "no quest carries a chat icon, so nothing was checked");
            Assert.Empty(dangling);
        }

        /// <summary>Whether an opcode sequence never steps backwards.</summary>
        /// <param name="opcodes">The sequence, in stored order.</param>
        /// <returns>Whether an encoder walking opcodes 1..n would reproduce it.</returns>
        private static bool IsAscending(int[] opcodes)
        {
            for (int i = 1; i < opcodes.Length; i++)
                if (opcodes[i] < opcodes[i - 1])
                    return false;
            return true;
        }
    }
}
