using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     One font's metrics as a list row.
    /// </summary>
    /// <remarks>
    ///     A font id is a group id on index 13 and on index 8 alike, so this row's id is also the id
    ///     of the 256-glyph sheet that draws it. Nothing here holds pixels; a preview has to join the
    ///     two indexes.
    /// </remarks>
    public sealed class FontListing {
        /// <summary>Binds one decoded font to where it came from and to the name it was recovered as.</summary>
        /// <param name="address">The group and file, and the font id they carry.</param>
        /// <param name="record">The decoded metrics.</param>
        /// <param name="name">The recovered name, or null when it has not been cracked.</param>
        public FontListing(DefinitionAddress address, FontDefinition record, string? name) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
            Name = name;
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded metrics.</summary>
        public FontDefinition Record { get; }

        /// <summary>
        ///     The recovered name, or null.
        /// </summary>
        /// <remarks>
        ///     Only ever a name whose hash matches the stored identifier, so an empty cell means the
        ///     name is unknown rather than absent. See <see cref="FontNames"/>.
        /// </remarks>
        public string? Name { get; }

        /// <summary>The font id, which is its index-13 group id.</summary>
        public int FontId => Address.GroupId;

        /// <summary>Whether the record carries the kerning tables.</summary>
        /// <remarks>
        ///     A column because it changes the record's length, so it is the single most consequential
        ///     thing about a font on this index. No group in either supported cache sets it.
        /// </remarks>
        public string Kerned => Record.IsKerned ? "yes" : "no";

        /// <summary>
        ///     The space character's advance, shown next to the line height because the two are
        ///     routinely confused.
        /// </summary>
        /// <remarks>
        ///     They are unrelated: the four verdana fonts store a line height of 35 against a space
        ///     advance of 4. Showing both makes that visible instead of inviting the reader to assume
        ///     one is the other.
        /// </remarks>
        public int SpaceAdvance => Record.AdvanceOf(FontDefinition.SpaceCharacter);
    }

    /// <summary>
    ///     Index 13 as a definition list: one row per font.
    /// </summary>
    /// <remarks>
    ///     Flat, because the index is - every group holds exactly one file and the group id is the
    ///     font id (<c>Class119_Sub1.java:42</c> reads it through the single-file accessor). The 256
    ///     advance widths are not columns: they belong in a per-character editor beside the index-8
    ///     glyph sheet, where an edited width can actually be seen.
    ///     <para>
    ///     <b>Editable in the scalar fields.</b> Each is an independent stored byte, so writing one
    ///     leaves every other byte of the record alone and the re-encode stays byte-identical
    ///     elsewhere. The line height is refused on a kerned record because there is no byte to
    ///     write it into - the client derives it from the space glyph's profile instead.
    ///     </para>
    /// </remarks>
    public sealed class FontListDescriptor : DefinitionListDescriptor<FontListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every font the index declares.</summary>
        public FontListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<FontListing>("Font", row => row.FontId, 70),
                DefinitionColumn.ReadOnly<FontListing>("Name", row => row.Name, 170),
                DefinitionColumn.ReadOnly<FontListing>("Kerned", row => row.Kerned, 70),
                DefinitionColumn.Number<FontListing>("Line height", row => row.Record.LineHeight,
                    SetLineHeight, 90),
                DefinitionColumn.Number<FontListing>("Ascent", row => (int) row.Record.Ascent,
                    (row, value) => row.Record.Ascent = ToByte(value), 70),
                DefinitionColumn.Number<FontListing>("Descent", row => (int) row.Record.Descent,
                    (row, value) => row.Record.Descent = ToByte(value), 70),
                DefinitionColumn.ReadOnly<FontListing>("Space adv", row => row.SpaceAdvance, 80),
                DefinitionColumn.Number<FontListing>("Byte 259", row => (int) row.Record.UnusedByte259,
                    (row, value) => row.Record.UnusedByte259 = ToByte(value), 80),
                DefinitionColumn.Number<FontListing>("Byte 260", row => (int) row.Record.UnusedByte260,
                    (row, value) => row.Record.UnusedByte260 = ToByte(value), 80)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.FONTS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "font";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override FontListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            var record = new FontDefinition { Id = address.GroupId };
            record.Decode(payload);
            return new FontListing(address, record, FontNames.NameOf(cache, address.GroupId));
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(FontListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(FontListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>
        ///     Applies a line-height edit, or leaves a kerned record alone.
        /// </summary>
        /// <remarks>
        ///     A kerned record stores no line-height byte at all, so there is nothing to write and
        ///     the cell is not an editable field of that layout. Swallowing the edit is right where
        ///     throwing out of a grid callback is not - the value shown is still the derived one, so
        ///     the cell reverts and says so.
        /// </remarks>
        /// <param name="row">The row being edited.</param>
        /// <param name="value">The new line height.</param>
        private static void SetLineHeight(FontListing row, int value) {
            if (row.Record.IsKerned)
                return;
            row.Record.LineHeight = ToByte(value);
        }

        private static byte ToByte(int value) {
            return (byte) Math.Clamp(value, byte.MinValue, byte.MaxValue);
        }
    }
}
