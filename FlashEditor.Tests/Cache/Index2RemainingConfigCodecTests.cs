using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Config;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     The hazards in index 2's five newly modelled families that no sweep over either cache can
    ///     see, because the input that triggers them is absent from both.
    /// </summary>
    /// <remarks>
    ///     A sweep only exercises the encodings the shipped files happen to use. Seventeen of a render
    ///     animation's opcodes occur in no file, nine of the ten identity kit head-model opcodes do
    ///     not, and four of a quest's do not; the two opcodes a quest's own dispatcher fails to name
    ///     cannot appear in a shipped file at all. Every rule that depends on one of those is
    ///     unverifiable against the cache and is pinned here instead. Nothing in this file reads a
    ///     cache.
    /// </remarks>
    public sealed class Index2RemainingConfigCodecTests
    {
        /// <summary>Builds a record's bytes from a readable list of byte values.</summary>
        /// <param name="values">The bytes, each 0..255.</param>
        /// <returns>The record.</returns>
        private static byte[] Record(params int[] values)
        {
            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
                bytes[i] = (byte)values[i];
            return bytes;
        }

        /// <summary>Decodes a record, requires it to consume exactly, and encodes it straight back.</summary>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="stored">The stored bytes.</param>
        /// <param name="definition">The decoded definition.</param>
        /// <returns>What the encoder wrote.</returns>
        private static byte[] RoundTrip<T>(byte[] stored, out T definition) where T : ConfigDefinition, new()
        {
            definition = new T { Id = 0 };
            var stream = new JagStream(stored);
            definition.Decode(stream);

            Assert.Equal(stored.Length, stream.Position);
            return definition.Encode().ToArray();
        }

        /// <summary>Round trips a record holding one opcode and nothing else.</summary>
        /// <typeparam name="T">The definition type.</typeparam>
        /// <param name="opcode">The opcode under test.</param>
        /// <param name="payload">Its payload bytes.</param>
        private static void AssertOpcodeRoundTrips<T>(int opcode, byte[] payload)
            where T : ConfigDefinition, new()
        {
            var stored = new List<byte> { (byte)opcode };
            stored.AddRange(payload);
            stored.Add(0);

            byte[] written = RoundTrip(stored.ToArray(), out T definition);

            Assert.Equal(stored.ToArray(), written);
            Assert.Equal(new[] { opcode },
                definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray());
        }

        // =================================================================== identity kits

        /// <summary>
        ///     An identity kit's opcode 1 byte survives even though the client throws it away.
        /// </summary>
        /// <remarks>
        ///     Class152.java:257 reads it into nothing, so no field of the client's record holds it
        ///     and there is nothing to recompute it from. It occurs on every record in the cache, so
        ///     dropping it would fail the sweep on all of them - this states why.
        /// </remarks>
        [Fact]
        public void AnIdentityKitKeepsTheByteTheClientDiscards()
        {
            byte[] written = RoundTrip(Record(1, 13, 0), out IdentityKitDefinition definition);

            Assert.Equal(Record(1, 13, 0), written);
            Assert.Equal(13, definition.Unknown1);
        }

        /// <summary>
        ///     All ten head-model opcodes round trip, not just the one the cache uses.
        /// </summary>
        /// <remarks>
        ///     The client's array is <c>int[5]</c> while its dispatcher accepts opcodes 60 to 69, so
        ///     65 to 69 would throw <c>ArrayIndexOutOfBounds</c> there. Only opcode 60 occurs in
        ///     either cache, so the defect is latent; this decoder carries ten slots so that a record
        ///     using the upper five could still be read and written back unchanged, and that decision
        ///     is only testable here.
        /// </remarks>
        [Fact]
        public void EveryHeadModelOpcodeRoundTrips()
        {
            for (int opcode = 60; opcode <= 69; opcode++)
                AssertOpcodeRoundTrips<IdentityKitDefinition>(opcode, Record(0x01, 0x02));

            byte[] stored = Record(69, 0x0A, 0x0B, 60, 0x00, 0x07, 0);
            byte[] written = RoundTrip(stored, out IdentityKitDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(7, definition.HeadModelIds[0]);
            Assert.Equal(0x0A0B, definition.HeadModelIds[9]);
            Assert.Equal(IdentityKitDefinition.HeadModelSlots, definition.HeadModelIds.Length);
        }

        /// <summary>An identity kit's colour tables round trip, high values included.</summary>
        /// <remarks>
        ///     Both tables are read as unsigned shorts and stored as signed <c>short</c>, exactly as
        ///     the client does, so a value above 32767 comes back negative and has to be written back
        ///     as the same two bytes.
        /// </remarks>
        [Fact]
        public void IdentityKitColourTablesRoundTrip()
        {
            byte[] stored = Record(
                40, 2, 0xFF, 0x9C, 0x00, 0x2A, 0x12, 0x34, 0x56, 0x78,
                41, 1, 0x80, 0x00, 0x7F, 0xFF,
                0);

            byte[] written = RoundTrip(stored, out IdentityKitDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(new short[] { unchecked((short)0xFF9C), 0x1234 }, definition.RecolourFrom);
            Assert.Equal(new short[] { 0x002A, 0x5678 }, definition.RecolourTo);
            Assert.Equal(new short[] { unchecked((short)0x8000) }, definition.RetextureFrom);
            Assert.Equal(new short[] { 0x7FFF }, definition.RetextureTo);
        }

        /// <summary>A colour table whose two halves disagree is refused rather than truncated.</summary>
        [Fact]
        public void AnIdentityKitColourTableWithMismatchedHalvesIsRefused()
        {
            var definition = new IdentityKitDefinition { Id = 0 };
            definition.Decode(new JagStream(Record(0)));
            definition.RecolourFrom = new short[] { 1, 2 };
            definition.RecolourTo = new short[] { 1 };

            Assert.ThrowsAny<Exception>(() => definition.Encode());
        }

        /// <summary>Every opcode an identity kit defines round trips on its own.</summary>
        [Fact]
        public void EveryIdentityKitOpcodeRoundTripsOnItsOwn()
        {
            AssertOpcodeRoundTrips<IdentityKitDefinition>(1, Record(7));
            AssertOpcodeRoundTrips<IdentityKitDefinition>(2, Record(2, 0x01, 0x00, 0x02, 0x00));
            AssertOpcodeRoundTrips<IdentityKitDefinition>(3, Array.Empty<byte>());
            AssertOpcodeRoundTrips<IdentityKitDefinition>(40, Record(1, 0x00, 0x01, 0x00, 0x02));
            AssertOpcodeRoundTrips<IdentityKitDefinition>(41, Record(1, 0x00, 0x03, 0x00, 0x04));
        }

        /// <summary>An identity kit refuses the opcodes its dispatcher does not name.</summary>
        /// <remarks>
        ///     The client's chain has no final else, so an opcode it does not name consumes nothing
        ///     and every field after it is read out of the wrong bytes. 42 and 59 sit either side of
        ///     the head-model band and 70 just past it, which is where an off-by-one in the range test
        ///     would show.
        /// </remarks>
        [Fact]
        public void AnIdentityKitRefusesOpcodesOutsideItsTable()
        {
            foreach (byte opcode in new byte[] { 4, 39, 42, 59, 70, 249 })
            {
                var definition = new IdentityKitDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => definition.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        // =================================================================== structs

        /// <summary>
        ///     A struct's parameter block keeps duplicate keys, in order.
        /// </summary>
        /// <remarks>
        ///     Six records in the cache carry a repeated key, so this one is defended by the sweep as
        ///     well - but the sweep says only that the bytes changed, and the client's own store keeps
        ///     the <i>first</i> occurrence (InterfaceConfig.java:125), so a dictionary would drop the
        ///     loser rather than the winner.
        /// </remarks>
        [Fact]
        public void AStructKeepsDuplicateParameterKeysInOrder()
        {
            byte[] stored = Record(
                249,
                3,
                0, 0x00, 0x05, 0x10, 0x00, 0x00, 0x00, 0x07,
                0, 0x00, 0x05, 0x10, 0xFF, 0xFF, 0xFF, 0xFF,
                1, 0x00, 0x05, 0x11, (byte)'h', (byte)'i', 0,
                0);

            byte[] written = RoundTrip(stored, out StructDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(3, definition.Parameters.Count);
            Assert.Equal(0x000510, definition.Parameters[0].Key);
            Assert.Equal(7, definition.Parameters[0].IntValue);
            Assert.Equal(0x000510, definition.Parameters[1].Key);
            Assert.Equal(-1, definition.Parameters[1].IntValue);
            Assert.Equal("hi", definition.Parameters[2].StringValue);
        }

        /// <summary>A struct refuses every opcode but 249.</summary>
        /// <remarks>
        ///     Its record class has exactly one field, so any other opcode means the file is not a
        ///     struct - and the client would consume nothing for it and mis-read the rest.
        /// </remarks>
        [Fact]
        public void AStructRefusesEveryOpcodeButItsParameterBlock()
        {
            foreach (byte opcode in new byte[] { 1, 2, 17, 248, 250 })
            {
                var definition = new StructDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => definition.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        // =================================================================== light curves

        /// <summary>
        ///     A light curve replays the order the cache stores, which is never ascending.
        /// </summary>
        /// <remarks>
        ///     All four records in the cache store 3, 2, 4, 1, so an encoder walking opcodes 1..n
        ///     would rewrite every one of them.
        /// </remarks>
        [Fact]
        public void ALightCurveReplaysItsStoredOpcodeOrder()
        {
            byte[] stored = Record(
                3, 0x02, 0x66,
                2, 0x06, 0x66,
                4, 0x00, 0x00,
                1, 3,
                0);

            byte[] written = RoundTrip(stored, out LightIntensityDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(614, definition.Amplitude);
            Assert.Equal(1638, definition.Rate);
            Assert.Equal(0, definition.Offset);
            Assert.Equal(3, definition.Waveform);
            Assert.Equal(new[] { 3, 2, 4, 1 },
                definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray());
        }

        /// <summary>A light curve's offset is signed, as the client's reader makes it.</summary>
        /// <remarks>
        ///     Every value in the cache is positive, so nothing there would notice an unsigned field;
        ///     only <c>Class379.java:69</c>'s <c>readShort</c> settles it.
        /// </remarks>
        [Fact]
        public void ALightCurveOffsetIsSigned()
        {
            byte[] written = RoundTrip(Record(4, 0xFF, 0x38, 0), out LightIntensityDefinition definition);

            Assert.Equal(Record(4, 0xFF, 0x38, 0), written);
            Assert.Equal(-200, definition.Offset);
        }

        /// <summary>A light curve's constructor defaults match the client's.</summary>
        [Fact]
        public void AnEmptyLightCurveKeepsTheClientsDefaults()
        {
            RoundTrip(Record(0), out LightIntensityDefinition definition);

            Assert.Equal(0, definition.Waveform);
            Assert.Equal(2048, definition.Rate);
            Assert.Equal(2048, definition.Amplitude);
            Assert.Equal(0, definition.Offset);
        }

        // =================================================================== render animations

        /// <summary>
        ///     Every opcode a render animation defines round trips on its own.
        /// </summary>
        /// <remarks>
        ///     Seventeen of them - 27 to 37, 43 to 45, 53, 55 and 56 - occur in no file of either
        ///     cache, so a passing sweep says nothing about any of them. Walking each opcode alone
        ///     attributes a mis-sized payload to the opcode that owns it rather than to whatever
        ///     followed it.
        /// </remarks>
        [Fact]
        public void EveryRenderAnimationOpcodeRoundTripsOnItsOwn()
        {
            var payloads = new Dictionary<int, byte[]>
            {
                [1] = Record(0x03, 0xE8, 0xFF, 0xFF),
                [26] = Record(0x10, 0x20),
                [27] = Record(3, 0xFF, 0x9C, 0x00, 0x0A, 0xFF, 0xFF, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03),
                [28] = Record(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFF),
                [29] = Record(9),
                [30] = Record(0x08, 0x00),
                [31] = Record(4),
                [32] = Record(0x04, 0x00),
                [33] = Record(0xFF, 0x00),
                [34] = Record(2),
                [35] = Record(0x02, 0x00),
                [36] = Record(0xFE, 0x00),
                [37] = Record(6),
                [52] = Record(2, 0x03, 0xE8, 40, 0x03, 0xE9, 60),
                [53] = Array.Empty<byte>(),
                [54] = Record(0x05, 0x06),
                [55] = Record(11, 0x01, 0x2C),
                [56] = Record(7, 0xFF, 0xFF, 0x00, 0x64, 0xFF, 0x9C)
            };

            foreach (int opcode in new[] { 2, 3, 4, 5, 6, 7, 8, 9 }.Concat(Enumerable.Range(38, 14)))
                payloads[opcode] = Record(0x01, 0x2C);

            foreach (KeyValuePair<int, byte[]> entry in payloads.OrderBy(entry => entry.Key))
                AssertOpcodeRoundTrips<RenderAnimationDefinition>(entry.Key, entry.Value);
        }

        /// <summary>
        ///     A render animation's opcode 28 keeps the 255 that means "no slot".
        /// </summary>
        /// <remarks>
        ///     The client maps a stored 255 to -1 (Class294.java:232-234) and -1 has exactly one
        ///     encoding, so writing 255 back is safe - but the opcode occurs in no file of either
        ///     cache, so only this says the alias survives rather than becoming a truncated -1.
        /// </remarks>
        [Fact]
        public void ARenderAnimationEquipmentSlotOrderKeepsItsAbsentMarker()
        {
            byte[] stored = Record(28, 0xFF, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFF, 0);

            byte[] written = RoundTrip(stored, out RenderAnimationDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(RenderAnimationDefinition.EquipmentSlots, definition.EquipmentSlotOrder.Length);
            Assert.Equal(-1, definition.EquipmentSlotOrder[0]);
            Assert.Equal(-1, definition.EquipmentSlotOrder[11]);
            Assert.Equal(5, definition.EquipmentSlotOrder[5]);
        }

        /// <summary>
        ///     An equipment slot table that is not exactly twelve long is refused.
        /// </summary>
        /// <remarks>
        ///     Opcode 28 carries no count, so a shorter or longer table has no encoding at all and a
        ///     padded one would describe an equipment mapping nobody asked for.
        /// </remarks>
        [Fact]
        public void AnEquipmentSlotTableOfTheWrongLengthIsRefused()
        {
            RoundTrip(Record(28, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 0),
                out RenderAnimationDefinition definition);
            definition.EquipmentSlotOrder = new[] { 0, 1, 2 };

            Assert.ThrowsAny<Exception>(() => definition.Encode());
        }

        /// <summary>
        ///     Two indexed slot writes of the same opcode both survive, targeting different slots.
        /// </summary>
        /// <remarks>
        ///     Opcodes 27, 55 and 56 each carry a slot index, so one record can write several slots
        ///     with repeated occurrences. Materialising the twelve-slot array at decode would keep
        ///     the values and lose which opcode wrote which slot and in what order, and neither
        ///     opcode occurs in either cache, so nothing else would ever catch it.
        /// </remarks>
        [Fact]
        public void RepeatedIndexedSlotWritesKeepBothOccurrences()
        {
            byte[] stored = Record(
                55, 2, 0x00, 0x0A,
                55, 7, 0x00, 0x14,
                0);

            byte[] written = RoundTrip(stored, out RenderAnimationDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(2, definition.Unknown55Slots.Count);
            Assert.Equal(2, definition.Unknown55Slots[0].Slot);
            Assert.Equal(new[] { 10 }, definition.Unknown55Slots[0].Values);
            Assert.Equal(7, definition.Unknown55Slots[1].Slot);
            Assert.Equal(new[] { 20 }, definition.Unknown55Slots[1].Values);
        }

        /// <summary>
        ///     Editing the last indexed slot write reaches the file and the earlier one is untouched.
        /// </summary>
        /// <remarks>
        ///     The last occurrence is the one the encoder rebuilds from a field, so it has to be the
        ///     last entry of the list that gets written - taking the first would put the wrong slot
        ///     into the last position and silently swap two slots' values.
        /// </remarks>
        [Fact]
        public void AnEditReachesTheLastIndexedSlotWrite()
        {
            RoundTrip(Record(56, 1, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03,
                             56, 4, 0x00, 0x04, 0x00, 0x05, 0x00, 0x06, 0),
                out RenderAnimationDefinition definition);

            definition.Unknown56Slots[1] = new RenderAnimationSlot(9, new[] { 7, 8, 9 });

            Assert.Equal(
                Record(56, 1, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03,
                       56, 9, 0x00, 0x07, 0x00, 0x08, 0x00, 0x09, 0),
                definition.Encode().ToArray());
        }

        /// <summary>
        ///     A render animation keeps the raw bytes of the two opcodes the client scales.
        /// </summary>
        /// <remarks>
        ///     Opcode 26 stores <c>value * 4</c> in the client's fields and opcode 54 stores
        ///     <c>value &lt;&lt; 6</c>. Both scales are injective over a byte, so keeping the decoded
        ///     value would also round-trip; keeping the byte cannot be wrong, and this pins that
        ///     choice so a later "tidy-up" to the scaled form is a visible change.
        /// </remarks>
        [Fact]
        public void ARenderAnimationKeepsTheRawBytesOfItsScaledOpcodes()
        {
            byte[] stored = Record(26, 0xFF, 0x01, 54, 0xFE, 0x02, 0);

            byte[] written = RoundTrip(stored, out RenderAnimationDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(255, definition.Unknown26A);
            Assert.Equal(1, definition.Unknown26B);
            Assert.Equal(254, definition.Unknown54A);
            Assert.Equal(2, definition.Unknown54B);
        }

        /// <summary>A render animation's idle pool keeps its weights and reports their total.</summary>
        /// <remarks>
        ///     The client accumulates the weights into <c>anInt2367</c> while reading, which is
        ///     derived state; it is recomputed here rather than stored so an edit cannot leave the
        ///     total disagreeing with the list it came from.
        /// </remarks>
        [Fact]
        public void ARenderAnimationIdlePoolKeepsItsWeights()
        {
            byte[] stored = Record(52, 3, 0x03, 0xE8, 10, 0x03, 0xE9, 20, 0x03, 0xEA, 70, 0);

            byte[] written = RoundTrip(stored, out RenderAnimationDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(new[] { 1000, 1001, 1002 }, definition.IdlePoolAnimationIds);
            Assert.Equal(new[] { 10, 20, 70 }, definition.IdlePoolWeights);
            Assert.Equal(100, definition.IdlePoolWeightTotal);
        }

        /// <summary>An idle pool whose two halves disagree is refused.</summary>
        [Fact]
        public void AnIdlePoolWithMismatchedHalvesIsRefused()
        {
            RoundTrip(Record(52, 1, 0x00, 0x01, 5, 0), out RenderAnimationDefinition definition);
            definition.IdlePoolWeights = new[] { 5, 6 };

            Assert.ThrowsAny<Exception>(() => definition.Encode());
        }

        /// <summary>A render animation's constructor defaults match the client's.</summary>
        /// <remarks>
        ///     Class294.java:132-174 sets thirty-eight fields, several to values a file could legally
        ///     store, which is why presence is read off the decoded opcode list and never inferred
        ///     from a field.
        /// </remarks>
        [Fact]
        public void AnEmptyRenderAnimationKeepsTheClientsDefaults()
        {
            RoundTrip(Record(0), out RenderAnimationDefinition definition);

            Assert.Equal(-1, definition.IdleAnimationId);
            Assert.Equal(-1, definition.MoveForwardAnimationId);
            Assert.Equal(-1, definition.WalkForwardAnimationId);
            Assert.Equal(-1, definition.RunForwardAnimationId);
            Assert.Equal(-1, definition.Unknown37);
            Assert.Equal(-1, definition.Unknown43);
            Assert.Equal(-1, definition.Unknown44);
            Assert.Equal(-1, definition.Unknown45);
            Assert.Equal(0, definition.Unknown29);
            Assert.Equal(0, definition.Unknown30);
            Assert.True(definition.Unknown53);
            Assert.Null(definition.IdlePoolAnimationIds);
            Assert.Null(definition.EquipmentSlotOrder);
            Assert.False(definition.Has(1));
        }

        /// <summary>A render animation refuses the opcodes its dispatcher does not name.</summary>
        [Fact]
        public void ARenderAnimationRefusesOpcodesOutsideItsTable()
        {
            foreach (byte opcode in new byte[] { 10, 25, 57, 100, 249, 255 })
            {
                var definition = new RenderAnimationDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => definition.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        // =================================================================== quests

        /// <summary>
        ///     Every opcode a quest defines round trips on its own.
        /// </summary>
        /// <remarks>
        ///     Opcodes 12, 15, 18 and 19 occur in no file of either cache. Four of the six the client
        ///     reads and discards do occur, and all six have to be kept verbatim because there is no
        ///     field on the client's record to reconstruct them from.
        /// </remarks>
        [Fact]
        public void EveryQuestOpcodeRoundTripsOnItsOwn()
        {
            AssertOpcodeRoundTrips<QuestDefinition>(1, Record(0, (byte)'C', (byte)'o', (byte)'o', (byte)'k', 0));
            AssertOpcodeRoundTrips<QuestDefinition>(2, Record(0, 0));
            AssertOpcodeRoundTrips<QuestDefinition>(3,
                Record(1, 0x00, 0x0C, 0x00, 0x00, 0x00, 0x2A, 0xFF, 0xFF, 0xFF, 0xFF));
            AssertOpcodeRoundTrips<QuestDefinition>(4,
                Record(1, 0x00, 0x0D, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02));
            AssertOpcodeRoundTrips<QuestDefinition>(5, Record(0x01, 0x2C));
            AssertOpcodeRoundTrips<QuestDefinition>(6, Record(3));
            AssertOpcodeRoundTrips<QuestDefinition>(7, Record(4));
            AssertOpcodeRoundTrips<QuestDefinition>(8, Array.Empty<byte>());
            AssertOpcodeRoundTrips<QuestDefinition>(9, Record(5));
            AssertOpcodeRoundTrips<QuestDefinition>(10, Record(1, 0x00, 0x00, 0x00, 0x63));
            AssertOpcodeRoundTrips<QuestDefinition>(12, Record(0xFF, 0xFF, 0xFF, 0xFF));
            AssertOpcodeRoundTrips<QuestDefinition>(13, Record(2, 0x00, 0x01, 0x00, 0x02));
            AssertOpcodeRoundTrips<QuestDefinition>(14, Record(2, 1, 2, 3, 4));
            AssertOpcodeRoundTrips<QuestDefinition>(15, Record(0x00, 0x2A));
            AssertOpcodeRoundTrips<QuestDefinition>(17, Record(0x0B, 0xE5));
            AssertOpcodeRoundTrips<QuestDefinition>(18,
                Record(1, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x03,
                       (byte)'o', (byte)'k', 0));
            AssertOpcodeRoundTrips<QuestDefinition>(19,
                Record(1, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00, 0x06, 0));
            AssertOpcodeRoundTrips<QuestDefinition>(249,
                Record(1, 0, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x63));
        }

        /// <summary>
        ///     A quest refuses opcodes 11 and 16, which its own client dispatcher fails to name.
        /// </summary>
        /// <remarks>
        ///     Both fall through <c>Class220.method2818</c> without consuming a payload - the same
        ///     defect floor overlay opcodes 4, 6 and 15 have - so the client would read the next
        ///     payload byte as an opcode and mis-read the rest of the record. Neither can appear in a
        ///     shipped file for that reason, which is exactly why only a hand-built record can state
        ///     that this decoder refuses them.
        /// </remarks>
        [Fact]
        public void AQuestRefusesTheTwoOpcodesTheClientFailsToConsume()
        {
            foreach (byte opcode in new byte[] { 11, 16, 20, 21, 100, 248 })
            {
                var definition = new QuestDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => definition.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        /// <summary>
        ///     A quest's two names are <c>gjstr2</c>, so each keeps its leading version byte.
        /// </summary>
        /// <remarks>
        ///     Dropping the byte costs one byte per string and shifts everything after it. An empty
        ///     name is the case that makes this sharp: its payload is <c>00 00</c>, a version byte
        ///     followed by a bare terminator, so a reader that skipped the version byte would still
        ///     produce a plausible string for a named quest and run off the end here.
        /// </remarks>
        [Fact]
        public void QuestNamesKeepTheirVersionByte()
        {
            byte[] stored = Record(1, 0, (byte)'C', (byte)'o', (byte)'o', (byte)'k', 0, 2, 0, 0, 0);

            byte[] written = RoundTrip(stored, out QuestDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal("Cook", definition.Name);
            Assert.Equal("", definition.AlternateName);
        }

        /// <summary>A quest name with any version byte but 0 is refused, as the client refuses it.</summary>
        [Fact]
        public void ANonZeroQuestNameVersionIsRefused()
        {
            var definition = new QuestDefinition { Id = 0 };
            Assert.ThrowsAny<Exception>(
                () => definition.Decode(new JagStream(Record(1, 1, (byte)'x', 0, 0))));
        }

        /// <summary>
        ///     A quest that carries no opcode 2 does not gain one on re-encode.
        /// </summary>
        /// <remarks>
        ///     <c>Class13.method220</c> calls <c>Class220.method2819</c> after every decode, which
        ///     copies opcode 1's string into opcode 2's field when opcode 2 was absent. That is a
        ///     post-decode transform, and applying it at decode would make the encoder write opcode 2
        ///     into the 183 records of this cache that do not carry it - a silent rewrite of files
        ///     nobody edited.
        /// </remarks>
        [Fact]
        public void TheClientsNameFallbackIsNotAppliedAtDecode()
        {
            byte[] stored = Record(1, 0, (byte)'X', 0, 0);

            byte[] written = RoundTrip(stored, out QuestDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal("X", definition.Name);
            Assert.Null(definition.AlternateName);
            Assert.False(definition.Has(2));
        }

        /// <summary>
        ///     The bytes a quest's client reads and discards survive, including a stored zero.
        /// </summary>
        /// <remarks>
        ///     A zero is the case a "keep it if it is set" encoder would lose, and opcodes 6, 7 and 9
        ///     are single bytes where zero is a legal value; presence has to come from the opcode list
        ///     instead.
        /// </remarks>
        [Fact]
        public void TheBytesAQuestDiscardsSurviveEvenWhenTheyAreZero()
        {
            byte[] stored = Record(9, 0, 7, 0, 6, 0, 5, 0x00, 0x00, 0);

            byte[] written = RoundTrip(stored, out QuestDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(0, definition.Unknown9);
            Assert.Equal(0, definition.Unknown7);
            Assert.Equal(0, definition.Unknown6);
            Assert.Equal(0, definition.Unknown5);
            Assert.True(definition.Has(5));
        }

        /// <summary>A quest's constructor defaults leave every optional field absent.</summary>
        [Fact]
        public void AnEmptyQuestKeepsTheClientsDefaults()
        {
            RoundTrip(Record(0), out QuestDefinition definition);

            Assert.Null(definition.Name);
            Assert.Null(definition.AlternateName);
            Assert.Equal(-1, definition.IconSpriteId);
            Assert.Empty(definition.Conditions3);
            Assert.Empty(definition.Conditions4);
            Assert.Empty(definition.Parameters);
            Assert.False(definition.Unknown8);
        }

        // =================================================================== empty groups

        /// <summary>
        ///     The provider-less groups refuse every opcode, which is what makes their sweep a claim.
        /// </summary>
        /// <remarks>
        ///     Their records are bare terminators in both caches, so a decoder that guessed at a table
        ///     would sweep clean. Refusing is what turns a future cache that fills one of them into a
        ///     failure rather than into silently mis-read fields.
        /// </remarks>
        [Fact]
        public void TheProviderlessGroupsRefuseEveryOpcode()
        {
            Assert.Equal(19, ConfigGroup.EmptyProviderless.Count);
            Assert.DoesNotContain(ConfigGroup.ClientString, ConfigGroup.EmptyProviderless);

            foreach (byte opcode in new byte[] { 1, 2, 5, 17, 249, 255 })
            {
                var definition = new EmptyConfigDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => definition.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        /// <summary>Every index 2 group this editor names has exactly one family.</summary>
        /// <remarks>
        ///     Two rows for one group would make <see cref="ConfigFamily.For"/> silently prefer
        ///     whichever came first, which is the sort of thing a cache-backed test cannot see because
        ///     the losing row simply never runs.
        /// </remarks>
        [Fact]
        public void NoConfigGroupIsRegisteredTwice()
        {
            int[] groups = ConfigFamily.Modelled.Select(family => family.GroupId).ToArray();

            Assert.Equal(groups.Length, groups.Distinct().Count());
            Assert.All(groups, group => Assert.Equal(group, ConfigFamily.For(group).GroupId));
            Assert.All(ConfigFamily.Modelled, family => Assert.True(family.IsModelled));
        }
    }
}
