using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.cache;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Which cache a patch key draws its sample from.
    /// </summary>
    /// <remarks>
    ///     The bank is a single bit of the key's stored sample reference and it selects between two
    ///     entirely different sample formats, so it cannot be inferred from the sample id.
    /// </remarks>
    public enum MidiSampleBank {
        /// <summary>
        ///     Index 4, the procedural sound-effect bank.
        /// </summary>
        /// <remarks>
        ///     <c>Class308.method3611</c> (<c>Class308.java:108-113</c>) resolves the id through
        ///     <c>aJS5Archive_2578</c>, which <c>ClientScript.java:61</c> constructs from the index-4
        ///     archive. Each record is a synthesiser patch rendered to PCM, not stored audio.
        /// </remarks>
        SoundEffects = 0,

        /// <summary>
        ///     Index 14, the Vorbis sample bank.
        /// </summary>
        /// <remarks>
        ///     <c>Class308.method3613</c> (<c>Class308.java:156-161</c>) resolves the id through
        ///     <c>aJS5Archive_2577</c>, the index-14 archive, whose group 0 is the shared Vorbis
        ///     setup header rather than a sample.
        /// </remarks>
        Vorbis = 1
    }

    /// <summary>
    ///     One MIDI instrument: up to 128 keys, each pointing at a sample and carrying its own
    ///     tuning, volume, pan, mute group and envelope.
    /// </summary>
    /// <remarks>
    ///     JS5 index 15 (<c>RSConstants.MIDI_PATCH_INDEX</c>), the client's <c>Node_Sub44</c>. One
    ///     file per group and the group id is the patch id: <c>Class355.method3875</c>
    ///     (<c>Class355.java:15-19</c>) fetches it through the single-file accessor and its only
    ///     caller (<c>Node_Sub31_Sub2.java:1141-1146</c>) passes the program number a track's
    ///     header asked for. That archive reaches the synthesiser through
    ///     <c>Particle_Sub3_Sub5_Sub2.java:99-100</c>, alongside index 14 and index 4, which is
    ///     what settles index 15 as the patch bank rather than a third sound-effect bank.
    ///     <para>
    ///     <b>Nothing here plays anything.</b> This is the field-level codec only; the synthesiser
    ///     that consumes it is a separate piece of work, which is why the decoded state is a plain
    ///     description of the file rather than the client's rendering scratch space.
    ///     </para>
    ///     <para>
    ///     The file is not an opcode stream and not a table of records. It is six run-length-encoded
    ///     planes over the 128 keys, laid out so that all six run lists appear near the front and
    ///     their values much later, plus a pool of shared envelopes. <b>Nothing in it is
    ///     self-describing:</b> the plane that says which keys hold a sample is read fifth, and the
    ///     three planes before it are skipped over on the way past and only interpreted once that
    ///     plane is known. A decoder that reads the file front to back in one pass cannot work.
    ///     </para>
    ///     <para>
    ///     <b>The run lists are held verbatim.</b> Where a run is split is a choice the packer made
    ///     and the decoded per-key values cannot recover it - two adjacent runs carrying the same
    ///     value decode identically to one long run and re-encode to a different file. The same
    ///     goes for the two tuning planes and every envelope time chain, which are stored as the
    ///     deltas the file holds rather than as the totals they accumulate to.
    ///     </para>
    /// </remarks>
    public sealed class MidiPatchDefinition {
        /// <summary>Keys a patch describes, always all 128 whether or not they hold a sample.</summary>
        /// <remarks><c>Node_Sub44.java:106-112</c> sizes every plane at 128 and never varies it.</remarks>
        public const int Keys = 128;

        /// <summary>The patch id, which is the group id.</summary>
        public int Id { get; set; }

        // ===================================================================
        //  The run lists, as stored
        // ===================================================================

        /// <summary>
        ///     Run lengths of the mute-group plane, excluding the zero that terminates the list.
        /// </summary>
        /// <remarks>
        ///     Signed, and deliberately so: the client reads each with <c>readSignedByte</c>
        ///     (<c>Node_Sub44.java:120</c>) into a counter it decrements, so a length of 128 or more
        ///     comes back negative and the run never ends. That is indistinguishable from the
        ///     run past the end of the list, which the client models as -1, and a decoder that read
        ///     these unsigned would split one run into several and change the file.
        /// </remarks>
        public sbyte[] MuteGroupRuns { get; set; } = Array.Empty<sbyte>();

        /// <summary>
        ///     One mute-group byte per run, plus one for the unbounded run past the last.
        /// </summary>
        /// <remarks>
        ///     Always <c>MuteGroupRuns.Length + 1</c> entries and kept whole, because the block's
        ///     length follows from the run list rather than from how many runs the 128-key walk
        ///     actually reaches. A run list that already covers all 128 keys leaves its tail
        ///     entries stored, unread by the client, and still part of the file, so sizing the
        ///     block from the walk would truncate it.
        /// </remarks>
        public byte[] MuteGroupValues { get; set; } = Array.Empty<byte>();

        /// <summary>Run lengths of the pan plane, excluding the terminator.</summary>
        public sbyte[] PanRuns { get; set; } = Array.Empty<sbyte>();

        /// <summary>One pan byte per run, plus one for the unbounded run past the last.</summary>
        public byte[] PanValues { get; set; } = Array.Empty<byte>();

        /// <summary>Run lengths of the envelope-assignment plane, excluding the terminator.</summary>
        public sbyte[] EnvelopeRuns { get; set; } = Array.Empty<sbyte>();

        /// <summary>
        ///     Which envelope each run uses, one entry per run plus one for the unbounded run.
        /// </summary>
        /// <remarks>
        ///     Stored decoded rather than raw. The file writes these as a back-reference chain -
        ///     zero means "a new envelope", anything else names one already in use, biased so that
        ///     the current one is unnameable (<c>Node_Sub44.java:155-166</c>) - and
        ///     <see cref="Encode"/> rebuilds that chain. The first two entries are never written at
        ///     all: they are fixed at envelope 0 and envelope 1.
        /// </remarks>
        public int[] EnvelopeSelectors { get; set; } = Array.Empty<int>();

        /// <summary>Run lengths of the sample plane, excluding the terminator.</summary>
        /// <remarks>
        ///     This one list drives two separate walks over the keys - the sample references and
        ///     the per-key volumes - which is why the volumes sit so much later in the file than
        ///     the list that shapes them.
        /// </remarks>
        public sbyte[] SampleRuns { get; set; } = Array.Empty<sbyte>();

        // ===================================================================
        //  The planes the runs carry
        // ===================================================================

        /// <summary>
        ///     The sample each run's keys play, in the order the walk reads them.
        /// </summary>
        /// <remarks>
        ///     0 means the run's keys are silent. Otherwise the value less one splits three ways:
        ///     bit 0 is the <see cref="MidiSampleBank"/>, bit 1 sets the top bit of the key's
        ///     tuning word, and the rest is the sample id (<c>Node_Sub44.java:215-219</c> and
        ///     <c>:476-485</c>). Use <see cref="BankOf"/>, <see cref="SampleIdOf"/> and
        ///     <see cref="HeldOf"/> rather than unpacking it at a call site.
        ///     <para>
        ///     Fewer entries than <c>SampleRuns.Length + 1</c> when the declared runs already cover
        ///     all 128 keys, because the walk stops reading once it runs out of keys.
        ///     </para>
        /// </remarks>
        public int[] SampleReferences { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     How many bytes each entry of <see cref="SampleReferences"/> occupied.
        /// </summary>
        /// <remarks>
        ///     The reference is a MIDI-style variable-length quantity, which can encode a small
        ///     value in more bytes than it needs, and this bank does so constantly: measured over
        ///     all 176 patches, <b>1060 references are two bytes wide against 91 that are one</b>.
        ///     An encoder that wrote the shortest form would therefore rewrite nearly every patch
        ///     in the index, so the width is recorded rather than recomputed - the same
        ///     encoding-choice capture the other non-canonical indexes need.
        /// </remarks>
        public int[] SampleReferenceWidths { get; set; } = Array.Empty<int>();

        /// <summary>
        ///     Per-run volume, less one, for the runs whose first key holds a sample.
        /// </summary>
        /// <remarks>
        ///     Walked under <see cref="SampleRuns"/> a second time. A run whose first key is silent
        ///     stores nothing and inherits whatever the previous run set
        ///     (<c>Node_Sub44.java:275-288</c>), so the entry count depends on the sample plane and
        ///     the two cannot be decoded independently.
        /// </remarks>
        public byte[] VolumeValues { get; set; } = Array.Empty<byte>();

        /// <summary>
        ///     Low byte of each key's tuning, as the 128 stored deltas.
        /// </summary>
        /// <remarks>
        ///     The client accumulates these in an <c>int</c> and stores each running total through
        ///     a <c>short</c> (<c>Node_Sub44.java:196-199</c>), and the totals are then modified
        ///     again by the sample reference's bit 1. Neither step is reversible, so the deltas are
        ///     what is kept and <see cref="TuningOf"/> derives the totals on demand.
        /// </remarks>
        public byte[] FineTuneDeltas { get; set; } = new byte[Keys];

        /// <summary>High byte of each key's tuning, as the 128 stored deltas.</summary>
        /// <remarks><c>Node_Sub44.java:200-204</c>.</remarks>
        public byte[] CoarseTuneDeltas { get; set; } = new byte[Keys];

        // ===================================================================
        //  Envelopes and the two whole-patch curves
        // ===================================================================

        /// <summary>The envelopes this patch's keys share.</summary>
        /// <remarks>
        ///     Always at least one, and at least two whenever the envelope plane has more than one
        ///     run: <c>Node_Sub44.java:152-153</c> seeds the count at two before reading a single
        ///     back-reference byte.
        /// </remarks>
        public List<MidiPatchEnvelope> Envelopes { get; } = new List<MidiPatchEnvelope>();

        /// <summary>
        ///     Levels of the volume curve applied across the keyboard, empty when there is none.
        /// </summary>
        /// <remarks>
        ///     A whole-patch shape that scales the per-key volumes before anything plays, applied
        ///     at <c>Node_Sub44.java:335-365</c>. It is <b>consumed at decode</b> in the client -
        ///     the curve is folded into the volume plane and thrown away - so this codec keeps it
        ///     as its own field and leaves <see cref="VolumeValues"/> unfolded. Folding it in would
        ///     lose the curve and change the file.
        /// </remarks>
        public sbyte[] VolumeCurveLevels { get; set; } = Array.Empty<sbyte>();

        /// <summary>
        ///     Where each volume-curve breakpoint sits, as stored.
        /// </summary>
        /// <remarks>
        ///     The first entry is an absolute key, the rest are <c>1 + delta</c> steps from it
        ///     (<c>Node_Sub44.java:336-341</c>). Kept as stored for the same reason the envelope
        ///     time chains are.
        /// </remarks>
        public byte[] VolumeCurvePositions { get; set; } = Array.Empty<byte>();

        /// <summary>Levels of the pan curve applied across the keyboard, empty when there is none.</summary>
        /// <remarks><c>Node_Sub44.java:366-417</c>, the pan counterpart of the volume curve.</remarks>
        public sbyte[] PanCurveLevels { get; set; } = Array.Empty<sbyte>();

        /// <summary>Where each pan-curve breakpoint sits, as stored.</summary>
        public byte[] PanCurvePositions { get; set; } = Array.Empty<byte>();

        /// <summary>The whole-patch volume, less one, as stored.</summary>
        /// <remarks>
        ///     <c>Node_Sub44.anInt4249</c>, multiplied into every voice's gain along with the note
        ///     velocity squared at <c>Node_Sub31_Sub2.java:977-978</c>. Stored biased by one, so a
        ///     stored 0 is a real volume of 1 and the patch is never silent by default.
        /// </remarks>
        public int PatchVolume { get; set; }

        // ===================================================================
        //  Per-key views, for anything that wants to play or display a patch
        // ===================================================================

        /// <summary>The raw sample reference for a key, 0 when the key is silent.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The stored reference.</returns>
        public int SampleReferenceOf(int key) => WalkSamples()[Check(key)];

        /// <summary>Whether a key plays anything at all.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>Whether the key names a sample.</returns>
        public bool IsKeyUsed(int key) => SampleReferenceOf(key) != 0;

        /// <summary>Which cache index a key's sample lives in.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The bank, or <c>null</c> when the key is silent.</returns>
        public MidiSampleBank? BankOf(int key) {
            int reference = SampleReferenceOf(key);
            return reference == 0 ? (MidiSampleBank?) null : (MidiSampleBank) ((reference - 1) & 1);
        }

        /// <summary>The id of the sample a key plays, within its own bank.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The sample id, or -1 when the key is silent.</returns>
        public int SampleIdOf(int key) {
            int reference = SampleReferenceOf(key);
            return reference == 0 ? -1 : (reference - 1) >> 2;
        }

        /// <summary>
        ///     Whether a key's voice is held until it is released rather than for a counted length.
        /// </summary>
        /// <remarks>
        ///     Bit 1 of the sample reference, which the client folds into the top bit of the tuning
        ///     word (<c>Node_Sub44.java:217</c>) and later tests as "is the tuning negative"
        ///     (<c>Node_Sub31_Sub2.java:997</c>) to set the voice's remaining-ticks counter to -1,
        ///     which never expires. Two unrelated-looking fields, one bit.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>Whether the key sustains indefinitely.</returns>
        public bool HeldOf(int key) {
            int reference = SampleReferenceOf(key);
            return reference != 0 && ((reference - 1) & 2) != 0;
        }

        /// <summary>
        ///     A key's tuning word: the two delta planes accumulated, plus the sustain bit.
        /// </summary>
        /// <remarks>
        ///     The client turns this into a pitch offset of <c>(key &lt;&lt; 8) - (word &amp;
        ///     0x7fff)</c> (<c>Node_Sub31_Sub2.java:980</c>), so the low 15 bits are a detune in
        ///     1/256ths of a semitone and the top bit is not part of the number at all.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The tuning word as the client would hold it.</returns>
        public short TuningOf(int key) {
            Check(key);
            int fine = 0;
            int coarse = 0;
            for (int i = 0; i <= key; i++) {
                fine += FineTuneDeltas[i];
                coarse += CoarseTuneDeltas[i];
            }

            int word = fine + (coarse << 8);
            if (HeldOf(key))
                word += 0x8000;
            return unchecked((short) word);
        }

        /// <summary>
        ///     A key's mute group: playing it silences whatever else in the group is sounding.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub31_Sub2.java:1001-1009</c> keeps one voice slot per group per channel and
        ///     cuts the previous occupant. Stored biased by one so that 0 means no group; -1 here
        ///     is that "no group", and is also what a silent key reports.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The mute group, or -1 for none.</returns>
        public int MuteGroupOf(int key) => WalkMuteGroups()[Check(key)];

        /// <summary>
        ///     A key's pan position, 0 hard left to 128 hard right.
        /// </summary>
        /// <remarks>
        ///     Stored as <c>(byte + 16) * 4</c> (<c>Node_Sub44.java:244</c>) and then bent by the
        ///     pan curve; the client blends it against the channel's own pan at
        ///     <c>Node_Sub31_Sub2.java:1109-1118</c>. This returns the plane's value before the
        ///     curve is applied, because the curve is a field of its own here rather than folded in.
        /// </remarks>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The pan, or -1 when the key is silent.</returns>
        public int PanOf(int key) => WalkPans()[Check(key)];

        /// <summary>The envelope a key uses, or -1 when the key is silent.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>An index into <see cref="Envelopes"/>.</returns>
        public int EnvelopeOf(int key) => WalkEnvelopes()[Check(key)];

        /// <summary>A key's volume, as the client computes it before velocity is applied.</summary>
        /// <param name="key">The key, 0..127.</param>
        /// <returns>The volume.</returns>
        public int VolumeOf(int key) => WalkVolumes()[Check(key)];

        /// <summary>Keys that name a sample.</summary>
        public IEnumerable<int> UsedKeys {
            get {
                int[] samples = WalkSamples();
                for (int key = 0; key < Keys; key++)
                    if (samples[key] != 0)
                        yield return key;
            }
        }

        // ===================================================================
        //  Decode
        // ===================================================================

        /// <summary>
        ///     Reads one patch out of its stored file.
        /// </summary>
        /// <remarks>
        ///     Follows <c>Node_Sub44.&lt;init&gt;</c> (<c>Node_Sub44.java:103-447</c>) field for
        ///     field. The one structural difference is that the client keeps two saved cursors and
        ///     reads the mute-group and pan value blocks from them much later; here each block is
        ///     read where it sits, because the block's length is fixed by the run list immediately
        ///     before it and nothing between them depends on it.
        /// </remarks>
        /// <param name="stream">The file, positioned at its first byte.</param>
        /// <returns>This definition, decoded.</returns>
        /// <exception cref="InvalidDataException">The file is not a well-formed patch.</exception>
        public MidiPatchDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            MuteGroupRuns = ReadRunLengths(stream);
            MuteGroupValues = stream.ReadBytes(MuteGroupRuns.Length + 1);

            PanRuns = ReadRunLengths(stream);
            PanValues = stream.ReadBytes(PanRuns.Length + 1);

            EnvelopeRuns = ReadRunLengths(stream);
            EnvelopeSelectors = ReadEnvelopeSelectors(stream, EnvelopeRuns.Length + 1, out int envelopeCount);

            Envelopes.Clear();
            for (int i = 0; i < envelopeCount; i++) {
                var envelope = new MidiPatchEnvelope();
                int attackPoints = stream.ReadUnsignedByte();
                if (attackPoints > 0) {
                    envelope.AttackLevels = new sbyte[attackPoints];
                    envelope.AttackTimeDeltas = new byte[attackPoints - 1];
                }

                int releasePoints = stream.ReadUnsignedByte();
                if (releasePoints > 0) {
                    envelope.ReleaseLevels = new sbyte[releasePoints - 1];
                    envelope.ReleaseTimeDeltas = new byte[releasePoints];
                }

                Envelopes.Add(envelope);
            }

            int volumeCurvePoints = stream.ReadUnsignedByte();
            VolumeCurveLevels = new sbyte[volumeCurvePoints];
            VolumeCurvePositions = new byte[volumeCurvePoints];

            int panCurvePoints = stream.ReadUnsignedByte();
            PanCurveLevels = new sbyte[panCurvePoints];
            PanCurvePositions = new byte[panCurvePoints];

            SampleRuns = ReadRunLengths(stream);

            FineTuneDeltas = stream.ReadBytes(Keys);
            CoarseTuneDeltas = stream.ReadBytes(Keys);

            //The sample plane. Everything above was read blind; this is the plane that says which
            //keys exist at all, and every walk below is gated on it.
            var references = new List<int>();
            var widths = new List<int>();
            var samples = new int[Keys];
            {
                int remaining = 0;
                int run = 0;
                int reference = 0;
                for (int key = 0; key < Keys; key++) {
                    if (remaining == 0) {
                        remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                        int before = stream.Position;
                        reference = stream.ReadVarInt();
                        references.Add(reference);
                        widths.Add(stream.Position - before);
                    }

                    samples[key] = reference;
                    remaining--;
                }
            }
            SampleReferences = references.ToArray();
            SampleReferenceWidths = widths.ToArray();

            //Per-run volumes, walked under the sample run list a second time. A run whose first
            //key is silent stores nothing, so this cannot be counted in advance.
            var volumes = new List<byte>();
            {
                int remaining = 0;
                int run = 0;
                for (int key = 0; key < Keys; key++) {
                    if (remaining == 0) {
                        remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                        if (samples[key] != 0)
                            volumes.Add((byte) stream.ReadUnsignedByte());
                    }

                    remaining--;
                }
            }
            VolumeValues = volumes.ToArray();

            PatchVolume = stream.ReadUnsignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes) {
                for (int i = 0; i < envelope.AttackLevels.Length; i++)
                    envelope.AttackLevels[i] = stream.ReadSignedByte();
                for (int i = 0; i < envelope.ReleaseLevels.Length; i++)
                    envelope.ReleaseLevels[i] = stream.ReadSignedByte();
            }

            for (int i = 0; i < VolumeCurveLevels.Length; i++)
                VolumeCurveLevels[i] = stream.ReadSignedByte();
            for (int i = 0; i < PanCurveLevels.Length; i++)
                PanCurveLevels[i] = stream.ReadSignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes)
                for (int i = 0; i < envelope.ReleaseTimeDeltas.Length; i++)
                    envelope.ReleaseTimeDeltas[i] = (byte) stream.ReadUnsignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes)
                for (int i = 0; i < envelope.AttackTimeDeltas.Length; i++)
                    envelope.AttackTimeDeltas[i] = (byte) stream.ReadUnsignedByte();

            for (int i = 0; i < VolumeCurvePositions.Length; i++)
                VolumeCurvePositions[i] = (byte) stream.ReadUnsignedByte();
            for (int i = 0; i < PanCurvePositions.Length; i++)
                PanCurvePositions[i] = (byte) stream.ReadUnsignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes)
                envelope.Decay = stream.ReadUnsignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes) {
                envelope.AttackRate = envelope.AttackLevels.Length > 0 ? stream.ReadUnsignedByte() : -1;
                envelope.ReleaseRate = envelope.ReleaseTimeDeltas.Length > 0 ? stream.ReadUnsignedByte() : -1;
                envelope.DecayRate = envelope.Decay > 0 ? stream.ReadUnsignedByte() : -1;
            }

            foreach (MidiPatchEnvelope envelope in Envelopes)
                envelope.VibratoRate = stream.ReadUnsignedByte();

            foreach (MidiPatchEnvelope envelope in Envelopes)
                envelope.VibratoDepth = envelope.VibratoRate > 0 ? stream.ReadUnsignedByte() : -1;

            foreach (MidiPatchEnvelope envelope in Envelopes)
                envelope.VibratoDelay = envelope.VibratoDepth > 0 ? stream.ReadUnsignedByte() : -1;

            return this;
        }

        // ===================================================================
        //  Encode
        // ===================================================================

        /// <summary>
        ///     Writes the patch back out in the layout the client reads.
        /// </summary>
        /// <remarks>
        ///     Field order mirrors <see cref="Decode"/> exactly, including the two places where a
        ///     later plane's length is decided by an earlier one, because the archive CRC covers
        ///     the stored bytes: a patch that re-encoded to a different length would change the
        ///     reference-table entry of every patch packed alongside it.
        /// </remarks>
        /// <returns>The encoded file, positioned at its start.</returns>
        /// <exception cref="InvalidDataException">The definition cannot be expressed on the wire.</exception>
        public JagStream Encode() {
            var stream = new JagStream();

            WriteRunLengths(stream, MuteGroupRuns);
            RequireBlock(MuteGroupValues.Length, MuteGroupRuns.Length + 1, "mute group");
            stream.Write(MuteGroupValues, 0, MuteGroupValues.Length);

            WriteRunLengths(stream, PanRuns);
            RequireBlock(PanValues.Length, PanRuns.Length + 1, "pan");
            stream.Write(PanValues, 0, PanValues.Length);

            WriteRunLengths(stream, EnvelopeRuns);
            WriteEnvelopeSelectors(stream);

            foreach (MidiPatchEnvelope envelope in Envelopes) {
                stream.WriteByte(envelope.AttackLevels.Length);
                stream.WriteByte(envelope.ReleaseTimeDeltas.Length);
            }

            stream.WriteByte(VolumeCurveLevels.Length);
            stream.WriteByte(PanCurveLevels.Length);

            WriteRunLengths(stream, SampleRuns);

            stream.Write(FineTuneDeltas, 0, Keys);
            stream.Write(CoarseTuneDeltas, 0, Keys);

            int[] samples = WalkSamples();
            {
                int remaining = 0;
                int run = 0;
                int emitted = 0;
                for (int key = 0; key < Keys; key++) {
                    if (remaining == 0) {
                        remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                        WriteVarInt(stream, SampleReferences[emitted], SampleReferenceWidths[emitted]);
                        emitted++;
                    }

                    remaining--;
                }
            }

            {
                int remaining = 0;
                int run = 0;
                int emitted = 0;
                for (int key = 0; key < Keys; key++) {
                    if (remaining == 0) {
                        remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                        if (samples[key] != 0)
                            stream.WriteByte(VolumeValues[emitted++]);
                    }

                    remaining--;
                }
            }

            stream.WriteByte(PatchVolume);

            foreach (MidiPatchEnvelope envelope in Envelopes) {
                foreach (sbyte level in envelope.AttackLevels)
                    stream.WriteSignedByte(level);
                foreach (sbyte level in envelope.ReleaseLevels)
                    stream.WriteSignedByte(level);
            }

            foreach (sbyte level in VolumeCurveLevels)
                stream.WriteSignedByte(level);
            foreach (sbyte level in PanCurveLevels)
                stream.WriteSignedByte(level);

            foreach (MidiPatchEnvelope envelope in Envelopes)
                foreach (byte delta in envelope.ReleaseTimeDeltas)
                    stream.WriteByte(delta);

            foreach (MidiPatchEnvelope envelope in Envelopes)
                foreach (byte delta in envelope.AttackTimeDeltas)
                    stream.WriteByte(delta);

            foreach (byte position in VolumeCurvePositions)
                stream.WriteByte(position);
            foreach (byte position in PanCurvePositions)
                stream.WriteByte(position);

            foreach (MidiPatchEnvelope envelope in Envelopes)
                stream.WriteByte(envelope.Decay);

            foreach (MidiPatchEnvelope envelope in Envelopes) {
                if (envelope.AttackLevels.Length > 0)
                    stream.WriteByte(envelope.AttackRate);
                if (envelope.ReleaseTimeDeltas.Length > 0)
                    stream.WriteByte(envelope.ReleaseRate);
                if (envelope.Decay > 0)
                    stream.WriteByte(envelope.DecayRate);
            }

            foreach (MidiPatchEnvelope envelope in Envelopes)
                stream.WriteByte(envelope.VibratoRate);

            foreach (MidiPatchEnvelope envelope in Envelopes)
                if (envelope.VibratoRate > 0)
                    stream.WriteByte(envelope.VibratoDepth);

            foreach (MidiPatchEnvelope envelope in Envelopes)
                if (envelope.VibratoDepth > 0)
                    stream.WriteByte(envelope.VibratoDelay);

            return stream.Flip();
        }

        // ===================================================================
        //  The walks
        // ===================================================================

        /// <summary>Expands the sample plane to one entry per key.</summary>
        /// <returns>The per-key sample references.</returns>
        private int[] WalkSamples() {
            var values = new int[Keys];
            int remaining = 0;
            int run = 0;
            int emitted = 0;
            int reference = 0;

            for (int key = 0; key < Keys; key++) {
                if (remaining == 0) {
                    remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                    reference = emitted < SampleReferences.Length ? SampleReferences[emitted] : 0;
                    emitted++;
                }

                values[key] = reference;
                remaining--;
            }

            return values;
        }

        /// <summary>Expands the mute-group plane, which only advances on keys that hold a sample.</summary>
        /// <returns>The per-key mute groups, -1 where there is none.</returns>
        private int[] WalkMuteGroups() {
            int[] samples = WalkSamples();
            var values = new int[Keys];
            int remaining = 0;
            int run = 0;
            int cursor = 0;
            int current = -1;

            for (int key = 0; key < Keys; key++) {
                values[key] = -1;
                if (samples[key] == 0)
                    continue;

                if (remaining == 0) {
                    current = cursor < MuteGroupValues.Length ? unchecked((sbyte) MuteGroupValues[cursor++]) - 1 : -1;
                    remaining = run < MuteGroupRuns.Length ? MuteGroupRuns[run++] : -1;
                }

                values[key] = current;
                remaining--;
            }

            return values;
        }

        /// <summary>Expands the pan plane.</summary>
        /// <returns>The per-key pan, -1 where the key is silent.</returns>
        private int[] WalkPans() {
            int[] samples = WalkSamples();
            var values = new int[Keys];
            int remaining = 0;
            int run = 0;
            int cursor = 0;
            int current = 0;

            for (int key = 0; key < Keys; key++) {
                values[key] = -1;
                if (samples[key] == 0)
                    continue;

                if (remaining == 0) {
                    int stored = cursor < PanValues.Length ? unchecked((sbyte) PanValues[cursor++]) : 0;
                    current = (stored + 16) << 2;
                    remaining = run < PanRuns.Length ? PanRuns[run++] : -1;
                }

                remaining--;
                values[key] = current & 0xFF;
            }

            return values;
        }

        /// <summary>Expands the envelope-assignment plane.</summary>
        /// <returns>The per-key envelope index, -1 where the key is silent.</returns>
        private int[] WalkEnvelopes() {
            int[] samples = WalkSamples();
            var values = new int[Keys];
            int remaining = 0;
            int run = 0;
            int current = -1;

            for (int key = 0; key < Keys; key++) {
                values[key] = -1;
                if (samples[key] == 0)
                    continue;

                if (remaining == 0) {
                    current = run < EnvelopeSelectors.Length ? EnvelopeSelectors[run] : -1;
                    remaining = run < EnvelopeRuns.Length ? EnvelopeRuns[run++] : -1;
                }

                remaining--;
                values[key] = current;
            }

            return values;
        }

        /// <summary>Expands the volume plane, which is walked under the sample run list.</summary>
        /// <returns>The per-key volume.</returns>
        private int[] WalkVolumes() {
            int[] samples = WalkSamples();
            var values = new int[Keys];
            int remaining = 0;
            int run = 0;
            int cursor = 0;
            int current = 0;

            for (int key = 0; key < Keys; key++) {
                if (remaining == 0) {
                    remaining = run < SampleRuns.Length ? SampleRuns[run++] : -1;
                    if (samples[key] != 0)
                        current = (cursor < VolumeValues.Length ? VolumeValues[cursor++] : 0) + 1;
                }

                values[key] = current;
                remaining--;
            }

            return values;
        }

        // ===================================================================
        //  Wire helpers
        // ===================================================================

        /// <summary>Reads a zero-terminated list of signed run lengths and consumes the terminator.</summary>
        /// <param name="stream">The file.</param>
        /// <returns>The run lengths, without the terminator.</returns>
        private static sbyte[] ReadRunLengths(JagStream stream) {
            int start = stream.Position;
            int length = 0;
            while (stream.Get(start + length) != 0)
                length++;

            var runs = new sbyte[length];
            for (int i = 0; i < length; i++)
                runs[i] = stream.ReadSignedByte();

            stream.ReadUnsignedByte();
            return runs;
        }

        /// <summary>Writes a run-length list and its terminator.</summary>
        /// <param name="stream">The file.</param>
        /// <param name="runs">The run lengths.</param>
        /// <exception cref="InvalidDataException">A run length is zero, which would terminate the list early.</exception>
        private static void WriteRunLengths(JagStream stream, sbyte[] runs) {
            foreach (sbyte run in runs) {
                if (run == 0)
                    throw new InvalidDataException(
                        "A run length of zero is the list terminator, so it cannot also be a run.");
                stream.WriteSignedByte(run);
            }

            stream.WriteByte(0);
        }

        /// <summary>
        ///     Reads the envelope back-reference chain into plain indices.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub44.java:147-167</c>. The first two slots are implicit - envelope 0 then
        ///     envelope 1 - and only the third onward is stored. A zero means "the next envelope
        ///     not yet used"; anything else names one already in use, biased down by one whenever
        ///     it is at or below the current selection so that the current one cannot be named.
        /// </remarks>
        /// <param name="stream">The file.</param>
        /// <param name="slots">How many selector slots the run list implies.</param>
        /// <param name="envelopeCount">How many distinct envelopes the chain creates.</param>
        /// <returns>The selector for each slot.</returns>
        private static int[] ReadEnvelopeSelectors(JagStream stream, int slots, out int envelopeCount) {
            var selectors = new int[slots];
            if (slots <= 1) {
                envelopeCount = slots;
                return selectors;
            }

            selectors[1] = 1;
            envelopeCount = 2;
            int previous = 1;

            for (int slot = 2; slot < slots; slot++) {
                int stored = stream.ReadUnsignedByte();
                if (stored != 0) {
                    if (stored <= previous)
                        stored--;
                    previous = stored;
                } else {
                    previous = envelopeCount++;
                }

                selectors[slot] = previous;
            }

            return selectors;
        }

        /// <summary>Rebuilds the back-reference chain from the decoded selectors.</summary>
        /// <param name="stream">The file.</param>
        /// <exception cref="InvalidDataException">A selector cannot be expressed in the chain.</exception>
        private void WriteEnvelopeSelectors(JagStream stream) {
            if (EnvelopeSelectors.Length != EnvelopeRuns.Length + 1)
                throw new InvalidDataException(
                    $"Patch {Id} has {EnvelopeSelectors.Length} envelope selectors for " +
                    $"{EnvelopeRuns.Length} runs; there is exactly one per run plus one for the " +
                    "unbounded run past the last.");

            if (EnvelopeSelectors.Length <= 1)
                return;

            int previous = 1;
            int created = 2;

            for (int slot = 2; slot < EnvelopeSelectors.Length; slot++) {
                int target = EnvelopeSelectors[slot];
                if (target == created) {
                    stream.WriteByte(0);
                    previous = created++;
                    continue;
                }

                //A stored byte equal to the current selection decodes to one below it, so the
                //current selection is the one value the chain cannot re-state. Decoding never
                //produces it, and an editor that hand-built one would write a file the client
                //reads as something else.
                if (target == previous)
                    throw new InvalidDataException(
                        $"Patch {Id} selects envelope {target} twice in a row at slot {slot}, " +
                        "which the back-reference chain cannot encode.");

                int stored = target < previous ? target + 1 : target;
                if (stored < 1 || stored > 255)
                    throw new InvalidDataException(
                        $"Patch {Id} selects envelope {target} at slot {slot}, which does not fit " +
                        "the one byte the chain allows.");

                stream.WriteByte(stored);
                previous = target;
            }
        }

        /// <summary>
        ///     Writes a variable-length quantity in a stated number of bytes.
        /// </summary>
        /// <remarks>
        ///     The width is honoured rather than minimised so that a file using a wider form than
        ///     it needs re-encodes to itself. <see cref="JagStream.WriteVarInt"/> always writes the
        ///     shortest form and so cannot be used here.
        /// </remarks>
        /// <param name="stream">The file.</param>
        /// <param name="value">The value.</param>
        /// <param name="width">How many bytes it occupied when it was read.</param>
        /// <exception cref="InvalidDataException">The value does not fit the stated width.</exception>
        private static void WriteVarInt(JagStream stream, int value, int width) {
            if (width < 1 || width > 5)
                throw new InvalidDataException(
                    $"A variable-length quantity occupies one to five bytes, not {width}.");
            if (width < 5 && (uint) value >> (7 * width) != 0)
                throw new InvalidDataException($"{value} does not fit {width} continuation bytes.");

            for (int shift = (width - 1) * 7; shift > 0; shift -= 7)
                stream.WriteByte((byte) (((value >> shift) & 0x7F) | 0x80));
            stream.WriteByte((byte) (value & 0x7F));
        }

        /// <summary>Rejects a value block whose length does not follow from its run list.</summary>
        /// <param name="actual">The block length held.</param>
        /// <param name="expected">The length the run list implies.</param>
        /// <param name="plane">The plane's name, for the message.</param>
        /// <exception cref="InvalidDataException">The two disagree.</exception>
        private static void RequireBlock(int actual, int expected, string plane) {
            if (actual != expected)
                throw new InvalidDataException(
                    $"The {plane} plane holds {actual} values for {expected - 1} runs; the block is " +
                    "always one longer than the run list, because of the unbounded run past the last.");
        }

        /// <summary>Rejects a key outside the 128 a patch describes.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The key.</returns>
        private static int Check(int key) {
            if (key < 0 || key >= Keys)
                throw new ArgumentOutOfRangeException(nameof(key), key, "A patch describes keys 0..127.");
            return key;
        }
    }
}
