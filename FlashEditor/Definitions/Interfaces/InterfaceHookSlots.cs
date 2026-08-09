using System;
using System.Collections.Generic;
using System.Text;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     What the twenty stored hook slots are, and how to read one.
    /// </summary>
    /// <remarks>
    ///     <b>A hook is the only behaviour an interface file carries.</b> Everything a component
    ///     does when it is clicked, hovered, dragged or opened is a CS2 script fired from one of
    ///     these slots, and until now the tab showed them as a count. A component with eight hooks
    ///     and one with none looked the same apart from the number.
    ///     <para>
    ///     <b>The slots are named after their storage and their setter, never after an event.</b>
    ///     Which event fires which array is decided outside the CS2 dispatcher entirely, so calling
    ///     slot 1 <c>on-click</c> would be inventing the name rather than deriving it - and an
    ///     invented name in an editor is worse than a number, because a number is visibly unknown.
    ///     What is checkable is the client field each slot lands in and the CS2 opcode that writes
    ///     it, and that is what is listed.
    ///     </para>
    ///     <para>
    ///     <b>Slot 0 has no setter and that is a finding, not a gap.</b> It is the hook the client
    ///     fires itself over every component as an interface opens
    ///     (<c>Class247.java:4130-4136</c>); the only opcode in the 1400 block that touches it is
    ///     1499, which clears everything.
    ///     </para>
    /// </remarks>
    public static class InterfaceHookSlots {
        /// <summary>One stored hook slot: where it lives and what writes it.</summary>
        /// <param name="ClientField">The client field the slot decodes into.</param>
        /// <param name="SetterOpcode">The CS2 opcode that writes it, or -1 when nothing does.</param>
        /// <param name="TriggerArray">The trigger array it pairs with, or -1.</param>
        public readonly record struct Slot(string ClientField, int SetterOpcode, int TriggerArray);

        /// <summary>
        ///     The twenty slots, in the order the wire format stores them.
        /// </summary>
        /// <remarks>
        ///     Transcribed from <c>RSInterface.unpackConfig</c>'s read order
        ///     (<c>RSInterface.java:1308-1340</c>) crossed with the 1400 setter block
        ///     (<c>Class247.java:1254-1317</c>). The pairing is checkable in both directions, which
        ///     is what makes it evidence rather than a mapping someone wrote down.
        /// </remarks>
        private static readonly Slot[] Table = {
            new Slot("anObjectArray2332", -1, -1),
            new Slot("anObjectArray2227", 1403, -1),
            new Slot("anObjectArray2272", 1404, -1),
            new Slot("anObjectArray2324", 1406, -1),
            new Slot("anObjectArray2257", 1416, -1),
            new Slot("anObjectArray2269", 1407, 0),
            new Slot("anObjectArray2252", 1414, 1),
            new Slot("anObjectArray2278", 1415, 2),
            new Slot("anObjectArray2270", 1408, -1),
            new Slot("some_interface_script", 1409, -1),
            new Slot("anObjectArray2314", 1412, -1),
            new Slot("anObjectArray2291", 1400, -1),
            new Slot("anObjectArray2335", 1411, -1),
            new Slot("anObjectArray2356", 1402, -1),
            new Slot("anObjectArray2230", 1401, -1),
            new Slot("anObjectArray2316", 1405, -1),
            new Slot("anObjectArray2313", 1410, -1),
            new Slot("anObjectArray2277", 1417, -1),
            new Slot("anObjectArray2212", 1428, 3),
            new Slot("anObjectArray2320", 1429, 4)
        };

        /// <summary>
        ///     The integer values a stored argument can carry that are replaced when the hook fires.
        /// </summary>
        /// <remarks>
        ///     Read off <c>Class247.method3150</c> (<c>:3862-3905</c>), which is the one place a hook
        ///     array is unpacked onto the script's argument stacks. Without these the arguments read
        ///     as absurd negative constants - a hook that passes the component's own id stores
        ///     -2147483645, which looks like corruption and is a placeholder.
        /// </remarks>
        private static readonly Dictionary<int, string> Sentinels = new() {
            [-2147483647] = "mouse x, relative to the component",
            [-2147483646] = "mouse y, relative to the component",
            [-2147483645] = "this component's id",
            [-2147483644] = "the op index that fired it",
            [-2147483643] = "this component's slot",
            [-2147483642] = "the other component's id",
            [-2147483641] = "the other component's slot",
            [-2147483640] = "the key or button pressed",
            [-2147483639] = "the key code"
        };

        /// <summary>
        ///     The one string argument replaced at call time.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:3910-3912</c>. It is a literal string in the file, not a number, so
        ///     it is the only sentinel a reader would notice unaided.
        /// </remarks>
        public const string StringSentinel = "event_opbase";

        /// <summary>How many slots the wire format stores.</summary>
        public static int Count => Table.Length;

        /// <summary>What one slot is.</summary>
        /// <param name="slot">The slot index, as the wire format orders them.</param>
        /// <returns>The slot.</returns>
        public static Slot At(int slot) {
            if (slot < 0 || slot >= Table.Length)
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "There are twenty stored hook slots.");

            return Table[slot];
        }

        /// <summary>A slot's heading, naming its storage and what writes it.</summary>
        /// <param name="slot">The slot index.</param>
        /// <returns>The heading.</returns>
        public static string Describe(int slot) {
            Slot entry = At(slot);

            string setter = entry.SetterOpcode < 0
                ? "no setter, the client fires it as the interface opens"
                : "set by CS2 " + entry.SetterOpcode;

            string triggers = entry.TriggerArray < 0
                ? ""
                : ", with trigger array " + entry.TriggerArray;

            return "slot " + slot + "  " + entry.ClientField + "  (" + setter + triggers + ")";
        }

        /// <summary>
        ///     A stored hook array as the script it calls and the arguments it passes.
        /// </summary>
        /// <remarks>
        ///     <b>The script id is the FIRST operand, not the last.</b>
        ///     <c>Class247.method3150:3846</c> takes <c>objects[0]</c> as the script and walks
        ///     <c>objects[1..]</c> onto the argument stacks. Later revisions of this format put the
        ///     id last, so reading it from the end here would name a plausible wrong script on every
        ///     hook that takes arguments - and name the right one on every hook that takes none,
        ///     which is most of them, so the mistake would look like it worked.
        /// </remarks>
        /// <param name="hook">The stored operand array.</param>
        /// <returns>The description, or an empty string for an absent hook.</returns>
        public static string DescribeCall(InterfaceScriptOperand[]? hook) {
            if (hook == null || hook.Length == 0)
                return string.Empty;

            var text = new StringBuilder();

            InterfaceScriptOperand first = hook[0];
            if (first.TypeByte == InterfaceScriptOperand.IntegerType)
                text.Append("script ").Append(first.Integer);
            else
                text.Append("script named ").Append(first.Text?.Text ?? "");

            if (hook.Length == 1)
                return text.ToString();

            text.Append("  (");
            for (int i = 1; i < hook.Length; i++) {
                if (i > 1)
                    text.Append(", ");

                text.Append(DescribeArgument(hook[i]));
            }

            return text.Append(')').ToString();
        }

        /// <summary>One stored argument, with a call-time substitution named where it is one.</summary>
        /// <param name="operand">The operand.</param>
        /// <returns>The description.</returns>
        public static string DescribeArgument(InterfaceScriptOperand operand) {
            if (operand.TypeByte != InterfaceScriptOperand.IntegerType) {
                string value = operand.Text?.Text ?? "";
                return value == StringSentinel ? "[the op's base text]" : "\"" + value + "\"";
            }

            return Sentinels.TryGetValue(operand.Integer, out string? named)
                ? "[" + named + "]"
                : operand.Integer.ToString();
        }
    }
}
