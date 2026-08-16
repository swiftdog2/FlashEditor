using System.Collections.Generic;
using FlashEditor.Definitions.Audio;
using Xunit;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     The General MIDI table the MIDI patch tab labels index 15 from.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing on disk backs any of these names.</b> Index 15 carries no name hashes, so every
    ///     label in that tab is a claim about General MIDI keyed on the group id. What makes the join
    ///     safe is the id layout, which <c>RealCacheMidiPatchTests</c> asserts against both caches;
    ///     what these tests pin is that the table is total over that layout, that no id anywhere in
    ///     it comes back nameless, and that the two places where this cache and the published GS
    ///     table disagree are handled as disagreements rather than papered over.
    /// </remarks>
    public sealed class GeneralMidiTests
    {
        /// <summary>The ten drum-bank ids this cache holds, as the id-layout assertion states them.</summary>
        /// <remarks>
        ///     Copied here so the naming table can be checked without a cache. It is the same list
        ///     <c>RealCacheMidiPatchTests.TheMidiPatchBank_HoldsWhatTheCodecClaimsItDoes</c> asserts,
        ///     and <c>RealCacheMidiPatchTabTests</c> re-checks it against the loaded cache so the two
        ///     cannot drift apart silently.
        /// </remarks>
        private static readonly int[] DrumBankIds = { 128, 129, 136, 144, 152, 153, 168, 176, 178, 184 };

        /// <summary>Every melodic program has a name, and they are all different.</summary>
        /// <remarks>
        ///     Distinctness is the assertion that catches a table with a duplicated or dropped row:
        ///     a copy-paste slip in a 128-entry literal shifts every name after it by one and leaves
        ///     the count right, so a length check alone would pass.
        /// </remarks>
        [Fact]
        public void EveryMelodicProgram_HasADistinctName()
        {
            var seen = new HashSet<string>();

            for (int program = 0; program < GeneralMidi.DrumBankBase; program++)
            {
                string name = GeneralMidi.PatchName(program);

                Assert.False(string.IsNullOrWhiteSpace(name), "Program " + program + " has no name.");
                Assert.True(seen.Add(name), "Program " + program + " repeats the name " + name + ".");
                Assert.Equal(MidiPatchFamily.Melodic, GeneralMidi.FamilyOf(program));
                Assert.False(GeneralMidi.IsPercussion(program));

                /* A table one row short would fall through to the "bank n, program n" wording, which
                   is still non-blank and still distinct, so distinctness alone would not catch it. */
                Assert.DoesNotContain("program", name);
            }
        }

        /// <summary>The four programs whose position in the table is easiest to get wrong.</summary>
        /// <remarks>
        ///     Spot checks at the boundaries of the sixteen-program families, because an off-by-one in
        ///     the literal shows as a name from the neighbouring family and nowhere else. Program 40
        ///     is the one the tab's own brief names.
        /// </remarks>
        [Theory]
        [InlineData(0, "Acoustic Grand Piano")]
        [InlineData(40, "Violin")]
        [InlineData(56, "Trumpet")]
        [InlineData(127, "Gunshot")]
        public void NamedProgram_IsTheOneGeneralMidiPublishes(int program, string expected)
        {
            Assert.Equal(expected, GeneralMidi.PatchName(program));
        }

        /// <summary>
        ///     Every drum-bank id this cache holds is named, and the one the GS table does not cover
        ///     is described rather than invented.
        /// </summary>
        /// <remarks>
        ///     <b>The published kits and this cache's ids do not line up, and that is the point.</b>
        ///     GS puts its kits at program offsets 0, 1, 8, 16, 24, 25, 32, 40, 48 and 56, so a Jazz
        ///     kit would be id 160. This cache has no 160 and does have a 178, which no published
        ///     table names. Naming 178 anyway would put a false claim on screen, so it reports as the
        ///     drum bank without a GS name and <see cref="MidiPatchFamily.Jagex"/> is what
        ///     <see cref="GeneralMidi.FamilyOf"/> returns for it.
        /// </remarks>
        [Fact]
        public void EveryDrumBankId_IsNamedOrDeclaredUnnamed()
        {
            foreach (int id in DrumBankIds)
            {
                Assert.True(GeneralMidi.IsPercussion(id), "Id " + id + " should be in the drum bank.");
                Assert.False(string.IsNullOrWhiteSpace(GeneralMidi.PatchName(id)));
            }

            Assert.Equal("Standard Kit", GeneralMidi.PatchName(128));
            Assert.Equal("Room Kit", GeneralMidi.PatchName(136));
            Assert.Equal("Sound FX Kit", GeneralMidi.PatchName(184));

            //178 is program 50 of the drum bank, which no published kit table names.
            Assert.Equal(MidiPatchFamily.Jagex, GeneralMidi.FamilyOf(178));
            Assert.Equal("Drum kit (bank 1, program 50)", GeneralMidi.PatchName(178));
            Assert.Equal("Drum bank, unnamed", GeneralMidi.FamilyName(178));

            //And the kit that would be here if the cache followed GS is absent, which is what makes
            //the mismatch a fact about the cache rather than a gap in the table.
            Assert.Equal("Jazz Kit", GeneralMidi.PatchName(160));
            Assert.DoesNotContain(160, DrumBankIds);
        }

        /// <summary>Ids past the drum bank are described by their bank and program, never left blank.</summary>
        /// <remarks>
        ///     255 and 256 to 292 are Jagex's own instruments and nothing names them. A blank cell in
        ///     that column would read as a load failure, so the label states the decomposition the
        ///     synthesiser actually performs.
        /// </remarks>
        [Fact]
        public void JagexInstruments_AreDescribedByTheirBankAndProgram()
        {
            Assert.Equal("Jagex instrument (bank 2, program 0)", GeneralMidi.PatchName(256));
            Assert.Equal("Jagex instrument (bank 2, program 36)", GeneralMidi.PatchName(292));
            Assert.Equal(MidiPatchFamily.Jagex, GeneralMidi.FamilyOf(292));
            Assert.False(GeneralMidi.IsPercussion(292));

            //255 is the last slot of the drum bank rather than the first of the next, which is what
            //the bank arithmetic says and is easy to read the other way round.
            Assert.True(GeneralMidi.IsPercussion(255));
            Assert.Equal("Drum kit (bank 1, program 127)", GeneralMidi.PatchName(255));
        }

        /// <summary>Middle C is C4, and the octave numbering follows from it.</summary>
        /// <remarks>
        ///     Key 60 is middle C. The other convention in circulation calls it C3, and a keyboard
        ///     labelled one way while the user reads the other misnames every octave on it.
        /// </remarks>
        [Theory]
        [InlineData(0, "C-1")]
        [InlineData(21, "A0")]
        [InlineData(60, "C4")]
        [InlineData(61, "C#4")]
        [InlineData(127, "G9")]
        public void NoteName_PutsMiddleCAtC4(int key, string expected)
        {
            Assert.Equal(expected, GeneralMidi.NoteName(key));
        }

        /// <summary>
        ///     A key of a drum kit is labelled with its percussion slot; a key of a melodic program
        ///     is not.
        /// </summary>
        /// <remarks>
        ///     The percussion map only means anything inside the drum bank. Labelling key 42 of a
        ///     violin "Closed Hi Hat" would be a category error, so the map is only reached through
        ///     <see cref="GeneralMidi.KeyLabel"/> and only for a percussion patch.
        /// </remarks>
        [Fact]
        public void KeyLabel_NamesPercussionOnlyInsideTheDrumBank()
        {
            Assert.Equal("F#2 - Closed Hi Hat", GeneralMidi.KeyLabel(128, 42));
            Assert.Equal("A#2 - Open Hi-Hat", GeneralMidi.KeyLabel(128, 46));
            Assert.Equal("F#2", GeneralMidi.KeyLabel(40, 42));

            //Outside the published 35..81 map even a kit's key is just a note.
            Assert.Null(GeneralMidi.PercussionName(34));
            Assert.Null(GeneralMidi.PercussionName(82));
            Assert.Equal("C1", GeneralMidi.KeyLabel(128, 24));
        }

        /// <summary>The white keys are the seven pitch classes a piano draws white.</summary>
        [Fact]
        public void IsWhiteKey_MatchesThePianoLayout()
        {
            var white = new HashSet<int> { 0, 2, 4, 5, 7, 9, 11 };

            for (int key = 0; key < 128; key++)
                Assert.Equal(white.Contains(key % 12), GeneralMidi.IsWhiteKey(key));
        }
    }
}
