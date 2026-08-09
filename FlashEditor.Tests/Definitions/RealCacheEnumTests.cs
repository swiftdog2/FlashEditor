using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Cache.Util;
using FlashEditor.Definitions.Enums;
using FlashEditor.Definitions.Tracks;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Decodes every enum the index-17 reference table declares, requires exact buffer
    ///     consumption, and requires each one to re-encode to the bytes it came from.
    /// </summary>
    /// <remarks>
    ///     Index 17 is the client's enum table despite the constant naming it
    ///     <c>CLIENTSCRIPT_SETTINGS</c>; the client's own field is <c>enumFileStore</c>
    ///     (Node_Sub10_Sub24.java:9). An enum id splits 256 to a group, so the sweep addresses it
    ///     through <see cref="CacheAddressing"/> like any other paged index.
    ///     <para>
    ///     The interesting property this pins is the opcode order. Only a handful of orders occur
    ///     and every one of them writes the default after the table, which is the opposite of what
    ///     an ascending-order encoder produces - so the byte-identity sweep here is really a test of
    ///     whether the recorded stream is being replayed at all.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheEnumTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheEnumTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-17 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.CLIENTSCRIPT_SETTINGS);

        /// <summary>Files the index-17 reference table declares across every group.</summary>
        private int EnumsDeclared => _fixture.DeclaredFiles(RSConstants.CLIENTSCRIPT_SETTINGS);

        /// <summary>
        ///     The enum index bound to the production codec.
        /// </summary>
        /// <remarks>
        ///     Every group rather than the sample. The whole index decompresses to well under a
        ///     megabyte, and the assertions below are relationships against what the reference table
        ///     declares - a claim a sample cannot make.
        /// </remarks>
        /// <returns>A sweep over every declared enum.</returns>
        private DefinitionSweep<EnumDefinition> Sweep()
        {
            return new DefinitionSweep<EnumDefinition>(_fixture, _output, RSConstants.CLIENTSCRIPT_SETTINGS,
                new DefinitionCodec<EnumDefinition>("enum",
                    (id, stream) => new EnumDefinition { Id = id }.Decode(stream),
                    definition => definition.Encode(),
                    definition => definition.Opcodes.Select(record => record.Opcode)))
                .AcrossEveryGroup();
        }

        /// <summary>Every declared enum decodes and finishes on the last byte of its file.</summary>
        /// <remarks>
        ///     Sharp because the harness decodes a padded copy too. Two payloads here are sized by
        ///     something the file states rather than by the opcode - the table's entry count, and
        ///     each string's terminator - so a mis-sized read lands somewhere other than the file's
        ///     end.
        /// </remarks>
        [RealCacheFact]
        public void EveryEnum_DecodesAndConsumesItsBufferExactly()
        {
            DefinitionSweepResult swept = Sweep().AssertExactConsumption();

            Assert.True(EnumsDeclared > 0, "index 17 declares no files, so nothing was checked");
            Assert.Equal(GroupsDeclared, swept.Groups);
            Assert.Equal(EnumsDeclared, swept.Records);
            Assert.Equal(EnumsDeclared, swept.Passed);
        }

        /// <summary>Every declared enum re-encodes to the bytes it was decoded from.</summary>
        [RealCacheFact]
        public void EveryEnum_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.True(EnumsDeclared > 0, "index 17 declares no files, so nothing was checked");
            Assert.Equal(EnumsDeclared, swept.Records);
            Assert.Equal(EnumsDeclared, swept.Passed);
            Assert.Equal(0, swept.Reordered);
        }

        /// <summary>The encoder's own output decodes back to something that encodes identically.</summary>
        [RealCacheFact]
        public void EveryEnum_EncodeIsAFixedPointOfDecode()
        {
            Sweep().AssertEncodeIsAFixedPointOfDecode();
        }

        /// <summary>
        ///     The default is written after the table in every enum that carries both, which is why
        ///     the recorded opcode order cannot be replaced by an ascending one.
        /// </summary>
        /// <remarks>
        ///     Asserted as a relationship rather than as a count of matching enums, so it holds in
        ///     any cache: whatever the population, not one record may put opcode 3 or 4 in front of
        ///     opcode 5 or 6. The population is asserted to be non-zero separately, so a cache with
        ///     no such enum cannot pass the property vacuously.
        /// </remarks>
        [RealCacheFact]
        public void TheDefaultIsAlwaysWrittenAfterTheTable()
        {
            int withBoth = 0;
            int ascending = 0;
            var orders = new SortedDictionary<string, int>();

            Sweep().ForEachDecoded((record, definition) =>
            {
                int[] opcodes = definition.Opcodes.Select(entry => entry.Opcode).ToArray();
                if (opcodes.Length == 0)
                    return;

                string order = string.Join(",", opcodes);
                orders.TryGetValue(order, out int seen);
                orders[order] = seen + 1;

                int table = LastIndexOfAny(opcodes, 5, 6);
                int fallback = LastIndexOfAny(opcodes, 3, 4);
                if (table < 0 || fallback < 0)
                    return;

                withBoth++;
                Assert.True(fallback > table,
                    $"enum {record.Id} (group {record.GroupId} file {record.FileId}) writes its default " +
                    $"before its table, order [{order}] - the encoder's assumption that the default " +
                    "comes last no longer holds");

                if (opcodes.SequenceEqual(opcodes.OrderBy(opcode => opcode)))
                    ascending++;
            });

            foreach (KeyValuePair<string, int> order in orders)
                _output.WriteLine($"opcode order [{order.Key}]: {order.Value}");

            Assert.True(withBoth > 0,
                "no enum carries both a table and a default, so the ordering property was not exercised");

            //An ascending-order encoder would have to reproduce these, and it reproduces none of
            //them. Stated as a measurement so a cache where the order did become canonical is
            //visible rather than silently changing what the sweep proves.
            Assert.Equal(0, ascending);
        }

        /// <summary>
        ///     Unallocated enum slots and populated enums together account for every declared file.
        /// </summary>
        /// <remarks>
        ///     Most of this index is a single terminator byte. Treating those as "no enum here" and
        ///     skipping them would pass a decode sweep and lose most of the index the moment a group
        ///     is written back, so the split is asserted to partition the declared population
        ///     exactly rather than being counted against a literal.
        /// </remarks>
        [RealCacheFact]
        public void UnallocatedSlotsAndPopulatedEnumsPartitionTheIndex()
        {
            int empty = 0;
            int populated = 0;
            int stringTables = 0;
            int intTables = 0;
            int bothTables = 0;
            int entries = 0;
            var keyTypes = new SortedDictionary<int, int>();
            var valueTypes = new SortedDictionary<int, int>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.IsEmpty)
                {
                    empty++;
                    return;
                }

                populated++;
                entries += definition.Entries.Count;
                if (definition.Opcodes.Has(5))
                    stringTables++;
                if (definition.Opcodes.Has(6))
                    intTables++;
                if (definition.Opcodes.Has(5) && definition.Opcodes.Has(6))
                    bothTables++;

                Count(keyTypes, definition.KeyTypeByte);
                Count(valueTypes, definition.ValueTypeByte);
            });

            _output.WriteLine($"{populated} populated enums holding {entries} entries, {empty} unallocated slots");
            _output.WriteLine($"{stringTables} string tables, {intTables} int tables");
            _output.WriteLine("key type bytes: " + Histogram(keyTypes));
            _output.WriteLine("value type bytes: " + Histogram(valueTypes));

            Assert.Equal(EnumsDeclared, swept.Records);
            Assert.Equal(EnumsDeclared, empty + populated);
            Assert.True(empty > 0, "no unallocated slot was seen, so the bare-terminator path is untested");
            Assert.True(populated > 0, "no populated enum was seen, so nothing about the format was tested");
            Assert.True(stringTables > 0 && intTables > 0,
                "one of the two table shapes is absent, so half the table codec is untested");

            //A table is a string table or an int table, never both. The value shape follows from
            //which opcode carried it, so a record holding both would leave ValuesAreStrings
            //describing only whichever came last - and the other table replayed from its own bytes,
            //unreachable from the fields.
            Assert.Equal(0, bothTables);
        }

        /// <summary>
        ///     A value type byte outside ASCII occurs, so the type must be kept as a byte.
        /// </summary>
        /// <remarks>
        ///     Round-tripping a type through <c>char</c> is the avoidable way to lose one: the
        ///     client's mapping sends 0x80-0x9F through cp1252 and turns its unassigned slots into
        ///     '?', which cannot be inverted. The byte-identity sweep would catch that, but only for
        ///     as long as such a byte is in the cache - this states the requirement directly.
        /// </remarks>
        [RealCacheFact]
        public void TypeBytesSurviveAsBytesRatherThanCharacters()
        {
            int nonAscii = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.IsEmpty)
                    return;

                if (definition.KeyTypeByte > 127 || definition.ValueTypeByte > 127)
                    nonAscii++;

                //Whatever the byte, re-encoding must reproduce it - the char is display only.
                var reread = new EnumDefinition { Id = record.Id }.Decode(new JagStream(definition.Encode().ToArray()));
                Assert.Equal(definition.KeyTypeByte, reread.KeyTypeByte);
                Assert.Equal(definition.ValueTypeByte, reread.ValueTypeByte);
            });

            _output.WriteLine($"{nonAscii} enums carry a type byte above 0x7F");
            Assert.True(nonAscii > 0,
                "no enum carries a type byte above 0x7F, so nothing here exercises the remap");
        }

        /// <summary>
        ///     The music-track name enum still decodes through the general codec, and its values are
        ///     what the track join hashes.
        /// </summary>
        /// <remarks>
        ///     <c>TrackNames</c> keys by the hash of each <em>value</em>, not by the enum key, and
        ///     the reason is measured rather than assumed: the keys look like index-6 group ids and
        ///     are not - the values are in alphabetical order, so the key is the music player's list
        ///     position. Generalising the enum decoder must not tempt anyone into "fixing" that, so
        ///     the falsifying case is pinned here. Group 0's identifier is the hash of
        ///     "scape main", and this enum holds that name against a key that is not 0.
        /// </remarks>
        [RealCacheFact]
        public void TheMusicNameEnumIsKeyedByListPositionRatherThanByGroupId()
        {
            RSCache cache = _fixture.OpenCache();
            CacheAddressing addressing = CacheAddressing.For(RSConstants.CLIENTSCRIPT_SETTINGS);
            int id = TrackNames.MusicNameEnumId;

            JagStream stored = cache.ReadFile(RSConstants.CLIENTSCRIPT_SETTINGS,
                addressing.GroupOf(id), addressing.FileOf(id));
            var definition = new EnumDefinition { Id = id }.Decode(stored);

            _output.WriteLine($"enum {id}: key type 0x{definition.KeyTypeByte:X2}, value type " +
                              $"0x{definition.ValueTypeByte:X2}, {definition.Entries.Count} entries");

            Assert.True(definition.ValuesAreStrings, "the track name enum does not hold strings");
            Assert.True(definition.Entries.Count > 0, "the track name enum is empty");

            EnumEntry scapeMain = definition.Entries.First(entry =>
                string.Equals(entry.Text, "Scape Main", StringComparison.Ordinal));

            //Index 6 group 0's stored identifier is the hash of this name, so the join by hash is
            //self-proving. Its enum key is not 0, which is the single row that falsifies the
            //plausible-looking alternative of keying by group id.
            Assert.NotEqual(0, scapeMain.Key);
            Assert.Equal(
                cache.GetReferenceTable(RSConstants.MUSIC_INDEX).GetArchiveEntry(0).GetIdentifier(),
                NameHasher.GetNameHash(scapeMain.Text));
        }

        private static int LastIndexOfAny(int[] opcodes, int first, int second)
        {
            for (int i = opcodes.Length - 1; i >= 0; i--)
                if (opcodes[i] == first || opcodes[i] == second)
                    return i;
            return -1;
        }

        private static void Count(SortedDictionary<int, int> counts, int value)
        {
            counts.TryGetValue(value, out int seen);
            counts[value] = seen + 1;
        }

        private static string Histogram(SortedDictionary<int, int> counts)
        {
            return string.Join(", ", counts.Select(entry => $"0x{entry.Key:X2}={entry.Value}"));
        }
    }
}
