using System;
using System.IO;
using FlashEditor.Definitions.Tracks;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Tracks
{
    /// <summary>
    ///     Hand-built packed tracks, decoded and re-encoded without touching a cache.
    /// </summary>
    /// <remarks>
    ///     The cache sweep can only ever show that this codec agrees with itself about files the
    ///     packer produced. These files were never produced by this encoder - each is laid out by
    ///     hand from <c>Node_Sub7.java</c> and its expected MIDI worked out from the client's arms
    ///     rather than from our output - so they test the two things a round trip cannot.
    ///     <para>
    ///     <b>The aliasing is real and is the reason the stored form is kept.</b> Three of the pairs
    ///     below differ in their stored bytes and project to byte-identical MIDI, so no encoder
    ///     working from the MIDI could choose between them. An encoder that recomputed deltas would
    ///     pass every structural check and rewrite files nobody edited.
    ///     </para>
    ///     <para>
    ///     Every file here is a single MTrk with division 480, which keeps the expected MIDI short
    ///     enough to state literally. The layout is: opcode stream, delta-time quantities, controller
    ///     numbers, the twenty-one runs in <see cref="TrackRun"/> order, then the three-byte trailer
    ///     the format keeps at the <b>end</b>.
    ///     </para>
    /// </remarks>
    public sealed class TrackCodecTests
    {
        /// <summary>
        ///     A note on then a note off, with the note and velocity deltas stored positively.
        /// </summary>
        /// <remarks>
        ///     Opcodes <c>00 01 07</c> - note on, note off, end of track, all with a channel delta of
        ///     zero. Three delta times of zero (one per event plus the end of track), no control
        ///     changes, then a two-byte note run, a one-byte note-on velocity and a one-byte note-off
        ///     velocity.
        /// </remarks>
        private static byte[] NotePairPositive() => new byte[]
        {
            0x00, 0x01, 0x07,       //opcodes
            0x00, 0x00, 0x00,       //delta times
            0x3C, 0x00,             //note run: +60 then +0
            0x40,                   //note-on velocity: +64
            0x00,                   //note-off velocity: +0
            0x01, 0x01, 0xE0        //trailer: 1 track, division 480
        };

        /// <summary>
        ///     The same two events with the note and velocity deltas stored negatively.
        /// </summary>
        /// <remarks>
        ///     The accumulators are full <c>int</c>s and the output is <c>accumulator &amp; 127</c>,
        ///     so <c>-68</c> and <c>+60</c> are indistinguishable in the MIDI and so are <c>-64</c>
        ///     and <c>+64</c>. This is the alias that makes the format non-canonical in the direction
        ///     that matters, and it is why the runs are kept verbatim.
        /// </remarks>
        private static byte[] NotePairNegative() => new byte[]
        {
            0x00, 0x01, 0x07,
            0x00, 0x00, 0x00,
            0xBC, 0x00,             //note run: -68, which masks to the same 60
            0xC0,                   //note-on velocity: -64, which masks to the same 64
            0x00,
            0x01, 0x01, 0xE0
        };

        /// <summary>The MIDI both note-pair files project to.</summary>
        private static byte[] NotePairMidi() => new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06, //MThd, length 6
            0x00, 0x00,                                     //format 0, one track
            0x00, 0x01,                                     //one MTrk
            0x01, 0xE0,                                     //division 480
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x0C, //MTrk, 12 bytes
            0x00, 0x90, 0x3C, 0x40,                         //note on, channel 0, note 60, velocity 64
            0x00, 0x80, 0x3C, 0x00,                         //note off, channel 0, note 60, velocity 0
            0x00, 0xFF, 0x2F, 0x00                          //end of track
        };

        /// <summary>One volume control change, its controller number stored in seven bits.</summary>
        private static byte[] VolumeControlNarrow() => new byte[]
        {
            0x02, 0x07,             //opcodes: control change, end of track
            0x00, 0x00,             //delta times
            0x07,                   //controller number delta: +7, which is volume
            0x64,                   //volume run: +100
            0x01, 0x01, 0xE0
        };

        /// <summary>The same control change with bit 7 of the controller-number delta set.</summary>
        /// <remarks>
        ///     The number is accumulated as <c>(running + stored) &amp; 127</c>
        ///     (<c>Node_Sub7.java:94</c> and <c>:235</c>), so bit 7 of every controller-number byte
        ///     is discarded on the way in and cannot be reconstructed on the way out.
        /// </remarks>
        private static byte[] VolumeControlWide() => new byte[]
        {
            0x02, 0x07,
            0x00, 0x00,
            0x87,                   //+135, which masks to the same controller 7
            0x64,
            0x01, 0x01, 0xE0
        };

        /// <summary>The MIDI both volume files project to.</summary>
        private static byte[] VolumeControlMidi() => new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
            0x00, 0x00, 0x00, 0x01, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x08, //MTrk, 8 bytes
            0x00, 0xB0, 0x07, 0x64,                         //control change, channel 0, volume 100
            0x00, 0xFF, 0x2F, 0x00
        };

        /// <summary>A tempo change immediately followed by the end of track.</summary>
        /// <remarks>
        ///     The arrangement that triggers the client bug. Opcode 23 masks to nibble 7 and so does
        ///     opcode 7, so the client's running-status test sees no change and drops the
        ///     end-of-track meta event's <c>0xFF</c>.
        /// </remarks>
        private static byte[] TempoThenEndOfTrack() => new byte[]
        {
            0x17, 0x07,             //opcodes: set tempo, end of track
            0x00, 0x00,             //delta times
            0x07, 0xA1, 0x20,       //tempo run: 500000 microseconds per quarter note
            0x01, 0x01, 0xE0
        };

        /// <summary>The MIDI the tempo file projects to, with the repaired status byte in place.</summary>
        private static byte[] TempoThenEndOfTrackMidi() => new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, 0x00, 0x00, 0x00, 0x06,
            0x00, 0x00, 0x00, 0x01, 0x01, 0xE0,
            0x4D, 0x54, 0x72, 0x6B, 0x00, 0x00, 0x00, 0x0B, //MTrk, 11 bytes
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,       //set tempo
            0x00, 0xFF, 0x2F, 0x00                          //end of track, 0xFF written back in
        };

        /// <summary>
        ///     Two packed files that differ in their stored bytes project to the same MIDI, and each
        ///     re-encodes to itself.
        /// </summary>
        /// <remarks>
        ///     This is the whole argument for retaining the stored form rather than rebuilding it.
        ///     Given the MIDI alone there is no way back to either file: both are legal, both are
        ///     what the packer might have written, and nothing in the output distinguishes them.
        /// </remarks>
        [Fact]
        public void SignedRunDeltas_AliasUnderTheSevenBitMask()
        {
            AssertProjects(NotePairPositive(), NotePairMidi());
            AssertProjects(NotePairNegative(), NotePairMidi());
        }

        /// <summary>Bit 7 of a controller-number delta is discarded and still has to be replayed.</summary>
        [Fact]
        public void ControllerNumberDeltas_AliasAboveSevenBits()
        {
            AssertProjects(VolumeControlNarrow(), VolumeControlMidi());
            AssertProjects(VolumeControlWide(), VolumeControlMidi());
        }

        /// <summary>
        ///     The meta status byte the client drops is added to the MIDI and kept out of the packed
        ///     form.
        /// </summary>
        /// <remarks>
        ///     Both halves matter. The projection has to write the <c>0xFF</c>, because the MIDI
        ///     specification forbids running status across meta events and the export has to play
        ///     outside the client. The encoder has to not write it, because the packed file has no
        ///     representation of it at all - it is implied by the opcode pair - and the file's own
        ///     length prediction does not allow for it. So the projected MIDI is legitimately one
        ///     byte longer than <c>ExpectedMidiLength</c>, and the round trip is unaffected.
        /// </remarks>
        [Fact]
        public void TheDroppedMetaStatusByte_IsRepairedInTheMidiAndNotInThePackedForm()
        {
            byte[] stored = TempoThenEndOfTrack();
            Track track = Decode(stored);

            Assert.Equal(1, track.RepairedMetaStatusBytes);
            Assert.Equal(TempoThenEndOfTrackMidi(), track.Midi);

            //The prediction is the packed file's own, and it does not know about the repair
            Assert.Equal(track.MidiLength, track.ExpectedMidiLength + track.RepairedMetaStatusBytes);

            //And none of it reaches the packed bytes
            Assert.Equal(stored, track.Encode().ToArray());
        }

        /// <summary>
        ///     A delta time stored in more bytes than it needs survives the round trip, and the
        ///     projection normalises it.
        /// </summary>
        /// <remarks>
        ///     A variable-length quantity has more than one encoding of the same number, and the
        ///     projection re-encodes what it reads rather than copying it - which is what the client
        ///     does (<c>Node_Sub7.java:191-192</c>). So a wide form makes the emitted MIDI shorter
        ///     than the packed file's own prediction, because that prediction counts the delta-time
        ///     block's raw bytes (<c>:78</c>). Retaining the bytes is what keeps the packed file
        ///     reproducible anyway.
        ///     <para>
        ///     Nothing in either shipped cache does this - if anything did, the length reconciliation
        ///     in <c>RealCacheTrackTests</c> would already be failing on it. The case is pinned here
        ///     so the tolerance is deliberate rather than accidental.
        ///     </para>
        /// </remarks>
        [Fact]
        public void AWideDeltaTime_ReEncodesUnchangedWhileTheProjectionNarrowsIt()
        {
            byte[] stored =
            {
                0x00, 0x01, 0x07,
                0x80, 0x00, 0x00, 0x00, //the first delta time is a two-byte encoding of zero
                0x3C, 0x00,
                0x40,
                0x00,
                0x01, 0x01, 0xE0
            };

            Track track = Decode(stored);

            Assert.Equal(4, track.DeltaTimes.Length);
            Assert.Equal(stored, track.Encode().ToArray());

            //The MIDI is the narrow form, so it is a byte shorter than the file predicts
            Assert.Equal(NotePairMidi(), track.Midi);
            Assert.Equal(track.MidiLength + 1, track.ExpectedMidiLength);
        }

        /// <summary>Every run of a decoded track is exactly as long as its events require.</summary>
        /// <remarks>
        ///     Nothing in this format states a length, so the run boundaries are the whole of it.
        ///     Stating the expected lengths for a file whose events are known by construction is the
        ///     one way to check them that does not go back through the same counting pass that
        ///     produced them.
        /// </remarks>
        [Fact]
        public void TheRunLengths_FollowFromTheEventsAlone()
        {
            Track notes = Decode(NotePairPositive());

            Assert.Equal(2, notes.Run(TrackRun.Note).Length);
            Assert.Single(notes.Run(TrackRun.NoteOnVelocity));
            Assert.Single(notes.Run(TrackRun.NoteOffVelocity));
            Assert.Empty(notes.Run(TrackRun.Tempo));
            Assert.Empty(notes.ControllerNumbers);

            Track volume = Decode(VolumeControlNarrow());

            Assert.Single(volume.ControllerNumbers);
            Assert.Single(volume.Run(TrackRun.Volume));
            Assert.Empty(volume.Run(TrackRun.OtherController));
            Assert.Empty(volume.Run(TrackRun.Program));

            Track tempo = Decode(TempoThenEndOfTrack());

            Assert.Equal(3, tempo.Run(TrackRun.Tempo).Length);
            Assert.Equal(1, tempo.TempoEvents);
        }

        /// <summary>A file whose runs do not reach its trailer is rejected rather than truncated.</summary>
        /// <remarks>
        ///     The failure this guards is silent everywhere else: a miscounted event shortens one run
        ///     and lengthens the next, and every downstream check still passes because the projection
        ///     is self-consistent. Dropping a byte from the note run leaves the last run short of the
        ///     trailer, which is the only place the file can contradict itself.
        /// </remarks>
        [Fact]
        public void ATruncatedFile_IsReportedRatherThanDecodedShort()
        {
            byte[] stored =
            {
                0x00, 0x01, 0x07,
                0x00, 0x00, 0x00,
                0x3C, 0x00,
                0x40,
                //the note-off velocity byte is missing
                0x01, 0x01, 0xE0
            };

            Assert.Throws<InvalidDataException>(() => Decode(stored));
        }

        /// <summary>An opcode the client has no case for is refused, as the client refuses it.</summary>
        /// <remarks>
        ///     <c>Node_Sub7.java:62-68</c> throws on any low nibble above 6. Carrying on instead would
        ///     misplace every run boundary after it, so the alternative to throwing is a file that
        ///     decodes into nonsense.
        /// </remarks>
        [Fact]
        public void AnUnknownOpcode_IsRefused()
        {
            byte[] stored =
            {
                0x0F, 0x07,             //nibble 15 has no case
                0x00, 0x00,
                0x01, 0x01, 0xE0
            };

            Assert.Throws<InvalidDataException>(() => Decode(stored));
        }

        /// <summary>
        ///     Decodes a packed file, then asserts its MIDI and that it re-encodes to itself.
        /// </summary>
        /// <param name="stored">The packed file.</param>
        /// <param name="midi">The MIDI it must project to.</param>
        private static void AssertProjects(byte[] stored, byte[] midi)
        {
            Track track = Decode(stored);

            Assert.Equal(midi, track.Midi);
            Assert.Equal(stored.Length, track.PackedLength);
            Assert.Equal(stored.Length, track.StoredLength);
            Assert.Empty(track.TrailingBytes);
            Assert.Equal(track.MidiLength, track.ExpectedMidiLength + track.RepairedMetaStatusBytes);

            byte[] encoded = track.Encode().ToArray();
            Assert.Equal(stored, encoded);

            //And the encoder's own output has to decode to the same thing again
            Track again = Decode(encoded);
            Assert.Equal(stored, again.Encode().ToArray());
            Assert.Equal(midi, again.Midi);
        }

        private static Track Decode(byte[] stored)
        {
            return new Track { Id = 0, IndexId = 6 }.Decode(new JagStream(stored));
        }
    }
}
