using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Everything one key of one patch holds, expanded out of the run-length planes once.
    /// </summary>
    /// <remarks>
    ///     <b>The expansion is the risk, not the codec.</b> <c>MidiPatchDefinition.Encode</c> replays
    ///     the stored run lists verbatim, so the byte-identity sweep over all 176 patches would stay
    ///     green with every walk off by one key. What pins the walks is
    ///     <c>MidiPatchWalkTests</c>, against hand-built plane bytes; this type only calls those
    ///     pinned accessors and never re-derives a plane of its own, so nothing here can disagree
    ///     with them.
    ///     <para>
    ///     Taken as a snapshot rather than read per paint because each accessor expands the whole
    ///     plane on every call: <c>MuteGroupOf</c> alone walks the sample plane and then the mute
    ///     plane, so a keyboard that asked during <c>OnPaint</c> would run 128 of those per repaint.
    ///     </para>
    /// </remarks>
    public readonly struct MidiKeySnapshot {
        /// <summary>Takes one key's values off a decoded patch.</summary>
        /// <param name="patch">The patch.</param>
        /// <param name="key">The key, 0..127.</param>
        public MidiKeySnapshot(MidiPatchDefinition patch, int key) {
            if (patch == null)
                throw new ArgumentNullException(nameof(patch));

            Key = key;
            Reference = patch.SampleReferenceOf(key);
            Bank = patch.BankOf(key);
            SampleId = patch.SampleIdOf(key);
            Held = patch.HeldOf(key);
            Tuning = patch.TuningOf(key);
            MuteGroup = patch.MuteGroupOf(key);
            Pan = patch.PanOf(key);
            Volume = patch.VolumeOf(key);
            Envelope = patch.EnvelopeOf(key);
        }

        /// <summary>The key, 0..127.</summary>
        public int Key { get; }

        /// <summary>The stored sample reference, 0 when the key is silent.</summary>
        public int Reference { get; }

        /// <summary>Which index the key's sample lives in, or null when it is silent.</summary>
        public MidiSampleBank? Bank { get; }

        /// <summary>The sample id within that bank, or -1.</summary>
        public int SampleId { get; }

        /// <summary>Whether the voice sustains until it is released rather than for a counted length.</summary>
        public bool Held { get; }

        /// <summary>The tuning word, the two delta planes accumulated with the sustain bit on top.</summary>
        public short Tuning { get; }

        /// <summary>The mute group, or -1 for none.</summary>
        public int MuteGroup { get; }

        /// <summary>The pan, 0 hard left to 128 hard right, or -1 when the key is silent.</summary>
        public int Pan { get; }

        /// <summary>The key's volume.</summary>
        public int Volume { get; }

        /// <summary>The envelope index, or -1 when the key is silent.</summary>
        public int Envelope { get; }

        /// <summary>Whether the key names a sample at all.</summary>
        public bool Sounds => Reference != 0;

        /// <summary>
        ///     Whether this key is silent in this editor's player despite naming a sample.
        /// </summary>
        /// <remarks>
        ///     Index 4 is a procedural bank whose records describe a synthesiser patch rather than
        ///     storing audio, and this project has a codec for it and no renderer. See
        ///     <c>MidiSoundBank.Sample</c>, which counts every such key into
        ///     <c>MidiSoundBank.UnrenderedEffectKeys</c> rather than dropping it quietly.
        /// </remarks>
        public bool SilentHere => Sounds && Bank == MidiSampleBank.SoundEffects;

        /// <summary>
        ///     The detune the client applies, in 256ths of a semitone below the key's own pitch.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:980</c> takes the pitch offset as <c>(key &lt;&lt; 8) - (word
        ///     &amp; 0x7fff)</c>, so the tuning word is subtracted rather than added and its top bit
        ///     is the sustain flag rather than part of the number.
        /// </remarks>
        public int PitchOffset => (Key << 8) - (Tuning & 0x7fff);
    }

    /// <summary>
    ///     One index-15 patch as a row of the MIDI patch list.
    /// </summary>
    /// <remarks>
    ///     The label comes from <see cref="GeneralMidi"/> keyed on the group id, because index 15 has
    ///     no name hashes and there is nothing else to key on. The census is computed from the pinned
    ///     per-key accessors at decode, over the sounding keys only, which is the same walk
    ///     <c>RealCacheMidiPatchTests</c> makes over the whole bank.
    /// </remarks>
    public sealed class MidiPatchListing : IDetailRow {
        private MidiKeySnapshot[]? keys;

        /// <summary>Builds a row around a decoded patch.</summary>
        /// <param name="address">Where the patch was read from.</param>
        /// <param name="patch">The decoded patch.</param>
        /// <exception cref="ArgumentNullException">The patch is null.</exception>
        public MidiPatchListing(DefinitionAddress address, MidiPatchDefinition patch) {
            Address = address;
            Patch = patch ?? throw new ArgumentNullException(nameof(patch));

            /* Walked over the sounding keys rather than all 128, and the accessors are asked once
               each. Every one of them expands a whole plane per call, so a census that asked nine
               questions of all 128 keys of all 176 patches would expand the planes a third of a
               million times to fill a grid of counts. */
            var groups = new SortedSet<int>();
            foreach (int key in patch.UsedKeys) {
                SoundingKeys++;

                if (patch.BankOf(key) == MidiSampleBank.Vorbis)
                    VorbisKeys++;
                else
                    EffectKeys++;

                if (patch.HeldOf(key))
                    HeldKeys++;

                int group = patch.MuteGroupOf(key);
                if (group < 0)
                    continue;

                MuteGroupKeys++;
                groups.Add(group);
            }

            MuteGroups = groups.Count;
        }

        /// <summary>Where the patch was read from. Index 15 is one file per group, so the group is the id.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded patch, which is what an edit is applied to and re-encoded from.</summary>
        public MidiPatchDefinition Patch { get; }

        /// <summary>The patch id, which is the index-15 group id.</summary>
        public int Id => Patch.Id;

        /// <summary>What General MIDI calls this program, keyed on the id alone.</summary>
        public string Name => GeneralMidi.PatchName(Id);

        /// <summary>Which part of the id space the patch sits in.</summary>
        public string Family => GeneralMidi.FamilyName(Id);

        /// <summary>How many of the 128 keys name a sample.</summary>
        public int SoundingKeys { get; }

        /// <summary>Sounding keys whose sample is a recorded index-14 Vorbis record.</summary>
        public int VorbisKeys { get; }

        /// <summary>
        ///     Sounding keys whose sample is an index-4 procedural record.
        /// </summary>
        /// <remarks>
        ///     These are the keys this editor cannot render. Shown as its own column rather than
        ///     folded into the sounding count, because a patch with one of them plays every note but
        ///     that one and the gap otherwise reads as a decode fault.
        /// </remarks>
        public int EffectKeys { get; }

        /// <summary>Sounding keys whose voice sustains until it is released.</summary>
        public int HeldKeys { get; }

        /// <summary>Sounding keys that belong to a mute group.</summary>
        public int MuteGroupKeys { get; }

        /// <summary>How many distinct mute groups the patch uses.</summary>
        public int MuteGroups { get; }

        /// <summary>How many envelopes the patch's keys share.</summary>
        public int Envelopes => Patch.Envelopes.Count;

        /// <summary>The whole-patch volume, as the file stores it.</summary>
        public int PatchVolume {
            get => Patch.PatchVolume;
            set => Patch.PatchVolume = Math.Clamp(value, 0, 255);
        }

        /// <summary>
        ///     Every key's values, expanded once and kept.
        /// </summary>
        /// <remarks>
        ///     Built on first use rather than in the constructor. The list loads all 176 patches and
        ///     only the selected one is ever drawn as a keyboard, so expanding every plane of every
        ///     patch at load would pay for 175 keyboards nobody looks at.
        /// </remarks>
        public IReadOnlyList<MidiKeySnapshot> Keys {
            get {
                if (keys != null)
                    return keys;

                var expanded = new MidiKeySnapshot[MidiPatchDefinition.Keys];
                for (int key = 0; key < expanded.Length; key++)
                    expanded[key] = new MidiKeySnapshot(Patch, key);

                keys = expanded;
                return keys;
            }
        }

        /// <summary>Forgets the expanded keys, so an edit to the patch is picked up on the next look.</summary>
        public void Invalidate() {
            keys = null;
        }

        /// <summary>The patch in one line.</summary>
        public string Summary =>
            "Patch " + Id.ToString(CultureInfo.InvariantCulture) + " - " + Name + " (" + Family + "), " +
            SoundingKeys.ToString(CultureInfo.InvariantCulture) + " sounding keys, " +
            Envelopes.ToString(CultureInfo.InvariantCulture) + " envelope" + (Envelopes == 1 ? "" : "s") +
            (EffectKeys > 0
                ? ", " + EffectKeys.ToString(CultureInfo.InvariantCulture) + " silent here (index 4)"
                : string.Empty);

        /// <summary>Every value the patch carries, for the detail pane.</summary>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Patch id (index 15 group)", Id.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("General MIDI name", Name),
                    new DetailField("Family", Family),
                    new DetailField("Bank select / program",
                        "bank LSB " + (Id >> 7).ToString(CultureInfo.InvariantCulture) + ", program " +
                        (Id & 0x7f).ToString(CultureInfo.InvariantCulture) +
                        " (patch = (bankSelect << 7) | program)"),
                    new DetailField("Sounding keys",
                        SoundingKeys.ToString(CultureInfo.InvariantCulture) + " of " +
                        MidiPatchDefinition.Keys.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Keys on index 14 (Vorbis)",
                        VorbisKeys.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Keys on index 4 (procedural, not rendered here)",
                        EffectKeys.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Held keys", HeldKeys.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Mute-group keys",
                        MuteGroupKeys.ToString(CultureInfo.InvariantCulture) + " in " +
                        MuteGroups.ToString(CultureInfo.InvariantCulture) + " group" +
                        (MuteGroups == 1 ? "" : "s")),
                    new DetailField("Patch volume (stored)",
                        PatchVolume.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Volume curve points",
                        Patch.VolumeCurveLevels.Length.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Pan curve points",
                        Patch.PanCurveLevels.Length.ToString(CultureInfo.InvariantCulture)),

                    /* The run lists are shown because they are an encoding choice the per-key values
                       cannot recover: where a run is split is the packer's decision, and two adjacent
                       runs carrying the same value decode identically to one long run and re-encode
                       to a different file. */
                    new DetailField("Sample runs, as stored", Runs(Patch.SampleRuns)),
                    new DetailField("Mute-group runs, as stored", Runs(Patch.MuteGroupRuns)),
                    new DetailField("Pan runs, as stored", Runs(Patch.PanRuns)),
                    new DetailField("Envelope runs, as stored", Runs(Patch.EnvelopeRuns)),
                    new DetailField("Envelope selectors", DetailText.Ids(Patch.EnvelopeSelectors)),
                    new DetailField("Sample reference widths", DetailText.Ids(Patch.SampleReferenceWidths))
                };

                for (int i = 0; i < Patch.Envelopes.Count; i++)
                    fields.Add(new DetailField(
                        "Envelope " + i.ToString(CultureInfo.InvariantCulture),
                        Describe(Patch.Envelopes[i])));

                return fields;
            }
        }

        /// <summary>One envelope in a line, in the order the file states its parts.</summary>
        /// <param name="envelope">The envelope.</param>
        /// <returns>The rendered envelope.</returns>
        public static string Describe(MidiPatchEnvelope envelope) {
            if (envelope == null)
                return "none";

            return "attack " + envelope.AttackPoints.ToString(CultureInfo.InvariantCulture) +
                   " points at rate " + DetailText.OrAbsent(envelope.AttackRate) +
                   ", release " + envelope.ReleasePoints.ToString(CultureInfo.InvariantCulture) +
                   " points at rate " + DetailText.OrAbsent(envelope.ReleaseRate) +
                   ", decay " + envelope.Decay.ToString(CultureInfo.InvariantCulture) +
                   " at rate " + DetailText.OrAbsent(envelope.DecayRate) +
                   ", vibrato rate " + envelope.VibratoRate.ToString(CultureInfo.InvariantCulture) +
                   " depth " + DetailText.OrAbsent(envelope.VibratoDepth) +
                   " delay " + DetailText.OrAbsent(envelope.VibratoDelay);
        }

        /// <summary>A run list as the signed bytes the file holds.</summary>
        /// <remarks>
        ///     Signed on purpose: the client reads each with <c>readSignedByte</c>
        ///     (<c>Node_Sub44.java:120</c>), so a run of 128 or more comes back negative and never
        ///     ends, and a display that showed it unsigned would misreport the shape of the file.
        /// </remarks>
        /// <param name="runs">The run lengths.</param>
        /// <returns>The rendered list.</returns>
        private static string Runs(sbyte[] runs) {
            if (runs == null || runs.Length == 0)
                return "none, so one unbounded run covers the keyboard";

            var parts = new List<int>(runs.Length);
            foreach (sbyte run in runs)
                parts.Add(run);

            return DetailText.Ids(parts) + ", then one unbounded run";
        }
    }

    /// <summary>
    ///     One selected key, as the detail pane shows it.
    /// </summary>
    /// <remarks>
    ///     Its own row type rather than fields hung off the patch, because the two answer different
    ///     questions and a pane that showed both at once would bury the key under the patch. What it
    ///     says out loud is the sample reference's three-way split, which is invisible in the stored
    ///     number and decides which of two entirely different sample formats the key plays.
    /// </remarks>
    public sealed class MidiKeyDetail : IDetailRow {
        private readonly MidiPatchListing listing;
        private readonly MidiKeySnapshot key;

        /// <summary>Binds a key of a patch.</summary>
        /// <param name="listing">The patch the key belongs to.</param>
        /// <param name="key">The key's expanded values.</param>
        /// <exception cref="ArgumentNullException">The listing is null.</exception>
        public MidiKeyDetail(MidiPatchListing listing, MidiKeySnapshot key) {
            this.listing = listing ?? throw new ArgumentNullException(nameof(listing));
            this.key = key;
        }

        /// <summary>The key in one line.</summary>
        public string Summary =>
            "Key " + key.Key.ToString(CultureInfo.InvariantCulture) + " (" +
            GeneralMidi.KeyLabel(listing.Id, key.Key) + ") of patch " +
            listing.Id.ToString(CultureInfo.InvariantCulture) + " - " +
            (key.Sounds
                ? "sample " + key.SampleId.ToString(CultureInfo.InvariantCulture) + " in index " +
                  (key.Bank == MidiSampleBank.Vorbis ? "14" : "4")
                : "silent, this key names no sample");

        /// <summary>Every value the key carries.</summary>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Key", key.Key.ToString(CultureInfo.InvariantCulture)),
                    new DetailField("Note", GeneralMidi.KeyLabel(listing.Id, key.Key)),
                    new DetailField("Sample reference (stored)",
                        key.Reference.ToString(CultureInfo.InvariantCulture) + Split(key))
                };

                if (!key.Sounds) {
                    fields.Add(new DetailField("Plays", "nothing; a reference of 0 is silence"));
                    return fields;
                }

                fields.Add(new DetailField("Bank (bit 0)", BankText(key)));
                fields.Add(new DetailField("Sample id (v >> 2)",
                    key.SampleId.ToString(CultureInfo.InvariantCulture)));
                fields.Add(new DetailField("Sustain (bit 1)", key.Held
                    ? "held: the voice runs until the note is released"
                    : "not held: the voice runs for a counted length"));
                fields.Add(new DetailField("Tuning word",
                    "0x" + ((ushort) key.Tuning).ToString("X4", CultureInfo.InvariantCulture) +
                    ", detune " + (key.Tuning & 0x7fff).ToString(CultureInfo.InvariantCulture) +
                    "/256 semitones, top bit is the sustain flag"));
                fields.Add(new DetailField("Pitch offset the client uses",
                    key.PitchOffset.ToString(CultureInfo.InvariantCulture) +
                    " (256ths of a semitone, (key << 8) - (word & 0x7fff))"));
                fields.Add(new DetailField("Pan", PanText(key.Pan)));
                fields.Add(new DetailField("Volume", key.Volume.ToString(CultureInfo.InvariantCulture)));
                fields.Add(new DetailField("Mute group", MuteGroupText()));
                fields.Add(new DetailField("Envelope", key.Envelope < 0
                    ? "none"
                    : key.Envelope.ToString(CultureInfo.InvariantCulture) + " of " +
                      listing.Envelopes.ToString(CultureInfo.InvariantCulture)));

                if (key.Envelope >= 0 && key.Envelope < listing.Patch.Envelopes.Count)
                    fields.Add(new DetailField("Envelope shape",
                        MidiPatchListing.Describe(listing.Patch.Envelopes[key.Envelope])));

                return fields;
            }
        }

        /// <summary>How the stored reference comes apart, spelled out beside the number.</summary>
        /// <remarks>
        ///     One of the two things the tab has to say out loud. The reference is a single integer
        ///     on screen and it is three unrelated fields: the value less one carries the bank in
        ///     bit 0, the sustain flag in bit 1, and the sample id above them
        ///     (<c>Node_Sub44.java:215-219</c> and <c>:476-485</c>).
        /// </remarks>
        /// <param name="snapshot">The key.</param>
        /// <returns>The split, as text.</returns>
        private static string Split(MidiKeySnapshot snapshot) {
            if (!snapshot.Sounds)
                return " (0 means the key is silent)";

            int value = snapshot.Reference - 1;
            return " (v = " + value.ToString(CultureInfo.InvariantCulture) +
                   " after the stored bias: bit 0 selects the bank, bit 1 is sustain, id is v >> 2)";
        }

        /// <summary>Which index the key reads from, and whether this editor can play it.</summary>
        /// <param name="snapshot">The key.</param>
        /// <returns>The bank, as text.</returns>
        private static string BankText(MidiKeySnapshot snapshot) {
            return snapshot.Bank == MidiSampleBank.Vorbis
                ? "1 - index 14, a recorded Vorbis sample"
                : "0 - index 4, a procedural synth patch. THIS EDITOR HAS NO INDEX-4 RENDERER, " +
                  "so this key is silent in the player rather than wrong.";
        }

        /// <summary>The pan as a position rather than as a number nobody can place.</summary>
        /// <param name="pan">The stored pan, 0 to 128.</param>
        /// <returns>The pan, as text.</returns>
        private static string PanText(int pan) {
            string place = pan < 56 ? "left of centre" : pan > 72 ? "right of centre" : "centre";
            return pan.ToString(CultureInfo.InvariantCulture) + " of 128, " + place +
                   " (the channel's own pan bends this rather than replacing it)";
        }

        /// <summary>
        ///     The mute group, said in what it does rather than as an integer.
        /// </summary>
        /// <remarks>
        ///     The one field on this pane that is meaningless as a number. A mute group is how a
        ///     closed hi-hat chokes an open one: <c>Node_Sub31_Sub2.java:1001-1009</c> keeps one voice
        ///     slot per group per channel and cuts the previous occupant, so the group is a statement
        ///     about which of a patch's own keys can sound at the same time.
        /// </remarks>
        /// <returns>The group, as text.</returns>
        private string MuteGroupText() {
            if (key.MuteGroup < 0)
                return "none; this key does not cut anything and nothing cuts it";

            var companions = new List<string>();
            foreach (MidiKeySnapshot other in listing.Keys)
                if (other.Key != key.Key && other.MuteGroup == key.MuteGroup)
                    companions.Add(GeneralMidi.KeyLabel(listing.Id, other.Key));

            string shared = companions.Count == 0
                ? "no other key in this patch shares it"
                : "shared with " + string.Join(", ", companions);

            return key.MuteGroup.ToString(CultureInfo.InvariantCulture) +
                   " - playing this key cuts whatever else in group " +
                   key.MuteGroup.ToString(CultureInfo.InvariantCulture) +
                   " is sounding on the channel; " + shared;
        }
    }
}
