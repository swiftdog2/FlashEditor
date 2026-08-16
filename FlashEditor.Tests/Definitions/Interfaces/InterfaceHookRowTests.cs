using System.Collections.Generic;
using FlashEditor.Definitions.Interfaces;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     What the behaviour panel says about each of the twenty stored hook slots.
    /// </summary>
    /// <remarks>
    ///     The rows are built off the definition alone and hold no controls, which is the only reason
    ///     any of this is testable - nothing in this suite covers WinForms, so a table assembled
    ///     inside a grid's population loop would be defended by nothing at all.
    /// </remarks>
    public sealed class InterfaceHookRowTests {
        private static InterfaceComponentDefinition Component() {
            return new InterfaceComponentDefinition(3, 4) { ComponentType = 5 };
        }

        private static InterfaceScriptOperand[] Script(int id, params int[] arguments) {
            var operands = new List<InterfaceScriptOperand> {
                InterfaceScriptOperand.OfInteger(id)
            };

            foreach (int argument in arguments)
                operands.Add(InterfaceScriptOperand.OfInteger(argument));

            return operands.ToArray();
        }

        /// <summary>
        ///     Twenty rows for a component that stores nothing, and no more.
        /// </summary>
        /// <remarks>
        ///     Both halves matter. Twenty, because the slot-to-storage-to-setter mapping is a
        ///     property of the format rather than of the component on screen. And no more, because
        ///     the ten CS2 opcodes 1418 to 1427 set hook arrays the wire format does not carry - a
        ///     reader who saw thirty rows would go looking in the bytes for ten arrays that are not
        ///     in them.
        /// </remarks>
        [Fact]
        public void ForAComponentWithNoHooks_ThereAreTwentyRowsAndAllAreEmpty() {
            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(Component());

            Assert.Equal(20, rows.Count);

            for (int slot = 0; slot < rows.Count; slot++) {
                Assert.Equal(slot, rows[slot].Slot);
                Assert.False(rows[slot].IsStored);
                Assert.Equal(-1, rows[slot].ScriptId);
                Assert.Equal(string.Empty, rows[slot].Call);
                Assert.Equal(string.Empty, rows[slot].Operands);
            }
        }

        /// <summary>
        ///     Slot 0 says it has no setter rather than leaving the cell blank.
        /// </summary>
        /// <remarks>
        ///     It is the hook the client fires itself over every component as an interface opens
        ///     (<c>Class247.java:4130-4136</c>), and the only opcode in the 1400 block that touches
        ///     it is 1499, which clears everything. A blank cell in a column headed "set by" reads as
        ///     a gap in the table; this is a finding.
        /// </remarks>
        [Fact]
        public void Slot0_SaysItHasNoSetterAndNamesWhoFiresIt() {
            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(Component());

            Assert.Equal(InterfaceHookSlots.ClientFiresIt, rows[0].Setter);
            Assert.Contains("client fires it", rows[0].Setter);
            Assert.Equal("anObjectArray2332", rows[0].Storage);

            //And every other slot names an opcode, so slot 0 is the exception rather than the
            //shape of an unfinished table.
            for (int slot = 1; slot < rows.Count; slot++)
                Assert.StartsWith("CS2 1", rows[slot].Setter);
        }

        /// <summary>
        ///     The five paired slots show their trigger array and the other fifteen say they have
        ///     none.
        /// </summary>
        /// <remarks>
        ///     Their CS2 setters - 1407, 1414, 1415, 1428 and 1429 - each assign the hook and its
        ///     int array in one statement, so a panel showing the hook without the triggers shows
        ///     half the record. "No trigger array" and "stores none" are also different facts, and
        ///     only these five can ever be the second.
        /// </remarks>
        [Fact]
        public void TheFivePairedSlots_ShowTheirTriggerArrayAndTheRestSayTheyHaveNone() {
            InterfaceComponentDefinition component = Component();
            component.Triggers[2] = new[] { 7, 8 };

            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(component);

            var paired = new Dictionary<int, int> { [5] = 0, [6] = 1, [7] = 2, [18] = 3, [19] = 4 };

            for (int slot = 0; slot < rows.Count; slot++) {
                if (!paired.TryGetValue(slot, out int array)) {
                    Assert.Equal(InterfaceHookSlots.NoTriggerArray, rows[slot].Triggers);
                    continue;
                }

                Assert.StartsWith("array " + array + ":", rows[slot].Triggers);
            }

            //Slot 7 holds the array that was filled, and slot 5 the one that was not - the wording
            //has to tell those apart or an empty pairing reads as an absent one.
            Assert.Equal("array 2: 7, 8", rows[7].Triggers);
            Assert.Equal("array 0: not stored", rows[5].Triggers);
        }

        /// <summary>
        ///     The script is the first operand and the rest are its arguments.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.method3150:3846</c> takes <c>objects[0]</c> as the script and walks
        ///     <c>objects[1..]</c> onto the argument stacks. Later revisions put the id last, so
        ///     reading it from the end would name a plausible wrong script on every hook that takes
        ///     arguments and the right one on every hook that takes none - which is most of them, so
        ///     the mistake would look like it worked.
        /// </remarks>
        [Fact]
        public void TheScriptIsTheFirstOperandAndNotTheLast() {
            InterfaceComponentDefinition component = Component();
            component.Hooks[1] = Script(4021, 17, 99);

            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(component);

            Assert.True(rows[1].IsStored);
            Assert.Equal(4021, rows[1].ScriptId);
            Assert.Equal("4021, 17, 99", rows[1].Operands);
            Assert.Contains("script 4021", rows[1].Call);
        }

        /// <summary>
        ///     A hook named by a string has no script id, and the name survives in the call.
        /// </summary>
        /// <remarks>
        ///     A name is not an id, so nothing may present one as a followable index-12 record. No
        ///     file in either supported cache stores one in the first position, which is exactly why
        ///     it is asserted here rather than left to a sweep.
        /// </remarks>
        [Fact]
        public void AHookNamedByAStringHasNoScriptIdToFollow() {
            InterfaceComponentDefinition component = Component();
            component.Hooks[3] = new[] { InterfaceScriptOperand.OfString(InterfaceText.FromText("doit")) };

            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(component);

            Assert.True(rows[3].IsStored);
            Assert.Equal(-1, rows[3].ScriptId);
            Assert.Contains("doit", rows[3].Call);
            Assert.Equal("\"doit\"", rows[3].Operands);
        }

        /// <summary>
        ///     The version-gated twenty-first array gets a row only when it holds something.
        /// </summary>
        /// <remarks>
        ///     It is not a slot. It sits between slots 9 and 10 in the stream and is read only when
        ///     the version byte is non-negative (<c>RSInterface.java:1320-1322</c>), and every
        ///     component in both supported caches stores 255 - so a permanent row for it would be a
        ///     twenty-first line that could never be filled by any file on disk.
        /// </remarks>
        [Fact]
        public void TheVersionGatedArray_GetsARowOnlyWhenItIsStored() {
            InterfaceComponentDefinition component = Component();
            Assert.Equal(20, InterfaceHookRow.For(component).Count);

            component.VersionedHook = Script(11);

            IReadOnlyList<InterfaceHookRow> rows = InterfaceHookRow.For(component);
            Assert.Equal(21, rows.Count);
            Assert.Equal(-1, rows[20].Slot);
            Assert.Equal(11, rows[20].ScriptId);
            Assert.Equal(InterfaceHookSlots.NoTriggerArray, rows[20].Triggers);
        }
    }
}
