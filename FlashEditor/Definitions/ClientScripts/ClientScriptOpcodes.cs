using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     The CS2 opcode table, built from the 637 client's own dispatch rather than from any
    ///     published name list.
    /// </summary>
    /// <remarks>
    ///     <b>Every mnemonic here is proven by a line of <c>Class247</c> that this file cites.</b>
    ///     Opcodes whose arm reaches a helper this project has not read keep the number and carry a
    ///     description instead, so a reader can always tell a settled name from an open question.
    ///     <para>
    ///     <b>Why nothing was taken from RuneStar.</b> <c>RuneStar/cs2</c> carries a good opcode
    ///     table and it is for Old School RuneScape, revisions 194 to 199 - its sibling
    ///     <c>cs2-scripts</c> commits are literally named after those revisions. The numbering in the
    ///     RS2 lineage this cache belongs to is not the same numbering, and the divergence is
    ///     checkable in one line rather than by inference: RuneStar declares <c>SWITCH = 60</c> and
    ///     has no opcode 51, 86 or 87 at all, while this cache holds 831 opcode-51 instructions that
    ///     the 637 client dispatches as a switch at <c>Class247.java:7975</c> and 4,411 opcode-86 and
    ///     87 branches it dispatches at <c>:7982</c> and <c>:7986</c>. Adopting that table would have
    ///     silently mislabelled every switch in the index. The <i>vocabulary</i> it uses is
    ///     conventional and worth matching where the 637 dispatch independently lands on the same
    ///     operation, and the names below do; the number-to-name binding is re-derived here in every
    ///     case.
    ///     </para>
    ///     <para>
    ///     <b>The 32 opcodes below 100 carry 85.62% of the index's 335,158 instructions</b>, measured
    ///     over the vanilla b639 capture, and they are exactly the set the client's in-line chain at
    ///     <c>Class247.java:7781-7988</c> handles. That is why a first pass is worth having at all:
    ///     the whole high-frequency core is one screen of readable dispatch, and the long tail of
    ///     roughly 550 further opcodes is the part that costs.
    ///     </para>
    ///     <para>
    ///     <b>The <c>cc_</c> and <c>if_</c> prefixes name an addressing mode, and only one of the two
    ///     words is the client's.</b> <c>cc_</c> is written in this client, twice, in opcode 101's
    ///     exception messages at <c>Class247.java:246</c> and <c>:249</c>. <c>if_</c> is written
    ///     nowhere in its 854 source files: it is the conventional spelling for the form that pops its
    ///     target component off the stack, and the <i>mechanism</i> is what is proven here -
    ///     <c>Class247.java:408-418</c> reaches one arm body with the component bound either from the
    ///     interpreter's active register or from a popped
    ///     <c>(interfaceId &lt;&lt; 16) | componentId</c>. The split is re-derived and only the word is
    ///     borrowed, on the same terms as the vocabulary argument above. The tab says so on screen,
    ///     because its notice promises names are carried only where the client's dispatch proves them
    ///     and a borrowed prefix has to be declared rather than assumed harmless.
    ///     </para>
    /// </remarks>
    public static class ClientScriptOpcodes {
        /// <summary>Lowest opcode the two dispatchers above the in-line chain handle.</summary>
        private const int FirstDispatchedOpcode = 100;

        /// <summary>The prefix on a mnemonic whose target is the interpreter's active component.</summary>
        private const string ActivePrefix = "cc_";

        /// <summary>The prefix on a mnemonic whose target is popped off the integer stack.</summary>
        private const string StackPrefix = "if_";

        /// <summary>First opcode handled by <c>Class247.method3156</c> rather than <c>method3148</c>.</summary>
        private const int SecondDispatcherFloor = 5000;

        /// <summary>First opcode no dispatcher handles at all.</summary>
        /// <remarks>
        ///     <c>Class247.java:7997</c> breaks out of the interpreter loop for anything at or above
        ///     it, which raises <c>IllegalStateException</c>. No script in either supported cache
        ///     holds one.
        /// </remarks>
        private const int UndispatchedFloor = 10000;

        /// <summary>
        ///     Component opcodes in these ranges are the same operation as the range a thousand below,
        ///     reached with the target component popped off the stack.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:403-418</c> is the pattern, repeated at <c>:526</c>, <c>:872</c>,
        ///     <c>:1034</c>, <c>:1206</c> and <c>:1545</c>: the branch condition admits both ranges,
        ///     then <c>if(i >= 2000) { i -= 1000; ... }</c> pops a packed component id and resolves it,
        ///     where the low form instead takes the interpreter's current active component. So a 2xxx
        ///     opcode consumes one more integer than its 1xxx twin and is otherwise identical, and any
        ///     name proven for the twin is proven for both.
        ///     <para>
        ///     The pairing does <b>not</b> extend to 2500-2899, which have their own arms, which is
        ///     why this is a table of ranges rather than a blanket rule about the 2000s.
        ///     </para>
        /// </remarks>
        private static readonly (int Low, int High)[] ComponentAliasRanges = {
            (2000, 2499),
            (2900, 2999)
        };

        /// <summary>
        ///     Opcodes that move the program counter by an operand measured in instructions.
        /// </summary>
        /// <remarks>
        ///     Every one of them is an arm of the in-line chain that ends in
        ///     <c>current += is_265_[current]</c>. Opcode 51 is excluded because its delta comes from
        ///     a switch block rather than from its own operand.
        /// </remarks>
        private static readonly HashSet<int> BranchOpcodes = new HashSet<int> { 6, 7, 8, 9, 10, 31, 32, 86, 87 };

        private static readonly Dictionary<int, ClientScriptOpcodeInfo> Table = Build();

        /// <summary>Opcodes this table carries a proven mnemonic for.</summary>
        public static IReadOnlyCollection<int> NamedOpcodes { get; } = BuildNamedSet();

        /// <summary>The opcode that selects one of the script's switch blocks.</summary>
        /// <remarks><c>Class247.java:7975-7981</c>. Its operand indexes the block array.</remarks>
        public const int SwitchOpcode = 51;

        /// <summary>The opcode that ends the current script or returns to its caller.</summary>
        /// <remarks><c>Class247.java:7820-7832</c>, which is what makes it a basic-block terminator.</remarks>
        public const int ReturnOpcode = 21;

        /// <summary>The unconditional jump, which unlike the rest never falls through.</summary>
        /// <remarks><c>Class247.java:7794-7795</c>.</remarks>
        public const int JumpOpcode = 6;

        /// <summary>
        ///     Everything known about an opcode, which is never nothing.
        /// </summary>
        /// <remarks>
        ///     An opcode with no entry still gets a description naming the dispatcher that would
        ///     handle it and, in the aliased ranges, the twin whose arm it shares - both of which
        ///     follow from the number arithmetically and are therefore as sound as a table row.
        /// </remarks>
        /// <param name="opcode">The stored opcode.</param>
        /// <returns>The opcode's entry, synthesised when the table has no row for it.</returns>
        public static ClientScriptOpcodeInfo Describe(int opcode) {
            if (Table.TryGetValue(opcode, out ClientScriptOpcodeInfo? known))
                return known;

            if (TryResolveComponentAlias(opcode, out int twin)) {
                if (Table.TryGetValue(twin, out ClientScriptOpcodeInfo? twinInfo) &&
                    twinInfo.Addressing == ClientScriptComponentAddressing.ActiveComponent)
                    return StackAddressedForm(opcode, twin, twinInfo);

                return new ClientScriptOpcodeInfo(opcode, null,
                    "Not yet named. Shares its arm with opcode " + twin +
                    ", taking the target component off the stack instead of using the active one.",
                    "Class247.java:403-418");
            }

            if (opcode < FirstDispatchedOpcode)
                return new ClientScriptOpcodeInfo(opcode, null,
                    "Not yet named. Below 100 and so an arm of the interpreter's in-line chain.",
                    "Class247.java:7781-7988");

            if (opcode < SecondDispatcherFloor)
                return new ClientScriptOpcodeInfo(opcode, null,
                    "Not yet named. Dispatched by method3148, whose operand is a flag rather than a value.",
                    "Class247.java:187");

            if (opcode < UndispatchedFloor)
                return new ClientScriptOpcodeInfo(opcode, null,
                    "Not yet named. Dispatched by method3156, whose operand is a flag rather than a value.",
                    "Class247.java:4139");

            return new ClientScriptOpcodeInfo(opcode, null,
                "No dispatcher handles this at all - the interpreter breaks out of its loop and throws.",
                "Class247.java:7997-7998");
        }

        /// <summary>
        ///     The proven mnemonic, or <c>null</c> where none has been established.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="Describe"/> rather than straight off the table, so a folded twin
        ///     answers with its own name. Naming 1000 and reading the table directly would leave 2000
        ///     unnamed on screen and in every coverage figure, even though the two are literally the
        ///     same arm body.
        /// </remarks>
        /// <param name="opcode">The stored opcode.</param>
        /// <returns>The mnemonic or null.</returns>
        public static string? MnemonicOf(int opcode) {
            return Describe(opcode).Mnemonic;
        }

        /// <summary>Whether the opcode's operand is a program-counter delta in instructions.</summary>
        /// <param name="opcode">The stored opcode.</param>
        /// <returns>Whether it branches.</returns>
        public static bool IsBranch(int opcode) {
            return BranchOpcodes.Contains(opcode);
        }

        /// <summary>
        ///     Maps a component opcode onto the twin whose dispatch arm it shares.
        /// </summary>
        /// <param name="opcode">The stored opcode.</param>
        /// <param name="twin">The opcode a thousand below, when the ranges pair.</param>
        /// <returns>Whether this opcode is the stack-addressed form of another.</returns>
        public static bool TryResolveComponentAlias(int opcode, out int twin) {
            foreach ((int low, int high) in ComponentAliasRanges) {
                if (opcode >= low && opcode <= high) {
                    twin = opcode - 1000;
                    return true;
                }
            }

            twin = opcode;
            return false;
        }

        /// <summary>
        ///     The stack-addressed twin's entry, built from the arm the two share.
        /// </summary>
        /// <remarks>
        ///     The mnemonic is the base's with <c>cc_</c> swapped for <c>if_</c>, and only that: a
        ///     base named anything else declines rather than inventing a spelling, because the swap is
        ///     the only part of the naming this file can perform mechanically.
        /// </remarks>
        /// <param name="opcode">The stack-addressed opcode.</param>
        /// <param name="twin">The active-component opcode a thousand below.</param>
        /// <param name="twinInfo">The twin's entry.</param>
        /// <returns>The twin's entry rewritten for the stack-addressed form.</returns>
        private static ClientScriptOpcodeInfo StackAddressedForm(int opcode, int twin,
            ClientScriptOpcodeInfo twinInfo) {
            string? mnemonic = twinInfo.Mnemonic != null && twinInfo.Mnemonic.StartsWith(ActivePrefix, StringComparison.Ordinal)
                ? StackPrefix + twinInfo.Mnemonic.Substring(ActivePrefix.Length)
                : null;

            return new ClientScriptOpcodeInfo(opcode, mnemonic,
                twinInfo.Summary + " Reached with the target popped off the stack rather than taken from the " +
                "active component, which is opcode " + twin + ".",
                twinInfo.Citation + ", Class247.java:408-418",
                twinInfo.Operands.WithStackComponent(),
                ClientScriptComponentAddressing.StackComponent);
        }

        /// <summary>
        ///     The opcodes carrying a mnemonic, so a coverage sweep can enumerate them.
        /// </summary>
        /// <remarks>
        ///     The folded twins are added here rather than left to <see cref="Describe"/>, because
        ///     this set is what the tab measures its mnemonic column against and what a coverage sweep
        ///     intersects with the index. A set that held only the table's own keys would report every
        ///     2xxx instruction as unnamed while the grid beside it showed a name.
        /// </remarks>
        /// <returns>The named opcodes.</returns>
        private static HashSet<int> BuildNamedSet() {
            var named = new HashSet<int>();

            foreach (KeyValuePair<int, ClientScriptOpcodeInfo> entry in Table) {
                if (!entry.Value.IsNamed)
                    continue;

                named.Add(entry.Key);

                if (entry.Value.Addressing == ClientScriptComponentAddressing.ActiveComponent &&
                    TryResolveComponentAlias(entry.Key + 1000, out _))
                    named.Add(entry.Key + 1000);
            }

            return named;
        }

        /// <summary>Builds the table, one row per opcode this project has read the arm for.</summary>
        /// <returns>The opcode table.</returns>
        private static Dictionary<int, ClientScriptOpcodeInfo> Build() {
            var table = new Dictionary<int, ClientScriptOpcodeInfo>();

            AddInLineChain(table);
            AddArithmetic(table);
            AddStringOperations(table);
            AddComponentLifecycle(table);
            AddComponentMutators(table);
            AddComponentHooks(table);
            AddComponentAccessors(table);

            return table;
        }

        /// <summary>Adds one row.</summary>
        /// <param name="table">The table being built.</param>
        /// <param name="opcode">The opcode.</param>
        /// <param name="mnemonic">The proven mnemonic, or null to describe it without naming it.</param>
        /// <param name="summary">What the dispatch arm does.</param>
        /// <param name="line">The line of <c>Class247.java</c> that proves it.</param>
        private static void Add(IDictionary<int, ClientScriptOpcodeInfo> table, int opcode, string? mnemonic,
            string summary, int line) {
            table[opcode] = new ClientScriptOpcodeInfo(opcode, mnemonic, summary, "Class247.java:" + line);
        }

        /// <summary>
        ///     Adds one row of the component family, which states two things the rest of the table
        ///     does not.
        /// </summary>
        /// <remarks>
        ///     A component opcode's operand byte is a register selector rather than a value, and its
        ///     stack arguments are the whole of its calling convention, so a row that omitted either
        ///     would be wrong rather than merely thin. <paramref name="alsoCite"/> carries the second
        ///     line for the rows whose name rests on a consumer outside <c>Class247</c> - a renderer
        ///     that reads the field, a provider that resolves the id - which is most of the ones that
        ///     are named at all.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        /// <param name="opcode">The opcode.</param>
        /// <param name="mnemonic">The proven mnemonic, or null to describe it without naming it.</param>
        /// <param name="summary">What the dispatch arm does.</param>
        /// <param name="line">The line of <c>Class247.java</c> the arm begins on.</param>
        /// <param name="operands">What it consumes off the stacks, in push order.</param>
        /// <param name="addressing">Where its target component comes from.</param>
        /// <param name="alsoCite">A further <c>file:line</c> the name rests on, or null.</param>
        private static void AddComponent(IDictionary<int, ClientScriptOpcodeInfo> table, int opcode, string? mnemonic,
            string summary, int line, ClientScriptStackOperands operands,
            ClientScriptComponentAddressing addressing, string? alsoCite = null) {
            string citation = "Class247.java:" + line;
            if (alsoCite != null)
                citation += ", " + alsoCite;

            table[opcode] = new ClientScriptOpcodeInfo(opcode, mnemonic, summary, citation, operands, addressing);
        }

        /// <summary>
        ///     The 32 opcodes below 100, which the interpreter handles in line.
        /// </summary>
        /// <remarks>
        ///     Read straight off <c>Class247.java:7781-7988</c>, where <c>is_265_[current]</c> is the
        ///     operand array and <c>current</c> is the program counter. Two of the names are settled
        ///     by evidence a step further out rather than by the arm itself:
        ///     <list type="bullet">
        ///     <item>
        ///     <b>varp against varbit.</b> Opcodes 1 and 2 index <c>Class140.anIntArray3244</c>
        ///     directly, while 25 and 27 go through <c>Class140.method7</c> and <c>method2289</c>,
        ///     which look a <c>VarBit</c> definition up by id and then read or write
        ///     <c>anIntArray3244[varBit.anInt3115] &gt;&gt; fromBit</c> masked to
        ///     <c>toBit - fromBit</c> (<c>Class140.java:193-208</c> and <c>:137-153</c>). So the same
        ///     array is the player-variable store and a varbit is a bitfield within one of its
        ///     entries, which settles all four at once rather than one at a time.
        ///     </item>
        ///     <item>
        ///     <b>The jump is in instructions.</b> Every branch ends in
        ///     <c>current += is_265_[current]</c> where <c>current</c> is the index the counter was
        ///     just advanced to by <c>OPCODE = is[++current]</c> (<c>:7779</c>), so the next
        ///     instruction executed is at <c>position + 1 + delta</c>. Measured over the vanilla
        ///     capture: all 42,884 branches in the index land on a real instruction under that
        ///     reading, and exactly one does not under <c>position + delta</c> - script 686's
        ///     unconditional jump at position 8 has a delta of -9 in a 13-instruction script, which
        ///     is a loop back to instruction 0 under the correct reading and instruction -1 under the
        ///     other. One witness, not an aggregate.
        ///     </item>
        ///     </list>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddInLineChain(IDictionary<int, ClientScriptOpcodeInfo> table) {
            Add(table, 0, "push_constant_int", "Pushes the operand onto the integer stack.", 7782);
            Add(table, 1, "push_varp", "Pushes player variable [operand] onto the integer stack.", 7784);
            Add(table, 2, "pop_varp", "Pops the integer stack into player variable [operand].", 7788);
            Add(table, 3, "push_constant_string", "Pushes the string operand onto the string stack.", 7792);
            Add(table, 6, "jump", "Unconditional. Next instruction is this position + 1 + operand.", 7794);
            Add(table, 7, "branch_not_equal", "Pops two integers and jumps when they differ.", 7796);
            Add(table, 8, "branch_equal", "Pops two integers and jumps when they are equal.", 7802);
            Add(table, 9, "branch_less", "Pops two integers and jumps when the first is smaller.", 7808);
            Add(table, 10, "branch_greater", "Pops two integers and jumps when the first is larger.", 7814);
            Add(table, 21, "return",
                "Returns to the calling script, or ends the run when the invocation stack is empty.", 7820);
            Add(table, 25, "push_varbit", "Pushes varbit [operand], a bitfield within a player variable.", 7833);
            Add(table, 27, "pop_varbit", "Pops the integer stack into varbit [operand].", 7837);
            Add(table, 31, "branch_less_or_equal",
                "Pops two integers and jumps when the first is not larger.", 7841);
            Add(table, 32, "branch_greater_or_equal",
                "Pops two integers and jumps when the first is not smaller.", 7847);
            Add(table, 33, "push_int_local", "Pushes integer local [operand] of the current frame.", 7853);
            Add(table, 34, "pop_int_local", "Pops the integer stack into integer local [operand].", 7855);
            Add(table, 35, "push_string_local", "Pushes string local [operand] onto the string stack.", 7857);
            Add(table, 36, "pop_string_local", "Pops the string stack into string local [operand].", 7859);
            Add(table, 37, "join_string", "Pops [operand] strings and pushes them concatenated in order.", 7861);
            Add(table, 38, "discard_int", "Drops the top of the integer stack without reading it.", 7869);
            Add(table, 39, "discard_string", "Drops the top of the string stack without reading it.", 7871);
            Add(table, 40, "call_script",
                "Calls script [operand], moving its declared parameters off both stacks into a new frame.", 7873);
            Add(table, 42, "push_varc_int", "Pushes client variable [operand] onto the integer stack.", 7913);
            Add(table, 43, "pop_varc_int",
                "Pops the integer stack into client variable [operand] and marks it changed.", 7915);
            Add(table, 44, "define_array",
                "Allocates array [operand >> 16], sized from the stack; type 'i' fills with 0, anything else -1.",
                7921);
            Add(table, 45, "push_array_int", "Pops an index and pushes array [operand] at it.", 7941);
            Add(table, 46, "pop_array_int", "Pops a value and an index and stores into array [operand].", 7950);
            Add(table, 47, "push_varc_string",
                "Pushes client string variable [operand], or \"null\" when it is unset.", 7962);
            Add(table, 48, "pop_varc_string", "Pops the string stack into client string variable [operand].", 7970);
            Add(table, SwitchOpcode, "switch",
                "Pops a value, looks it up in switch block [operand] and jumps by the arm's delta.", 7975);
            Add(table, 86, "branch_if_true", "Pops one integer and jumps when it is 1.", 7982);
            Add(table, 87, "branch_if_false", "Pops one integer and jumps when it is 0.", 7986);
        }

        /// <summary>
        ///     The 4000 block, which is arithmetic on the integer stack and nothing else.
        /// </summary>
        /// <remarks>
        ///     Every arm here is self-contained - it pops, computes with Java operators or
        ///     <c>java.lang.Math</c>, and pushes - so each name is read off the expression rather than
        ///     inferred. 4019 is the one that is not merely arithmetic and is named anyway, because
        ///     what it does wrong is visible in the same few lines: see its own note below.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddArithmetic(IDictionary<int, ClientScriptOpcodeInfo> table) {
            Add(table, 4000, "add", "Pops two integers and pushes their sum.", 3003);
            Add(table, 4001, "sub", "Pops two integers and pushes the first minus the second.", 3014);
            Add(table, 4002, "multiply", "Pops two integers and pushes their product.", 3025);
            Add(table, 4003, "divide", "Pops two integers and pushes the first divided by the second.", 3036);
            Add(table, 4004, "random", "Pops n and pushes a random integer in 0..n-1.", 3047);
            Add(table, 4005, "random_inclusive", "Pops n and pushes a random integer in 0..n.", 3055);
            Add(table, 4006, "interpolate",
                "Pops five and pushes a linear interpolation: y0 + (y1 - y0) * (x - x0) / (x1 - x0).", 3063);
            Add(table, 4007, "add_percent", "Pops a value and a percentage and pushes value + value * pct / 100.",
                3077);
            Add(table, 4008, "set_bit", "Pops a value and a bit index and pushes the value with that bit set.", 3088);
            Add(table, 4009, "clear_bit", "Pops a value and a bit index and pushes it with that bit cleared.", 3099);
            Add(table, 4010, "test_bit", "Pops a value and a bit index and pushes 1 when the bit is set.", 3110);
            Add(table, 4011, "modulo", "Pops two integers and pushes the remainder of the first over the second.",
                3121);
            Add(table, 4012, "power", "Pops a base and an exponent and pushes the power, or 0 for a base of 0.",
                3132);
            Add(table, 4013, "root",
                "Pops a value and a degree and pushes the root, 0 for a value of 0 and int max for a degree of 0.",
                3149);
            Add(table, 4014, "and", "Pops two integers and pushes their bitwise and.", 3172);
            Add(table, 4015, "or", "Pops two integers and pushes their bitwise or.", 3183);
            Add(table, 4016, "min", "Pops two integers and pushes the smaller.", 3194);
            Add(table, 4017, "max", "Pops two integers and pushes the larger.", 3205);
            Add(table, 4018, "scale",
                "Pops value, denominator and numerator and pushes value * numerator / denominator in 64-bit.", 3216);

            //APPARENT CLIENT BUG, and stated as apparent because the evidence is decompiled source
            //rather than bytecode. Class247.java:3232-3234 pushes 256 when either bound exceeds 700
            //and then falls through and pushes a second value, leaving the stack one entry deeper
            //than the path that does not trigger it. The neighbouring arms 4012 and 4013 both carry
            //an explicit return inside the equivalent branch, so JODE does emit one where the class
            //file has one, which is what makes its absence here worth recording. Two instructions in
            //the vanilla capture use this opcode, so the path is reachable in real data.
            Add(table, 4019, "random_exponential",
                "Pops two bounds and pushes 2 ^ ((random(a + b) - a + 800) / 100). Apparent client bug: for a " +
                "bound above 700 it pushes 256 and then pushes again on the same path.", 3228);
        }

        /// <summary>
        ///     The 4100 block, which is string manipulation.
        /// </summary>
        /// <remarks>
        ///     Named only where the arm resolves to a <c>java.lang.String</c> or
        ///     <c>java.lang.Character</c> call whose meaning is in the language rather than in the
        ///     client. Ten of the twenty-six arms hand off to an obfuscated helper - text measurement,
        ///     name formatting, the quick-chat encoder - and those keep the number and say what they
        ///     touch instead.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddStringOperations(IDictionary<int, ClientScriptOpcodeInfo> table) {
            Add(table, 4100, "append_int", "Pops a string and an integer and pushes the two concatenated.", 3245);
            Add(table, 4101, "append_string", "Pops two strings and pushes them concatenated in order.", 3254);
            Add(table, 4102, null,
                "Appends a number formatted by Class44.method428, which this project has not read.", 3265);
            Add(table, 4103, "lowercase", "Pops a string and pushes it lowercased.", 3274);
            Add(table, 4104, null, "Pushes a string built by Class247.method3149 from a popped integer.", 3282);
            Add(table, 4105, null,
                "Pops two strings and pushes one of them, chosen by a flag on the local player's appearance.", 3288);
            Add(table, 4106, "int_to_string", "Pops an integer and pushes its decimal text.", 3306);
            Add(table, 4107, null, "Compares two popped strings through Class336.method3772 and pushes the result.",
                3314);
            Add(table, 4108, null, "Pushes a text measurement of a popped string in a font loaded by id.", 3322);
            Add(table, 4109, null, "Pushes a second text measurement of a popped string in a font loaded by id.",
                3336);
            Add(table, 4110, "select_string",
                "Pops two strings and a flag and pushes the first when the flag is 1, the second otherwise.", 3351);
            Add(table, 4111, null, "Pushes a popped string transformed by Class249.method3160.", 3368);
            Add(table, 4112, "append_char",
                "Pops a string and a character code and pushes the two concatenated; -1 throws.", 3376);
            Add(table, 4113, null, "Maps a popped character through Class247.method3146 and pushes the result.",
                3389);
            Add(table, 4114, null, "Pushes 1 when a popped character satisfies Class114.method2147.", 3397);
            Add(table, 4115, null, "Pushes 1 when a popped character satisfies Node_Sub46_Sub15.method1611.", 3405);
            Add(table, 4116, null, "Pushes 1 when a popped character satisfies Class134_Sub1.method2245.", 3413);
            Add(table, 4117, "string_length", "Pops a string and pushes its length, or 0 when it is null.", 3421);
            Add(table, 4118, "substring", "Pops a string and two indices and pushes the substring between them.",
                3435);
            Add(table, 4119, "strip_tags",
                "Pops a string and pushes it with every span between '<' and '>' removed.", 3448);
            Add(table, 4120, "index_of_char",
                "Pops a string, a character and a start index and pushes the first position, or -1.", 3470);
            Add(table, 4121, "index_of_string",
                "Pops two strings and a start index and pushes where the second occurs in the first, or -1.", 3483);
            Add(table, 4122, "char_to_lower", "Pops a character code and pushes its lower case.", 3495);
            Add(table, 4123, "char_to_upper", "Pops a character code and pushes its upper case.", 3503);
            Add(table, 4124, null, "Pushes a display name decoded from a popped value by Class39.nameForLong.", 3511);
            Add(table, 4125, null, "Pushes a further text measurement of a popped string in a font loaded by id.",
                3520);
        }

        /// <summary>Shorthand for a stated operand list, in push order.</summary>
        /// <param name="pushOrder">The values, a bare name for an integer and a <c>:s</c> suffix for a string.</param>
        /// <returns>The operand list.</returns>
        private static ClientScriptStackOperands Takes(params string[] pushOrder) {
            return ClientScriptStackOperands.Of(pushOrder);
        }

        private const ClientScriptComponentAddressing Active = ClientScriptComponentAddressing.ActiveComponent;
        private const ClientScriptComponentAddressing Stack = ClientScriptComponentAddressing.StackComponent;
        private const ClientScriptComponentAddressing NoComponent = ClientScriptComponentAddressing.None;

        /// <summary>
        ///     The lifecycle and addressing opcodes, 100 to 203, which decide what "the active
        ///     component" refers to.
        /// </summary>
        /// <remarks>
        ///     101 is the only opcode in the index whose mnemonic is written in the client as a
        ///     string: its two guard clauses raise <c>"Tried to cc_delete static active-component!"</c>
        ///     at <c>Class247.java:246</c> and <c>:249</c>. 100 and 102 are the create and
        ///     delete-every-child arms either side of it and are named from what they do to the same
        ///     child array, <c>aRSInterfaceArray2339</c>.
        ///     <para>
        ///     <b>102 was <c>cc_delete_all</c> here and is wrong under that prefix.</b> Its arm pops
        ///     the target off the stack (<c>:261</c>) and never reads <c>bool</c> at all, so the
        ///     operand byte selects nothing and the name claimed a calling convention the arm does not
        ///     have. It was written before the addressing mode was a field on the row, back when this
        ///     adder held three opcodes and described all three as taking their target off the stack -
        ///     which is true of 100 and 102 and false of 101. 100 keeps <c>cc_</c> because it
        ///     <i>writes</i> the register the byte selects, which is the claim the prefix makes.
        ///     </para>
        ///     <para>
        ///     <b>200 and 201 are deliberately unnamed.</b> They are the only two opcodes that set the
        ///     active register from the stack, so a name for them would have to invent a verb for a
        ///     mechanism the client never spells; describing them costs nothing and claims nothing.
        ///     </para>
        ///     <para>
        ///     <b>202 and 203 are named from the draw order they produce, not from a field.</b>
        ///     <c>method3142</c> (<c>:92-118</c>) moves the component to the last slot of
        ///     <c>Class64_Sub13.aRSInterfaceArrayArray3674[interfaceId]</c> and <c>method3145</c>
        ///     (<c>:139-165</c>) to slot 0; <c>client.render_interface</c> walks that array in
        ///     ascending order (<c>client.java:713</c>), so last is drawn on top and first is drawn
        ///     behind. Both halves are read here rather than assumed.
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddComponentLifecycle(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddComponent(table, 100, "cc_create",
                "Pops a parent component, a component type and a slot, grows the parent's dynamic child array to " +
                "fit, creates a child of that type in the slot and makes it the active component. Refuses to leave " +
                "a gap in the array.", 190, Takes("parent", "type", "slot"), Active);
            AddComponent(table, 101, "cc_delete",
                "Nulls the active component's entry in its parent's dynamic child array. Throws on a static " +
                "component, in the two messages that are the only place the client spells .active-component and " +
                "active-component.", 241, ClientScriptStackOperands.Empty, Active);
            AddComponent(table, 102, "if_delete_all",
                "Pops a component and drops its whole dynamic child array.", 260, Takes("component"), Stack);
            AddComponent(table, 200, null,
                "Pops a component and a dynamic child slot, resolves that child, and on success stores it in the " +
                "active-component register the operand byte selects and pushes 1. Pushes 0 otherwise. Nothing in " +
                "the client names this.", 269, Takes("component", "child_slot"), Active);
            AddComponent(table, 201, null,
                "Pops a component, and on success stores it in the active-component register the operand byte " +
                "selects and pushes 1. Pushes 0 otherwise. Nothing in the client names this.", 293,
                Takes("component"), Active);
            AddComponent(table, 202, "if_bring_to_front",
                "Pops a component and moves it to the last slot of its interface's draw array, which the renderer " +
                "walks in ascending order, so it is drawn on top of everything else in that interface.", 314,
                Takes("component"), Stack, "Class247.java:92-118, client.java:713");
            AddComponent(table, 203, "if_send_to_back",
                "Pops a component and moves it to slot 0 of its interface's draw array, so it is drawn before - and " +
                "therefore behind - everything else in that interface.", 322,
                Takes("component"), Stack, "Class247.java:139-165, client.java:713");
        }

        /// <summary>
        ///     The four folded blocks that write a component's fields: 1000, 1100, 1200 and 1300, each
        ///     with its 2xxx twin.
        /// </summary>
        /// <remarks>
        ///     <b>Every name here is a field this project's own decoder reads at the same ordinal.</b>
        ///     The client's decompiled field names are shifted by one against the read order, so the
        ///     identity of <c>y</c>, <c>width</c>, <c>height</c> and <c>anInt2242</c> is settled by
        ///     what these arms do with them - 1000 clamps its two modes to 0..5 and 1001 clamps its two
        ///     to 0..4, which is the positioning and sizing pair the resolver at
        ///     <c>Class253.java:316-347</c> reads - never from the labels.
        ///     <para>
        ///     <b>Where a field's meaning is not settled, the row declines.</b> A dozen arms write a
        ///     field this project can only name after its own hedged decoder property
        ///     (<c>SpriteTransform1Byte</c>, <c>RawVersionedShort</c>) or reach a helper nobody here
        ///     has read. Those keep the number and say which field they touch, which is the useful
        ///     half of a name without the misleading half.
        ///     </para>
        ///     <para>
        ///     <b>1002, 1121 and their twins have no arm at all.</b> Nothing in <c>Class247</c> tests
        ///     for either number, so they are holes rather than opcodes this table has not reached, and
        ///     both numbers are stated so a reader does not go looking for the arm.
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddComponentMutators(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddPositionAndSize(table);
            AddAppearance(table);
            AddModelSlot(table);
            AddInteraction(table);
        }

        /// <summary>The 1000 block: where a component sits, how large it is and whether it is drawn.</summary>
        /// <param name="table">The table being built.</param>
        private static void AddPositionAndSize(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddComponent(table, 1000, "cc_set_position",
                "Writes the base X and Y and the two positioning modes, each clamped to 0..5, then re-resolves the " +
                "layout and tells the server when the component is static.", 420,
                Takes("x", "y", "x_mode", "y_mode"), Active, "KeyStroke.java:12-40");
            AddComponent(table, 1001, "cc_set_size",
                "Writes the base width and height and the two sizing modes, each clamped to 0..4, clears the two " +
                "model size extras and re-lays out the children of a layer.", 453,
                Takes("width", "height", "width_mode", "height_mode"), Active, "Class253.java:316-338");
            AddHole(table, 1002, "1000");
            AddComponent(table, 1003, "cc_set_hidden",
                "Sets whether the component is drawn, and tells the server when it is static.", 488,
                Takes("hidden"), Active);
            AddComponent(table, 1004, "cc_set_aspect_ratio",
                "Writes the aspect numerator and denominator, which the resolver divides with to derive the width " +
                "from the height under sizing mode 4 and the height from the width under mode 4 on the other axis. " +
                "Neither field is in the wire format.", 507,
                Takes("numerator", "denominator"), Active, "Class253.java:341-347");
            AddComponent(table, 1005, null,
                "Sets the client's aBoolean2286, this project's LayerFlagByte, which gates the mouse-capture path " +
                "that cancels pending click scripts while the pointer is inside the component. Not named: the " +
                "polarity reads as a capture flag and nothing here settles which way round.", 521,
                Takes("flag"), Active, "client.java:767, client.java:802");
        }

        /// <summary>The 1100 block: colour, sprite, model view, text and the parameter table.</summary>
        /// <param name="table">The table being built.</param>
        private static void AddAppearance(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddComponent(table, 1100, "cc_set_scroll_position",
                "Writes the horizontal and vertical scroll offsets, each clamped into 0..extent minus the resolved " +
                "size on its own axis, which is what settles which of the two fields is which.", 536,
                Takes("scroll_x", "scroll_y"), Active);
            AddComponent(table, 1101, "cc_set_colour",
                "Writes the component's colour - the fill for a rectangle, the ink for text, the tint for a sprite.",
                567, Takes("colour"), Active);
            AddComponent(table, 1102, "cc_set_rectangle_filled",
                "Selects between the two rectangle draws, which is meaningful only on a type 3 component.", 578,
                Takes("filled"), Active, "Node_Sub10_Sub24.java:441-455");
            AddComponent(table, 1103, "cc_set_transparency",
                "Writes the transparency, which is an inverted alpha: 0 is opaque.", 585,
                Takes("transparency"), Active, "Node_Sub10_Sub24.java:443-449");
            AddComponent(table, 1104, "cc_set_line_width",
                "Writes the line width, which is meaningful only on a type 9 component.", 592,
                Takes("width"), Active);
            AddComponent(table, 1105, "cc_set_sprite",
                "Points the component at sprite [id] in index 8, and tells the server when it is static.", 599,
                Takes("sprite_id"), Active);
            AddComponent(table, 1106, null,
                "Writes the client's anInt2255, this project's SpriteTransform, which the sprite draw uses against " +
                "a 4096 scale. Not named: the value's units are not settled here.", 614,
                Takes("value"), Active, "Node_Sub10_Sub24.java:603-663");
            AddComponent(table, 1107, null,
                "Writes bit 0 of this project's SpriteFlags as a boolean. Not named: the bit selects between two " +
                "draw paths and neither this arm nor the decoder says which.", 621, Takes("flag"), Active);
            AddComponent(table, 1108, "cc_set_model",
                "Points the model slot at model [id] in index 7, by setting the model-source kind to 1 - the kind " +
                "under which the renderer loads the id from the model archive.", 628,
                Takes("model_id"), Active, "RSInterface.java:608-646");
            AddComponent(table, 1109, null,
                "Writes six model-view fields at once: the client's anInt2268 and anInt2273, which the renderer " +
                "shifts left 2 as a draw offset, then the three rotations and the zoom. Not named: the first two " +
                "are distinct from this project's ModelOffsetX and ModelOffsetY, which opcode 1125 writes.", 640,
                Takes("offset_x", "offset_y", "rotate_x", "rotate_y", "rotate_z", "zoom"), Active,
                "Node_Sub10_Sub24.java:822-824");
            AddComponent(table, 1110, "cc_set_animation",
                "Points the component at animation [id] and restarts the sequence from frame 0.", 658,
                Takes("animation_id"), Active);
            AddComponent(table, 1111, null,
                "Writes bit 2 of this project's ModelSettings as a boolean. Not named: nothing read here says what " +
                "the bit selects.", 684, Takes("flag"), Active);
            AddComponent(table, 1112, "cc_set_text",
                "Writes the drawn text, ignoring the literal \"N/A\" and any value that is already there.", 691,
                Takes("text:s"), Active);
            AddComponent(table, 1113, "cc_set_font",
                "Points the component at font [id] in index 13.", 707, Takes("font_id"), Active);
            AddComponent(table, 1114, "cc_set_text_layout",
                "Writes the horizontal alignment, the vertical alignment and the line height, in that pushed order. " +
                "The three-way assignment is settled by which argument of RSFont.drawText each field becomes: the " +
                "line height is the per-line step, the vertical alignment picks the y origin and the horizontal " +
                "alignment picks the per-line x. The arm writes them in a different order from the one the decoder " +
                "reads them in, so the ordinal alone would not have settled it.", 718,
                Takes("horizontal_alignment", "vertical_alignment", "line_height"), Active,
                "Node_Sub10_Sub24.java:521-528, RSFont.java:371-373");
            AddComponent(table, 1115, "cc_set_text_shadow",
                "Sets whether the text is drawn with a shadow.", 728, Takes("shadow"), Active);
            AddComponent(table, 1116, "cc_set_outline_thickness",
                "Writes the outline thickness.", 735, Takes("thickness"), Active);
            AddComponent(table, 1117, "cc_set_outline_colour",
                "Writes the outline or shadow colour, 0 meaning none.", 742, Takes("colour"), Active);
            AddComponent(table, 1118, null,
                "Writes this project's SpriteTransform1Byte as a boolean. Not named: the decoder's own name for the " +
                "field is a hedge, so a mnemonic would be a guess dressed as a reading.", 749, Takes("flag"), Active);
            AddComponent(table, 1119, null,
                "Writes this project's SpriteTransform2Byte as a boolean, on the same terms as 1118.", 756,
                Takes("flag"), Active);
            AddComponent(table, 1120, "cc_set_scroll_extent",
                "Writes the horizontal and vertical scroll extents and re-lays out the children of a layer.", 763,
                Takes("max_horizontal", "max_vertical"), Active);
            AddHole(table, 1121, "1100");
            AddComponent(table, 1122, "cc_set_sprite_tiled",
                "Sets bit 1 of this project's SpriteFlags, which tiles the sprite across the component.", 776,
                Takes("tiled"), Active);
            AddComponent(table, 1123, "cc_set_model_zoom",
                "Writes the model zoom alone, without the five fields 1109 writes beside it.", 783,
                Takes("zoom"), Active);
            AddComponent(table, 1124, "cc_set_line_flipped",
                "Flips which diagonal of its rectangle a type 9 component draws.", 794,
                Takes("flipped"), Active, "Node_Sub10_Sub24.java:885-897");
            AddComponent(table, 1125, "cc_set_model_offset",
                "Writes this project's ModelOffsetX and ModelOffsetY.", 803, Takes("x", "y"), Active);
            AddComponent(table, 1126, null,
                "Writes this project's TextVersionedByte, which no file in either supported cache stores. Not " +
                "named: nothing reads the field in a way that would settle a name.", 812, Takes("value"), Active);
            AddComponent(table, 1127, "cc_set_parameter_int",
                "Sets integer parameter [id] on the component's parameter table, or removes it when the value " +
                "equals the parameter type's own default.", 819, Takes("parameter_id", "value"), Active);
            AddComponent(table, 1128, "cc_set_parameter_string",
                "Sets string parameter [id] on the component's parameter table, or removes it when the value " +
                "equals the parameter type's own default.", 837, Takes("parameter_id", "value:s"), Active);
            AddComponent(table, 1129, null,
                "Writes the client's anInt2211 and takes effect only on a type 5 component. The renderer turns the " +
                "same field into a sprite through Class200.method2693 for a sprite component and into text through " +
                "Class48.method454 for a text one. Not named: neither helper has been read here, so what the value " +
                "keys into is unsettled.", 853, Takes("value"), Active, "Node_Sub10_Sub24.java:586");
            AddComponent(table, 1130, null,
                "The same arm as 1129, taking effect only on a type 4 component, where the field resolves to text " +
                "rather than to a sprite. Unnamed for the same reason.", 853,
                Takes("value"), Active, "Node_Sub10_Sub24.java:503");
        }

        /// <summary>
        ///     The 1200 block: what the model slot points at.
        /// </summary>
        /// <remarks>
        ///     <b>Six numbers share one arm body and are told apart by two ternaries.</b> Four of them
        ///     are separable and named; 1212 and 1213 are not, because both set the same pair of
        ///     values and no test anywhere in the arm distinguishes them - the same shape as 2704 and
        ///     2705. Naming both would need two spellings of one behaviour.
        ///     <para>
        ///     The two ternaries are <c>aBoolean2262</c>, which decides whether the item sprite is
        ///     built against the local player's appearance (<c>Class205.java:290</c>,
        ///     <c>ItemDefinition.java:246-256</c>), and <c>anInt2305</c>, which decides whether the
        ///     quantity is drawn over the sprite: 0 never, 1 always, 2 when the item stacks or the
        ///     count is not 1 (<c>ItemDefinition.java:363-366</c>).
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddModelSlot(IDictionary<int, ClientScriptOpcodeInfo> table) {
            const string itemArm =
                "Points the model slot at item [id] with quantity [count], copying the item definition's six model " +
                "transform fields; an id of -1 clears the slot instead. ";

            AddComponent(table, 1200, "cc_set_item",
                itemArm + "Draws the quantity when the item stacks or the count is not 1.", 884,
                Takes("item_id", "count"), Active, "ItemDefinition.java:363-366");
            AddComponent(table, 1205, "cc_set_item_no_count",
                itemArm + "Never draws the quantity.", 884,
                Takes("item_id", "count"), Active, "ItemDefinition.java:363-366");
            AddComponent(table, 1208, "cc_set_item_worn",
                itemArm + "Builds the sprite against the local player's appearance, and draws the quantity when " +
                "the item stacks or the count is not 1.", 884,
                Takes("item_id", "count"), Active, "Class205.java:290, ItemDefinition.java:246-256");
            AddComponent(table, 1209, "cc_set_item_worn_no_count",
                itemArm + "Builds the sprite against the local player's appearance and never draws the quantity.",
                884, Takes("item_id", "count"), Active, "Class205.java:290, ItemDefinition.java:246-256");
            AddComponent(table, 1212, null,
                itemArm + "Always draws the quantity. Not named: 1213 sets exactly the same two values and no test " +
                "in the arm tells the two numbers apart.", 884,
                Takes("item_id", "count"), Active, "ItemDefinition.java:363-366");
            AddComponent(table, 1213, null,
                itemArm + "Always draws the quantity. Not named, for the same reason as 1212.", 884,
                Takes("item_id", "count"), Active, "ItemDefinition.java:363-366");

            AddComponent(table, 1201, "cc_set_model_npc",
                "Points the model slot at NPC [id], by setting the model-source kind to 2 - the kind under which " +
                "the renderer resolves the id through the NPC definition provider.", 939,
                Takes("npc_id"), Active, "RSInterface.java:649-660, Class301.java:200-214");
            AddComponent(table, 1202, "cc_set_model_self_appearance",
                "Points the model slot at the local player's own appearance model, by setting the model-source " +
                "kind to 3 and clearing the id.", 950,
                ClientScriptStackOperands.Empty, Active, "RSInterface.java:662-678");
            AddComponent(table, 1203, null,
                "Points the model slot at NPC [id] as kind 6, which resolves through the same NPC definition " +
                "provider as 1201 but a different render call. Not named: nothing read here settles how the two " +
                "differ.", 961, Takes("npc_id"), Active, "RSInterface.java:693-705");
            AddComponent(table, 1204, "cc_set_model_player",
                "Points the model slot at the player in slot [index] of the watched-player table, by setting the " +
                "model-source kind to 5. The renderer draws it only when the slot is the local player's or the " +
                "player's display-name hash matches the client's anInt2210.", 972,
                Takes("player_index"), Active, "Node_Sub10_Sub24.java:756-772");
            AddComponent(table, 1206, null,
                "Writes four model-view fields the client calls anInt2267, anInt2306, anInt2260 and anInt2334, " +
                "none of which is in the wire format. Not named: no consumer has been read here.", 983,
                Takes("value_1", "value_2", "value_3", "value_4"), Active);
            AddComponent(table, 1207, null,
                "Writes the client's anInt2216 and anInt2261, which the renderer shifts left 3. Not named: the " +
                "shift says the units are eighths of something and nothing read here says of what.", 994,
                Takes("value_1", "value_2"), Active, "Node_Sub10_Sub24.java:569-570");
            AddComponent(table, 1210, null,
                "Points the model slot at model-source kind 8 or 9 - a Node_Sub3 the renderer loads by id - with an " +
                "extra id, the kind chosen by the third value and the wear-on-player flag by the fourth. Not " +
                "named: what kinds 8 and 9 are has not been read here.", 1003,
                Takes("id", "extra_id", "kind_flag", "worn"), Active, "Node_Sub10_Sub24.java:709-724");
            AddComponent(table, 1211, "cc_set_model_self_player",
                "Points the model slot at the local player's entry in the watched-player table, as kind 5 with the " +
                "name hash cleared - which is the path 1204 takes for another player.", 1023,
                ClientScriptStackOperands.Empty, Active, "Node_Sub10_Sub24.java:756-772");
        }

        /// <summary>The 1300 block: context menu options, drag behaviour and the tooltip.</summary>
        /// <param name="table">The table being built.</param>
        private static void AddInteraction(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddComponent(table, 1300, "cc_set_context_option",
                "Sets context menu option [slot], counting from 1. A slot outside 1..10 still pops the string, so " +
                "the stack stays balanced and the option is silently dropped.", 1044,
                Takes("slot", "text:s"), Active);
            AddComponent(table, 1301, null,
                "Pops an interface and a child slot and stores the resolved component as the client's " +
                "aRSInterface_2219; (-1, -1) clears it. Not named: no reader of that field has been read here.",
                1059, Takes("interface_id", "child_slot"), Active);
            AddComponent(table, 1302, null,
                "Writes the client's anInt2289, this project's HintSlot, but only when the value equals one of " +
                "three client constants - so it is an enumeration rather than a slot number. Not named: what the " +
                "three constants select has not been read here.", 1076,
                Takes("value"), Active, "Node_Sub10_Sub24.java:137-138");
            AddComponent(table, 1303, "cc_set_drag_deadzone",
                "Writes the drag deadzone in pixels.", 1088, Takes("pixels"), Active, "Class111_Sub3.java:87-95");
            AddComponent(table, 1304, "cc_set_drag_delay",
                "Writes the drag delay in ticks.", 1094, Takes("ticks"), Active, "Class111_Sub3.java:83");
            AddComponent(table, 1305, "cc_set_option_base",
                "Writes the option base, the text the client prefixes a two-part menu entry with.", 1100,
                Takes("text:s"), Active, "Class8.java:41");
            AddComponent(table, 1306, "cc_set_tooltip",
                "Writes the tooltip, which the client returns only when the access mask allows it and the text is " +
                "not blank.", 1106, Takes("text:s"), Active, "Class170.java:10-22");
            AddComponent(table, 1307, "cc_clear_context_options",
                "Drops every context menu option the component has.", 1112,
                ClientScriptStackOperands.Empty, Active);
            AddComponent(table, 1308, null,
                "Writes two of the three access-mask-gated target shorts, the client's anInt2318 and then " +
                "anInt2309 - so the script pushes them the other way round. Not named: the decoder's own names for " +
                "the pair are positional.", 1118, Takes("target_cursor", "target_operand"), Active,
                "RSInterface.java:1266-1276");
            AddComponent(table, 1309, null,
                "Passes a value into the component's option table for slot [slot], counting from 1, through " +
                "RSInterface.method3474. Not named: that helper has not been read here.", 1125,
                Takes("slot", "value"), Active);
            AddComponent(table, 1310, "cc_set_selected_action",
                "Writes the action text the client uses for a menu entry built on this component.", 1136,
                Takes("text:s"), Active, "Class8.java:86-94");
            AddComponent(table, 1311, null,
                "Writes the client's anInt2254, which a menu-entry builder passes on. Not named: no reading here " +
                "settles what the value means.", 1142, Takes("value"), Active, "Class8.java:63");
            AddComponent(table, 1312, null,
                "Writes a pair of bytes into the component's per-option arrays at slot [slot], counting from 1, " +
                "and throws \"IOR13121313\" outside 1..10. Not named: what the two byte arrays gate is not read " +
                "here.", 1148, Takes("slot", "value_1", "value_2"), Active);
            AddComponent(table, 1313, null,
                "The same arm as 1312 with the slot fixed at 10, so it takes one value fewer.", 1148,
                Takes("value_1", "value_2"), Active);
            AddComponent(table, 1314, null,
                "Writes this project's RawVersionedShort, which no file in either supported cache stores. Not " +
                "named: nothing reads the field in a way that would settle a name.", 1200, Takes("value"), Active);
        }

        /// <summary>
        ///     The 1400 hook block and the 1900 invoker beside it.
        /// </summary>
        /// <remarks>
        ///     <b>None of the thirty setters is named, and the reason is the same for all of them.</b>
        ///     Each writes one of the client's hook arrays, and which event fires which array is
        ///     decided outside this dispatcher entirely. Naming them <c>on-something</c> would be
        ///     inventing the event; naming the storage slot instead would restate the number. So each
        ///     row states the client field it writes and, where the wire format stores that field,
        ///     which of this project's twenty <c>Hooks</c> slots it lands in - which is checkable
        ///     against <c>RSInterface.unpackConfig</c>'s read order at
        ///     <c>RSInterface.java:1308-1340</c> and is the useful half.
        ///     <para>
        ///     <b>Their arity is not a number.</b> One shared body (<c>Class247.java:1219-1250</c>)
        ///     pops a format string, optionally a trigger array when the format ends in <c>'Y'</c>,
        ///     then one value per remaining character - string for <c>'s'</c>, integer otherwise - and
        ///     finally the script id, where -1 means "no hook". So the operand list for every one of
        ///     them is variadic by construction.
        ///     </para>
        ///     <para>
        ///     <b>Exactly one stored hook slot has no setter here.</b> Slot 0,
        ///     <c>anObjectArray2332</c>, is the one the client fires itself over every component of an
        ///     interface as it opens (<c>Class247.java:4130-4136</c>), and 1499 is the only opcode in
        ///     the block that touches it, by clearing everything.
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddComponentHooks(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddHook(table, 1400, 1254, "anObjectArray2291", "Hooks slot 11");
            AddHook(table, 1401, 1256, "anObjectArray2230", "Hooks slot 14");
            AddHook(table, 1402, 1258, "anObjectArray2356", "Hooks slot 13");
            AddHook(table, 1403, 1260, "anObjectArray2227", "Hooks slot 1");
            AddHook(table, 1404, 1262, "anObjectArray2272", "Hooks slot 2");
            AddHook(table, 1405, 1264, "anObjectArray2316", "Hooks slot 15");
            AddHook(table, 1406, 1266, "anObjectArray2324", "Hooks slot 3");
            AddHook(table, 1407, 1268, "anObjectArray2269", "Hooks slot 5", 0);
            AddHook(table, 1408, 1271, "anObjectArray2270", "Hooks slot 8");
            AddHook(table, 1409, 1273, "some_interface_script", "Hooks slot 9");
            AddHook(table, 1410, 1275, "anObjectArray2313", "Hooks slot 16");
            AddHook(table, 1411, 1277, "anObjectArray2335", "Hooks slot 12");
            AddHook(table, 1412, 1279, "anObjectArray2314", "Hooks slot 10");
            AddHole(table, 1413, "1400");
            AddHook(table, 1414, 1281, "anObjectArray2252", "Hooks slot 6", 1);
            AddHook(table, 1415, 1284, "anObjectArray2278", "Hooks slot 7", 2);
            AddHook(table, 1416, 1287, "anObjectArray2257", "Hooks slot 4");
            AddHook(table, 1417, 1289, "anObjectArray2277", "Hooks slot 17");
            AddHook(table, 1418, 1291, "anObjectArray2239", null);
            AddHook(table, 1419, 1293, "anObjectArray2274", null);
            AddHook(table, 1420, 1295, "anObjectArray2215", null);
            AddHook(table, 1421, 1297, "anObjectArray2292", null);
            AddHook(table, 1422, 1299, "anObjectArray2340", null);
            AddHook(table, 1423, 1301, "anObjectArray2330", null);
            AddHook(table, 1424, 1303, "anObjectArray2319", null);
            AddHook(table, 1425, 1305, "anObjectArray2294", null);
            AddHook(table, 1426, 1307, "anObjectArray2220", null);
            AddHook(table, 1427, 1309, "anObjectArray2266", null);
            AddHook(table, 1428, 1311, "anObjectArray2212", "Hooks slot 18", 3);
            AddHook(table, 1429, 1314, "anObjectArray2320", "Hooks slot 19", 4);
            AddHook(table, 1430, 1317, "anObjectArray2253", "VersionedHook, the version-gated twenty-first array, " +
                                                            "which no file in either supported cache stores");

            AddComponent(table, 1499, "cc_clear_hooks",
                "Nulls all thirty-one hook arrays and all five trigger arrays, including the load hook no setter " +
                "in this block can write.", 1216,
                ClientScriptStackOperands.Empty, Active, "RSInterface.java:798-843");

            AddComponent(table, 1927, null,
                "Queues the hook held in the client's anObjectArray2266 - the array opcode 1427 sets - as a script " +
                "to run, one recursion level deeper, and throws \"C29xx-1\" past ten levels. Not named: the " +
                "pairing is provable from the shared field but the hook itself has no name.", 1559,
                ClientScriptStackOperands.Empty, Active);
        }

        /// <summary>Adds one hook setter, all of which differ only in which array they land in.</summary>
        /// <param name="table">The table being built.</param>
        /// <param name="opcode">The opcode.</param>
        /// <param name="line">The line of the assignment chain that names it.</param>
        /// <param name="field">The client field it writes.</param>
        /// <param name="storedAs">Where the wire format keeps it, or null when the format does not.</param>
        /// <param name="triggerSlot">This project's <c>Triggers</c> slot, when the setter writes one too.</param>
        private static void AddHook(IDictionary<int, ClientScriptOpcodeInfo> table, int opcode, int line,
            string field, string? storedAs, int? triggerSlot = null) {
            string summary = "Sets the hook array the client calls " + field + ", from a script id and however many " +
                             "arguments the format string on the stack names. ";

            summary += storedAs == null
                ? "The wire format does not store this array at all, so it exists only at run time."
                : "The wire format stores it as " + storedAs + ".";

            if (triggerSlot != null)
                summary += " Also writes Triggers slot " + triggerSlot + " from the trailing integer array.";

            AddComponent(table, opcode, null, summary, line,
                ClientScriptStackOperands.Variadic("script_id", "format:s"), Active,
                "Class247.java:1219-1250, RSInterface.java:1308-1340");
        }

        /// <summary>
        ///     Records a number in a folded block that has no arm, for both forms.
        /// </summary>
        /// <remarks>
        ///     A hole is worth a row because the alternative is the generic "dispatched by method3148"
        ///     line, which reads as "this table has not got to it yet" and would send a reader looking
        ///     for an arm that is not there. Both numbers get their own row so the twin is never
        ///     synthesised from one that describes a hole.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        /// <param name="opcode">The number with no arm.</param>
        /// <param name="block">The block it sits in.</param>
        private static void AddHole(IDictionary<int, ClientScriptOpcodeInfo> table, int opcode, string block) {
            string text = "No arm. Nothing in Class247 tests for this number, so the " + block +
                          " block skips it and an instruction carrying it falls through the whole dispatcher.";

            table[opcode] = new ClientScriptOpcodeInfo(opcode, null,
                text + " Its stack-addressed form, " + (opcode + 1000) + ", is absent with it.",
                "Class247.java:187", ClientScriptStackOperands.Empty, NoComponent);
            table[opcode + 1000] = new ClientScriptOpcodeInfo(opcode + 1000, null,
                text + " It would be the stack-addressed form of " + opcode + ", which has no arm either.",
                "Class247.java:187", ClientScriptStackOperands.Empty, NoComponent);
        }

        /// <summary>
        ///     The accessor blocks, 1500-1802 against the active component and 2500-2802 against a
        ///     component off the stack.
        /// </summary>
        /// <remarks>
        ///     <b>These are not folded, so the two forms are two hand-written copies and the table has
        ///     to be too.</b> The guards at <c>Class247.java:1329</c>, <c>:1375</c>, <c>:1482</c> and
        ///     <c>:1508</c> resolve the active component with no <c>i &gt;= 2000</c> branch at all, and
        ///     the stack-addressed twins are separate blocks at <c>:1573</c>, <c>:1619</c>,
        ///     <c>:1711</c> and <c>:1790</c>. Copying by hand is also how the two defects below got in.
        ///     <para>
        ///     <b>Two arms in these blocks can never match, and two numbers therefore do not exist.</b>
        ///     <c>if(i == 2614)</c> at <c>:1477</c> sits inside the <c>i &lt; 1700</c> guard, which is
        ///     only entered for 1600..1699, and <c>if(i == 1506)</c> at <c>:1612</c> sits inside the
        ///     <c>i &lt; 2600</c> guard, only entered for 2500..2599. Both are dead where they stand
        ///     and both have a live copy elsewhere, so the numbers that are actually missing are 1615 -
        ///     what the dead 2614 was evidently meant to be - and 2506. Neither is a decompiler
        ///     artefact: the condition itself is outside its guard.
        ///     </para>
        ///     <para>
        ///     The 2700 block additionally holds three opcodes with no 1700 counterpart, because they
        ///     query open interface windows rather than a component. 2704 and 2705 run one body with
        ///     no test on the number and are therefore not named, on the same terms as 1212 and 1213.
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddComponentAccessors(IDictionary<int, ClientScriptOpcodeInfo> table) {
            AddAccessorPair(table, 1500, 1332, 2500, 1576, "cc_get_x",
                "Pushes the component's resolved X, which is the origin the renderer draws it at.",
                "client.java:717");
            AddAccessorPair(table, 1501, 1338, 2501, 1582, "cc_get_y",
                "Pushes the component's resolved Y, which is the origin the renderer draws it at.",
                "client.java:718");
            AddAccessorPair(table, 1502, 1344, 2502, 1588, "cc_get_width",
                "Pushes the component's resolved width, which is the base width after its sizing mode is applied.",
                "client.java:729, Class253.java:316-347");
            AddAccessorPair(table, 1503, 1350, 2503, 1594, "cc_get_height",
                "Pushes the component's resolved height, which is the base height after its sizing mode is applied.",
                "client.java:730, Class253.java:316-347");
            AddAccessorPair(table, 1504, 1356, 2504, 1600, "cc_get_hidden",
                "Pushes 1 when the component is hidden and 0 when it is not.", null);
            AddAccessorPair(table, 1505, 1362, 2505, 1606, "cc_get_parent",
                "Pushes the component's parent id as the client resolved it.", null);

            AddComponent(table, 1506, null,
                "Pushes the id of the component Class360.method3910 returns for this one, or -1. Not named: that " +
                "helper has not been read here.", 1368, ClientScriptStackOperands.Empty, Active);
            AddAbsent(table, 2506, 1612, 1573,
                "The stack-addressed form of 1506 does not exist. The arm that would be it is written as " +
                "if(i == 1506) at Class247.java:1612, inside the i < 2600 guard at :1573, which is only entered " +
                "for 2500..2599 - so it can never match and the live 1506 at :1368 handles that number instead.");

            AddAccessorPair(table, 1600, 1378, 2600, 1622, "cc_get_scroll_x",
                "Pushes the horizontal scroll offset.", null);
            AddAccessorPair(table, 1601, 1384, 2601, 1628, "cc_get_scroll_y",
                "Pushes the vertical scroll offset.", null);
            AddAccessorPair(table, 1602, 1390, 2602, 1634, "cc_get_text",
                "Pushes the component's text onto the string stack.", null);
            AddAccessorPair(table, 1603, 1396, 2603, 1640, "cc_get_scroll_extent_x",
                "Pushes the horizontal scroll extent.", null);
            AddAccessorPair(table, 1604, 1402, 2604, 1646, "cc_get_scroll_extent_y",
                "Pushes the vertical scroll extent.", null);
            AddAccessorPair(table, 1605, 1408, 2605, 1652, "cc_get_model_zoom",
                "Pushes the model zoom.", null);
            AddAccessorPair(table, 1606, 1414, 2606, 1658, "cc_get_model_rotate_x",
                "Pushes the model's X rotation.", null);
            AddAccessorPair(table, 1607, 1420, 2607, 1664, "cc_get_model_rotate_z",
                "Pushes the model's Z rotation. Note the order: Z comes before Y in both blocks.", null);
            AddAccessorPair(table, 1608, 1426, 2608, 1670, "cc_get_model_rotate_y",
                "Pushes the model's Y rotation.", null);
            AddAccessorPair(table, 1609, 1432, 2609, 1676, "cc_get_transparency",
                "Pushes the transparency, which is an inverted alpha: 0 is opaque.", null);

            AddAccessorPair(table, 1610, 1438, 2610, 1682, null,
                "Pushes the client's anInt2268, the first of the two model-view offsets opcode 1109 writes. Not " +
                "named, for the same reason 1109 is not.", "Node_Sub10_Sub24.java:822-824");
            AddAccessorPair(table, 1611, 1444, 2611, 1688, null,
                "Pushes the client's anInt2273, the second of the two. Not named, for the same reason.",
                "Node_Sub10_Sub24.java:822-824");

            AddAccessorPair(table, 1612, 1450, 2612, 1694, "cc_get_sprite",
                "Pushes the sprite id, which is this project's SpriteId.", null);

            AddComponent(table, 1613, null,
                "Pops a parameter id and pushes that parameter's value off the component's parameter table, onto " +
                "the string stack or the integer stack according to the parameter type. Not named: the pair with " +
                "1127 and 1128 is clear but the push target depends on a type lookup, which no single mnemonic " +
                "states.", 1456, Takes("parameter_id"), Active);

            AddAccessorPair(table, 1614, 1471, 2613, 1700, null,
                "Pushes the client's anInt2255, this project's SpriteTransform. Not named, for the same reason " +
                "1106 is not. Note the numbering: the stack-addressed form is 2613 rather than 2614, so it does " +
                "not line up with the 1600 block the way every other pair here does.",
                "Node_Sub10_Sub24.java:603-663");

            AddComponent(table, 2614, null,
                "Pushes the model id, but only while the model-source kind is still 1; otherwise -1. This is the " +
                "stack-addressed form of a getter that has no active-component twin.", 1706,
                Takes("component"), Stack, "RSInterface.java:1099");
            AddAbsent(table, 1615, 1477, 1375,
                "The active-component form of \"get the model id\" does not exist. The arm that would be it is " +
                "written as if(i == 2614) at Class247.java:1477, inside the i < 1700 guard at :1375, which is only " +
                "entered for 1600..1699 - so it can never match, and the live 2614 at :1706 is the only way to " +
                "reach that read.");

            AddAccessorPair(table, 1700, 1485, 2700, 1712, "cc_get_item",
                "Pushes the item id the model slot holds, or -1.", null);
            AddAccessorPair(table, 1701, 1491, 2701, 1720, "cc_get_item_count",
                "Pushes the item quantity, or 0 when the slot holds no item.", null);

            AddComponent(table, 1702, null,
                "Pushes the component's slot in its parent's dynamic child array, or -1 when it is static. Not " +
                "named: no stack-addressed twin exists to pair it with and the field's own name here is " +
                "positional.", 1503, ClientScriptStackOperands.Empty, Active);

            AddAccessorPair(table, 1800, 1511, 2800, 1793, "cc_get_access_mask",
                "Pushes the component's access mask.", null);
            AddAccessorPair(table, 1801, 1517, 2801, 1799, "cc_get_context_option",
                "Pops a slot, counting from 1, and pushes that context menu option, or an empty string.", null,
                Takes("slot"));
            AddAccessorPair(table, 1802, 1534, 2802, 1816, "cc_get_option_base",
                "Pushes the option base text, or an empty string.", "Class8.java:41");

            AddComponent(table, 2702, null,
                "Pops a window id and pushes 1 when a window with that id is open. Not a component accessor at " +
                "all, despite sitting in the 2700 block, and no 1702 counterpart exists.", 1734,
                Takes("window_id"), NoComponent);
            AddComponent(table, 2703, "if_get_child_count",
                "Pops a component and pushes the length of its dynamic child array up to the first empty slot, or " +
                "0 when it has none.", 1749, Takes("component"), Stack);
            AddComponent(table, 2704, null,
                "Pops a window slot and an interface id and pushes 1 when the window open at that slot holds that " +
                "interface. Not named: 2705 runs the same body with no test on the number to tell them apart.",
                1773, Takes("window_slot", "interface_id"), NoComponent);
            AddComponent(table, 2705, null,
                "The same body as 2704, reached by a different number. Not named, for the same reason.", 1773,
                Takes("window_slot", "interface_id"), NoComponent);
        }

        /// <summary>
        ///     Adds an accessor and its hand-written stack-addressed copy in one statement.
        /// </summary>
        /// <remarks>
        ///     One call rather than two so a pair can never be given two different summaries by
        ///     accident, which is exactly the mistake the client made in these blocks.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        /// <param name="active">The active-component opcode.</param>
        /// <param name="activeLine">Its arm's line.</param>
        /// <param name="stack">The stack-addressed opcode.</param>
        /// <param name="stackLine">Its arm's line.</param>
        /// <param name="mnemonic">The active form's mnemonic, or null to describe both without naming them.</param>
        /// <param name="summary">What both arms push.</param>
        /// <param name="alsoCite">A further <c>file:line</c> the reading rests on, or null.</param>
        /// <param name="operands">What the pair consumes beyond the component, or null for nothing.</param>
        private static void AddAccessorPair(IDictionary<int, ClientScriptOpcodeInfo> table, int active, int activeLine,
            int stack, int stackLine, string? mnemonic, string summary, string? alsoCite,
            ClientScriptStackOperands? operands = null) {
            ClientScriptStackOperands taken = operands ?? ClientScriptStackOperands.Empty;

            AddComponent(table, active, mnemonic, summary, activeLine, taken, Active, alsoCite);
            AddComponent(table, stack,
                mnemonic == null ? null : StackPrefix + mnemonic.Substring(ActivePrefix.Length),
                summary + " Reached with the target popped off the stack rather than taken from the active " +
                "component, which is opcode " + active + ".",
                stackLine, taken.WithStackComponent(), Stack, alsoCite);
        }

        /// <summary>
        ///     Records a number whose arm exists in the source but sits outside the guard that would
        ///     reach it, so the number does not exist in this build.
        /// </summary>
        /// <remarks>
        ///     A <c>CLIENT BUG</c> row in the shape <c>reference/hydra-637-maps/01-cache-access.md</c>
        ///     uses. Worth a row rather than a comment because the generic fallback would describe
        ///     these two as ordinary unnamed opcodes, and a script author reading the tab has no other
        ///     way to learn that the number is unreachable.
        /// </remarks>
        /// <param name="table">The table being built.</param>
        /// <param name="opcode">The number that cannot be dispatched.</param>
        /// <param name="deadArmLine">Where the unreachable arm is written.</param>
        /// <param name="guardLine">The guard that shuts it out.</param>
        /// <param name="summary">Which arm was meant to serve it and why it cannot.</param>
        private static void AddAbsent(IDictionary<int, ClientScriptOpcodeInfo> table, int opcode, int deadArmLine,
            int guardLine, string summary) {
            table[opcode] = new ClientScriptOpcodeInfo(opcode, null, summary,
                "Class247.java:" + deadArmLine + ", Class247.java:" + guardLine,
                ClientScriptStackOperands.Empty, NoComponent);
        }
    }
}
