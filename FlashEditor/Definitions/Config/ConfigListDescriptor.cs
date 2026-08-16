using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>One config record as the editor's list shows it, whatever family it belongs to.</summary>
    public sealed class ConfigListing {
        /// <summary>Binds a decoded record to the address it was read from.</summary>
        /// <param name="address">Where the file lives.</param>
        /// <param name="sizeBytes">The stored payload length, before compression.</param>
        /// <param name="record">The decoded record.</param>
        public ConfigListing(DefinitionAddress address, int sizeBytes, ConfigRecord record) {
            Address = address;
            SizeBytes = sizeBytes;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The definition id, which for index 2 is the file id within the family's group.</summary>
        public int FileId => Address.FileId;

        /// <summary>The decoded payload length.</summary>
        public int SizeBytes { get; }

        /// <summary>The decoded record.</summary>
        public ConfigRecord Record { get; private set; }

        /// <summary>How many opcodes the record carried, counting a repeated one each time.</summary>
        public int OpcodeCount => Record.Opcodes.Count;

        /// <summary>
        ///     Rebuilds the description after an edit has changed the decoded record in place.
        /// </summary>
        /// <remarks>
        ///     The decoded record itself is not replaced - the field editors close over it, and
        ///     swapping it would leave every open editor writing into an object nothing reads. What
        ///     is rebuilt is the summary, the field list and the opcode list, all of which were
        ///     rendered when the row was read and are stale the moment a field changes.
        /// </remarks>
        /// <param name="family">The family this row belongs to.</param>
        public void Refresh(ConfigFamily family) {
            if (family == null)
                throw new ArgumentNullException(nameof(family));
            if (Record.Definition == null || !family.CanEncode)
                return;

            Record = family.Describe(Record.Definition);
        }
    }

    /// <summary>
    ///     One group of index 2 presented as a list of records.
    /// </summary>
    /// <remarks>
    ///     Scoped to a group rather than to the index, because a group in index 2 <b>is</b> the record
    ///     type - thirty-five unrelated families share the index and nothing arithmetic relates a
    ///     definition id to a group, which is why index 2 has no row in
    ///     <see cref="CacheAddressing.TryGetFor"/>. Listing the whole index at once would put varplayers
    ///     and cursors in the same grid under the same headings.
    ///     <para>
    ///     The columns are family-independent on purpose. What differs per family is the
    ///     <see cref="ConfigRecord.Summary"/> and the field list behind it, which the panel shows in a
    ///     detail pane; keeping the grid itself uniform is what lets one descriptor serve all
    ///     thirty-five groups, including the ones with no codec at all.
    ///     </para>
    ///     <para>
    ///     <b>Every column here stays read only, and the descriptor is editable anyway.</b> A column
    ///     is an address, a count, or a rendering of the whole record - not one of them is a single
    ///     field an edit could be written back through, and a cell editor over "rgb 0x3C1E0A, texture
    ///     41, priority 8" would have to parse its own summary back into three opcodes.
    ///     <see cref="IsEditable"/> is what lets the panel re-encode a row; the editing surface is
    ///     <c>ConfigEditorPanel</c>'s field pane, which shows one field per line.
    ///     </para>
    /// </remarks>
    public sealed class ConfigListDescriptor : DefinitionListDescriptor<ConfigListing> {
        private readonly ConfigFamily family;
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists one config family.</summary>
        /// <remarks>
        ///     The family is a constructor argument rather than a mutable property because
        ///     <c>DefinitionListPanel.Bind</c> keys on descriptor identity - a rebind of the same
        ///     instance is a deliberate no-op, so a panel that swapped families in place would go on
        ///     showing the previous group's records.
        /// </remarks>
        /// <param name="family">The family to list.</param>
        public ConfigListDescriptor(ConfigFamily family) {
            this.family = family ?? throw new ArgumentNullException(nameof(family));
            columns = BuildColumns(family);
        }

        /// <summary>
        ///     The uniform columns, plus a thumbnail, a swatch and a texture link for the families
        ///     that have them.
        /// </summary>
        /// <remarks>
        ///     The grid stays family-independent for the thirty-odd families that store none of the
        ///     three, so one descriptor still serves all thirty-five groups. What changes is that the
        ///     two floor families stop presenting their colour only as the six hex digits inside a
        ///     summary string: those two are the source of every material in the game world, and
        ///     "which id is the lava" is not a question a column of numbers can answer. The four
        ///     sprite-naming families - cursors, map scene icons, world map elements and damage marks
        ///     - get the same treatment for the same reason, through
        ///     <see cref="ConfigFamily.Sprite"/>, which also states which sprite a record naming
        ///     several is drawn from.
        ///     <para>
        ///     Built per instance rather than held static, because it now depends on the family.
        ///     That is safe against the panel's identity check - <c>Bind</c> keys on the descriptor
        ///     instance, and there is one instance per family already.
        ///     </para>
        /// </remarks>
        /// <param name="family">The family being listed.</param>
        /// <returns>The columns, left to right.</returns>
        private static IReadOnlyList<DefinitionColumn> BuildColumns(ConfigFamily family) {
            var built = new List<DefinitionColumn> {
                DefinitionColumn.ReadOnly<ConfigListing>("Id", row => row.FileId, 70),
                DefinitionColumn.ReadOnly<ConfigListing>("Opcodes", row => row.OpcodeCount, 80)
            };

            if (family.Sprite != null) {
                built.Add(DefinitionColumn.Thumbnail<ConfigListing>("Sprite", RSConstants.SPRITES_INDEX,
                    row => row.Record.Definition is object record ? family.Sprite(record) : null));
            }

            if (family.Colour != null) {
                built.Add(DefinitionColumn.Colour<ConfigListing>("Colour",
                    row => row.Record.Definition is object record ? family.Colour(record) : null));
            }

            if (family.Texture != null) {
                built.Add(DefinitionColumn.Link<ConfigListing>("Texture", RSConstants.TEXTURES,
                    row => row.Record.Definition is object record ? family.Texture(record) : null));
            }

            built.Add(DefinitionColumn.ReadOnly<ConfigListing>("Order", row => row.Record.Order, 190));
            built.Add(DefinitionColumn.ReadOnly<ConfigListing>("Summary", row => row.Record.Summary, 420));
            built.Add(DefinitionColumn.ReadOnly<ConfigListing>("Bytes", row => row.SizeBytes, 70));

            return built;
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CONFIG;

        /// <inheritdoc/>
        public override string RowNoun => family.RowNoun;

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <summary>The family this descriptor lists.</summary>
        public ConfigFamily Family => family;

        /// <summary>
        ///     Every record the family's group declares.
        /// </summary>
        /// <remarks>
        ///     Table-driven, and it has to be: eight of index 2's groups have holes in the middle of
        ///     their id range, so a <c>0..count-1</c> walk would ask for records that do not exist and
        ///     stop short of the ones that do. The definition id is the file id, which is the addressing
        ///     <see cref="ConfigGroup"/> states for every family.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int file in cache.GetFileIds(IndexId, family.GroupId))
                yield return new DefinitionAddress(family.GroupId, file, file);
        }

        /// <inheritdoc/>
        public override ConfigListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            //Read before the decode moves the position, so the column reports the stored length
            //rather than whatever the codec left behind.
            int length = payload.Length;
            return new ConfigListing(address, length, family.Read(address.FileId, payload));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ConfigListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <summary>
        ///     Whether this family's records can be written back.
        /// </summary>
        /// <remarks>
        ///     True for all thirty-five groups the reference table declares, because every one of
        ///     them has a codec - the nineteen with no client provider take
        ///     <see cref="EmptyConfigDefinition"/>, which refuses every opcode and therefore encodes
        ///     back to the bare terminator it read. False only for a group this cache declares that
        ///     the family table does not name, which falls to <see cref="ConfigFamily.Unmodelled"/>
        ///     and has no decoded record to encode.
        /// </remarks>
        public override bool IsEditable => family.CanEncode;

        /// <summary>
        ///     Re-encodes one record.
        /// </summary>
        /// <remarks>
        ///     Through the family, which goes straight to the record class's own <c>Encode</c>. That
        ///     replays the opcode order and the repetition the file was read with and re-derives only
        ///     the last occurrence of each opcode from the live fields, which is what makes an edit
        ///     to one field leave every other byte of a non-canonical record alone.
        /// </remarks>
        /// <param name="row">The row.</param>
        /// <returns>The encoded file.</returns>
        public override JagStream Encode(ConfigListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            object? definition = row.Record.Definition;
            if (definition == null)
                throw new NotSupportedException(
                    "Config group " + family.GroupId + " decoded no record for " + row.Address +
                    ", so there is nothing to write back.");

            return family.Encode(definition);
        }
    }
}
