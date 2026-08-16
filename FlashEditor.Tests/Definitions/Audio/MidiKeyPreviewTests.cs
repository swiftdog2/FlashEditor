using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Audio.Synth;
using Xunit;

namespace FlashEditor.Tests.Definitions.Audio
{
    /// <summary>
    ///     The one-note MIDI file the MIDI patch tab plays a key with.
    /// </summary>
    /// <remarks>
    ///     <b>The bank-select derivation is what these pin, and it is checkable without hearing
    ///     anything.</b> A patch id is <c>(bankSelect &lt;&lt; 7) | program</c>
    ///     (<c>Node_Sub31_Sub2.java:647-651</c>), so a preview of patch 184 has to send bank LSB 1
    ///     and program 56 in that order. Getting it wrong plays the right key at the right time on
    ///     the wrong instrument, which sounds like a working player and is not, and the two halves of
    ///     the derivation fail in ways that look alike: sending the program first selects the melodic
    ///     program of the same number, so every drum kit would play as a piano.
    ///     <para>
    ///     Read back through <see cref="MidiSynthesiser.PatchIdOf"/> rather than by inspecting the
    ///     bytes. A byte-level assertion would only prove this file says what this test says it says;
    ///     driving the production synthesiser proves the file selects the patch it names. The bank is
    ///     bound to a null cache, which resolves nothing and sounds nothing, so no cache is touched.
    ///     </para>
    /// </remarks>
    public sealed class MidiKeyPreviewTests
    {
        /// <summary>Ids that exercise both banks and both ends of the patch space this cache uses.</summary>
        /// <remarks>
        ///     0 and 127 are the melodic block, 128 and 184 are drum kits, 255 is the last slot of
        ///     the drum bank and 256 and 292 are the second bank. Between them they are the only
        ///     shapes the id layout takes.
        /// </remarks>
        public static IEnumerable<object[]> PatchIds =>
            new[] { 0, 40, 127, 128, 184, 255, 256, 292 }.Select(id => new object[] { id });

        /// <summary>A preview selects the patch it names, through the production synthesiser.</summary>
        [Theory]
        [MemberData(nameof(PatchIds))]
        public void APreview_SelectsThePatchItNames(int patchId)
        {
            var synthesiser = new MidiSynthesiser(new MidiSoundBank(null));

            foreach (MidiSequenceEvent message in Parse(patchId, 60))
                if (!message.IsTempo)
                    synthesiser.Send(message.Status, message.Data1, message.Data2);

            Assert.Equal(patchId, synthesiser.PatchIdOf(MidiKeyPreview.Channel));
        }

        /// <summary>
        ///     The sequence is a bank select, a program change, a note on, a note off and a cut, in
        ///     that order and at those times.
        /// </summary>
        /// <remarks>
        ///     The order is load bearing twice over. A program change applies against whatever bank
        ///     is current, so the two swapped select the wrong patch; and the All Sound Off has to be
        ///     last, because <c>TrackRenderer.Finished</c> waits for every event to have been
        ///     dispatched as well as for the voices to have stopped, so an event after the cut would
        ///     leave the player rendering silence until it arrived.
        /// </remarks>
        [Fact]
        public void TheSequence_IsFiveEventsInTheOrderThePlayerNeeds()
        {
            var events = Parse(184, 42).ToArray();

            Assert.Equal(5, events.Length);

            Assert.Equal(0xb0 | MidiKeyPreview.Channel, events[0].Status);
            Assert.Equal(32, events[0].Data1);           //bank select LSB
            Assert.Equal(1, events[0].Data2);            //184 >> 7
            Assert.Equal(0L, events[0].Tick);

            Assert.Equal(0xc0 | MidiKeyPreview.Channel, events[1].Status);
            Assert.Equal(184 & 0x7f, events[1].Data1);
            Assert.Equal(0L, events[1].Tick);

            Assert.Equal(0x90 | MidiKeyPreview.Channel, events[2].Status);
            Assert.Equal(42, events[2].Data1);
            Assert.Equal(MidiKeyPreview.Velocity, events[2].Data2);
            Assert.Equal(0L, events[2].Tick);

            Assert.Equal(0x80 | MidiKeyPreview.Channel, events[3].Status);
            Assert.Equal(42, events[3].Data1);
            Assert.Equal((long) MidiKeyPreview.HoldTicks, events[3].Tick);

            //The full stop. Controller 120 is All Sound Off, which MidiSynthesiser answers by
            //clearing every voice, so the renderer can then report itself finished.
            Assert.Equal(0xb0 | MidiKeyPreview.Channel, events[4].Status);
            Assert.Equal(120, events[4].Data1);
            Assert.Equal((long) (MidiKeyPreview.HoldTicks + MidiKeyPreview.TailTicks), events[4].Tick);
        }

