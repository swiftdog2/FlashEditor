using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.ClientScripts;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-12 CS2 codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     Two sources, neither of them this project's encoder. <see cref="CapturedScript"/> is index
    ///     12 group 1679 verbatim - <c>RealCacheClientScriptTests</c> asserts it still is - and the
    ///     synthetic records below are laid out by hand to the read order in
    ///     <c>Class22.java:11-78</c>.
    ///     <para>
    ///     The synthetic half is not decoration. Three branches of this format occur nowhere in
    ///     either supported cache, so the byte-identity sweep passes whether or not they are right:
    ///     the leading name field, a string carrying a byte the cp1252 table cannot round trip, and
    ///     an empty switch section stored with no block-count byte. Each of those is a place where a
    ///     decoder that kept only the decoded value would silently rewrite a file, and only a
    ///     hand-built record can catch it.
    ///     </para>
    /// </remarks>
    public class ClientScriptDefinitionCodecTests
    {
        /// <summary>
        ///     Index 12 group 1679 exactly as both supported caches store it.
        /// </summary>
        /// <remarks>
        ///     Picked because it is 83 bytes and still covers every branch the shipped data has:
        ///     an absent name, all three operand widths - including opcode 3 with an empty string
        ///     and a carve-out opcode 21 - and a switch section holding one block of one case.
        /// </remarks>
        private static readonly byte[] CapturedScript =
        {
            0x00,                                            //no name
            0x00, 0x28, 0x00, 0x00, 0x06, 0x8E,              //opcode 40, integer 1678
            0x00, 0x2A, 0x00, 0x00, 0x04, 0x14,              //opcode 42, integer 1044
            0x00, 0x33, 0x00, 0x00, 0x00, 0x00,              //opcode 51, switch on block 0
            0x00, 0x06, 0x00, 0x00, 0x00, 0x02,              //opcode 6, integer 2
            0x00, 0x28, 0x00, 0x00, 0x06, 0x97,              //opcode 40, integer 1687
            0x00, 0x06, 0x00, 0x00, 0x00, 0x04,              //opcode 6, integer 4
            0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF,              //opcode 0, integer -1
            0x00, 0x03, 0x00,                                //opcode 3, empty string
            0x00, 0x00, 0x00, 0x55, 0x00, 0x00,              //opcode 0, integer 5570560
            0x09, 0x7A, 0x00,                                //opcode 2426, byte 0
            0x00, 0x15, 0x00,                                //opcode 21, byte 0 - a carve-out
            0x00, 0x00, 0x00, 0x0B,                          //footer: 11 instructions
            0x00, 0x00, 0x00, 0x00,                          //no integer or string locals
            0x00, 0x00, 0x00, 0x00,                          //no integer or string parameters
            0x01,                                            //one switch block
            0x00, 0x01,                                      //holding one case
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01,  //case 0 jumps forward 1
            0x00, 0x0B                                       //the switch section is 11 bytes
        };

        /// <summary>The group the captured bytes were read from.</summary>
        public const int CapturedScriptId = 1679;

        /// <summary>Those bytes, so the cache-backed test can compare without a second copy.</summary>
        /// <returns>A fresh copy of the captured record.</returns>
        public static byte[] CapturedScriptBytes() => (byte[]) CapturedScript.Clone();

        /// <summary>
        ///     A real script decodes into the instruction sequence the interpreter would run.
        /// </summary>
        /// <remarks>
        ///     This is what settles the operand-width rule. Nothing delimits an instruction, so a
        ///     reader that took opcode 21 as a four byte operand - it is below 100, and only the
        ///     carve-out saves it - would consume three bytes too many and land inside the footer
        ///     while still producing a plausible-looking decode.
        /// </remarks>
        [Fact]
        public void ACapturedScript_DecodesIntoTheInstructionsTheClientWouldRun()
        {
            var stream = new JagStream(CapturedScriptBytes());
            var script = new ClientScriptDefinition { Id = CapturedScriptId }.Decode(stream);

            Assert.Equal(CapturedScript.Length, stream.Position);
            Assert.Null(script.Name);
            Assert.Equal(11, script.Instructions.Count);

            Assert.Equal(new[] { 40, 42, 51, 6, 40, 6, 0, 3, 0, 2426, 21 },
                script.Instructions.Select(instruction => instruction.Opcode).ToArray());

            Assert.Equal(1678, script.Instructions[0].IntegerOperand);
            Assert.Equal(1044, script.Instructions[1].IntegerOperand);
            Assert.Equal(0, script.Instructions[2].IntegerOperand);
            Assert.Equal(-1, script.Instructions[6].IntegerOperand);
            Assert.Equal(5570560, script.Instructions[8].IntegerOperand);

            //The empty string operand is a single terminator byte, and it is not the same shape as
            //an absent one: opcode 3 always carries a field, unlike the leading name.
            Assert.Equal(ClientScriptOperandKind.Text, script.Instructions[7].OperandKind);
            Assert.Equal(string.Empty, script.Instructions[7].TextOperand);
            Assert.Empty(script.Instructions[7].TextOperandBytes);

            //Both byte-width shapes: an opcode above the ceiling, and one of the three below it.
            Assert.Equal(ClientScriptOperandKind.Byte, script.Instructions[9].OperandKind);
            Assert.Equal(ClientScriptOperandKind.Byte, script.Instructions[10].OperandKind);

            Assert.Equal(0, script.IntegerLocalCount);
            Assert.Equal(0, script.StringLocalCount);
            Assert.Equal(0, script.IntegerParameterCount);
            Assert.Equal(0, script.StringParameterCount);

            ClientScriptSwitchBlock block = Assert.Single(script.SwitchBlocks);
            ClientScriptSwitchCase arm = Assert.Single(block.Cases);
            Assert.Equal(0, arm.Value);
            Assert.Equal(1, arm.JumpOffset);
            Assert.False(script.OmitsSwitchBlockCount);
            Assert.Equal(11, script.SwitchSectionLength);
        }

        /// <summary>A real script re-encodes to the bytes it was read from.</summary>
        [Fact]
        public void ACapturedScript_ReEncodesToTheCapturedBytes()
        {
            var script = new ClientScriptDefinition { Id = CapturedScriptId }
                .Decode(new JagStream(CapturedScriptBytes()));

            Assert.Equal(CapturedScriptBytes(), script.Encode().ToArray());
        }

        /// <summary>
        ///     The operand width follows the client's chain, carve-outs included.
        /// </summary>
        /// <remarks>
        ///     Stated over the whole range rather than by example, because the failure this guards
        ///     against is one missed value out of a hundred and it would only show up as a
        ///     desynchronised stream several instructions later.
        /// </remarks>
        [Fact]
        public void TheOperandWidth_FollowsTheOpcodeExactlyAsTheClientReadsIt()
        {
            var carveOuts = new HashSet<int>(ClientScriptInstruction.NarrowOperandExceptions);

            for (int opcode = 0 ; opcode < ClientScriptInstruction.WideOperandCeiling ; opcode++)
            {
                ClientScriptOperandKind expected =
                    opcode == ClientScriptInstruction.TextOpcode ? ClientScriptOperandKind.Text
                    : carveOuts.Contains(opcode) ? ClientScriptOperandKind.Byte
                    : ClientScriptOperandKind.Integer;

                Assert.Equal(expected, ClientScriptInstruction.OperandKindOf(opcode));
            }

            //Everything at or above the ceiling is a byte, across all three client dispatchers.
            foreach (int opcode in new[] { 100, 2426, 4999, 5000, 7314, 9999, 0xFFFF })
                Assert.Equal(ClientScriptOperandKind.Byte, ClientScriptInstruction.OperandKindOf(opcode));

            //The three carve-outs are the sub-100 opcodes the interpreter reads no operand for.
            Assert.Equal(new[] { 21, 38, 39 }, ClientScriptInstruction.NarrowOperandExceptions);
        }

        /// <summary>The four counts in the footer are read in the client's order.</summary>
        /// <remarks>
        ///     All four are 16-bit and adjacent, so any permutation of them consumes the same twelve
        ///     bytes and no sweep over the cache can tell them apart - 1602 of the declared scripts
        ///     have all four at zero. The order is <c>Class22.java:26-29</c>, and which pair means
        ///     what is <c>Class247.java:7881-7893</c>: the first two size the callee's local arrays
        ///     and the second two are how many values are popped off the caller's stacks into them.
        /// </remarks>
        [Fact]
        public void TheFooterCounts_AreReadInTheClientsOrder()
        {
            byte[] record = Minimal(footer: new byte[]
            {
                0x00, 0x00, 0x00, 0x00,  //no instructions
                0x00, 0x01,              //integer locals
                0x00, 0x02,              //string locals
                0x00, 0x03,              //integer parameters
                0x00, 0x04               //string parameters
            });

            var script = new ClientScriptDefinition().Decode(new JagStream(record));

            Assert.Equal(1, script.IntegerLocalCount);
            Assert.Equal(2, script.StringLocalCount);
            Assert.Equal(3, script.IntegerParameterCount);
            Assert.Equal(4, script.StringParameterCount);
            Assert.Equal(record, script.Encode().ToArray());
        }

        /// <summary>
        ///     A script that stores its empty switch section with no count byte is written back the
        ///     same way.
        /// </summary>
        /// <remarks>
        ///     The one aliased encoding this format allows, and neither cache contains it: all 4149
        ///     declared scripts write a section length of 1 and a zero count byte. A length of 0
        ///     decodes identically in the client - the count byte it reads is then the high byte of
        ///     the trailing length field, which is itself 0 - so the two forms cannot be told apart
        ///     from the decoded content, and an encoder that always derived the length would grow
        ///     such a file by one byte, change its CRC, and drag in the reference-table entry of
        ///     every archive packed beside it. Nothing in the cache would have caught that, which is
        ///     why the choice is recorded and pinned here.
        /// </remarks>
        [Fact]
        public void AnEmptySwitchSectionStoredWithNoCountByte_IsWrittenBackThatWay()
        {
            byte[] withCountByte = Minimal();
            byte[] withoutCountByte = MinimalWithNoSwitchSection();

            var stored = new ClientScriptDefinition().Decode(new JagStream(withCountByte));
            var omitted = new ClientScriptDefinition().Decode(new JagStream(withoutCountByte));

            //Both decode to exactly the same script.
            Assert.Empty(stored.SwitchBlocks);
            Assert.Empty(omitted.SwitchBlocks);
            Assert.Empty(stored.Instructions);
            Assert.Empty(omitted.Instructions);

            //And the two forms differ by the byte neither decode can see.
            Assert.False(stored.OmitsSwitchBlockCount);
            Assert.True(omitted.OmitsSwitchBlockCount);
            Assert.Equal(1, stored.SwitchSectionLength);
            Assert.Equal(0, omitted.SwitchSectionLength);

            Assert.Equal(withCountByte, stored.Encode().ToArray());
            Assert.Equal(withoutCountByte, omitted.Encode().ToArray());
            Assert.Equal(withCountByte.Length - 1, withoutCountByte.Length);
        }

        /// <summary>
        ///     A string operand carrying a byte the cp1252 table cannot represent survives a round
        ///     trip.
        /// </summary>
        /// <remarks>
        ///     Five byte values in the 0x80-0x9F band are unassigned; <c>ReadJagexString</c> hands
        ///     back <c>'?'</c> for each and <c>WriteJagexString</c> writes 0x3F for that, so a codec
        ///     holding only the decoded text rewrites the byte. No string operand in either cache
        ///     carries one - 82 bytes above 0x7F occur and all of them map - so this branch is
        ///     invisible to the sweep and would rot unnoticed.
        /// </remarks>
        [Fact]
        public void AStringOperandCarryingAnUnmappedByte_SurvivesTheRoundTrip()
        {
            byte[] record = Minimal(
                instructions: new byte[] { 0x00, 0x03, 0x81, 0x00 },
                instructionCount: 1);

            var script = new ClientScriptDefinition().Decode(new JagStream(record));
            ClientScriptInstruction instruction = Assert.Single(script.Instructions);

            Assert.Equal(new byte[] { 0x81 }, instruction.TextOperandBytes);
            Assert.Equal("?", instruction.TextOperand);
            Assert.Equal(record, script.Encode().ToArray());

            //Assigning the text is what loses the byte, and it has to be the caller's choice rather
            //than something decoding did on their behalf.
            string asText = instruction.TextOperand;
            instruction.TextOperand = asText;
            Assert.Equal(new byte[] { 0x3F }, instruction.TextOperandBytes);
        }

        /// <summary>
        ///     The leading name survives a round trip, including a byte the cp1252 table cannot
        ///     represent.
        /// </summary>
        /// <remarks>
        ///     Not one script in either cache carries a name at all, so every branch of this field
        ///     rests on this test.
        /// </remarks>
        [Fact]
        public void ALeadingName_SurvivesTheRoundTripIncludingAnUnmappedByte()
        {
            byte[] named = Minimal(name: new byte[] { (byte) 'h', (byte) 'i', 0x81, 0x00 });

            var script = new ClientScriptDefinition().Decode(new JagStream(named));

            Assert.Equal(new byte[] { (byte) 'h', (byte) 'i', 0x81 }, script.NameBytes);
            Assert.Equal("hi?", script.Name);
            Assert.Equal(named, script.Encode().ToArray());
        }

        /// <summary>
        ///     An empty name is the same thing as an absent one, and is stored as one.
        /// </summary>
        /// <remarks>
        ///     <c>RSBuffer.method1222(-1)</c> returns null the instant the first byte is 0, so a
        ///     zero-length name cannot be expressed on the wire. Normalising it away here is what
        ///     stops an editor writing a record it would read back differently.
        /// </remarks>
        [Fact]
        public void AnEmptyName_IsStoredAsAnAbsentOne()
        {
            byte[] unnamed = Minimal();
            var script = new ClientScriptDefinition().Decode(new JagStream(unnamed));

            Assert.Null(script.Name);

            script.Name = string.Empty;
            Assert.Null(script.Name);
            Assert.Null(script.NameBytes);
            Assert.Equal(unnamed, script.Encode().ToArray());

            script.Name = "x";
            Assert.Equal("x", script.Name);
            Assert.Equal(unnamed.Length + 1, script.Encode().ToArray().Length);
        }

        /// <summary>A footer whose instruction count disagrees with the stream is rejected.</summary>
        /// <remarks>
        ///     The count is the format's own statement of what the instruction stream holds, so a
        ///     disagreement means the operand widths are wrong. Reporting it beats decoding on: a
        ///     desynchronised stream still produces instructions, and they are all wrong.
        /// </remarks>
        [Fact]
        public void AFooterDeclaringTheWrongInstructionCount_IsRejected()
        {
            byte[] record = CapturedScriptBytes();
            record[61] = 0x0A;  //the footer's instruction count, one short of the eleven present

            var exception = Assert.Throws<InvalidOperationException>(
                () => new ClientScriptDefinition().Decode(new JagStream(record)));

            Assert.Contains("declares 10 instructions", exception.Message);
        }

        /// <summary>A trailer whose length does not match the switch blocks it covers is rejected.</summary>
        /// <remarks>
        ///     Both failure modes, because they are caught in different places: a length that will
        ///     not fit the record at all puts the footer before the name byte, and one that fits but
        ///     over-states the section leaves the blocks ending short of the trailer.
        /// </remarks>
        [Fact]
        public void ASwitchSectionLengthThatDoesNotCoverItsBlocks_IsRejected()
        {
            byte[] overstated = Minimal(
                instructions: new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 },
                instructionCount: 1);
            overstated[overstated.Length - 1] = 0x03;  //the section is one byte, not three

            var mismatch = Assert.Throws<InvalidOperationException>(
                () => new ClientScriptDefinition().Decode(new JagStream(overstated)));
            Assert.Contains("switch section ends at", mismatch.Message);

            byte[] impossible = Minimal();
            impossible[impossible.Length - 1] = 0x40;  //longer than the whole record

            var offset = Assert.Throws<InvalidOperationException>(
                () => new ClientScriptDefinition().Decode(new JagStream(impossible)));
            Assert.Contains("cannot start below 1", offset.Message);
        }

        /// <summary>An operand too wide for the byte its opcode stores it in is refused.</summary>
        /// <remarks>
        ///     Masking it would write a file that decodes to a different instruction, and the editor
        ///     would report the save as successful.
        /// </remarks>
        [Fact]
        public void AByteOperandThatDoesNotFitAByte_IsRefusedRatherThanMasked()
        {
            var script = new ClientScriptDefinition();
            script.Instructions.Add(new ClientScriptInstruction(2426) { IntegerOperand = 256 });

            Assert.Throws<InvalidOperationException>(() => script.Encode());
        }

        /// <summary>An opcode too wide for the two bytes that store it is refused.</summary>
        [Fact]
        public void AnOpcodeThatDoesNotFitTwoBytes_IsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClientScriptInstruction(0x10000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClientScriptInstruction(-1));
        }

        /// <summary>A record shorter than the fixed fields it must contain is rejected.</summary>
        [Fact]
        public void ARecordTooShortToHoldItsFixedFields_IsRejected()
        {
            Assert.Throws<InvalidOperationException>(
                () => new ClientScriptDefinition().Decode(new JagStream(new byte[8])));
        }

        /// <summary>
        ///     Builds the smallest well-formed script, optionally with a name and instructions.
        /// </summary>
        /// <param name="name">The name field including its terminator, or <c>null</c> for the absent marker.</param>
        /// <param name="instructions">The instruction stream, or <c>null</c> for none.</param>
        /// <param name="instructionCount">What the footer should declare.</param>
        /// <param name="footer">The whole twelve byte footer, overriding <paramref name="instructionCount"/>.</param>
        /// <returns>The encoded record.</returns>
        private static byte[] Minimal(byte[] name = null, byte[] instructions = null,
            int instructionCount = 0, byte[] footer = null)
        {
            var record = new List<byte>();
            record.AddRange(name ?? new byte[] { 0x00 });
            record.AddRange(instructions ?? Array.Empty<byte>());

            if (footer != null)
            {
                record.AddRange(footer);
            }
            else
            {
                record.AddRange(new byte[]
                {
                    (byte) (instructionCount >> 24), (byte) (instructionCount >> 16),
                    (byte) (instructionCount >> 8), (byte) instructionCount,
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
                });
            }

            record.Add(0x00);              //no switch blocks
            record.AddRange(new byte[] { 0x00, 0x01 });  //the section is the count byte alone
            return record.ToArray();
        }

        /// <summary>
        ///     Builds the same script with the switch section absent entirely rather than empty.
        /// </summary>
        /// <returns>The encoded record.</returns>
        private static byte[] MinimalWithNoSwitchSection()
        {
            return new byte[]
            {
                0x00,                                            //no name
                0x00, 0x00, 0x00, 0x00,                          //no instructions
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  //no locals, no parameters
                0x00, 0x00                                       //the switch section is 0 bytes
            };
        }
    }
}
