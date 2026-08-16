using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     Index 15, the MIDI patch bank, as a definition list.
    /// </summary>
    /// <remarks>
    ///     One file per group and the group id is the patch id, which is what
    ///     <c>Class355.method3875</c> (<c>Class355.java:15-19</c>) relies on: it fetches a patch
    ///     through the single-file accessor with no arithmetic in between, so a program number is a
    ///     group id outright.
    ///     <para>
    ///     <b>Every column but the id is derived rather than stored.</b> Index 15 has no name hashes,
    ///     so the instrument name is a General MIDI lookup on the id, and the key census is the
    ///     pinned per-key accessors walked once at decode. Nothing in the file says what any of it
    ///     means.
    ///     </para>
    ///     <para>
    ///     <b>Editable, but only the whole-patch volume.</b> The codec re-encodes all 176 patches
    ///     byte for byte, so writing one back is safe; the rest of the record is run-length planes
    ///     whose lengths are decided by each other, and a grid cell is the wrong surface for editing
    ///     one. The keyboard beside the list is where the per-key work belongs when it is written.
    ///     </para>
    /// </remarks>
    public sealed class MidiPatchListDescriptor : DefinitionListDescriptor<MidiPatchListing> {
        /// <inheritdoc/>
        public override int IndexId => RSConstants.MIDI_PATCH_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "patch";

        /// <summary>
        ///     Whether a cell edit may be committed.
        /// </summary>
        /// <remarks>
        ///     True because <see cref="Encode"/> is the codec's own encoder, which
        ///     <c>RealCacheMidiPatchTests</c> pins byte for byte over every declared patch in both
        ///     caches. Only the columns that carry a setter can actually be edited.
        /// </remarks>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns { get; } = new[] {
            DefinitionColumn.Number<MidiPatchListing>("Patch", row => row.Id, width: 70),
            DefinitionColumn.Text<MidiPatchListing>("Instrument", row => row.Name, width: 220),
            DefinitionColumn.Text<MidiPatchListing>("Family", row => row.Family, width: 130),
            DefinitionColumn.Number<MidiPatchListing>("Keys", row => row.SoundingKeys, width: 70),
            DefinitionColumn.Number<MidiPatchListing>("Index 14", row => row.VorbisKeys, width: 80),
            /* Its own column rather than a footnote: these are the keys the player cannot render, so
               a patch with a non-zero count here plays every note but those and the gap would
               otherwise read as a decode fault. */
            DefinitionColumn.Number<MidiPatchListing>("Index 4", row => row.EffectKeys, width: 80),
            DefinitionColumn.Number<MidiPatchListing>("Held", row => row.HeldKeys, width: 70),
            DefinitionColumn.Number<MidiPatchListing>("Mute groups", row => row.MuteGroups, width: 100),
            DefinitionColumn.Number<MidiPatchListing>("Envelopes", row => row.Envelopes, width: 90),
            DefinitionColumn.Number<MidiPatchListing>("Volume", row => row.PatchVolume,
                (row, value) => row.PatchVolume = value, 80)
        };

        /// <inheritdoc/>
        public override MidiPatchListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            //The group id is the patch id, so it is taken from the address rather than from the file,
            //which carries no id of its own.
            var patch = new MidiPatchDefinition { Id = address.GroupId }.Decode(payload);
            return new MidiPatchListing(address, patch);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(MidiPatchListing row) {
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(MidiPatchListing row) {
            //The expanded keys are a view of the patch, so they have to be dropped when it changes.
            row.Invalidate();
            return row.Patch.Encode();
        }
    }
}
