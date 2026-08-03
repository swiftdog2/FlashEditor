using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Audio {
    /// <summary>
    ///     One sound effect as a list row: what it is made of, and where it came from.
    /// </summary>
    /// <remarks>
    ///     Wraps the definition rather than deriving from it so the address survives the round trip.
    ///     Index 4 is <c>GroupPerId</c> and its file id is declared by the reference table rather than
    ///     computed, so a row that kept only the effect id could not say which file to write back to
    ///     (<c>CacheAddressing.FileOf</c> refuses to answer for this shape, and rightly - index 23
    ///     proves the file id is not always 0).
    /// </remarks>
    public sealed class SoundEffectListing {
        /// <summary>Binds one decoded effect to the address it was read from.</summary>
        /// <param name="address">Where the file lives.</param>
        /// <param name="effect">The decoded effect.</param>
        /// <param name="sizeBytes">The stored payload length, before compression.</param>
        public SoundEffectListing(DefinitionAddress address, SoundEffectDefinition effect, int sizeBytes) {
            Address = address;
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
            SizeBytes = sizeBytes;
        }

        /// <summary>Where the effect lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded effect, which is what an edit changes.</summary>
        public SoundEffectDefinition Effect { get; }

        /// <summary>The effect id, which is the group id.</summary>
        public int EffectId => Effect.Id;

        /// <summary>The decoded payload length.</summary>
        public int SizeBytes { get; }

        /// <summary>How many of the ten slots hold a tone.</summary>
        public int Tones => Effect.ToneCount;

        /// <summary>Which slots hold them, so a gap is visible in the list.</summary>
        /// <remarks>
        ///     Shown because the gap is real structure rather than a rendering detail: 1884 of the
        ///     effects in this cache leave a slot empty in the middle, and an editor that silently
        ///     compacted them would rewrite every one.
        /// </remarks>
        public string Slots => string.Join(",", Effect.OccupiedSlots);

        /// <summary>Partials across every tone.</summary>
        public int Harmonics => Effect.Tones.OfType<SoundEffectTone>().Sum(tone => tone.Harmonics.Count);

        /// <summary>Tones carrying a filter block.</summary>
        public int Filters => Effect.Tones.OfType<SoundEffectTone>().Count(tone => tone.Filter.IsPresent);

        /// <summary>Where the loop begins, in milliseconds.</summary>
        public int LoopStart {
            get => Effect.LoopStart;
            set => Effect.LoopStart = value;
        }

        /// <summary>Where the loop ends, in milliseconds.</summary>
        public int LoopEnd {
            get => Effect.LoopEnd;
            set => Effect.LoopEnd = value;
        }

        /// <summary>Whether the effect loops, by the client's own test.</summary>
        public bool Loops => Effect.Loops;

        /// <summary>The longest tone's end, in milliseconds - what the mixdown allocates for.</summary>
        /// <remarks><c>Class37.java:76-81</c> takes the maximum of offset plus duration across the slots.</remarks>
        public int LengthMs => Effect.Tones
            .OfType<SoundEffectTone>()
            .Select(tone => tone.Offset + tone.Duration)
            .DefaultIfEmpty(0)
            .Max();
    }

    /// <summary>
    ///     Presents index 4 as one row per sound effect.
    /// </summary>
    /// <remarks>
    ///     Editable, because <see cref="SoundEffectDefinition.Encode"/> reproduces the stored bytes of
    ///     every one of the 10,238 records in this cache. Only the loop window is editable in the
    ///     cells: it is two independent milliseconds and nothing else in the record depends on either.
    ///     The rest of a patch is nested three deep and belongs on a detail pane rather than a grid.
    ///     <para>
    ///     Rows come from the reference table, so the orphan group the idx still holds - id 4787,
    ///     which the table does not declare - is not listed. That is the same view the client has:
    ///     it resolves every group through the table, so a group the table omits cannot be loaded at
    ///     all.
    ///     </para>
    /// </remarks>
    public sealed class SoundEffectListDescriptor : DefinitionListDescriptor<SoundEffectListing> {
        private static readonly IReadOnlyList<DefinitionColumn> SoundEffectColumns = new[] {
            DefinitionColumn.ReadOnly<SoundEffectListing>("Id", row => row.EffectId, 70),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Tones", row => row.Tones, 60),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Slots", row => row.Slots, 100),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Harmonics", row => row.Harmonics, 90),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Filters", row => row.Filters, 70),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Length ms", row => row.LengthMs, 90),
            DefinitionColumn.Number<SoundEffectListing>("Loop from", row => row.LoopStart,
                (row, value) => row.LoopStart = value, 90),
            DefinitionColumn.Number<SoundEffectListing>("Loop to", row => row.LoopEnd,
                (row, value) => row.LoopEnd = value, 90),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Loops", row => row.Loops, 60),
            DefinitionColumn.ReadOnly<SoundEffectListing>("Bytes", row => row.SizeBytes, 70)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.SOUND_EFFECTS;

        /// <inheritdoc/>
        public override string RowNoun => "sound effect";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => SoundEffectColumns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override SoundEffectListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            int length = payload.Length;
            var effect = new SoundEffectDefinition { Id = address.GroupId }.Decode(payload);
            return new SoundEffectListing(address, effect, length);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(SoundEffectListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(SoundEffectListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Effect.Encode();
        }
    }
}
