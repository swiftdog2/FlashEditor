using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     Editing a colour on a real component, setting it back, and landing on the bytes it was
    ///     read from.
    /// </summary>
    /// <remarks>
    ///     <b>The byte-identity sweep next door cannot make this claim.</b> It proves an
    ///     <i>unedited</i> component re-encodes to what it was read from, which is a different
    ///     statement - four real defects in this repository have lived in exactly that gap, and every
    ///     one of them was an asymmetric setter that passed a sweep and failed a set-and-unset.
    ///     <para>
    ///     The interface tab now edits two colour fields: the shared <c>Colour</c> that types 3, 4, 5
    ///     and 9 read, and the type-5 <c>OutlineColour</c>. Both are plain <c>int32</c> fields with no
    ///     opcode and no length change, so the risk is not the same shape as a bare flag's - what
    ///     could go wrong here is an encoder writing a field the type does not read, or reading one
    ///     it does. Both would show as a length change or as a byte moving, and both are caught below.
    ///     </para>
    ///     <para>
    ///     Every declared component is swept rather than a sample, and the assertion is over the
    ///     <i>relationship</i>: every component whose type reads a colour must survive the round
    ///     trip, and the population must not be zero. There is no <c>or</c> in it, so a decoder that
    ///     stopped classifying types would fail rather than quietly cover nothing.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheInterfaceColourEditTests : IClassFixture<RealCacheFixture> {
        /// <summary>
        ///     A colour no shipped component is likely to store, so the "it really changed" half of
        ///     the check is not satisfied by accident.
        /// </summary>
        /// <remarks>
        ///     The test asserts that the edited encoding <i>differs</i> before asserting that undoing
        ///     it matches. Without that first half, a setter that silently did nothing would pass the
        ///     second half on every record in the cache.
        /// </remarks>
        private const int ProbeColour = 0x5A3C1E;

        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheInterfaceColourEditTests(RealCacheFixture fixture, ITestOutputHelper output) {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Setting the shared colour and setting it back lands on the original stored bytes.
        /// </summary>
        /// <remarks>
        ///     Types 3, 4, 5 and 9 all read one <c>Colour</c> field, and only one type block ever
        ///     runs for a component, so for a layer or a model it is storage the decoder never wrote.
        ///     Those are excluded rather than counted: writing to a field the encoder does not emit
        ///     would round trip trivially and prove nothing.
        /// </remarks>
        [RealCacheFact]
        public void SettingTheSharedColourAndSettingItBack_LandsOnTheStoredBytes() {
            int candidates = 0;
            int survived = 0;

            foreach ((InterfaceComponentDefinition component, byte[] stored) in EveryComponent()) {
                if (component.ComponentType is not (3 or 4 or 5 or 9))
                    continue;

                candidates++;

                int original = component.Colour;
                if (SurvivesAnEditAndItsUndo(component, stored, original == ProbeColour ? ~ProbeColour & 0xFFFFFF : ProbeColour,
                        () => component.Colour, value => component.Colour = value))
                    survived++;
            }

            _output.WriteLine("Shared colour: " + survived + " of " + candidates +
                " components whose type reads it.");

            Assert.True(candidates > 0, "No component in this cache reads the shared colour field.");
            Assert.Equal(candidates, survived);
        }

        /// <summary>
        ///     Setting a sprite's outline colour and setting it back lands on the original bytes.
        /// </summary>
        /// <remarks>
        ///     The newly editable field, and the only other colour the format carries. Only type 5
        ///     reads it; every other type is excluded for the same reason as above.
        ///     <para>
        ///     A stored zero means "no outline" rather than black, and is included here anyway -
        ///     giving an outline to a sprite that had none and then taking it away again is the edit
        ///     the field pane's picker exists for, and it is the one most likely to leave a byte
        ///     behind if the encoder ever gained a "write this only when it is set" branch.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void SettingASpriteOutlineColourAndSettingItBack_LandsOnTheStoredBytes() {
            int candidates = 0;
            int survived = 0;
            int storedNone = 0;

            foreach ((InterfaceComponentDefinition component, byte[] stored) in EveryComponent()) {
                if (component.ComponentType != 5)
                    continue;

                candidates++;
                if (component.OutlineColour == 0)
                    storedNone++;

                int original = component.OutlineColour;
                if (SurvivesAnEditAndItsUndo(component, stored, original == ProbeColour ? ~ProbeColour & 0xFFFFFF : ProbeColour,
                        () => component.OutlineColour, value => component.OutlineColour = value))
                    survived++;
            }

            _output.WriteLine("Outline colour: " + survived + " of " + candidates +
                " sprite components, of which " + storedNone + " store no outline at all.");

            Assert.True(candidates > 0, "No sprite component in this cache to edit an outline on.");
            Assert.Equal(candidates, survived);
        }

        /// <summary>
        ///     Setting a colour on a component whose type does not read one changes no byte.
        /// </summary>
        /// <remarks>
        ///     The other side of the rule the descriptor's Outline column states in code: only type 5
        ///     writes an outline, so an edit to any other type is one the encoder silently drops.
        ///     That is why the column guards its setter - an edit that stages nothing reads as a save
        ///     that failed - and this pins that the encoder really does drop it rather than growing
        ///     the record by four bytes.
        /// </remarks>
        [RealCacheFact]
        public void EditingAColourFieldATypeDoesNotRead_ChangesNothingOnTheWire() {
            int checkedComponents = 0;

            foreach ((InterfaceComponentDefinition component, byte[] stored) in EveryComponent()) {
                //Layers and models: neither reads the shared colour, and neither reads an outline.
                if (component.ComponentType is not (0 or 6))
                    continue;

                component.Colour = ProbeColour;
                component.OutlineColour = ProbeColour;

                Assert.Equal(stored, component.Encode().ToArray());
                checkedComponents++;
            }

            _output.WriteLine("Colour writes dropped by the encoder on " + checkedComponents +
                " layer and model components.");

            Assert.True(checkedComponents > 0, "No layer or model component in this cache.");
        }

        /// <summary>
        ///     One record's set, unset and compare.
        /// </summary>
        /// <remarks>
        ///     Asserts rather than returns false on a failure, so the first bad record names itself
        ///     with its address instead of showing up as a count that is one short.
        /// </remarks>
        /// <param name="component">The decoded component, which is mutated and restored.</param>
        /// <param name="stored">The bytes it was read from.</param>
        /// <param name="probe">A value it does not already hold.</param>
        /// <param name="read">Reads the field.</param>
        /// <param name="write">Writes the field.</param>
        /// <returns>Always true, so a caller can count what it swept.</returns>
        private static bool SurvivesAnEditAndItsUndo(InterfaceComponentDefinition component,
            byte[] stored, int probe, System.Func<int> read, System.Action<int> write) {
            string where = "interface " + component.GroupId + " component " + component.FileId;
            int original = read();

            write(probe);
            byte[] edited = component.Encode().ToArray();

            //The edit has to be visible on the wire, or the undo below proves nothing at all.
            Assert.True(edited.Length == stored.Length,
                where + ": editing a colour changed the record's length.");
            Assert.False(AreEqual(edited, stored),
                where + ": editing a colour changed no stored byte.");

            write(original);
            Assert.Equal(stored, component.Encode().ToArray());

            return true;
        }

        private static bool AreEqual(byte[] left, byte[] right) {
            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++) {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        ///     Every declared component of every interface, with the bytes it was read from.
        /// </summary>
        /// <remarks>
        ///     Through <c>ReadGroup</c> rather than <c>ReadFile</c> per component. <c>ReadFile</c>
        ///     releases the group the moment it has handed back one file, so a per-file walk of index
        ///     3 costs one group decode per declared file - 40,883 against 1,067 in the vanilla
        ///     capture - for byte-for-byte the same result.
        /// </remarks>
        /// <returns>Each component and its stored bytes.</returns>
        private IEnumerable<(InterfaceComponentDefinition Component, byte[] Stored)> EveryComponent() {
            RSCache cache = _fixture.OpenCache();

            foreach (int groupId in cache.EnumerateGroups(RSConstants.INTERFACE_DEFINITIONS_INDEX)) {
                IReadOnlyDictionary<int, JagStream> files =
                    cache.ReadGroup(RSConstants.INTERFACE_DEFINITIONS_INDEX, groupId);

                foreach (KeyValuePair<int, JagStream> file in files) {
                    byte[] stored = file.Value.ToArray();

                    yield return (new InterfaceComponentDefinition(groupId, file.Key)
                        .Decode(new JagStream(stored)), stored);
                }
            }
        }
    }
}
