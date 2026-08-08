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
    /// </remarks>
    public static class ClientScriptOpcodes {
        /// <summary>Lowest opcode the two dispatchers above the in-line chain handle.</summary>
        private const int FirstDispatchedOpcode = 100;

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

        /// <summary>The proven mnemonic, or <c>null</c> where none has been established.</summary>
        /// <param name="opcode">The stored opcode.</param>
        /// <returns>The mnemonic or null.</returns>
        public static string? MnemonicOf(int opcode) {
            return Table.TryGetValue(opcode, out ClientScriptOpcodeInfo? known) ? known.Mnemonic : null;
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

        /// <summary>The opcodes carrying a mnemonic, so a coverage sweep can enumerate them.</summary>
        /// <returns>The named opcodes.</returns>
        private static HashSet<int> BuildNamedSet() {
            var named = new HashSet<int>();
            foreach (KeyValuePair<int, ClientScriptOpcodeInfo> entry in Table)
                if (entry.Value.IsNamed)
                    named.Add(entry.Key);
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

        /// <summary>
        ///     The three component lifecycle opcodes, which the client names itself.
        /// </summary>
        /// <remarks>
        ///     101 is the only opcode in the index whose mnemonic is written in the client as a
        ///     string: its two guard clauses raise <c>"Tried to cc_delete static active-component!"</c>
        ///     at <c>Class247.java:246</c> and <c>:249</c>. 100 and 102 are the create and
        ///     delete-every-child arms either side of it and are named from what they do to the same
        ///     child array, <c>aRSInterfaceArray2339</c>.
        ///     <para>
        ///     These three sit outside the 1xxx/2xxx pairing - they take their target off the stack
        ///     and set the interpreter's active component rather than reading it.
        ///     </para>
        /// </remarks>
        /// <param name="table">The table being built.</param>
        private static void AddComponentLifecycle(IDictionary<int, ClientScriptOpcodeInfo> table) {
            Add(table, 100, "cc_create",
                "Pops a component, a type and a slot, creates a child in that slot and makes it active.", 190);
            Add(table, 101, "cc_delete",
                "Removes the active component from its parent's child array; refuses a static one.", 241);
            Add(table, 102, "cc_delete_all", "Pops a component and clears its whole child array.", 260);
        }
    }
}
