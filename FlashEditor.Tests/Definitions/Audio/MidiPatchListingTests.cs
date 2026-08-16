using System.Collections.Generic;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;
using Xunit;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     The census and the per-key snapshot the MIDI patch tab is built out of.
    /// </summary>
    /// <remarks>
    ///     <b>These are new readings of the planes, so they need their own pinning.</b>
    ///     <c>MidiPatchWalkTests</c> pins the accessors against hand-built plane bytes and
    ///     <c>RealCacheMidiPatchTests</c> pins the codec byte for byte, and neither can see this:
    ///     <see cref="MidiKeySnapshot"/> takes each accessor once per key and
    ///     <see cref="MidiPatchListing"/> aggregates over the sounding keys only, so a census that
    ///     counted silent keys, or double-counted a bank, would pass both of them.
    ///     <para>
    ///     Built from bytes laid out here by hand rather than from a definition assembled in memory.
    ///     Round-tripping this project's encoder through its own decoder proves nothing about either,
    ///     which is a mistake this repository has already shipped twice.
    ///     </para>
    ///     <para>
    ///     The patch is chosen so that every distinction the tab draws occurs in it: two silent keys,
    ///     a short run on index 14, and a long run on index 4 that is also held. The expected values
    ///     below are written out as bands rather than derived, so a walk that moved a run boundary by
    ///     one key fails rather than agreeing with itself.
    ///     </para>
    /// </remarks>
    public sealed class MidiPatchListingTests
    {
        /// <summary>Keys 0 and 1 are silent, 2 to 4 sound from index 14, and 5 up from index 4.</summary>
        private const int FirstVorbisKey = 2;

        private const int FirstEffectKey = 5;

        /// <summary>The stored mute-group byte; the client subtracts one, so this is group 2.</summary>
        private const byte StoredMuteGroup = 3;

        /// <summary>The stored pan byte, read signed and mapped by <c>(b + 16) &lt;&lt; 2</c>.</summary>
        private const byte StoredPan = 0;

        /// <summary>The whole-patch volume byte.</summary>
        private const byte StoredPatchVolume = 100;

        /// <summary>Sample references, one per run of the sample plane.</summary>
        /// <remarks>
        ///     0 is silence. 22 less one is 21: bit 0 set, so index 14; bit 1 clear, so not held; id
        ///     21 >> 2 = 5. 15 less one is 14: bit 0 clear, so index 4; bit 1 set, so held; id 3.
        /// </remarks>
        private static readonly int[] SampleReferences = { 0, 22, 15 };

        /// <summary>
        ///     How many bytes each reference is written in, deliberately not all minimal.
        /// </summary>
        /// <remarks>
        ///     22 is written in two bytes where one would do, because the shipped bank does exactly
        ///     that for 1060 of its 1151 references.
        /// </remarks>
        private static readonly int[] SampleReferenceWidths = { 1, 2, 1 };

        /// <summary>Run lengths of the sample plane; the unbounded third run is implied.</summary>
        private static readonly sbyte[] SampleRuns = { 2, 3 };

        /// <summary>Per-run volumes; the first run is silent and stores nothing.</summary>
        private static readonly byte[] VolumeValues = { 49, 63 };

        /// <summary>The hand-built bytes decode and re-encode to exactly themselves.</summary>
        /// <remarks>
        ///     Not interesting on its own, but it is what licenses every test below to talk about
        ///     "the patch these bytes describe" rather than about a definition assembled in memory.
        /// </remarks>
        [Fact]
        public void TheHandBuiltPatch_RoundTripsToItsOwnBytes()
        {
            byte[] stored = BuildPatchBytes();

            Assert.Equal(stored, Decode(stored).Encode().ToArray());
        }

        /// <summary>The census counts only the sounding keys, split by the bank bit.</summary>
        /// <remarks>
        ///     Keys 0 and 1 are silent and must not appear in any count. That is the failure a census
        ///     written as a 0..127 loop makes: silent keys report bank 0 and mute group -1, so they
        ///     would inflate the index-4 column of every patch in the bank by however many keys it
        ///     leaves empty, which is most of them.
        /// </remarks>
        [Fact]
        public void TheCensus_CountsOnlyTheSoundingKeys()
        {
            MidiPatchListing listing = Listing();

            Assert.Equal(MidiPatchDefinition.Keys - FirstVorbisKey, listing.SoundingKeys);
            Assert.Equal(FirstEffectKey - FirstVorbisKey, listing.VorbisKeys);
            Assert.Equal(MidiPatchDefinition.Keys - FirstEffectKey, listing.EffectKeys);
            Assert.Equal(listing.SoundingKeys, listing.VorbisKeys + listing.EffectKeys);

            //Only the index-4 run is held, and every sounding key carries the one mute group.
            Assert.Equal(MidiPatchDefinition.Keys - FirstEffectKey, listing.HeldKeys);
            Assert.Equal(MidiPatchDefinition.Keys - FirstVorbisKey, listing.MuteGroupKeys);
            Assert.Equal(1, listing.MuteGroups);
            Assert.Equal(1, listing.Envelopes);
            Assert.Equal(StoredPatchVolume, listing.PatchVolume);
        }

        /// <summary>
        ///     Each key's snapshot holds what the plane says for that key, band by band.
        /// </summary>
        /// <remarks>
        ///     Written out as three bands covering all 128 keys rather than derived from the run
        ///     list, so a snapshot taken one key out of step fails on the boundary rather than
        ///     agreeing with the walk that produced it.
        /// </remarks>
        [Fact]
        public void EveryKeySnapshot_HoldsThatKeysValues()
        {
            IReadOnlyList<MidiKeySnapshot> keys = Listing().Keys;

            Assert.Equal(MidiPatchDefinition.Keys, keys.Count);

            for (int key = 0; key < FirstVorbisKey; key++)
            {
                Assert.Equal(key, keys[key].Key);
                Assert.False(keys[key].Sounds);
                Assert.False(keys[key].SilentHere);
                Assert.Null(keys[key].Bank);
                Assert.Equal(-1, keys[key].SampleId);
                Assert.Equal(-1, keys[key].MuteGroup);
                Assert.Equal(-1, keys[key].Pan);
            }

            for (int key = FirstVorbisKey; key < FirstEffectKey; key++)
            {
                Assert.True(keys[key].Sounds);
                Assert.Equal(MidiSampleBank.Vorbis, keys[key].Bank);
                Assert.Equal(5, keys[key].SampleId);
                Assert.False(keys[key].Held);

                //Index 14 is the bank this editor renders, so these keys are not silent here.
                Assert.False(keys[key].SilentHere);

                Assert.Equal(2, keys[key].MuteGroup);
                Assert.Equal((StoredPan + 16) << 2, keys[key].Pan);
                Assert.Equal(VolumeValues[0] + 1, keys[key].Volume);
                Assert.Equal(0, keys[key].Envelope);
            }

            for (int key = FirstEffectKey; key < MidiPatchDefinition.Keys; key++)
            {
                Assert.True(keys[key].Sounds);
                Assert.Equal(MidiSampleBank.SoundEffects, keys[key].Bank);
                Assert.Equal(3, keys[key].SampleId);
                Assert.True(keys[key].Held);

                //Index 4 is decoded here and not rendered here, which is what SilentHere states.
                Assert.True(keys[key].SilentHere);

                Assert.Equal(2, keys[key].MuteGroup);
                Assert.Equal(VolumeValues[1] + 1, keys[key].Volume);
            }
        }

        /// <summary>
        ///     A held key's tuning word carries the sustain bit, and the pitch offset drops it.
        /// </summary>
        /// <remarks>
        ///     The same bit twice over: bit 1 of the sample reference becomes the top bit of the
        ///     tuning word (<c>Node_Sub44.java:217</c>), and the client then takes the offset as
        ///     <c>(key &lt;&lt; 8) - (word &amp; 0x7fff)</c> (<c>Node_Sub31_Sub2.java:980</c>), so the
        ///     top bit is not part of the number. A display that showed the raw word as a detune would
        ///     report every held key as detuned by 32,768 divisions.
        /// </remarks>
        [Fact]
        public void PitchOffset_DropsTheSustainBitOutOfTheTuningWord()
        {
            IReadOnlyList<MidiKeySnapshot> keys = Listing().Keys;

            //Every tuning delta in these bytes is zero, so the accumulated word is the sustain bit
            //alone and the offset is the key's own pitch.
            Assert.Equal(0, keys[FirstVorbisKey].Tuning);
            Assert.Equal(FirstVorbisKey << 8, keys[FirstVorbisKey].PitchOffset);

            Assert.Equal(unchecked((short) 0x8000), keys[FirstEffectKey].Tuning);
            Assert.Equal(FirstEffectKey << 8, keys[FirstEffectKey].PitchOffset);
        }

        /// <summary>The detail panes name every value the format carries, and say what it means.</summary>
        /// <remarks>
        ///     The two statements the tab exists to make out loud: the reference's three-way split,
        ///     and that index 4 has no renderer here. Asserted on the text because that is the whole
        ///     surface a user sees; a pane that dropped either would look complete.
        /// </remarks>
        [Fact]
        public void TheKeyDetail_SaysWhatTheReferenceMeansAndWhatCannotBePlayed()
        {
            MidiPatchListing listing = Listing();

            string vorbis = Render(new MidiKeyDetail(listing, listing.Keys[FirstVorbisKey]));
            Assert.Contains("bit 0 selects the bank", vorbis);
            Assert.Contains("id is v >> 2", vorbis);
            Assert.Contains("index 14", vorbis);

            string effect = Render(new MidiKeyDetail(listing, listing.Keys[FirstEffectKey]));
            Assert.Contains("index 4", effect);
            Assert.Contains("NO INDEX-4 RENDERER", effect);

            //The mute group is the field that means nothing as an integer, so it is stated in what
            //it does and lists the keys it would cut.
            Assert.Contains("cuts whatever else in group 2", effect);
            Assert.Contains("shared with", effect);
        }

        /// <summary>A silent key's pane says so rather than showing a bank and an id of -1.</summary>
        [Fact]
        public void ASilentKeysDetail_SaysThereIsNothingToPlay()
        {
            MidiPatchListing listing = Listing();
            string text = Render(new MidiKeyDetail(listing, listing.Keys[0]));

            Assert.Contains("a reference of 0 is silence", text);
            Assert.DoesNotContain("Sample id", text);
        }

        /// <summary>The descriptor writes a row back through the codec's own encoder.</summary>
        /// <remarks>
        ///     The one edit the tab offers. Asserted against the bytes it was decoded from, so an
        ///     encoder that normalised anything shows here rather than in a cache the user has
        ///     already saved over.
        /// </remarks>
        [Fact]
        public void TheDescriptor_ReEncodesAnUneditedRowToItsOwnBytes()
        {
            byte[] stored = BuildPatchBytes();
            var descriptor = new MidiPatchListDescriptor();
            MidiPatchListing listing = Listing();

            Assert.True(descriptor.IsEditable);
            Assert.Equal(stored, descriptor.Encode(listing).ToArray());
        }

        /// <summary>An edited volume changes the bytes, and dropping the snapshot is part of that.</summary>
        /// <remarks>
        ///     The keys are a cached view of the patch, so an editor that changed the record without
        ///     invalidating them would leave the keyboard drawing the old patch until the tab was
        ///     rebound.
        /// </remarks>
        [Fact]
        public void EditingTheVolume_ChangesTheBytesAndDropsTheSnapshot()
        {
            byte[] stored = BuildPatchBytes();
            var descriptor = new MidiPatchListDescriptor();
            MidiPatchListing listing = Listing();

            //Touched first, so that a stale snapshot would be one that had already been built.
            Assert.Equal(MidiPatchDefinition.Keys, listing.Keys.Count);

            listing.PatchVolume = StoredPatchVolume - 1;
            byte[] edited = descriptor.Encode(listing).ToArray();

            Assert.NotEqual(stored, edited);
            Assert.Equal(StoredPatchVolume - 1, Decode(edited).PatchVolume);
            Assert.Equal(MidiPatchDefinition.Keys, listing.Keys.Count);
        }

        /// <summary>Every detail field of a row, joined, for asserting on what a pane says.</summary>
        private static string Render(IDetailRow row)
        {
            var text = new System.Text.StringBuilder(row.Summary);
            foreach (DetailField field in row.Fields)
                text.Append('\n').Append(field.Name).Append(": ").Append(field.Value);

            return text.ToString();
        }

        /// <summary>The hand-built patch as the tab's row type sees it.</summary>
        private static MidiPatchListing Listing()
        {
            //Group 40 is Violin, a melodic program, so the key labels are notes rather than
            //percussion slots. Which patch id it is does not affect any plane.
            return new MidiPatchListing(new DefinitionAddress(40, 0, 40), Decode(BuildPatchBytes()));
        }

        /// <summary>Decodes a patch from bytes.</summary>
        private static MidiPatchDefinition Decode(byte[] stored)
        {
            return new MidiPatchDefinition { Id = 40 }.Decode(new JagStream(stored));
        }

        /// <summary>
        ///     Writes the patch described by the constants above, in the client's field order.
        /// </summary>
        /// <remarks>
        ///     Laid out from <c>Node_Sub44.java:103-447</c> directly rather than produced by
        ///     <c>MidiPatchDefinition.Encode</c>. The patch is the simplest one that still reaches
        ///     every branch this tab draws: one envelope with no attack, no release, no decay and no
        ///     vibrato, so most of the file's tail is a single zero byte per envelope.
        /// </remarks>
        private static byte[] BuildPatchBytes()
        {
            var bytes = new List<byte>();

            //Mute-group plane: no runs, so the terminator immediately, then the one value the
            //unbounded run uses.
            bytes.Add(0);
            bytes.Add(StoredMuteGroup);

            //Pan, same shape.
            bytes.Add(0);
            bytes.Add(StoredPan);

            /* Envelope plane, same shape. With one slot the back-reference chain stores nothing at
               all: slots 0 and 1 are implicit and there is no slot 2. */
            bytes.Add(0);

            bytes.Add(0);   //envelope 0 attack points
            bytes.Add(0);   //envelope 0 release points

            bytes.Add(0);   //volume curve points
            bytes.Add(0);   //pan curve points

            foreach (sbyte run in SampleRuns)
                bytes.Add(unchecked((byte) run));
            bytes.Add(0);

            //Tuning deltas: 128 fine then 128 coarse, all zero, so a held key's tuning word is the
            //sustain bit and nothing else.
            bytes.AddRange(new byte[MidiPatchDefinition.Keys]);
            bytes.AddRange(new byte[MidiPatchDefinition.Keys]);

            for (int i = 0; i < SampleReferences.Length; i++)
                WriteVarInt(bytes, SampleReferences[i], SampleReferenceWidths[i]);

            bytes.AddRange(VolumeValues);
            bytes.Add(StoredPatchVolume);

            bytes.Add(0);   //envelope 0 decay
            bytes.Add(0);   //envelope 0 vibrato rate

            return bytes.ToArray();
        }

        /// <summary>Writes a variable-length quantity in a stated number of bytes.</summary>
        private static void WriteVarInt(List<byte> bytes, int value, int width)
        {
            for (int shift = (width - 1) * 7; shift > 0; shift -= 7)
                bytes.Add((byte) (((value >> shift) & 0x7F) | 0x80));
            bytes.Add((byte) (value & 0x7F));
        }
    }
}
