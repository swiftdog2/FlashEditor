using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Definitions.Audio;
using FlashEditor.Definitions.Audio.Synth;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the parts of the synthesiser that can be checked without hearing anything.
    /// </summary>
    /// <remarks>
    ///     <b>Nothing in this suite can hear the player</b>, and a synthesiser that decodes every
    ///     byte correctly and mixes them wrongly passes every test that can be written here. So this
    ///     covers only what is arithmetic: the patch derivation, the envelope chain reconstruction
    ///     and the sequence reader. What the result sounds like is judged by a person, against
    ///     <c>reference/track-player-listening-checklist.md</c>.
    ///     <para>
    ///     Each case below is one that fails silently in the audible domain. A wrong bank-select
    ///     combination plays every note on the wrong instrument with the pitches and timing intact,
    ///     which sounds like a deliberate arrangement rather than a defect; a release chain missing
    ///     its implied first and last points ends every note at the wrong level, which sounds like a
    ///     click.
    ///     </para>
    /// </remarks>
    public class MidiSynthesiserTests
    {
        /// <summary>
        ///     A synthesiser over a bank that holds nothing, for the state machine tests.
        /// </summary>
        /// <remarks>
        ///     Every test here is about channel state rather than about sound, so a bank bound to no
        ///     cache is exactly right: it resolves nothing, which makes any note-on a no-op and any
        ///     accidental dependence on real data show up as an empty assertion rather than as a
        ///     test that only passes on a machine with a cache.
        /// </remarks>
        /// <returns>A synthesiser whose bank resolves nothing.</returns>
        private static MidiSynthesiser Silent()
        {
            return new MidiSynthesiser(new MidiSoundBank((FlashEditor.Cache.RSCache?) null));
        }

        /// <summary>
        ///     A program change combines both bank-select controllers with the program number.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:513-519</c> and <c>:647-651</c>:
        ///     <c>(bankMSB &lt;&lt; 14) | (bankLSB &lt;&lt; 7) | program</c>. There is no lookup table
        ///     between that number and the cache - it is the index-15 group id outright, which is
        ///     what makes the derivation worth pinning: a synthesiser that ignored the bank
        ///     controllers would play program 0 of the melodic bank wherever a track asked for a
        ///     bank-selected instrument, and every note would still be in tune.
        /// </remarks>
        [Fact]
        public void ProgramChange_CombinesBothBankSelectControllersWithTheProgram()
        {
            MidiSynthesiser synthesiser = Silent();

            synthesiser.Send(0xc0, 40, 0);
            Assert.Equal(40, synthesiser.PatchIdOf(0));

            //Bank LSB 3, program 5: 3 << 7 plus 5.
            synthesiser.Send(0xb0, 32, 3);
            synthesiser.Send(0xc0, 5, 0);
            Assert.Equal((3 << 7) + 5, synthesiser.PatchIdOf(0));

            //Bank MSB 2 on top of it.
            synthesiser.Send(0xb0, 0, 2);
            synthesiser.Send(0xc0, 5, 0);
            Assert.Equal((2 << 14) + (3 << 7) + 5, synthesiser.PatchIdOf(0));

            //And the bank is per channel, not global.
            Assert.Equal(0, synthesiser.PatchIdOf(1));
        }

        /// <summary>
        ///     Channel 9 starts on patch 128 before any program change arrives.
        /// </summary>
        /// <remarks>
        ///     <c>Class111_Sub1.java:31</c> sets it at construction and <c>Node_Sub7.java:318</c>
        ///     relies on it when a song is pre-scanned for the patches it needs. Without it a track
        ///     that never sends a program change on channel 9 - which is most of them, since 9 is the
        ///     percussion channel by convention - plays its drums on melodic patch 0.
        /// </remarks>
        [Fact]
        public void ChannelNine_DefaultsToTheFirstDrumKit()
        {
            MidiSynthesiser synthesiser = Silent();

            Assert.Equal(128, synthesiser.PatchIdOf(9));
            for (int channel = 0; channel < MidiSynthesiser.Channels; channel++)
                if (channel != 9)
                    Assert.Equal(0, synthesiser.PatchIdOf(channel));
        }

        /// <summary>The attack chain interleaves the stored levels with a running total of the time deltas.</summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:293-295</c> writes the levels into the odd slots and <c>:327-332</c>
        ///     accumulates <c>1 + delta</c> into the even ones, starting from zero. The first point
        ///     therefore always sits at time 0 and is never stored.
        /// </remarks>
        [Fact]
        public void AttackChain_InterleavesLevelsWithARunningTotalOfTheDeltas()
        {
            var envelope = new MidiPatchEnvelope
            {
                AttackLevels = new sbyte[] { 0, 64, 32 },
                AttackTimeDeltas = new byte[] { 4, 9 }
            };

            //Times: 0, then 0 + 1 + 4 = 5, then 5 + 1 + 9 = 15.
            Assert.Equal(new byte[] { 0, 0, 5, 64, 15, 32 }, MidiSynthesiser.AttackChain(envelope));
        }

        /// <summary>
        ///     The release chain puts back the two entries the file never stores.
        /// </summary>
        /// <remarks>
        ///     The first level is a fixed 64 (<c>Node_Sub44.java:178</c>) and the last is left at
        ///     zero, so a release always begins at unity gain and ends in silence. Both are implied
        ///     rather than written, and a chain built only from the stored bytes would start a
        ///     release at whatever the first stored level happens to be and never reach zero - which
        ///     is a click at the end of every note.
        /// </remarks>
        [Fact]
        public void ReleaseChain_RestoresTheImpliedFirstAndLastPoints()
        {
            var envelope = new MidiPatchEnvelope
            {
                ReleaseLevels = new sbyte[] { 48 },
                ReleaseTimeDeltas = new byte[] { 2, 7 }
            };

            //Two declared points, so the chain is 2 * 2 + 2 long. Times 0, 3, 11; levels 64, 48, 0.
            Assert.Equal(new byte[] { 0, 64, 3, 48, 11, 0 }, MidiSynthesiser.ReleaseChain(envelope));
        }

        /// <summary>An envelope with no points produces no chain rather than a chain of nothing.</summary>
        /// <remarks>
        ///     The distinction matters because the synthesiser tests the chain's length to decide
        ///     whether to walk it at all, and a zero-length chain of the wrong shape would index
        ///     past its own end on the first control tick.
        /// </remarks>
        [Fact]
        public void AnEmptyEnvelope_ProducesNoChain()
        {
            var envelope = new MidiPatchEnvelope();

            Assert.Empty(MidiSynthesiser.AttackChain(envelope));
            Assert.Empty(MidiSynthesiser.ReleaseChain(envelope));
        }

        /// <summary>A note-off for a key that is not down is ignored rather than throwing.</summary>
        /// <remarks>
        ///     Real tracks send them - a note-off after an all-notes-off, or a duplicated one across
        ///     two tracks - and the client simply finds a null slot (<c>Node_Sub31_Sub2.java:1184</c>).
        /// </remarks>
        [Fact]
        public void ANoteOffForAKeyThatIsNotDown_IsIgnored()
        {
            MidiSynthesiser synthesiser = Silent();

            synthesiser.Send(0x80, 60, 64);
            synthesiser.Send(0x90, 60, 0);
            Assert.Equal(0, synthesiser.ActiveVoices);
        }

        // ===================================================================
        //  The sequence reader
        // ===================================================================

        /// <summary>A hand-built one-track MIDI file parses to the events it holds.</summary>
        /// <remarks>
        ///     Built byte by byte rather than by a library so the reader is checked against the
        ///     format rather than against a writer that shares its assumptions. It exercises the two
        ///     cases a naive reader gets wrong: a running-status event with no status byte of its
        ///     own, and a tempo meta event.
        /// </remarks>
        [Fact]
        public void TheSequenceReader_HandlesRunningStatusAndTempo()
        {
            var track = new List<byte>();

            //Delta 0: tempo, 600000 microseconds per quarter.
            track.AddRange(new byte[] { 0x00, 0xff, 0x51, 0x03, 0x09, 0x27, 0xc0 });
            //Delta 0: note on, channel 0, key 60, velocity 100.
            track.AddRange(new byte[] { 0x00, 0x90, 60, 100 });
            //Delta 96: another note on with no status byte, which is running status.
            track.AddRange(new byte[] { 0x60, 64, 100 });
            //Delta 96: note off for the first, as a note on with velocity zero.
            track.AddRange(new byte[] { 0x60, 60, 0 });
            //End of track.
            track.AddRange(new byte[] { 0x00, 0xff, 0x2f, 0x00 });

            var midi = new List<byte>();
            midi.AddRange(new byte[] { (byte) 'M', (byte) 'T', (byte) 'h', (byte) 'd' });
            midi.AddRange(new byte[] { 0, 0, 0, 6 });
            midi.AddRange(new byte[] { 0, 0 });         //format 0
            midi.AddRange(new byte[] { 0, 1 });         //one track
            midi.AddRange(new byte[] { 0x01, 0xe0 });   //480 ticks per quarter
            midi.AddRange(new byte[] { (byte) 'M', (byte) 'T', (byte) 'r', (byte) 'k' });
            midi.AddRange(new byte[]
            {
                (byte) (track.Count >> 24), (byte) (track.Count >> 16), (byte) (track.Count >> 8), (byte) track.Count
            });
            midi.AddRange(track);

            var sequence = new MidiSequence(midi.ToArray());

            Assert.Equal(480, sequence.Division);
            Assert.Equal(4, sequence.Events.Count);

            Assert.True(sequence.Events[0].IsTempo);
            Assert.Equal(600000, sequence.Events[0].Data1);

            Assert.Equal(0x90, sequence.Events[1].Status);
            Assert.Equal(60, sequence.Events[1].Data1);
            Assert.Equal(0L, sequence.Events[1].Tick);

            //The running-status event: same status, no status byte in the file.
            Assert.Equal(0x90, sequence.Events[2].Status);
            Assert.Equal(64, sequence.Events[2].Data1);
            Assert.Equal(96L, sequence.Events[2].Tick);

            Assert.Equal(192L, sequence.Events[3].Tick);
            Assert.Equal(0, sequence.Events[3].Data2);
            Assert.Equal(192L, sequence.LengthInTicks);
        }

        /// <summary>Anything that is not a standard MIDI file is refused by name.</summary>
        [Fact]
        public void TheSequenceReader_RefusesSomethingThatIsNotAMidiFile()
        {
            Assert.Throws<InvalidDataException>(() => new MidiSequence(new byte[] { 1, 2, 3, 4 }));
            Assert.Throws<ArgumentNullException>(() => new MidiSequence(null!));
        }

        /// <summary>
        ///     A track with no events renders silence and finishes rather than spinning.
        /// </summary>
        /// <remarks>
        ///     The renderer's completion test is "every event dispatched and no voice sounding", and
        ///     an empty sequence satisfies both from the start. Without this the playback thread
        ///     would queue silence forever on a track that failed to decode.
        /// </remarks>
        [Fact]
        public void AnEmptySequence_RendersNothingAndReportsItselfFinished()
        {
            var midi = new List<byte>();
            midi.AddRange(new byte[] { (byte) 'M', (byte) 'T', (byte) 'h', (byte) 'd' });
            midi.AddRange(new byte[] { 0, 0, 0, 6, 0, 0, 0, 0, 0x01, 0xe0 });

            var renderer = new TrackRenderer(new MidiSequence(midi.ToArray()), Silent());
            var buffer = new short[MidiSynthesiser.ControlTick * 2];

            Assert.True(renderer.Finished);
            Assert.Equal(0, renderer.Render(buffer, MidiSynthesiser.ControlTick));
        }
    }
}
