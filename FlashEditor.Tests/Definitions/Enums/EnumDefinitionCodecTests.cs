using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Enums;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Enums
{
    /// <summary>
    ///     The enum codec against bytes lifted from a real revision-639 cache.
    /// </summary>
    /// <remarks>
    ///     Round-tripping this encoder against this decoder proves nothing, so every fixture below
    ///     is a file captured out of the cache and every expected value comes from what the client
    ///     does with it. The four opcode orders that occur are all represented, because the order is
    ///     the one thing about this format an encoder can get wrong while producing a file of the
    ///     right length.
    /// </remarks>
    public sealed class EnumDefinitionCodecTests
    {
        /// <summary>An unallocated enum slot: one terminator byte and nothing else.</summary>
        /// <remarks>Most of index 17 looks like this. Group 0, file 0.</remarks>
        public static readonly byte[] UnallocatedSlot = { 0x00 };

        /// <summary>
        ///     A string table followed by its default. Group 0, file 62.
        /// </summary>
        /// <remarks>
        ///     Order 1, 2, 5, 3 - the default is written <em>after</em> the table. Key type 'o',
        ///     value type 's', three entries, default "coins".
        /// </remarks>
        public static readonly byte[] StringTableWithDefault =
        {
            0x01, 0x6F,
            0x02, 0x73,
            0x05, 0x00, 0x03,
                  0x00, 0x00, 0x18, 0xA2, 0x74, 0x72, 0x61, 0x64, 0x65, 0x20, 0x73, 0x74, 0x69, 0x63,
                        0x6B, 0x73, 0x00,
                  0x00, 0x00, 0x19, 0x81, 0x54, 0x6F, 0x6B, 0x4B, 0x75, 0x6C, 0x00,
                  0x00, 0x00, 0x22, 0xF7, 0x70, 0x69, 0x65, 0x63, 0x65, 0x73, 0x20, 0x6F, 0x66, 0x20,
                        0x65, 0x69, 0x67, 0x68, 0x74, 0x00,
            0x03, 0x63, 0x6F, 0x69, 0x6E, 0x73, 0x00,
            0x00
        };

        /// <summary>
        ///     An int table followed by its default. Group 0, file 169.
        /// </summary>
        /// <remarks>Order 1, 2, 6, 4. One entry, default -1.</remarks>
        public static readonly byte[] IntTableWithDefault =
        {
            0x01, 0x69,
            0x02, 0x4A,
            0x06, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0xFC,
            0x04, 0xFF, 0xFF, 0xFF, 0xFF,
            0x00
        };

        /// <summary>
        ///     A default with no table at all. Group 0, file 224.
        /// </summary>
        /// <remarks>
        ///     Order 1, 2, 4, and the default is 0 - the same value an absent opcode 4 leaves
        ///     behind. Absent versus stored-at-the-default, in six bytes.
        /// </remarks>
        public static readonly byte[] DefaultWithNoTable =
        {
            0x01, 0x69,
            0x02, 0x69,
            0x04, 0x00, 0x00, 0x00, 0x00,
            0x00
        };

        /// <summary>A string default with no table. Group 2, file 235, enum id 747.</summary>
        public static readonly byte[] StringDefaultWithNoTable =
        {
            0x01, 0x69,
            0x02, 0x73,
            0x03, 0x43, 0x68, 0x6F, 0x6F, 0x73, 0x65, 0x20, 0x61, 0x20, 0x73, 0x6B, 0x69, 0x6E,
                  0x20, 0x63, 0x6F, 0x6C, 0x6F, 0x75, 0x72, 0x00,
            0x00
        };

        /// <summary>
        ///     A value type byte outside ASCII. Group 11, file 222, enum id 3038.
        /// </summary>
        /// <remarks>
        ///     Value type 0xAB. The client maps a type byte through cp1252 and turns the unassigned
        ///     slots into '?', which is one way, so keeping the char instead of the byte would lose
        ///     this on the first save.
        /// </remarks>
        public static readonly byte[] NonAsciiValueType =
        {
            0x01, 0x69,
            0x02, 0xAB,
            0x06, 0x00, 0x04,
                  0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x05, 0x0C,
                  0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x05, 0x04,
                  0x00, 0x00, 0x00, 0x03, 0x00, 0x00, 0x05, 0x11,
                  0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x05, 0x27,
            0x04, 0xFF, 0xFF, 0xFF, 0xFF,
            0x00
        };

        /// <summary>Every captured file, for the sweeps that make the same claim about all of them.</summary>
        public static IEnumerable<object[]> EveryFixture()
        {
            yield return new object[] { nameof(UnallocatedSlot), UnallocatedSlot };
            yield return new object[] { nameof(StringTableWithDefault), StringTableWithDefault };
            yield return new object[] { nameof(IntTableWithDefault), IntTableWithDefault };
            yield return new object[] { nameof(DefaultWithNoTable), DefaultWithNoTable };
            yield return new object[] { nameof(StringDefaultWithNoTable), StringDefaultWithNoTable };
            yield return new object[] { nameof(NonAsciiValueType), NonAsciiValueType };
        }

        /// <summary>Every captured file consumes exactly and re-encodes to the bytes it came from.</summary>
        /// <param name="name">The fixture's name, so a failure names it.</param>
        /// <param name="stored">The captured bytes.</param>
        [Theory]
        [MemberData(nameof(EveryFixture))]
        public void EveryCapturedFileRoundTrips(string name, byte[] stored)
        {
            var stream = new JagStream(stored);
            var definition = new EnumDefinition { Id = 0 }.Decode(stream);

            Assert.True(stored.Length == stream.Position,
                $"{name} consumed {stream.Position} of its {stored.Length} bytes");
            Assert.True(stored.AsSpan().SequenceEqual(definition.Encode().ToArray()),
                $"{name} did not re-encode to the bytes it was decoded from");
        }

        /// <summary>An unallocated slot decodes to a present-but-empty enum and writes one byte back.</summary>
        /// <remarks>
        ///     The distinction that matters: "there is no enum here" and "the enum here is empty"
        ///     are the same decoded state, and only the first would let a writer skip the file.
        /// </remarks>
        [Fact]
        public void AnUnallocatedSlotStaysOneByte()
        {
            var definition = new EnumDefinition { Id = 0 }.Decode(new JagStream(UnallocatedSlot));

            Assert.True(definition.IsEmpty);
            Assert.Empty(definition.Entries);
            Assert.Equal(EnumDefinition.AbsentDefaultString, definition.DefaultString);
            Assert.Equal(0, definition.DefaultInt);
            Assert.Equal(new byte[] { 0 }, definition.Encode().ToArray());
        }

        /// <summary>The string table decodes to its three entries, its types and its default.</summary>
        [Fact]
        public void AStringTableDecodesToItsEntries()
        {
            var definition = new EnumDefinition { Id = 62 }.Decode(new JagStream(StringTableWithDefault));

            Assert.Equal('o', definition.KeyTypeChar);
            Assert.Equal('s', definition.ValueTypeChar);
            Assert.True(definition.ValuesAreStrings);
            Assert.Equal("coins", definition.DefaultString);

            Assert.Equal(new[] { 6306, 6529, 8951 }, definition.Entries.Select(entry => entry.Key));
            Assert.Equal(new[] { "trade sticks", "TokKul", "pieces of eight" },
                definition.Entries.Select(entry => entry.Text));
        }

        /// <summary>
        ///     The int table decodes to its entries, and its default follows the table on the wire.
        /// </summary>
        /// <remarks>
        ///     The order assertion is the point. An encoder writing opcodes in ascending order
        ///     produces a file of exactly the same length with opcode 4 in front of opcode 6, which
        ///     no comparison of decoded values would notice.
        /// </remarks>
        [Fact]
        public void AnIntTableDecodesToItsEntriesAndKeepsItsDefaultLast()
        {
            var definition = new EnumDefinition { Id = 169 }.Decode(new JagStream(IntTableWithDefault));

            Assert.False(definition.ValuesAreStrings);
            Assert.Equal(-1, definition.DefaultInt);

            EnumEntry only = Assert.Single(definition.Entries);
            Assert.Equal(1, only.Key);
            Assert.Equal(508, only.Number);

            Assert.Equal(new[] { 1, 2, 6, 4 },
                definition.Opcodes.Select(record => record.Opcode).ToArray());
        }

        /// <summary>
        ///     A default stored at the value an absent opcode would give is kept rather than dropped.
        /// </summary>
        [Fact]
        public void ADefaultStoredAtZeroSurvives()
        {
            var definition = new EnumDefinition { Id = 224 }.Decode(new JagStream(DefaultWithNoTable));

            Assert.Equal(0, definition.DefaultInt);
            Assert.True(definition.Opcodes.Has(4));
            Assert.Equal(DefaultWithNoTable, definition.Encode().ToArray());
        }

        /// <summary>A type byte above 0x7F survives as a byte, and its char is display only.</summary>
        [Fact]
        public void ANonAsciiTypeByteSurvives()
        {
            var definition = new EnumDefinition { Id = 3038 }.Decode(new JagStream(NonAsciiValueType));

            Assert.Equal(0xAB, definition.ValueTypeByte);

            //0xAB is outside the 0x80-0x9F band the client remaps, so it passes straight through -
            //written as a code point rather than as a literal so the assertion cannot depend on how
            //this source file happens to be encoded.
            Assert.Equal((char) 0xAB, definition.ValueTypeChar);
            Assert.Equal(NonAsciiValueType, definition.Encode().ToArray());
        }

        /// <summary>
        ///     An edit to an enum that carried no table adds one, and the file reads back.
        /// </summary>
        /// <remarks>
        ///     The opcode has to be chosen from the value shape rather than from the type char,
        ///     because the type char is a label the wire format does not consult.
        /// </remarks>
        [Fact]
        public void AddingATableEmitsTheOpcodeForItsValueShape()
        {
            var definition = new EnumDefinition { Id = 0 }.Decode(new JagStream(UnallocatedSlot));
            definition.ValuesAreStrings = true;
            definition.Entries.Add(new EnumEntry(7, "seven"));

            byte[] encoded = definition.Encode().ToArray();
            var reread = new EnumDefinition { Id = 0 }.Decode(new JagStream(encoded));

            Assert.Contains(5, reread.Opcodes.Select(record => record.Opcode));
            Assert.True(reread.ValuesAreStrings);
            EnumEntry only = Assert.Single(reread.Entries);
            Assert.Equal(7, only.Key);
            Assert.Equal("seven", only.Text);
        }

        /// <summary>An opcode the client does not handle is refused rather than desynchronising.</summary>
        /// <remarks>
        ///     <c>GameConfig.loadEnum</c> has no default arm - an unrecognised opcode consumes
        ///     nothing and the next payload byte is read as an opcode - so copying its loop would
        ///     silently eat any bug introduced here.
        /// </remarks>
        [Fact]
        public void UnknownOpcodesAreRefused()
        {
            foreach (byte opcode in new byte[] { 7, 8, 200 })
            {
                Assert.Throws<InvalidOperationException>(() =>
                    new EnumDefinition { Id = 0 }.Decode(new JagStream(new byte[] { opcode, 0, 0 })));
            }
        }
    }
}
