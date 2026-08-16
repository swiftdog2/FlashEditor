using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Builds a one-note standard MIDI file, so that auditioning a key goes through the track
    ///     player rather than through a second one.
    /// </summary>
    /// <remarks>
    ///     <b>Why a MIDI file and not a direct call into the synthesiser.</b> The editor already has a
    ///     working transport: <c>TrackPlayback</c> owns the thread, the <c>waveOut</c> device, the
    ///     pause that holds a voice mid-envelope, and the drain wait that stops the last buffer being
    ///     discarded on disposal. Driving <c>MidiSynthesiser</c> by hand from this tab would mean a
    ///     third copy of all of it, and the drain wait in particular is a defect this project has
    ///     already shipped twice - once on the SFX2 tab and once on the track player.
    ///     <para>
    ///     <b>The sequence ends itself, and it has to.</b> A note-off is not enough to stop every key.
    ///     <c>MidiSynthesiser.Tick</c> refuses to advance a voice's release while that voice still
    ///     owns its mute group (<c>Node_Sub31_Sub2</c>'s drum-choke behaviour, so an open hi-hat rings
    ///     until something cuts it), and 63 of the bank's 326 envelopes carry no release list at all.
    ///     Either case leaves a voice sounding forever, and <c>TrackRenderer.Finished</c> waits on the
    ///     voice count, so playback would never end. The sequence therefore closes with an All Sound
    ///     Off controller a fixed distance past the note-off, which bounds it from inside the data
    ///     rather than from a timer racing the playback thread.
    ///     </para>
    /// </remarks>
    public static class MidiKeyPreview {
        /// <summary>Ticks per quarter note the built file declares.</summary>
        /// <remarks>
        ///     96, against the 500,000 microseconds per quarter that <c>TrackRenderer</c> starts at
        ///     when a file states no tempo, so one tick is a 192nd of a second and the two constants
        ///     below read as the times they are.
        /// </remarks>
        public const int Division = 96;

        /// <summary>Ticks per second at the default tempo, which is what the two below are counted in.</summary>
        private const int TicksPerSecond = Division * 2;

        /// <summary>How long the note is held down.</summary>
        public const int HoldTicks = TicksPerSecond;

        /// <summary>
        ///     How long after the note-off the sequence waits before cutting everything.
        /// </summary>
        /// <remarks>
        ///     Long enough for a release to play out and short enough that a key which never releases
        ///     does not hold the device. A shorter tail would clip the end of a slow pad; a longer one
        ///     would leave a choked drum ringing after the user has moved on.
        /// </remarks>
        public const int TailTicks = TicksPerSecond * 2;

        /// <summary>The velocity a previewed note is struck at.</summary>
        /// <remarks>
        ///     A note's gain is proportional to the velocity squared
        ///     (<c>Node_Sub31_Sub2.java:977-978</c>), so a full 127 is not a neutral choice: it is the
        ///     loudest the instrument goes. 100 is a firm strike that leaves headroom above it.
        /// </remarks>
        public const int Velocity = 100;

        /// <summary>The channel the preview plays on.</summary>
        /// <remarks>
        ///     Channel 0 rather than 9. Channel 9 is seeded with patch 128 at construction
        ///     (<c>Class111_Sub1.java:31</c>), so a preview there would depend on a program change
        ///     landing before the note rather than on the bank select this builds; and the drum kits
        ///     are reached here by their bank-select id like any other patch, so there is nothing
        ///     channel 9 would add.
        /// </remarks>
        public const int Channel = 0;

        /// <summary>The highest patch id a seven-bit bank LSB and program can address.</summary>
        private const int HighestPatchId = 0x3fff;

        /// <summary>
        ///     A standard MIDI file that selects one patch and strikes one key.
        /// </summary>
        /// <remarks>
        ///     The patch is selected exactly the way a track selects one: a bank-select LSB followed
        ///     by a program change, which <c>Node_Sub31_Sub2.java:647-651</c> recombines as
        ///     <c>(bankSelect &lt;&lt; 7) | program</c>. That is the whole derivation, and getting it
        ///     wrong plays the right key on the wrong instrument while everything else looks correct,
        ///     which is why <c>MidiKeyPreviewTests</c> pins it by reading the id back off the
        ///     synthesiser rather than by inspecting the bytes.
        /// </remarks>
        /// <param name="patchId">The index-15 group id to select.</param>
        /// <param name="key">The key to strike, 0..127.</param>
        /// <param name="velocity">How hard to strike it, 1..127.</param>
        /// <returns>The file.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The patch, key or velocity is out of range.</exception>
        public static byte[] BuildSingleNote(int patchId, int key, int velocity = Velocity) {
            if (patchId < 0 || patchId > HighestPatchId)
                throw new ArgumentOutOfRangeException(nameof(patchId), patchId,
                    "A patch is addressed by a seven-bit bank LSB and a seven-bit program.");
            if (key < 0 || key >= MidiPatchDefinition.Keys)
                throw new ArgumentOutOfRangeException(nameof(key), key, "A patch describes keys 0..127.");
            if (velocity < 1 || velocity > 127)
                throw new ArgumentOutOfRangeException(nameof(velocity), velocity,
                    "A velocity of 0 is a note-off, so a struck note runs from 1 to 127.");

            var track = new List<byte>();

            //Bank select LSB, then the program change. In that order: a program change is applied
            //against whatever bank is current, so the two swapped would select the melodic program of
            //the same number and every drum kit would play as a piano.
            WriteVariableLength(track, 0);
            track.Add((byte) (0xb0 | Channel));
            track.Add(32);
            track.Add((byte) ((patchId >> 7) & 0x7f));

            WriteVariableLength(track, 0);
            track.Add((byte) (0xc0 | Channel));
            track.Add((byte) (patchId & 0x7f));

            WriteVariableLength(track, 0);
            track.Add((byte) (0x90 | Channel));
            track.Add((byte) key);
            track.Add((byte) velocity);

            WriteVariableLength(track, HoldTicks);
            track.Add((byte) (0x80 | Channel));
            track.Add((byte) key);
            track.Add(0);

            //All Sound Off. The sequence's own full stop; see the remarks on this class.
            WriteVariableLength(track, TailTicks);
            track.Add((byte) (0xb0 | Channel));
            track.Add(120);
            track.Add(0);

            //End of track. Ignored by this project's parser, and part of the format, so it is written
            //rather than left out on the strength of one reader's tolerance.
            WriteVariableLength(track, 0);
            track.Add(0xff);
            track.Add(0x2f);
            track.Add(0x00);

            var file = new List<byte>(track.Count + 22);
            file.AddRange(new byte[] { (byte) 'M', (byte) 'T', (byte) 'h', (byte) 'd' });
            WriteInt(file, 6);
            WriteShort(file, 0);            //format 0: one track
            WriteShort(file, 1);
            WriteShort(file, Division);

            file.AddRange(new byte[] { (byte) 'M', (byte) 'T', (byte) 'r', (byte) 'k' });
            WriteInt(file, track.Count);
            file.AddRange(track);

            return file.ToArray();
        }

        /// <summary>Writes a big-endian 32-bit length.</summary>
        private static void WriteInt(List<byte> bytes, int value) {
            bytes.Add((byte) (value >> 24));
            bytes.Add((byte) (value >> 16));
            bytes.Add((byte) (value >> 8));
            bytes.Add((byte) value);
        }

        /// <summary>Writes a big-endian 16-bit field.</summary>
        private static void WriteShort(List<byte> bytes, int value) {
            bytes.Add((byte) (value >> 8));
            bytes.Add((byte) value);
        }

        /// <summary>Writes a MIDI variable-length delta time, seven bits per byte, most significant first.</summary>
        private static void WriteVariableLength(List<byte> bytes, int value) {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "A delta time is not negative.");

            //Built back to front, because the low seven bits are the only group written without a
            //continuation bit and the count of groups is not known until the value has been consumed.
            var groups = new Stack<byte>();
            groups.Push((byte) (value & 0x7f));
            value >>= 7;

            while (value > 0) {
                groups.Push((byte) ((value & 0x7f) | 0x80));
                value >>= 7;
            }

            while (groups.Count > 0)
                bytes.Add(groups.Pop());
        }
    }
}
