using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Audio.Sfx2 {
    /// <summary>
    ///     One index-14 group as a list row: either a sound effect or the shared setup header.
    /// </summary>
    /// <remarks>
    ///     Both shapes share a row type because they share the index and the id space, and hiding
    ///     group 0 would misrepresent the index as 3,656 samples when the reference table declares
    ///     one more group than that. Its sample columns are empty because it has none.
    /// </remarks>
    public sealed class Sfx2Listing {
        /// <summary>Binds one decoded group to where it came from.</summary>
        /// <param name="address">The group and file, and the sound-effect id they carry.</param>
        /// <param name="record">The decoded group.</param>
        public Sfx2Listing(DefinitionAddress address, Sfx2Entry record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded group.</summary>
        public Sfx2Entry Record { get; }

        /// <summary>The sample record, or null when this row is the setup header.</summary>
        public Sfx2Sample? Sample => Record as Sfx2Sample;

        /// <summary>The setup header, or null when this row is a sample.</summary>
        public Sfx2SetupHeader? Setup => Record as Sfx2SetupHeader;

        /// <summary>The sound-effect id, which is the group id.</summary>
        public int SoundId => Address.GroupId;

        /// <summary>Which of the index's two record shapes this row holds.</summary>
        public string Kind => Setup != null ? "setup header" : "sample";

        /// <summary>Playback rate in Hz, or nothing for the setup header.</summary>
        public object? SampleRate => Sample?.SampleRate;

        /// <summary>Decoded PCM size in bytes, or nothing for the setup header.</summary>
        public object? PcmByteCount => Sample?.PcmByteCount;

        /// <summary>Loop start in PCM bytes, or nothing for the setup header.</summary>
        public object? LoopStart => Sample?.LoopStart;

        /// <summary>Loop end in PCM bytes, or nothing for the setup header.</summary>
        public object? LoopEnd => Sample?.LoopEnd;

        /// <summary>Whether playback loops, or nothing for the setup header.</summary>
        public string Looping => Sample == null ? "" : Sample.IsLooping ? "yes" : "no";

        /// <summary>How many Vorbis packets the record holds, or nothing for the setup header.</summary>
        public object? PacketCount => Sample?.PacketCount;

        /// <summary>
        ///     Vorbis payload in bytes, excluding the header and the length prefixes, or nothing for
        ///     the setup header.
        /// </summary>
        /// <remarks>
        ///     Not the stored file size, and deliberately not labelled as one. The prefixes are a
        ///     byte or more each and the header is another twenty, so the two differ by a few
        ///     hundred bytes on a typical record - close enough that a column claiming to be the
        ///     file size would be believed.
        /// </remarks>
        public object? AudioBytes => Sample?.PacketByteCount;

        /// <summary>
        ///     What the setup header declares, and nothing for a sample.
        /// </summary>
        /// <remarks>
        ///     The only place in the editor where group 0 says anything about itself. The sync
        ///     pattern is called out because a missing one would mean this group is not what the
        ///     client believes it is, and every sample on the index decodes against it.
        /// </remarks>
        public string Detail {
            get {
                Sfx2SetupHeader? setup = Setup;
                if (setup == null)
                    return "";

                string sync = setup.HasCodebookSyncPattern
                    ? "codebook sync ok"
                    : "codebook sync MISSING (0x" + setup.FirstCodebookSync.ToString("X", CultureInfo.InvariantCulture) + ")";

                return setup.RawBytes.Length + " bytes, blocksize " + setup.Blocksize0 + "/" +
                       setup.Blocksize1 + ", " + setup.CodebookCount + " codebooks, " + sync;
            }
        }
    }

    /// <summary>
    ///     Index 14 as a definition list: one row per group.
    /// </summary>
    /// <remarks>
    ///     Flat, because the index is - every group holds exactly one file and the group id is the
    ///     sound-effect id. The table sets no identifiers flag on this index in either cache, so
    ///     there is no name to recover and a row is addressable only by number.
    ///     <para>
    ///     <b>Editable in the playback fields only.</b> Rate and the two loop points are independent
    ///     stored int32s, so writing one leaves every other byte of the record alone. The PCM byte
    ///     count and the packets are not editable here: the first sizes the client's output buffer
    ///     for audio this editor cannot decode, and the second is the audio itself, which belongs to
    ///     an import path rather than to a grid cell.
    ///     </para>
    ///     <para>
    ///     The setup-header row is read only in effect - it exposes no editable field - and its
    ///     encoder writes the bytes back verbatim.
    ///     </para>
    /// </remarks>
    public sealed class Sfx2ListDescriptor : DefinitionListDescriptor<Sfx2Listing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every group the index declares.</summary>
        public Sfx2ListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<Sfx2Listing>("Sound", row => row.SoundId, 70),
                DefinitionColumn.ReadOnly<Sfx2Listing>("Kind", row => row.Kind, 100),
                DefinitionColumn.Number<Sfx2Listing>("Rate", row => row.SampleRate,
                    (row, value) => Apply(row, sample => sample.SampleRate = value), 70),
                DefinitionColumn.ReadOnly<Sfx2Listing>("PCM bytes", row => row.PcmByteCount, 90),
                DefinitionColumn.Number<Sfx2Listing>("Loop start", row => row.LoopStart,
                    (row, value) => Apply(row, sample => sample.LoopStart = value), 90),
                DefinitionColumn.Number<Sfx2Listing>("Loop end", row => row.LoopEnd,
                    (row, value) => Apply(row, sample => sample.LoopEnd = value), 90),
                DefinitionColumn.Text<Sfx2Listing>("Looping", row => row.Looping, SetLooping, 80),
                DefinitionColumn.ReadOnly<Sfx2Listing>("Packets", row => row.PacketCount, 70),
                DefinitionColumn.ReadOnly<Sfx2Listing>("Audio bytes", row => row.AudioBytes, 90),
                DefinitionColumn.ReadOnly<Sfx2Listing>("Detail", row => row.Detail, 260)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.SFX2_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "sound effect";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override Sfx2Listing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            return new Sfx2Listing(address, Sfx2Entry.Decode(address.GroupId, payload));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(Sfx2Listing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(Sfx2Listing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>Applies an edit to a sample row, and does nothing on the setup-header row.</summary>
        /// <remarks>
        ///     The setup header has no such field to write, so swallowing the edit reverts the cell
        ///     rather than inventing one. Throwing out of a grid callback would take the form down.
        /// </remarks>
        /// <param name="row">The row being edited.</param>
        /// <param name="edit">What to change on the sample.</param>
        private static void Apply(Sfx2Listing row, Action<Sfx2Sample> edit) {
            Sfx2Sample? sample = row.Sample;
            if (sample != null)
                edit(sample);
        }

        /// <summary>
        ///     Applies a looping edit, or leaves the record alone when the cell says neither.
        /// </summary>
        /// <remarks>
        ///     Strict rather than truthy, because the flag is not stored on its own: it is the sign
        ///     of the loop-end int32, so a wrong guess rewrites that field to its complement.
        /// </remarks>
        /// <param name="row">The row being edited.</param>
        /// <param name="text">The cell's text.</param>
        private static void SetLooping(Sfx2Listing row, string text) {
            string trimmed = (text ?? string.Empty).Trim();

            if (trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                trimmed == "1")
                Apply(row, sample => sample.IsLooping = true);
            else if (trimmed.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                     trimmed == "0")
                Apply(row, sample => sample.IsLooping = false);
        }
    }
}
