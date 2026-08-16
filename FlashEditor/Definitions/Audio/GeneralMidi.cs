using System.Collections.Generic;
using System.Globalization;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Which General MIDI family an index-15 group id falls in.
    /// </summary>
    /// <remarks>
    ///     The id block is the only thing that identifies a patch. Index 15 has no name hashes at all
    ///     (<c>RealCacheReferenceTableShapeTests</c> lists the indexes that set the identifiers flag
    ///     and 15 is not one of them), so every label a user sees is derived from the group id and
    ///     nothing else.
    /// </remarks>
    public enum MidiPatchFamily {
        /// <summary>Ids 0 to 127, the General MIDI melodic programs in their published order.</summary>
        Melodic,

        /// <summary>An id above 127 that lands on a published GS drum-kit program offset.</summary>
        DrumKit,

        /// <summary>An id the General MIDI and GS tables say nothing about.</summary>
        Jagex
    }

    /// <summary>
    ///     The General MIDI tables the MIDI patch tab reads its labels out of.
    /// </summary>
    /// <remarks>
    ///     <b>Every name here is a claim about General MIDI, not about this cache.</b> Index 15
    ///     carries no name hashes, so nothing on disk says what patch 40 is; what makes the join safe
    ///     is the measured id layout, which <c>RealCacheMidiPatchTests</c> asserts in both caches:
    ///     ids 0 to 127 are a contiguous block, then ten ids above 127, then 255 and 256 to 292. A
    ///     contiguous 0..127 block is what a General MIDI melodic bank looks like and is not a shape
    ///     an arbitrary id space would take by chance.
    ///     <para>
    ///     <b>Where the id block and the published tables disagree, this says so rather than guessing.</b>
    ///     The GS drum kits sit at program offsets 0, 1, 8, 16, 24, 25, 32, 40, 48 and 56, which
    ///     against the drum bank's base of 128 are ids 128, 129, 136, 144, 152, 153, 160, 168, 176 and
    ///     184. This cache holds 178 and does <b>not</b> hold 160, so it has no Jazz kit and it has one
    ///     kit at an offset the published table does not name. That id is labelled by its offset alone;
    ///     inventing a name for it would put a claim on screen that nothing supports.
    ///     </para>
    /// </remarks>
    public static class GeneralMidi {
        /// <summary>The lowest id that is not a melodic program.</summary>
        /// <remarks>
        ///     Also the drum bank's base: a patch id is <c>(bankSelect &lt;&lt; 7) | program</c>
        ///     (<c>Node_Sub31_Sub2.java:647-651</c>), so bank LSB 1 starts here.
        /// </remarks>
        public const int DrumBankBase = 128;

        /// <summary>The lowest key the General MIDI percussion map names.</summary>
        public const int FirstPercussionKey = 35;

        /// <summary>
        ///     The 128 General MIDI melodic programs, in program order.
        /// </summary>
        private static readonly string[] Programs = {
            "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano",
            "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavi",
            "Celesta", "Glockenspiel", "Music Box", "Vibraphone",
            "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
            "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ",
            "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
            "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)",
            "Electric Guitar (clean)",
            "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
            "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass",
            "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
            "Violin", "Viola", "Cello", "Contrabass",
            "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
            "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2",
            "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
            "Trumpet", "Trombone", "Tuba", "Muted Trumpet",
            "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
            "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax",
            "Oboe", "English Horn", "Bassoon", "Clarinet",
            "Piccolo", "Flute", "Recorder", "Pan Flute",
            "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
            "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)",
            "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
            "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)",
            "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
            "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)",
            "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
            "Sitar", "Banjo", "Shamisen", "Koto",
            "Kalimba", "Bagpipe", "Fiddle", "Shanai",
            "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock",
            "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
            "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet",
            "Telephone Ring", "Helicopter", "Applause", "Gunshot"
        };

        /// <summary>
        ///     The GS drum kits, keyed by the program offset they sit at inside the drum bank.
        /// </summary>
        /// <remarks>
        ///     Keyed by offset rather than by cache id so that the table states the published
        ///     convention and the id arithmetic stays in one place. An offset absent from here is a
        ///     kit the published table does not name, which is a fact worth showing rather than
        ///     papering over.
        /// </remarks>
        private static readonly Dictionary<int, string> DrumKits = new Dictionary<int, string> {
            { 0, "Standard Kit" },
            { 1, "Standard Kit 2" },
            { 8, "Room Kit" },
            { 16, "Power Kit" },
            { 24, "Electronic Kit" },
            { 25, "TR-808 Kit" },
            { 32, "Jazz Kit" },
            { 40, "Brush Kit" },
            { 48, "Orchestra Kit" },
            { 56, "Sound FX Kit" }
        };

        /// <summary>
        ///     The General MIDI percussion map, keys 35 to 81, in key order.
        /// </summary>
        /// <remarks>
        ///     Only meaningful on a drum kit. On a melodic program a key is a pitch, and labelling it
        ///     "Closed Hi Hat" would be a category error rather than a helpful hint, which is why
        ///     <see cref="PercussionName"/> is only reached through <see cref="KeyLabel"/>.
        /// </remarks>
        private static readonly string[] Percussion = {
            "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare",
            "Hand Clap", "Electric Snare", "Low Floor Tom", "Closed Hi Hat",
            "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat",
            "Low-Mid Tom", "Hi-Mid Tom", "Crash Cymbal 1", "High Tom",
            "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine",
            "Splash Cymbal", "Cowbell", "Crash Cymbal 2", "Vibraslap",
            "Ride Cymbal 2", "Hi Bongo", "Low Bongo", "Mute Hi Conga",
            "Open Hi Conga", "Low Conga", "High Timbale", "Low Timbale",
            "High Agogo", "Low Agogo", "Cabasa", "Maracas",
            "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro",
            "Claves", "Hi Wood Block", "Low Wood Block", "Mute Cuica",
            "Open Cuica", "Mute Triangle", "Open Triangle"
        };

        /// <summary>The twelve pitch classes, sharps rather than flats, as note names are written.</summary>
        private static readonly string[] PitchClasses = {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };

        /// <summary>
        ///     Which General MIDI family a patch id falls in.
        /// </summary>
        /// <param name="patchId">The index-15 group id.</param>
        /// <returns>The family.</returns>
        public static MidiPatchFamily FamilyOf(int patchId) {
            if (patchId >= 0 && patchId < DrumBankBase)
                return MidiPatchFamily.Melodic;

            //Only ids in the drum bank itself, and only at an offset the published table names.
            int offset = patchId - DrumBankBase;
            return offset >= 0 && offset < DrumBankBase && DrumKits.ContainsKey(offset)
                ? MidiPatchFamily.DrumKit
                : MidiPatchFamily.Jagex;
        }

        /// <summary>
        ///     Whether a patch's keys are percussion slots rather than pitches.
        /// </summary>
        /// <remarks>
        ///     Every id in the drum bank, not only the ones the GS table names. The bank is what
        ///     decides how a key is read - <c>Node_Sub31_Sub2.java:647-651</c> puts the whole bank at
        ///     128 and above - so a kit at an unnamed offset is still a kit.
        /// </remarks>
        /// <param name="patchId">The index-15 group id.</param>
        /// <returns>Whether the percussion map applies.</returns>
        public static bool IsPercussion(int patchId) {
            return patchId >= DrumBankBase && patchId < DrumBankBase * 2;
        }

        /// <summary>
        ///     What to call a patch, from its id alone.
        /// </summary>
        /// <remarks>
        ///     Never blank and never a bare number: a row with an empty name reads as a load failure.
        ///     An id the tables do not cover is described by the bank and program it decomposes into,
        ///     which is the most that can honestly be said about it.
        /// </remarks>
        /// <param name="patchId">The index-15 group id.</param>
        /// <returns>The label.</returns>
        public static string PatchName(int patchId) {
            if (patchId >= 0 && patchId < Programs.Length)
                return Programs[patchId];

            int bank = patchId >> 7;
            int program = patchId & 0x7f;

            if (IsPercussion(patchId))
                return DrumKits.TryGetValue(program, out string? kit)
                    ? kit
                    : "Drum kit (bank 1, program " + program.ToString(CultureInfo.InvariantCulture) + ")";

            return "Jagex instrument (bank " + bank.ToString(CultureInfo.InvariantCulture) +
                   ", program " + program.ToString(CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>The family's name, for a column that groups the bank.</summary>
        /// <param name="patchId">The index-15 group id.</param>
        /// <returns>The family's name.</returns>
        public static string FamilyName(int patchId) {
            switch (FamilyOf(patchId)) {
                case MidiPatchFamily.Melodic:
                    return "GM melodic";
                case MidiPatchFamily.DrumKit:
                    return "GM drum kit";
                default:
                    return IsPercussion(patchId) ? "Drum bank, unnamed" : "Jagex";
            }
        }

        /// <summary>
        ///     A key's note name, with middle C written as C4.
        /// </summary>
        /// <remarks>
        ///     Key 60 is middle C, so octave numbering starts at -1 for key 0. Stated because the
        ///     other convention in circulation calls middle C "C3", and a keyboard labelled one way
        ///     while the user reads the other misnames every octave.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The note name.</returns>
        public static string NoteName(int key) {
            int octave = (key / 12) - 1;
            return PitchClasses[key % 12] + octave.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The percussion instrument a key names, or null outside the published map.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The instrument, or null.</returns>
        public static string? PercussionName(int key) {
            int slot = key - FirstPercussionKey;
            return slot >= 0 && slot < Percussion.Length ? Percussion[slot] : null;
        }

        /// <summary>
        ///     What to call one key of one patch.
        /// </summary>
        /// <remarks>
        ///     A drum kit's key is a slot in the percussion map and its note name means nothing; a
        ///     melodic program's key is a pitch and the percussion map means nothing. Both are shown
        ///     on a kit, because a user comparing against a sequencer still needs the note number to
        ///     line up with what they are reading.
        /// </remarks>
        /// <param name="patchId">The index-15 group id.</param>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The label.</returns>
        public static string KeyLabel(int patchId, int key) {
            string note = NoteName(key);
            if (!IsPercussion(patchId))
                return note;

            string? percussion = PercussionName(key);
            return percussion == null ? note : note + " - " + percussion;
        }

        /// <summary>Whether a key is a white key on a piano keyboard.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>Whether it is white.</returns>
        public static bool IsWhiteKey(int key) {
            switch (key % 12) {
                case 1:
                case 3:
                case 6:
                case 8:
                case 10:
                    return false;
                default:
                    return true;
            }
        }
    }
}