        /// <summary>
        ///     The cut really does silence a voice that would otherwise never stop.
        /// </summary>
        /// <remarks>
        ///     The reason the sequence carries a cut at all. Without one a preview of a key whose
        ///     envelope has no release list, or whose voice owns its mute group, would leave
        ///     <c>TrackRenderer.Finished</c> false forever and hold the output device for as long as
        ///     the tab was open. Driven through <see cref="MidiSynthesiser"/> directly because the
        ///     claim is about the synthesiser's response to the message rather than about the bytes.
        /// </remarks>
        [Fact]
        public void AllSoundOff_ClearsEveryVoice()
        {
            var synthesiser = new MidiSynthesiser(new MidiSoundBank(null));

            //Nothing sounds against a null bank, so this asserts the message is handled rather than
            //that a voice was cut. The voice count is what TrackRenderer.Finished reads.
            synthesiser.Send(0xb0 | MidiKeyPreview.Channel, 120, 0);

            Assert.Equal(0, synthesiser.ActiveVoices);
        }

        /// <summary>The tail is long enough to be a release and short enough not to be a hang.</summary>
        /// <remarks>
        ///     Stated as a relationship rather than as two numbers, so the constants can be retuned
        ///     without the test becoming a copy of them. What matters is that the note is held for a
        ///     real length and that the cut comes after the release has had longer than the hold.
        /// </remarks>
        [Fact]
        public void TheHoldAndTail_AreRealLengthsAtTheDefaultTempo()
        {
            Assert.True(MidiKeyPreview.HoldTicks > 0);
            Assert.True(MidiKeyPreview.TailTicks >= MidiKeyPreview.HoldTicks);

            //96 ticks per quarter at 500,000 microseconds per quarter, which is what TrackRenderer
            //starts at when a file states no tempo, makes a tick a 192nd of a second.
            Assert.Equal(96, MidiKeyPreview.Division);
            Assert.Equal(192, MidiKeyPreview.HoldTicks);
        }

        /// <summary>An id, key or velocity outside the wire format is refused rather than truncated.</summary>
        /// <remarks>
        ///     A velocity of 0 is a note-off in MIDI, so a preview built with one would select the
        ///     patch, send a note-off for a note that was never struck, and play nothing at all - a
        ///     silence indistinguishable from an unrenderable sample.
        /// </remarks>
        [Fact]
        public void OutOfRangeArguments_AreRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiKeyPreview.BuildSingleNote(-1, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiKeyPreview.BuildSingleNote(0x4000, 60));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiKeyPreview.BuildSingleNote(0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MidiKeyPreview.BuildSingleNote(0, MidiPatchDefinition.Keys));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiKeyPreview.BuildSingleNote(0, 60, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MidiKeyPreview.BuildSingleNote(0, 60, 128));
        }

        /// <summary>Builds a preview and parses it back through the production sequence reader.</summary>
        /// <param name="patchId">The patch to select.</param>
        /// <param name="key">The key to strike.</param>
        /// <returns>The parsed events.</returns>
        private static IEnumerable<MidiSequenceEvent> Parse(int patchId, int key)
        {
            return new MidiSequence(MidiKeyPreview.BuildSingleNote(patchId, key)).Events;
        }
    }
}
