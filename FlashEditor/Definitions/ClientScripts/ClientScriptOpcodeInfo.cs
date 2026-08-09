using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     Where an opcode's target component comes from.
    /// </summary>
    /// <remarks>
    ///     The distinction is the interpreter's, not a convention imported from anywhere: the folded
    ///     component blocks resolve <c>RSInterface = bool ? aRSInterface_1877 : aRSInterface_1879</c>
    ///     for the low form and <c>Class159.method2509(cs2stack[--anInt1885])</c> for the high one, in
    ///     the same five lines, at <c>Class247.java:408-418</c>. It is carried on the row rather than
    ///     derived from the number because the numbers do not settle it: 100, 200 and 201 <i>write</i>
    ///     the active register, and the 2500-2899 accessors are hand-written rather than folded.
    /// </remarks>
    public enum ClientScriptComponentAddressing {
        /// <summary>The opcode addresses no component, or nothing here settles that it does.</summary>
        None,

        /// <summary>
        ///     Operates on the interpreter's active component, chosen by the stored operand byte.
        /// </summary>
        /// <remarks>
        ///     The byte is the only thing that picks between the two registers, so it is a selector
        ///     and not a value. <c>Class247.java:246</c> and <c>:249</c> spell the two forms
        ///     <c>.active-component</c> and <c>active-component</c> in an exception message, which is
        ///     the one place in the client that names either.
        /// </remarks>
        ActiveComponent,

        /// <summary>Pops a packed <c>(interfaceId &lt;&lt; 16) | componentId</c> off the integer stack.</summary>
        StackComponent
    }

    /// <summary>Which stack a CS2 opcode takes one of its arguments from.</summary>
    public enum ClientScriptStackType {
        /// <summary>The integer stack.</summary>
        Integer,

        /// <summary>The string stack.</summary>
        Text
    }

    /// <summary>One value an opcode consumes off a stack.</summary>
    public sealed class ClientScriptStackOperand {
        /// <summary>What the value means, in this project's vocabulary.</summary>
        public string Name { get; }

        /// <summary>Which stack it comes off.</summary>
        public ClientScriptStackType Type { get; }

        /// <summary>Binds a name to a stack.</summary>
        /// <param name="name">What the value means.</param>
        /// <param name="type">Which stack it comes off.</param>
        public ClientScriptStackOperand(string name, ClientScriptStackType type) {
            Name = name;
            Type = type;
        }
    }

    /// <summary>
    ///     What an opcode consumes off the two stacks, in the order a script pushes it.
    /// </summary>
    /// <remarks>
    ///     <b>Push order, not pop order, and the difference is real.</b> Opcode 1308 pops
    ///     <c>anInt2318</c> and then <c>anInt2309</c> (<c>Class247.java:1119-1120</c>), so the script
    ///     pushed them the other way round; a list written in pop order would tell a reader to emit
    ///     the two values reversed. The stack-addressed twin of a folded opcode pops its component
    ///     first (<c>:411</c>), so the component is the <i>last</i> thing pushed, which is what
    ///     <see cref="WithStackComponent"/> encodes.
    ///     <para>
    ///     <b>An unstated list is not an empty one.</b> Most of the 763 reachable opcodes have no row
    ///     here at all, and a row that has not been read cannot claim the opcode takes nothing -
    ///     <see cref="Unstated"/> renders blank and <see cref="Empty"/> renders as "nothing", so the
    ///     two never read alike.
    ///     </para>
    ///     <para>
    ///     <b>Eleven opcodes have no arity at all.</b> The 1400 hook block pops a format string and
    ///     then reads one value per character of it, string for <c>'s'</c> and integer otherwise, with
    ///     an optional trigger array in front when the format ends in <c>'Y'</c>
    ///     (<c>Class247.java:1219-1250</c>). Stating a number for those would be a lie, so
    ///     <see cref="IsVariadic"/> exists to say the count is decided at run time.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptStackOperands {
        /// <summary>Nothing has been read about what this opcode consumes.</summary>
        public static readonly ClientScriptStackOperands Unstated =
            new ClientScriptStackOperands(new List<ClientScriptStackOperand>(), false, false);

        /// <summary>The opcode consumes nothing, which has been read rather than assumed.</summary>
        public static readonly ClientScriptStackOperands Empty =
            new ClientScriptStackOperands(new List<ClientScriptStackOperand>(), false, true);

        private readonly List<ClientScriptStackOperand> slots;

        /// <summary>The values consumed, in push order.</summary>
        public IReadOnlyList<ClientScriptStackOperand> Slots => slots;

        /// <summary>Whether further values follow whose count a run-time format string decides.</summary>
        public bool IsVariadic { get; }

        /// <summary>Whether this project has read the arm and can speak to what it consumes.</summary>
        public bool IsStated { get; }

        private ClientScriptStackOperands(List<ClientScriptStackOperand> slots, bool variadic, bool stated) {
            this.slots = slots;
            IsVariadic = variadic;
            IsStated = stated;
        }

        /// <summary>
        ///     Builds a list from a compact spec, one entry per value in push order.
        /// </summary>
        /// <remarks>
        ///     A name alone is an integer and a name suffixed <c>:s</c> comes off the string stack.
        ///     The shorthand exists because the table holds well over a hundred of these and a
        ///     constructor call per value would bury the opcode rows it is meant to annotate.
        /// </remarks>
        /// <param name="spec">The values, in push order.</param>
        /// <returns>The operand list.</returns>
        public static ClientScriptStackOperands Of(params string[] spec) {
            return Build(spec, false);
        }

        /// <summary>
        ///     Builds a list whose tail is decided by a format string the script pushes.
        /// </summary>
        /// <param name="spec">The values that are always present, in push order.</param>
        /// <returns>The operand list.</returns>
        public static ClientScriptStackOperands Variadic(params string[] spec) {
            return Build(spec, true);
        }

        /// <summary>
        ///     The same list with the packed component id the stack-addressed form pops appended.
        /// </summary>
        /// <remarks>
        ///     Appended rather than prepended: the twin pops the component before it pops anything
        ///     else, so in push order it is last.
        /// </remarks>
        /// <returns>The twin's operand list.</returns>
        public ClientScriptStackOperands WithStackComponent() {
            if (!IsStated)
                return Unstated;

            var widened = new List<ClientScriptStackOperand>(slots) {
                new ClientScriptStackOperand("component", ClientScriptStackType.Integer)
            };

            return new ClientScriptStackOperands(widened, IsVariadic, true);
        }

        /// <summary>
        ///     The list as one line, blank when nothing has been read about the opcode.
        /// </summary>
        /// <returns>The rendered list.</returns>
        public string Text() {
            if (!IsStated)
                return string.Empty;

            if (slots.Count == 0)
                return IsVariadic ? "decided by the format string" : "nothing";

            var text = new StringBuilder();
            foreach (ClientScriptStackOperand slot in slots) {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(slot.Name);
                if (slot.Type == ClientScriptStackType.Text)
                    text.Append('$');
            }

            if (IsVariadic)
                text.Append(", then as many more as the format string names");

            return text.ToString();
        }

        private static ClientScriptStackOperands Build(string[] spec, bool variadic) {
            var built = new List<ClientScriptStackOperand>(spec.Length);

            foreach (string entry in spec) {
                bool isText = entry.EndsWith(":s", System.StringComparison.Ordinal);
                built.Add(new ClientScriptStackOperand(
                    isText ? entry.Substring(0, entry.Length - 2) : entry,
                    isText ? ClientScriptStackType.Text : ClientScriptStackType.Integer));
            }

            return new ClientScriptStackOperands(built, variadic, true);
        }
    }

    /// <summary>
    ///     What is known about one CS2 opcode, and how much of it is proven.
    /// </summary>
    /// <remarks>
    ///     <see cref="Mnemonic"/> is deliberately nullable and that is the whole point of this type.
    ///     A name is carried only where the 637 client's own dispatch settles it; everything else
    ///     keeps the number and says which dispatcher would have handled it. A wrong mnemonic is
    ///     worse than no mnemonic, because a number is honestly unknown while a name is confidently
    ///     misleading - and this project has already been burnt once by a mapping that looked right
    ///     in aggregate and was not.
    /// </remarks>
    public sealed class ClientScriptOpcodeInfo {
        /// <summary>The stored opcode.</summary>
        public int Opcode { get; }

        /// <summary>The mnemonic, or <c>null</c> when the client's dispatch does not settle one.</summary>
        public string? Mnemonic { get; }

        /// <summary>
        ///     What the client's dispatch arm does with the stacks, in one line.
        /// </summary>
        /// <remarks>
        ///     Present for every opcode, named or not: for a named one it is the evidence behind the
        ///     name, and for an unnamed one it is at least the dispatcher and the calling convention
        ///     it reaches, which is more than the number alone says.
        /// </remarks>
        public string Summary { get; }

        /// <summary>
        ///     Where in the 637 client the claim can be checked, as <c>file:line</c>.
        /// </summary>
        /// <remarks>
        ///     More than one, comma separated, wherever the name rests on something outside the
        ///     dispatch arm - a renderer that reads the field, a definition provider that resolves the
        ///     id, the decode ordinal that ties the field to this project's own name for it. The
        ///     first entry is always the arm in <c>Class247.java</c>; a name whose only evidence is a
        ///     second file is a name taken from a field label rather than from behaviour, which is the
        ///     failure this whole table exists to avoid.
        /// </remarks>
        public string Citation { get; }

        /// <summary>What the opcode consumes off the two stacks, in push order.</summary>
        public ClientScriptStackOperands Operands { get; }

        /// <summary>Where the component this opcode acts on comes from.</summary>
        public ClientScriptComponentAddressing Addressing { get; }

        /// <summary>Whether a mnemonic has been proven for this opcode.</summary>
        public bool IsNamed => Mnemonic != null;

        /// <summary>Binds one opcode's name, description and citation.</summary>
        /// <param name="opcode">The stored opcode.</param>
        /// <param name="mnemonic">The proven mnemonic, or null.</param>
        /// <param name="summary">What the dispatch does.</param>
        /// <param name="citation">The client line or lines that prove it.</param>
        /// <param name="operands">What it consumes off the stacks, in push order.</param>
        /// <param name="addressing">Where its target component comes from.</param>
        public ClientScriptOpcodeInfo(int opcode, string? mnemonic, string summary, string citation,
            ClientScriptStackOperands? operands = null,
            ClientScriptComponentAddressing addressing = ClientScriptComponentAddressing.None) {
            Opcode = opcode;
            Mnemonic = mnemonic;
            Summary = summary;
            Citation = citation;
            Operands = operands ?? ClientScriptStackOperands.Unstated;
            Addressing = addressing;
        }
    }
}
