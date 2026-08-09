using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FlashEditor.Definitions.ClientScripts;
using Xunit;

namespace FlashEditor.Tests.Definitions.ClientScripts
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
        /// <summary>
        ///     Every citation names lines of the client, and the first of them is always the dispatch
        ///     arm.
        /// </summary>
        /// <remarks>
        ///     The first-entry rule is the sharp half. Most component rows now carry a second
        ///     citation - the renderer that reads the field, the provider that resolves the id, the
        ///     resolver that divides with the pair - and a row whose <i>only</i> evidence was that
        ///     second file would be a name taken from a field label rather than from behaviour, which
        ///     is exactly the mistake the model dump's field-name table made. Anchoring entry zero to
        ///     <c>Class247.java</c> makes that unwritable.
        /// </remarks>
        [Fact]
        public void EveryOpcodeEntry_CitesALineOfTheClient()
        {
            var arm = new Regex(@"^Class247\.java:\d+(-\d+)?$");
            var clientLine = new Regex(@"^[A-Za-z][A-Za-z0-9_]*\.java:\d+(-\d+)?$");
            var wrong = new List<string>();

            foreach (int opcode in ClientScriptOpcodes.NamedOpcodes)
            {
                ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);
                string[] cited = info.Citation.Split(", ");

                if (!arm.IsMatch(cited[0]))
                    wrong.Add($"opcode {opcode} does not cite its arm first: '{info.Citation}'");

                foreach (string entry in cited)
                    if (!clientLine.IsMatch(entry))
                        wrong.Add($"opcode {opcode} cites '{entry}', which is not a client file and line");

                if (string.IsNullOrWhiteSpace(info.Summary))
                    wrong.Add($"opcode {opcode} has no summary");
            }

            Assert.NotEmpty(ClientScriptOpcodes.NamedOpcodes);
            Assert.Empty(wrong);
        }

        /// <summary>
        ///     A named opcode in a folded range names its stack-addressed twin too.
        /// </summary>
        /// <remarks>
        ///     The two are literally one arm body reached with the component bound differently
        ///     (<c>Class247.java:408-418</c>), so a set that held only the low form would report every
        ///     2xxx instruction as unnamed while the grid beside it showed a name, and the coverage
        ///     line under the tab would under-report by exactly the size of this family.
        /// </remarks>
        [Fact]
        public void NamedOpcodes_CoverTheFoldedTwins()
        {
            var missing = new List<string>();
            var named = new HashSet<int>(ClientScriptOpcodes.NamedOpcodes);

            foreach (int opcode in named)
            {
                if (!ClientScriptOpcodes.TryResolveComponentAlias(opcode + 1000, out _))
                    continue;

                if (ClientScriptOpcodes.Describe(opcode).Addressing !=
                    ClientScriptComponentAddressing.ActiveComponent)
                    continue;

                if (!named.Contains(opcode + 1000))
                    missing.Add($"{opcode} is named but its twin {opcode + 1000} is not");
            }

            Assert.Empty(missing);
            Assert.Equal("cc_set_position", ClientScriptOpcodes.MnemonicOf(1000));
            Assert.Equal("if_set_position", ClientScriptOpcodes.MnemonicOf(2000));
        }

        /// <summary>
        ///     A stack-addressed opcode consumes exactly one more value than the twin it shares an arm
        ///     with, and that value is pushed last.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:411</c> pops the packed component before the arm body pops anything
        ///     else, so in push order it comes after everything. A list that put it first would tell a
        ///     script author to emit the whole call in the wrong order.
        /// </remarks>
        [Fact]
        public void TheStackAddressedForm_TakesItsComponentLast()
        {
            ClientScriptOpcodeInfo active = ClientScriptOpcodes.Describe(1000);
            ClientScriptOpcodeInfo stack = ClientScriptOpcodes.Describe(2000);

            Assert.Equal(ClientScriptComponentAddressing.ActiveComponent, active.Addressing);
            Assert.Equal(ClientScriptComponentAddressing.StackComponent, stack.Addressing);
            Assert.Equal(active.Operands.Slots.Count + 1, stack.Operands.Slots.Count);
            Assert.Equal("component", stack.Operands.Slots[stack.Operands.Slots.Count - 1].Name);
            Assert.Equal("x", stack.Operands.Slots[0].Name);
        }

        /// <summary>
        ///     An operand list nobody has read is blank, not "nothing".
        /// </summary>
        /// <remarks>
        ///     Roughly three quarters of the reachable dispatch has no row here at all, and a row that
        ///     has not been read cannot claim the opcode consumes nothing. The two states have to
        ///     render differently or the grid asserts knowledge it does not have on several hundred
        ///     rows.
        /// </remarks>
        [Fact]
        public void AnUnreadOperandList_RendersBlankRatherThanAsNothing()
        {
            Assert.Equal(string.Empty, ClientScriptOpcodes.Describe(3300).Operands.Text());
            Assert.False(ClientScriptOpcodes.Describe(3300).Operands.IsStated);

            Assert.Equal("nothing", ClientScriptOpcodes.Describe(101).Operands.Text());
            Assert.True(ClientScriptOpcodes.Describe(101).Operands.IsStated);
        }

        /// <summary>
        ///     Every named component opcode wears the prefix its addressing mode implies.
        /// </summary>
        /// <remarks>
        ///     The prefixes are the only thing on screen that says where an instruction's target comes
        ///     from, so a <c>cc_</c> on a stack-addressed arm would be a wrong statement about the
        ///     calling convention rather than a cosmetic slip - a script written from it would leave
        ///     one value on the stack.
        /// </remarks>
        [Fact]
        public void EveryComponentMnemonic_MatchesItsAddressing()
        {
            var wrong = new List<string>();

            foreach (int opcode in ClientScriptOpcodes.NamedOpcodes)
            {
                ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);
                string mnemonic = info.Mnemonic!;

                switch (info.Addressing)
                {
                    case ClientScriptComponentAddressing.ActiveComponent when !mnemonic.StartsWith("cc_"):
                        wrong.Add($"{opcode} '{mnemonic}' acts on the active component but is not cc_");
                        break;
                    case ClientScriptComponentAddressing.StackComponent when !mnemonic.StartsWith("if_"):
                        wrong.Add($"{opcode} '{mnemonic}' takes its component off the stack but is not if_");
                        break;
                    case ClientScriptComponentAddressing.None
                        when mnemonic.StartsWith("cc_") || mnemonic.StartsWith("if_"):
                        wrong.Add($"{opcode} '{mnemonic}' claims an addressing mode its row does not state");
                        break;
                }
            }

            Assert.Empty(wrong);
        }

        /// <summary>
        ///     The six item opcodes that share one arm body each get their own row.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:884</c> is one body reached by six numbers, told apart only by two
        ///     ternaries inside it. Four of the six are separable and named; 1212 and 1213 set exactly
        ///     the same pair of values with no test between them, so naming either would be inventing
        ///     a distinction. What every one of the six must have is its own description, or five of
        ///     them read on screen as opcodes this project has never looked at.
        /// </remarks>
        /// <param name="opcode">One of the six.</param>
        /// <param name="named">Whether the two ternaries settle a distinct meaning for it.</param>
        [Theory]
        [InlineData(1200, true)]
        [InlineData(1205, true)]
        [InlineData(1208, true)]
        [InlineData(1209, true)]
        [InlineData(1212, false)]
        [InlineData(1213, false)]
        public void TheSharedItemArm_DescribesEachOfItsSixOpcodes(int opcode, bool named)
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);

            Assert.Equal(named, info.IsNamed);
            Assert.Contains("item", info.Summary);
            Assert.Equal("Class247.java:884", info.Citation.Split(", ")[0]);
            Assert.Equal(2, info.Operands.Slots.Count);
        }

        /// <summary>
        ///     A hook setter's arity is decided at run time and the table says so.
        /// </summary>
        /// <remarks>
        ///     One shared body (<c>Class247.java:1219-1250</c>) pops a format string and then reads one
        ///     value per character of it. Any fixed count on these rows would be wrong for most calls.
        /// </remarks>
        [Fact]
        public void AHookSetter_StatesThatItsArityIsVariadic()
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(1407);

            Assert.True(info.Operands.IsVariadic);
            Assert.Null(info.Mnemonic);
            Assert.Contains("Hooks slot 5", info.Summary);
            Assert.Contains("Triggers slot 0", info.Summary);
        }

        /// <summary>
        ///     Opcode 2506 does not exist, because the arm that would serve it sits outside its guard.
        /// </summary>
        /// <remarks>
        ///     A <c>_DocumentsKnownDefect</c> row per the convention at
        ///     <c>FlashEditor.Tests/Cache/RSFileStoreTests.cs:12-20</c>. <c>if(i == 1506)</c> is
        ///     written at <c>Class247.java:1612</c>, inside the <c>i &lt; 2600</c> guard at
        ///     <c>:1573</c>, which is only entered for 2500..2599. It is a verbatim copy of the live
        ///     1506 at <c>:1368</c> and can never match where it stands, so the stack-addressed form of
        ///     that getter is missing from the build. If a later client ever dispatches 2506, this test
        ///     is the thing that has to change.
        /// </remarks>
        [Fact]
        public void Opcode2506_IsUnreachableInThisBuild_DocumentsKnownDefect()
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(2506);

            Assert.Null(info.Mnemonic);
            Assert.Contains("does not exist", info.Summary);
            Assert.Contains("Class247.java:1612", info.Citation);
            Assert.Contains("Class247.java:1573", info.Citation);
        }

        /// <summary>
        ///     Opcode 1615 does not exist, for the mirror-image reason.
        /// </summary>
        /// <remarks>
        ///     <c>if(i == 2614)</c> is written at <c>Class247.java:1477</c>, inside the
        ///     <c>i &lt; 1700</c> guard at <c>:1375</c>, which is only entered for 1600..1699. Its
        ///     evident intent was 1615, the active-component form of "get the model id", so that read
        ///     is reachable only through the live 2614 at <c>:1706</c>. Neither this nor 2506 is a
        ///     decompiler artefact: the condition itself is outside the guard, which no missing
        ///     <c>return</c> could explain.
        /// </remarks>
        [Fact]
        public void Opcode1615_IsUnreachableInThisBuild_DocumentsKnownDefect()
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(1615);

            Assert.Null(info.Mnemonic);
            Assert.Contains("does not exist", info.Summary);
            Assert.Contains("Class247.java:1477", info.Citation);

            //The live one is reachable and takes its component off the stack, with no twin above it.
            Assert.Equal(ClientScriptComponentAddressing.StackComponent,
                ClientScriptOpcodes.Describe(2614).Addressing);
        }

        /// <summary>
        ///     A number a folded block skips is reported as a hole rather than as unfinished work.
        /// </summary>
        /// <remarks>
        ///     The generic fallback reads "dispatched by method3148", which for 1002, 1121 and 1413 is
        ///     false: nothing in <c>Class247</c> tests for any of them. Both forms of each get a row so
        ///     the twin is never synthesised from one that describes a hole.
        /// </remarks>
        /// <param name="opcode">A number with no arm.</param>
        [Theory]
        [InlineData(1002)]
        [InlineData(2002)]
        [InlineData(1121)]
        [InlineData(2121)]
        [InlineData(1413)]
        [InlineData(2413)]
        public void ANumberWithNoArm_SaysSoRatherThanNamingADispatcher(int opcode)
        {
            ClientScriptOpcodeInfo info = ClientScriptOpcodes.Describe(opcode);

            Assert.Null(info.Mnemonic);
            Assert.StartsWith("No arm.", info.Summary);
            Assert.Equal(ClientScriptComponentAddressing.None, info.Addressing);
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
