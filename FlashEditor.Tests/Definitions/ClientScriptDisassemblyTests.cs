using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FlashEditor.Definitions.ClientScripts;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the opcode table's shape and the disassembler's branch arithmetic against synthetic
    ///     scripts, so both hold without a cache.
    /// </summary>
    /// <remarks>
    ///     Nothing here can say whether a mnemonic is <i>correct</i> - only the 637 client can, and
    ///     that evidence lives in the citations rather than in an assertion. What these tests defend
    ///     is everything around the names that a cache-backed sweep cannot see: that no mnemonic is
    ///     duplicated onto two opcodes, that every claim carries a checkable citation, that the
    ///     component alias covers the ranges the client pairs and no more, and that a jump resolves
    ///     to the instruction the interpreter would reach.
    /// </remarks>
    public sealed class ClientScriptDisassemblyTests
    {
        /// <summary>Every citation names a line of the client, so every claim can be checked.</summary>
        [Fact]
        public void EveryOpcodeEntry_CitesALineOfTheClient()
        {
            var pattern = new Regex(@"^Class247\.java:\d+(-\d+)?$");
            var wrong = new List<string>();

            foreach (int opcode in ClientScriptOpcodes.NamedOpcodes)
            {
                ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);

                if (!pattern.IsMatch(info.Citation))
                    wrong.Add($"opcode {opcode} cites '{info.Citation}'");
                if (string.IsNullOrWhiteSpace(info.Summary))
                    wrong.Add($"opcode {opcode} has no summary");
            }

            Assert.NotEmpty(ClientScriptOpcodes.NamedOpcodes);
            Assert.Empty(wrong);
        }

        /// <summary>
        ///     No mnemonic is attached to two opcodes.
        /// </summary>
        /// <remarks>
        ///     The cheapest way to mislabel this index is to copy a row and forget to change the
        ///     name, which would put a proven name on an unproven opcode and read exactly like a
        ///     proven one. Nothing in the client's dispatch gives two opcodes the same operation.
        /// </remarks>
        [Fact]
        public void NoMnemonic_IsUsedTwice()
        {
            var byName = new Dictionary<string, int>();
            var clashes = new List<string>();

            foreach (int opcode in ClientScriptOpcodes.NamedOpcodes.OrderBy(value => value))
            {
                string mnemonic = ClientScriptOpcodes.MnemonicOf(opcode)!;
                if (byName.TryGetValue(mnemonic, out int first))
                    clashes.Add($"'{mnemonic}' is on both {first} and {opcode}");
                else
                    byName[mnemonic] = opcode;
            }

            Assert.Empty(clashes);
        }

        /// <summary>
        ///     An opcode with no table row is still described rather than left blank.
        /// </summary>
        /// <remarks>
        ///     The tab shows the Effect column for every instruction, so a null here would be an
        ///     empty cell that reads as a decode failure rather than as an unnamed opcode.
        /// </remarks>
        [Theory]
        [InlineData(5)]
        [InlineData(99)]
        [InlineData(2426)]
        [InlineData(4999)]
        [InlineData(7314)]
        [InlineData(65535)]
        public void AnUnnamedOpcode_IsStillDescribed(int opcode)
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);

            Assert.Null(info.Mnemonic);
            Assert.False(info.IsNamed);
            Assert.False(string.IsNullOrWhiteSpace(info.Summary));
            Assert.False(string.IsNullOrWhiteSpace(info.Citation));
            Assert.Equal(opcode, info.Opcode);
        }

        /// <summary>
        ///     The component alias covers exactly the ranges the client pairs.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:403</c> and its four repeats admit 1000-1499 alongside 2000-2499, and
        ///     <c>:1545</c> pairs 1900-1999 with 2900-2999. 2500-2899 have their own arms and must not
        ///     be folded in - treating the whole 2000s as aliases would describe two hundred opcodes
        ///     as something they are not.
        /// </remarks>
        /// <param name="opcode">The opcode to resolve.</param>
        /// <param name="aliased">Whether the ranges pair here.</param>
        [Theory]
        [InlineData(1999, false)]
        [InlineData(2000, true)]
        [InlineData(2003, true)]
        [InlineData(2499, true)]
        [InlineData(2500, false)]
        [InlineData(2703, false)]
        [InlineData(2899, false)]
        [InlineData(2900, true)]
        [InlineData(2999, true)]
        [InlineData(3000, false)]
        public void TheComponentAlias_CoversTheRangesTheClientPairs(int opcode, bool aliased)
        {
            bool resolved = ClientScriptOpcodes.TryResolveComponentAlias(opcode, out int twin);

            Assert.Equal(aliased, resolved);
            Assert.Equal(aliased ? opcode - 1000 : opcode, twin);
        }

        /// <summary>
        ///     A branch resolves to the instruction the interpreter would execute next.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:7779</c> advances the counter before dispatching and the branch arm
        ///     then adds the delta, so the target is <c>position + 1 + delta</c>. A delta of -1 on
        ///     instruction 3 is therefore a jump to instruction 3 - an infinite loop, and the case
        ///     that separates this reading from every off-by-one alternative.
        /// </remarks>
        /// <param name="position">Where the branch sits.</param>
        /// <param name="delta">Its stored operand.</param>
        /// <param name="expected">The instruction reached.</param>
        [Theory]
        [InlineData(0, 0, 1)]
        [InlineData(3, -1, 3)]
        [InlineData(3, -4, 0)]
        [InlineData(0, 4, 5)]
        public void ABranch_ResolvesToPositionPlusOnePlusItsDelta(int position, int delta, int expected)
        {
            ClientScriptDefinition script = ScriptOf(8, (position, 6, delta));

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(script);

            Assert.Equal(expected, disassembly.Lines[position].BranchTarget);
            Assert.True(disassembly.Lines[expected].IsLabel);
            Assert.Equal(0, disassembly.UnresolvableTargets);
        }

        /// <summary>
        ///     A jump off either end of the script is reported rather than resolved or thrown.
        /// </summary>
        /// <remarks>
        ///     No script in either supported cache has one. It is carried instead of throwing so a
        ///     hand-edited script still lists with the defect visible, rather than taking the tab
        ///     down on selection.
        /// </remarks>
        /// <param name="delta">A delta that lands outside the script.</param>
        [Theory]
        [InlineData(-6)]
        [InlineData(100)]
        public void AJumpOffTheEnd_IsCountedRatherThanResolved(int delta)
        {
            ClientScriptDefinition script = ScriptOf(8, (4, 6, delta));

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(script);

            Assert.Null(disassembly.Lines[4].BranchTarget);
            Assert.Equal(1, disassembly.UnresolvableTargets);
        }

        /// <summary>
        ///     An instruction that does not branch has no target, whatever its operand holds.
        /// </summary>
        /// <remarks>
        ///     Opcode 0's operand is a constant and 33's is a local slot. Both are ordinary integers
        ///     in the same field a jump delta occupies, so resolving one as a target would draw a
        ///     control flow edge out of a push.
        /// </remarks>
        /// <param name="opcode">A non-branching opcode.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(33)]
        [InlineData(40)]
        public void ANonBranchingOpcode_HasNoTarget(int opcode)
        {
            ClientScriptDefinition script = ScriptOf(8, (4, opcode, 2));

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(script);

            Assert.Null(disassembly.Lines[4].BranchTarget);
            Assert.False(ClientScriptOpcodes.IsBranch(opcode));
            Assert.Equal(0, disassembly.UnresolvableTargets);
        }

        /// <summary>
        ///     A switch arm's delta is measured from the instruction that selects the block, not from
        ///     the block.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:7980</c> applies the arm's value to the same <c>current</c> the
        ///     interpreter is already holding, so one block reached from two opcode-51 instructions
        ///     resolves the same arm to two different places. Storing a target on the block would be
        ///     wrong for at least one of them.
        /// </remarks>
        [Fact]
        public void ASwitchArm_ResolvesAgainstEachInstructionThatSelectsIt()
        {
            ClientScriptDefinition script = ScriptOf(10, (2, 51, 0), (6, 51, 0));
            var block = new ClientScriptSwitchBlock();
            block.Cases.Add(new ClientScriptSwitchCase(7, 2));
            script.SwitchBlocks.Add(block);

            Assert.Equal(5, ClientScriptDisassembly.ResolveSwitchTarget(script, 2, 2));
            Assert.Equal(9, ClientScriptDisassembly.ResolveSwitchTarget(script, 6, 2));

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(script);

            Assert.Equal(0, disassembly.Lines[2].SwitchBlock);
            Assert.Equal(0, disassembly.Lines[6].SwitchBlock);
            Assert.True(disassembly.Lines[5].IsLabel);
            Assert.True(disassembly.Lines[9].IsLabel);
            Assert.Equal(0, disassembly.UnresolvableTargets);
        }

        /// <summary>The per-script naming figure counts instructions, not distinct opcodes.</summary>
        /// <remarks>
        ///     The two differ enormously here - a named opcode repeated a hundred times is a hundred
        ///     named instructions - and the tab reports the instruction figure, so it is the one worth
        ///     pinning.
        /// </remarks>
        [Fact]
        public void TheNamedCount_IsPerInstruction()
        {
            //Opcode 0 is named and 2426 is not, so four instructions over two opcodes score 3.
            ClientScriptDefinition script = ScriptOf(4, (1, 0, 0), (2, 0, 0), (3, 2426, 0));

            ClientScriptDisassembly disassembly = ClientScriptDisassembly.Of(script);

            Assert.Equal(4, disassembly.InstructionCount);
            Assert.Equal(3, disassembly.NamedInstructions);
            Assert.NotNull(ClientScriptOpcodes.MnemonicOf(0));
            Assert.Null(ClientScriptOpcodes.MnemonicOf(2426));
        }

        /// <summary>
        ///     Builds a script of <paramref name="count"/> instructions, opcode 0 unless overridden.
        /// </summary>
        /// <param name="count">How many instructions.</param>
        /// <param name="overrides">Position, opcode and operand for the instructions that differ.</param>
        /// <returns>The script.</returns>
        private static ClientScriptDefinition ScriptOf(int count, params (int Position, int Opcode, int Operand)[] overrides)
        {
            var script = new ClientScriptDefinition { Id = 0 };

            for (int position = 0; position < count; position++)
                script.Instructions.Add(new ClientScriptInstruction(0));

            foreach ((int position, int opcode, int operand) in overrides)
            {
                if (position < 0 || position >= count)
                    throw new ArgumentOutOfRangeException(nameof(overrides), position,
                        "An override outside the script would silently not apply.");

                script.Instructions[position] = new ClientScriptInstruction(opcode) { IntegerOperand = operand };
            }

            return script;
        }
    }
}
