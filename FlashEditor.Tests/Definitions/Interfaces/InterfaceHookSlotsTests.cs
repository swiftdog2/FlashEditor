using System.Collections.Generic;
using FlashEditor.Definitions.Interfaces;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     How a stored hook array is read, and the two ways of reading it wrong.
    /// </summary>
    public sealed class InterfaceHookSlotsTests {
        /// <summary>
        ///     The script is the first operand, not the last.
        /// </summary>
        /// <remarks>
        ///     <b>The single claim this whole table rests on.</b>
        ///     <c>Class247.method3150:3846</c> takes <c>objects[0]</c> as the script id and walks
        ///     <c>objects[1..]</c> onto the argument stacks. Later revisions of this format put the
        ///     id last, so reading from the end is the natural mistake - and it would name the right
        ///     script on every hook that takes no arguments, which is most of them, so it would look
        ///     like it worked while being wrong on exactly the interesting ones.
        /// </remarks>
        [Fact]
        public void TheScriptIsTheFirstOperandAndTheRestAreArguments() {
            var hook = new[] {
                InterfaceScriptOperand.OfInteger(4271),
                InterfaceScriptOperand.OfInteger(7),
                InterfaceScriptOperand.OfInteger(9)
            };

            string described = InterfaceHookSlots.DescribeCall(hook);

            Assert.StartsWith("script 4271", described);
            Assert.Contains("7, 9", described);
            Assert.DoesNotContain("script 9", described);
        }

        /// <summary>An absent hook describes as nothing at all rather than as an empty call.</summary>
        [Fact]
        public void AnAbsentHookDescribesAsNothing() {
            Assert.Equal("", InterfaceHookSlots.DescribeCall(null));
            Assert.Equal("", InterfaceHookSlots.DescribeCall(new InterfaceScriptOperand[0]));
        }

        /// <summary>
        ///     The nine call-time sentinels are named rather than shown as huge negative constants.
        /// </summary>
        /// <remarks>
        ///     A hook that passes the component's own id stores -2147483645. Unnamed, that reads as
        ///     corruption; named, it reads as the placeholder it is.
        /// </remarks>
        [Theory]
        [InlineData(-2147483647, "mouse x")]
        [InlineData(-2147483646, "mouse y")]
        [InlineData(-2147483645, "this component's id")]
        [InlineData(-2147483644, "op index")]
        [InlineData(-2147483643, "this component's slot")]
        [InlineData(-2147483642, "the other component's id")]
        [InlineData(-2147483641, "the other component's slot")]
        [InlineData(-2147483640, "key or button")]
        [InlineData(-2147483639, "key code")]
        public void ACallTimeSentinelIsNamed(int stored, string expected) {
            string described =
                InterfaceHookSlots.DescribeArgument(InterfaceScriptOperand.OfInteger(stored));

            Assert.StartsWith("[", described);
            Assert.Contains(expected, described);
        }

        /// <summary>An ordinary integer argument is shown as itself, brackets reserved for sentinels.</summary>
        [Fact]
        public void AnOrdinaryArgumentIsNotBracketed() {
            Assert.Equal("42", InterfaceHookSlots.DescribeArgument(InterfaceScriptOperand.OfInteger(42)));
            Assert.Equal("-1", InterfaceHookSlots.DescribeArgument(InterfaceScriptOperand.OfInteger(-1)));
        }

        /// <summary>The one string sentinel is named and every other string is quoted.</summary>
        [Fact]
        public void TheStringSentinelIsNamedAndOtherStringsAreQuoted() {
            InterfaceScriptOperand sentinel = InterfaceScriptOperand.OfString(
                InterfaceText.FromText(InterfaceHookSlots.StringSentinel));
            InterfaceScriptOperand ordinary = InterfaceScriptOperand.OfString(
                InterfaceText.FromText("Bank"));

            Assert.Equal("[the op's base text]", InterfaceHookSlots.DescribeArgument(sentinel));
            Assert.Equal("\"Bank\"", InterfaceHookSlots.DescribeArgument(ordinary));
        }

        /// <summary>
        ///     Every slot names distinct storage, and exactly one has no setter.
        /// </summary>
        /// <remarks>
        ///     Both halves are findings rather than bookkeeping. Two slots sharing a client field
        ///     would mean the read order was transcribed wrong, which is the way this table breaks.
        ///     And slot 0 having no setter is the client firing it itself over every component as an
        ///     interface opens - a panel that showed a blank cell there would read as missing data.
        /// </remarks>
        [Fact]
        public void EverySlotNamesDistinctStorageAndOnlySlotZeroHasNoSetter() {
            var fields = new HashSet<string>();
            var setters = new HashSet<int>();
            int withoutSetter = 0;

            for (int slot = 0; slot < InterfaceHookSlots.Count; slot++) {
                InterfaceHookSlots.Slot entry = InterfaceHookSlots.At(slot);

                Assert.True(fields.Add(entry.ClientField),
                    $"slot {slot} names {entry.ClientField}, which another slot already names.");

                if (entry.SetterOpcode < 0) {
                    withoutSetter++;
                    Assert.Equal(0, slot);
                    continue;
                }

                Assert.True(setters.Add(entry.SetterOpcode),
                    $"CS2 {entry.SetterOpcode} is listed as writing more than one slot.");
            }

            Assert.Equal(20, InterfaceHookSlots.Count);
            Assert.Equal(1, withoutSetter);
        }

        /// <summary>
        ///     Exactly five slots pair with a trigger array, and each pairs with a distinct one.
        /// </summary>
        /// <remarks>
        ///     The wire format stores five trigger arrays and the client's CS2 setters assign a hook
        ///     and its triggers in one statement, so a slot paired with the wrong array would show a
        ///     hook beside triggers that belong to a different event.
        /// </remarks>
        [Fact]
        public void FiveSlotsPairWithDistinctTriggerArrays() {
            var arrays = new HashSet<int>();

            for (int slot = 0; slot < InterfaceHookSlots.Count; slot++) {
                int array = InterfaceHookSlots.At(slot).TriggerArray;
                if (array < 0)
                    continue;

                Assert.InRange(array, 0, 4);
                Assert.True(arrays.Add(array), $"trigger array {array} is claimed by two slots.");
            }

            Assert.Equal(5, arrays.Count);
        }

        /// <summary>A slot's heading says where it lives and what writes it.</summary>
        [Fact]
        public void ASlotHeadingNamesItsStorageAndItsSetter() {
            Assert.Contains("no setter", InterfaceHookSlots.Describe(0));
            Assert.Contains("anObjectArray2332", InterfaceHookSlots.Describe(0));

            Assert.Contains("CS2 1403", InterfaceHookSlots.Describe(1));
            Assert.Contains("trigger array 0", InterfaceHookSlots.Describe(5));
        }
    }
}
