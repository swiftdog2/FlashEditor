using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Config;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Cache
{
    /// <summary>
    ///     The index 2 config hazards that a byte-identity sweep over this cache cannot see.
    /// </summary>
    /// <remarks>
    ///     A sweep only exercises the encodings the shipped files happen to use, so every rule that
    ///     depends on an input the cache does not contain needs a hand-built record instead. Six of
    ///     group 36's opcodes occur in no file at all, five of group 46's do not, and two of the
    ///     aliasing rules - opcode 8's non-1 byte and the two encodings of a damage mark's fade start
    ///     - are unexercised in exactly the same way. Nothing here reads the cache.
    /// </remarks>
    public sealed class Index2ConfigCodecTests
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

        /// <summary>Decodes a record and encodes it straight back.</summary>
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

        /// <summary>
        ///     A record's stored opcode order is replayed rather than sorted.
        /// </summary>
        /// <remarks>
        ///     The order used here, <c>3, 6, 4, 8, 19</c>, is the one 415 of the cache's 1,051 map
        ///     elements store - and not one of the 1,051 is in ascending order, so an encoder that
        ///     emitted 3, 4, 6, 8, 19 would rewrite every file in the group.
        /// </remarks>
        [Fact]
        public void StoredOpcodeOrderIsReplayed()
        {
            byte[] stored = Record(
                3, 'E', 'x', 'i', 't', 0,
                6, 1,
                4, 0xFF, 0xFF, 0xFF,
                8, 1,
                19, 0x03, 0xE8,
                0);

            byte[] written = RoundTrip(stored, out MapElementDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal("Exit", definition.Label);
            Assert.Equal(1, definition.FontId);
            Assert.Equal(0xFFFFFF, definition.LabelRgb);
            Assert.Equal(1000, definition.CategoryId);
            Assert.Equal(new[] { 3, 6, 4, 8, 19 },
                definition.DecodedOpcodes.Select(entry => entry.Opcode).ToArray());
        }

        /// <summary>
        ///     A repeated opcode keeps both payloads; the fields hold the winning one.
        /// </summary>
        /// <remarks>
        ///     Map elements 779 and 780 each store opcode 22 twice. Keeping only the winner produces
        ///     a file of the right length and the wrong contents.
        /// </remarks>
        [Fact]
        public void RepeatedOpcodeKeepsBothPayloads()
        {
            byte[] stored = Record(
                22, 0xFF, 0xFF, 0xFF, 0xC8,
                22, 0x20, 0x96, 0x3F, 0xFF,
                0);

            byte[] written = RoundTrip(stored, out MapElementDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(2, definition.DecodedOpcodes.Count(entry => entry.Opcode == 22));
            Assert.Equal(0x20963FFF, definition.OutlineArgb);
        }

        /// <summary>
        ///     An edit reaches the last occurrence of a repeated opcode and leaves the earlier one.
        /// </summary>
        /// <remarks>
        ///     The last occurrence is the one that decided the field, so it is the only one an edit
        ///     can mean. Rewriting both would change a value the file deliberately overrides.
        /// </remarks>
        [Fact]
        public void AnEditRewritesOnlyTheWinningOccurrence()
        {
            byte[] stored = Record(
                22, 0xFF, 0xFF, 0xFF, 0xC8,
                22, 0x20, 0x96, 0x3F, 0xFF,
                0);

            RoundTrip(stored, out MapElementDefinition definition);
            definition.OutlineArgb = 0x01020304;

            Assert.Equal(
                Record(22, 0xFF, 0xFF, 0xFF, 0xC8, 22, 0x01, 0x02, 0x03, 0x04, 0),
                definition.Encode().ToArray());
        }

        /// <summary>
        ///     Opcode 8's byte survives even when it is not the 1 the client tests for.
        /// </summary>
        /// <remarks>
        ///     The client reads <c>readUnsignedByte() == 1</c>, so every other byte means the same
        ///     thing to it and a decoder that kept only the boolean could not say which was stored.
        ///     The cache holds only 0 and 1, so nothing in it would ever catch the collapse.
        /// </remarks>
        [Fact]
        public void MinimapFlagKeepsAByteThatIsNotOne()
        {
            byte[] written = RoundTrip(Record(8, 2, 0), out MapElementDefinition definition);

            Assert.Equal(Record(8, 2, 0), written);
            Assert.False(definition.DrawnOnMinimap);
            Assert.Equal(2, definition.MinimapVisibleByte);
        }

        /// <summary>
        ///     The 65535 a visibility gate stores for "no varbit" is written back as 65535.
        /// </summary>
        /// <remarks>
        ///     Both shorts of opcodes 9 and 20 map a stored 65535 to -1. -1 has exactly one encoding,
        ///     so the alias is safe only while the encoder puts the sentinel back rather than
        ///     truncating a -1 into the field by accident.
        /// </remarks>
        [Fact]
        public void VisibilityGateSentinelsRoundTrip()
        {
            byte[] stored = Record(
                9, 0xFF, 0xFF, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x05,
                0);

            byte[] written = RoundTrip(stored, out MapElementDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(-1, definition.VisibleVarbitId);
            Assert.Equal(42, definition.VisibleVarpId);
            Assert.Equal(1, definition.VisibleMin);
            Assert.Equal(5, definition.VisibleMax);
        }

        /// <summary>
        ///     Opcode 15's polygon round trips, including its signed coordinates and edge indices.
        /// </summary>
        /// <remarks>
        ///     One count sizes two arrays and the edge colour table is sized separately, so the block
        ///     is the easiest place in this format to lose a byte. Coordinates are signed - the cache
        ///     stores values down to -128 - and so are the per-vertex colour indices.
        /// </remarks>
        [Fact]
        public void PolygonRoundTrips()
        {
            byte[] stored = Record(
                15,
                3,                                          //3 vertices
                0xFF, 0x80, 0x00, 0x10,                     //(-128, 16)
                0x01, 0x80, 0x00, 0x20,                     //(384, 32)
                0x00, 0x00, 0x00, 0x00,                     //(0, 0)
                0x33, 0xAA, 0xBB, 0xFF,                     //fill
                1,                                          //1 edge colour
                0xFF, 0x00, 0xFF, 0xFF,                     //that colour
                0, 0, 0,                                    //edge colour index per vertex
                0);

            byte[] written = RoundTrip(stored, out MapElementDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(new[] { -128, 16, 384, 32, 0, 0 }, definition.PolygonVertices);
            Assert.Equal(0x33AABBFF, definition.PolygonFillArgb);
            Assert.Equal(new[] { -16711681 }, definition.PolygonEdgeArgb);
            Assert.Equal(new sbyte[] { 0, 0, 0 }, definition.PolygonEdgeColourIndices);
        }

        /// <summary>
        ///     A polygon whose two per-vertex arrays disagree is refused rather than truncated.
        /// </summary>
        /// <remarks>
        ///     Opcode 15 writes one count for both, so there is no encoding of a mismatch. Padding it
        ///     out would produce a readable record describing a shape nobody asked for.
        /// </remarks>
        [Fact]
        public void APolygonWithMismatchedArraysIsRefused()
        {
            var definition = new MapElementDefinition { Id = 0 };
            definition.Decode(new JagStream(Record(0)));
            definition.PolygonVertices = new[] { 0, 0, 1, 1 };
            definition.PolygonEdgeColourIndices = new sbyte[] { 0 };
            definition.PolygonEdgeArgb = new[] { -1 };

            Assert.ThrowsAny<Exception>(() => definition.Encode());
        }

        /// <summary>
        ///     Opcodes the record type does not define are refused, not skipped.
        /// </summary>
        /// <remarks>
        ///     The client's dispatchers are equality chains with no final else, so an opcode they do
        ///     not name consumes nothing and every field after it is read out of the wrong bytes.
        ///     Refusing turns a silent corruption into a failure.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 25, 26, 100, 248, 255 })
            {
                var element = new MapElementDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => element.Decode(new JagStream(Record(opcode, 0))));
            }

            //Every one of the twenty empty groups refuses every opcode, which is what makes a sweep
            //over them a statement about the cache rather than a placeholder.
            foreach (byte opcode in new byte[] { 1, 2, 5, 249 })
            {
                var empty = new EmptyConfigDefinition { Id = 0 };
                Assert.ThrowsAny<Exception>(() => empty.Decode(new JagStream(Record(opcode, 0))));
            }
        }

        /// <summary>An empty record is a bare terminator, and comes back as one.</summary>
        [Fact]
        public void AnEmptyRecordIsASingleTerminator()
        {
            byte[] written = RoundTrip(Record(0), out EmptyConfigDefinition definition);

            Assert.Equal(Record(0), written);
            Assert.Empty(definition.DecodedOpcodes);
        }

        /// <summary>
        ///     A damage mark's <c>gjstr2</c> keeps its leading version byte.
        /// </summary>
        /// <remarks>
        ///     Dropping it costs one byte per record and shifts everything after it, which the
        ///     exact-consumption sweep would catch without ever saying why.
        /// </remarks>
        [Fact]
        public void VersionedStringsKeepTheirVersionByte()
        {
            byte[] stored = Record(8, 0, '%', '1', 0, 0);

            byte[] written = RoundTrip(stored, out DamageMarkDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal("%1", definition.NumberTemplate);
        }

        /// <summary>A <c>gjstr2</c> with any version byte but 0 is refused, as the client refuses it.</summary>
        [Fact]
        public void ANonZeroStringVersionIsRefused()
        {
            var definition = new DamageMarkDefinition { Id = 0 };
            Assert.ThrowsAny<Exception>(
                () => definition.Decode(new JagStream(Record(8, 1, '%', '1', 0, 0))));
        }

        /// <summary>
        ///     A damage mark's fade start has two encodings and each survives as itself.
        /// </summary>
        /// <remarks>
        ///     Opcode 11 sets the field to 0 with no payload and opcode 14 stores an unsigned short,
        ///     so a stored 0 can be either. Opcode 11 occurs in no file of this cache, so only a
        ///     hand-built record can hold the two apart.
        /// </remarks>
        [Fact]
        public void TheTwoEncodingsOfAFadeStartAreNotInterchangeable()
        {
            byte[] flagForm = RoundTrip(Record(11, 0), out DamageMarkDefinition viaFlag);
            byte[] shortForm = RoundTrip(Record(14, 0, 0, 0), out DamageMarkDefinition viaShort);

            Assert.Equal(Record(11, 0), flagForm);
            Assert.Equal(Record(14, 0, 0, 0), shortForm);
            Assert.Equal(0, viaFlag.FadeStartMillis);
            Assert.Equal(0, viaShort.FadeStartMillis);
        }

        /// <summary>
        ///     A cursor's opcode 2 is two bytes, so a record carrying both opcodes consumes exactly.
        /// </summary>
        /// <remarks>
        ///     The client's <c>method2879</c> appears to read a third field because JODE merged
        ///     opcode 2's body into opcode 1's behind <c>client.aBoolean3553</c>, which is assigned
        ///     true only on a shutdown path. Reading it as four bytes would over-read every one of
        ///     the 175 records in the cache.
        /// </remarks>
        [Fact]
        public void ACursorHotspotIsTwoBytes()
        {
            byte[] stored = Record(1, 0x0F, 0xBB, 2, 5, 0, 0);

            byte[] written = RoundTrip(stored, out CursorDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(4027, definition.SpriteId);
            Assert.Equal(5, definition.HotspotX);
            Assert.Equal(0, definition.HotspotY);
        }

        /// <summary>
        ///     A type letter above 127 survives as the byte that was stored.
        /// </summary>
        /// <remarks>
        ///     Groups 11 and 19 each hold one record whose type byte is 0x80, which the client's
        ///     modified cp1252 maps to the euro sign rather than to U+0080. Storing the character
        ///     instead of the byte loses that, and the remap is not injective - five bytes in the
        ///     band collapse onto '?'.
        /// </remarks>
        [Fact]
        public void AHighTypeLetterByteSurvives()
        {
            byte[] written = RoundTrip(Record(1, 0x80, 0), out ParameterTypeDefinition parameter);

            Assert.Equal(Record(1, 0x80, 0), written);
            Assert.Equal(0x80, parameter.TypeLetterByte);
            //U+20AC, the euro sign: what modified cp1252 puts at 0x80 and Latin-1 does not. Written
            //as an escape so the assertion cannot turn on how this file's encoding is detected.
            Assert.Equal('\u20AC', parameter.TypeLetter);

            byte[] variable = RoundTrip(Record(1, 0x80, 0), out ClientVariableDefinition clientVariable);
            Assert.Equal(Record(1, 0x80, 0), variable);
            Assert.Equal(0x80, clientVariable.TypeLetterByte);
        }

        /// <summary>The parameter type string flag is exactly "the type letter is 's'".</summary>
        [Fact]
        public void OnlyTheLetterSMarksAStringParameter()
        {
            RoundTrip(Record(1, (byte)'s', 0), out ParameterTypeDefinition asString);
            RoundTrip(Record(1, (byte)'S', 0), out ParameterTypeDefinition asOther);

            Assert.True(asString.IsString);
            Assert.False(asOther.IsString);
        }

        /// <summary>
        ///     An opcode 249 parameter block keeps duplicate keys, in order.
        /// </summary>
        /// <remarks>
        ///     The client's own store keeps the <b>first</b> occurrence of a duplicate key
        ///     (InterfaceConfig.java:125), so folding the block into a dictionary both drops the
        ///     losing entry and reorders what survives. No record in group 36 carries opcode 249 at
        ///     all, so nothing in the cache would notice.
        /// </remarks>
        [Fact]
        public void ParameterBlocksKeepDuplicateKeysInOrder()
        {
            byte[] stored = Record(
                249,
                3,
                0, 0x00, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x07,   //int key 42 = 7
                1, 0x00, 0x00, 0x2A, 'h', 'i', 0,              //string key 42 = "hi"
                0, 0x00, 0x00, 0x2B, 0xFF, 0xFF, 0xFF, 0xFF,   //int key 43 = -1
                0);

            byte[] written = RoundTrip(stored, out MapElementDefinition definition);

            Assert.Equal(stored, written);
            Assert.Equal(3, definition.Parameters.Count);
            Assert.Equal(42, definition.Parameters[0].Key);
            Assert.Equal(7, definition.Parameters[0].IntValue);
            Assert.Equal(42, definition.Parameters[1].Key);
            Assert.Equal("hi", definition.Parameters[1].StringValue);
            Assert.Equal(-1, definition.Parameters[2].IntValue);
        }

        /// <summary>An edit to a field the record never carried appends its opcode.</summary>
        [Fact]
        public void AnEditedFieldEmitsItsOpcode()
        {
            var definition = new MapElementDefinition { Id = 0 };
            definition.Decode(new JagStream(Record(0)));

            definition.SpriteId = 1784;
            definition.Label = "Soul Obelisk";

            var round = new MapElementDefinition { Id = 0 };
            round.Decode(new JagStream(definition.Encode().ToArray()));

            Assert.Equal(1784, round.SpriteId);
            Assert.Equal("Soul Obelisk", round.Label);
        }

        /// <summary>
        ///     A record that sets nothing keeps the constructor defaults the 637 client sets.
        /// </summary>
        /// <remarks>
        ///     Several of those defaults are legal stored values, which is why presence is read off
        ///     the decoded opcode list and never inferred from a field.
        /// </remarks>
        [Fact]
        public void AnEmptyRecordKeepsTheClientsDefaults()
        {
            RoundTrip(Record(0), out MapElementDefinition element);
            Assert.Equal(-1, element.SpriteId);
            Assert.Equal(-1, element.HighlightedSpriteId);
            Assert.Equal(-1, element.CategoryId);
            Assert.Equal(-1, element.VisibleVarbitId);
            Assert.Equal(-1, element.VisibleVarpId);
            Assert.True(element.DrawnOnMinimap);
            Assert.True(element.Rendered);
            Assert.Empty(element.Parameters);
            Assert.False(element.Has(1));

            RoundTrip(Record(0), out DamageMarkDefinition mark);
            Assert.Equal(-1, mark.FontId);
            Assert.Equal(0xFFFFFF, mark.TextRgb);
            Assert.Equal(70, mark.LifetimeMillis);
            Assert.Equal(-1, mark.FadeStartMillis);
            Assert.Equal("", mark.NumberTemplate);

            RoundTrip(Record(0), out ParameterTypeDefinition parameter);
            Assert.True(parameter.Unknown4);
            Assert.False(parameter.IsString);

            RoundTrip(Record(0), out ClientVariableDefinition clientVariable);
            Assert.False(clientVariable.ServerWritable);

            RoundTrip(Record(0), out VarPlayerDefinition varPlayer);
            Assert.True(varPlayer.ResetOnLogout);

            RoundTrip(Record(0), out ContainerDefinition container);
            Assert.Equal(0, container.Capacity);
        }

        /// <summary>
        ///     Every opcode a record type defines survives a round trip, including the ones the cache
        ///     never uses.
        /// </summary>
        /// <remarks>
        ///     Six of group 36's opcodes - 5, 16, 18, 23, 24 and 249 - occur in no file of this
        ///     cache, and five of group 46's do not either, so a passing sweep says nothing about
        ///     any of them. This walks each opcode on its own so a mis-sized payload is attributed to
        ///     the opcode that owns it rather than to whatever followed it.
        /// </remarks>
        [Fact]
        public void EveryDefinedOpcodeRoundTripsOnItsOwn()
        {
            var mapElements = new Dictionary<int, byte[]>
            {
                [1] = Record(0x06, 0xF8),
                [2] = Record(0x06, 0xEF),
                [3] = Record((byte)'M', (byte)'a', (byte)'p', 0),
                [4] = Record(0x11, 0x22, 0x33),
                [5] = Record(0x44, 0x55, 0x66),
                [6] = Record(2),
                [7] = Record(3),
                [8] = Record(1),
                [9] = Record(0x16, 0xBB, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0),
                [10] = Record((byte)'O', (byte)'p', (byte)'e', (byte)'n', 0),
                [14] = Record(0),
                [16] = Array.Empty<byte>(),
                [17] = Record((byte)'M', (byte)'a', (byte)'p', 0),
                [18] = Record(0x01, 0x02),
                [19] = Record(0x08, 0x32),
                [20] = Record(0x14, 0xD4, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 12),
                [21] = Record(0xAF, 0xB8, 0x2B, 0x0F),
                [22] = Record(0xFF, 0xFF, 0xFF, 0xC8),
                [23] = Record(4, 1, 2),
                [24] = Record(0xFF, 0xF0, 0x00, 0x10),
                [249] = Record(1, 0, 0x00, 0x00, 0x0B, 0x00, 0x00, 0x00, 0x63)
            };

            foreach (KeyValuePair<int, byte[]> entry in mapElements)
                AssertOpcodeRoundTrips<MapElementDefinition>(entry.Key, entry.Value);

            var damageMarks = new Dictionary<int, byte[]>
            {
                [1] = Record(0x00, 0x0A),
                [2] = Record(0x11, 0x22, 0x33),
                [3] = Record(0x00, 0x14),
                [4] = Record(0x00, 0x15),
                [5] = Record(0x00, 0x16),
                [6] = Record(0x00, 0x17),
                [7] = Record(0xFF, 0xF6),
                [8] = Record(0, (byte)'%', (byte)'1', 0),
                [9] = Record(0x02, 0x58),
                [10] = Record(0xFF, 0xEC),
                [11] = Array.Empty<byte>(),
                [12] = Record(2),
                [13] = Record(0x00, 0x0F),
                [14] = Record(0x01, 0x2C)
            };

            foreach (KeyValuePair<int, byte[]> entry in damageMarks)
                AssertOpcodeRoundTrips<DamageMarkDefinition>(entry.Key, entry.Value);

            AssertOpcodeRoundTrips<ContainerDefinition>(2, Record(0x02, 0x04));
            AssertOpcodeRoundTrips<VarPlayerDefinition>(5, Record(0x00, 0x0A));
            AssertOpcodeRoundTrips<ClientVariableDefinition>(1, Record((byte)'i'));
            AssertOpcodeRoundTrips<ClientVariableDefinition>(2, Array.Empty<byte>());
            AssertOpcodeRoundTrips<CursorDefinition>(1, Record(0x00, 0xA8));
            AssertOpcodeRoundTrips<CursorDefinition>(2, Record(5, 0));
            AssertOpcodeRoundTrips<ParameterTypeDefinition>(1, Record((byte)'s'));
            AssertOpcodeRoundTrips<ParameterTypeDefinition>(2, Record(0, 0, 0x01, 0x2C));
            AssertOpcodeRoundTrips<ParameterTypeDefinition>(4, Array.Empty<byte>());
            AssertOpcodeRoundTrips<ParameterTypeDefinition>(5, Record((byte)'h', (byte)'i', 0));
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
    }
}
