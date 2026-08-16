using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     One hook slot of one component, as the behaviour panel lists it.
    /// </summary>
    /// <remarks>
    ///     <b>A row per stored slot, and never a row for anything else.</b> The wire format holds
    ///     twenty hook arrays and five trigger arrays, and the CS2 dispatcher has ten further setters
    ///     - opcodes <see cref="InterfaceHookSlots.RuntimeOnlySetters"/> - whose arrays exist only at
    ///     runtime. Those get no row: a reader who saw thirty here would go hunting in the bytes for
    ///     ten arrays that are not in them.
    ///     <para>
    ///     <b>Named after the storage and the setter, never after an event.</b> Which event fires
    ///     which array is decided outside the dispatcher, so calling slot 1 <c>on-click</c> would be
    ///     inventing a name rather than deriving one - and an invented name in an editor is worse
    ///     than a number, because a number is visibly unknown. The client field and the CS2 opcode
    ///     are both checkable, against <c>RSInterface.unpackConfig</c>'s read order
    ///     (<c>RSInterface.java:1308-1340</c>) and the 1400 setter block
    ///     (<c>Class247.java:1254-1317</c>).
    ///     </para>
    ///     <para>
    ///     Built off <see cref="InterfaceComponentDefinition"/> alone and holding no controls, so the
    ///     mapping can be tested - nothing in this repository's suite covers WinForms, and a table
    ///     assembled inside a grid's population loop would be defended by nothing.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceHookRow {
        private InterfaceHookRow(int slot, string storage, string setter, string triggers,
            InterfaceScriptOperand[] hook) {
            Slot = slot;
            Storage = storage;
            Setter = setter;
            Triggers = triggers;
            Hook = hook;
        }

        /// <summary>
        ///     The slot's position in the wire format, or -1 for the version-gated array.
        /// </summary>
        /// <remarks>
        ///     The twenty-first array is read between slots 9 and 10 and only when the version byte
        ///     is non-negative (<c>RSInterface.java:1320-1322</c>), so it has a place in the stream
        ///     and no slot number. Every component in both supported caches stores version 255, so
        ///     no file here has one.
        /// </remarks>
        public int Slot { get; }

        /// <summary>The client field the slot decodes into.</summary>
        public string Storage { get; }

        /// <summary>What writes the slot, in words.</summary>
        public string Setter { get; }

        /// <summary>The trigger array paired with the slot, and what it holds.</summary>
        public string Triggers { get; }

        /// <summary>The stored operand array, which is empty when the component stores no hook.</summary>
        public InterfaceScriptOperand[] Hook { get; }

        /// <summary>Whether the component stores anything in this slot.</summary>
        public bool IsStored => Hook.Length > 0;

        /// <summary>
        ///     The script this hook calls, or -1.
        /// </summary>
        /// <remarks>
        ///     <b>The first operand, not the last.</b> <c>Class247.method3150:3846</c> takes
        ///     <c>objects[0]</c> as the script and walks <c>objects[1..]</c> onto the argument
        ///     stacks. Later revisions of this format put the id last, so reading it from the end
        ///     would name a plausible wrong script on every hook that takes arguments and the right
        ///     one on every hook that takes none - which is most of them, so the mistake would look
        ///     like it worked.
        ///     <para>
        ///     -1 when the hook is absent <i>and</i> when its first operand is a string, because a
        ///     name is not an id. The call description keeps the name in that case.
        ///     </para>
        /// </remarks>
        public int ScriptId => Hook.Length > 0 && Hook[0].TypeByte == InterfaceScriptOperand.IntegerType
            ? Hook[0].Integer
            : -1;

        /// <summary>The script and its arguments, with the call-time substitutions named.</summary>
        public string Call => InterfaceHookSlots.DescribeCall(Hook);

        /// <summary>
        ///     The stored operands, with strings quoted.
        /// </summary>
        /// <remarks>
        ///     Beside <see cref="Call"/> rather than instead of it, because the readable form drops
        ///     the type bytes and the type byte is the only thing that tells an integer operand from
        ///     a string one on the wire. An editor has to be able to show both.
        /// </remarks>
        public string Operands {
            get {
                if (Hook.Length == 0)
                    return string.Empty;

                var parts = new List<string>(Hook.Length);
                foreach (InterfaceScriptOperand operand in Hook) {
                    parts.Add(operand.TypeByte == InterfaceScriptOperand.StringType
                        ? "\"" + (operand.Text?.Text ?? "") + "\""
                        : operand.Integer.ToString());
                }

                return string.Join(", ", parts);
            }
        }

        /// <summary>
        ///     Every slot of one component, in the order the wire format stores them.
        /// </summary>
        /// <remarks>
        ///     Always all twenty, whether the component stores them or not. The panel can hide the
        ///     empty ones, but the mapping from slot to storage to setter is a property of the format
        ///     rather than of this component, and a list that shrank to the four slots a button
        ///     happens to use would never show a reader that slot 0 has no setter at all.
        ///     <para>
        ///     The version-gated twenty-first array is appended only when the component stores one,
        ///     because it is not a slot: nothing in either supported cache reaches the branch that
        ///     reads it, so a permanent empty row for it would be twenty-one rows of which one could
        ///     never be filled.
        ///     </para>
        /// </remarks>
        /// <param name="component">The decoded component.</param>
        /// <returns>The rows.</returns>
        public static IReadOnlyList<InterfaceHookRow> For(InterfaceComponentDefinition component) {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            var rows = new List<InterfaceHookRow>(InterfaceHookSlots.Count + 1);

            for (int slot = 0; slot < InterfaceHookSlots.Count; slot++) {
                InterfaceHookSlots.Slot entry = InterfaceHookSlots.At(slot);

                string setter = entry.SetterOpcode < 0
                    ? InterfaceHookSlots.ClientFiresIt
                    : "CS2 " + entry.SetterOpcode;

                rows.Add(new InterfaceHookRow(slot, entry.ClientField, setter,
                    DescribeTriggers(component, entry.TriggerArray), component.Hooks[slot]));
            }

            if (component.VersionedHook.Length > 0) {
                rows.Add(new InterfaceHookRow(-1,
                    "anObjectArray2253, the version-gated twenty-first array",
                    "no setter in the 1400 block", InterfaceHookSlots.NoTriggerArray,
                    component.VersionedHook));
            }

            return rows;
        }

        /// <summary>
        ///     What the trigger cell says for one slot.
        /// </summary>
        /// <remarks>
        ///     A slot that pairs with an array says so even when the array is empty, because "this
        ///     hook has triggers and the component stores none" and "this hook has no triggers at
        ///     all" are different facts about the format - and only the five paired slots can ever
        ///     be the first.
        /// </remarks>
        /// <param name="component">The decoded component.</param>
        /// <param name="triggerArray">The paired array's index, or -1.</param>
        /// <returns>The description.</returns>
        private static string DescribeTriggers(InterfaceComponentDefinition component, int triggerArray) {
            if (triggerArray < 0)
                return InterfaceHookSlots.NoTriggerArray;

            int[] values = component.Triggers[triggerArray];
            return values.Length == 0
                ? "array " + triggerArray + ": not stored"
                : "array " + triggerArray + ": " + string.Join(", ", values);
        }
    }
}
