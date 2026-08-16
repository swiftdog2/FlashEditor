using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.Cache;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Where one row of a definition list lives in the cache.
    /// </summary>
    /// <remarks>
    ///     The (group, file) pair is carried rather than recomputed because not every index can
    ///     recompute it. A paged index derives the pair from the definition id through
    ///     <see cref="CacheAddressing"/>, but index 2 has no established id arithmetic at all - it is
    ///     thirty-five unrelated config families sharing one index - so there the pair <i>is</i> the
    ///     identity and <see cref="DefinitionId"/> is -1. A panel that
    ///     assumed an id could always be folded back into an address would have to invent a split
    ///     for every index whose split is not yet known, which is exactly the guess
    ///     <see cref="CacheAddressing"/> exists to refuse.
    /// </remarks>
    public readonly struct DefinitionAddress : IEquatable<DefinitionAddress> {
        /// <summary>The address of a file, and the definition id it carries where one is derivable.</summary>
        /// <param name="groupId">The group (archive) id within the index.</param>
        /// <param name="fileId">The file id within that group.</param>
        /// <param name="definitionId">The definition id, or -1 when the index has no id arithmetic.</param>
        public DefinitionAddress(int groupId, int fileId, int definitionId = -1) {
            if (groupId < 0)
                throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "Group ids are non-negative.");
            if (fileId < 0)
                throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "File ids are non-negative.");

            GroupId = groupId;
            FileId = fileId;
            DefinitionId = definitionId;
        }

        /// <summary>The group (archive) that stores the row.</summary>
        public int GroupId { get; }

        /// <summary>The file within that group.</summary>
        public int FileId { get; }

        /// <summary>
        ///     The definition id, or -1 when the index has no established group/file split.
        /// </summary>
        /// <remarks>
        ///     -1 rather than a fabricated <c>group * 256 + file</c>. A made-up id reads as a real
        ///     one everywhere downstream, and the first thing that folds it back into an address
        ///     would name a different file.
        /// </remarks>
        public int DefinitionId { get; }

        /// <summary>Whether this index derives a definition id from the address at all.</summary>
        public bool HasDefinitionId => DefinitionId >= 0;

        /// <inheritdoc/>
        public bool Equals(DefinitionAddress other) {
            return GroupId == other.GroupId && FileId == other.FileId && DefinitionId == other.DefinitionId;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) {
            return obj is DefinitionAddress other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode() {
            return HashCode.Combine(GroupId, FileId, DefinitionId);
        }

        /// <summary>The address in words, for logs and error messages.</summary>
        /// <returns>The group and file, and the definition id when there is one.</returns>
        public override string ToString() {
            return HasDefinitionId
                ? "group " + GroupId + ", file " + FileId + " (id " + DefinitionId + ")"
                : "group " + GroupId + ", file " + FileId;
        }
    }

    /// <summary>
    ///     One column of a definition list: its header, how to read it off a row, and how to write
    ///     it back when the column is editable.
    /// </summary>
    /// <remarks>
    ///     Deliberately a delegate pair rather than a property name. Reflection by name reads
    ///     whatever happens to be there, so a renamed field silently blanks a column; a delegate
    ///     stops compiling. It also lets a column show something the row does not store as a
    ///     property, which every raw listing needs.
    /// </remarks>
    public sealed class DefinitionColumn {
        private DefinitionColumn(string header, int width, Func<object, object?> read,
            Action<object, object?>? write, Func<object, DefinitionCellVisual>? visual = null) {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Width = width;
            Read = read ?? throw new ArgumentNullException(nameof(read));
            Write = write;
            Visual = visual;
        }

        /// <summary>The column heading.</summary>
        public string Header { get; }

        /// <summary>
        ///     The column width, in the panel's own font.
        /// </summary>
        /// <remarks>
        ///     The form scales by font ratio (<c>AutoScaleMode.Font</c>), so a width stated here
        ///     only holds because <c>DefinitionListPanel</c> pins the list's font rather than
        ///     inheriting the tab control's.
        /// </remarks>
        public int Width { get; }

        /// <summary>Reads the displayed value off a row.</summary>
        public Func<object, object?> Read { get; }

        /// <summary>Writes an edited value back into a row, or null when the column is read only.</summary>
        public Action<object, object?>? Write { get; }

        /// <summary>Whether this column can be edited in place.</summary>
        public bool IsEditable => Write != null;

        /// <summary>
        ///     What this column's cell draws besides its text, or null for text only.
        /// </summary>
        /// <remarks>
        ///     Null rather than a delegate that returns <see cref="DefinitionCellArt.None"/>, so
        ///     the panel can skip attaching a renderer at all for the columns that want the grid's
        ///     default. A column that renders costs a renderer instance and a second delegate call
        ///     per paint, and one that does not should cost neither.
        /// </remarks>
        public Func<object, DefinitionCellVisual>? Visual { get; }

        /// <summary>Whether activating a cell in this column means anything.</summary>
        public bool IsActivatable => Visual != null;

        /// <summary>
        ///     A packed <c>0xRRGGBB</c> colour, shown as a swatch with its hex kept beside it.
        /// </summary>
        /// <remarks>
        ///     <paramref name="read"/> returns null for "this record stores no colour", which is a
        ///     real state and not the same as black -
        ///     <see cref="Config.FloorOverlayDefinition"/> distinguishes the two because absent and
        ///     <c>0x000000</c> are different bytes. A swatch drawn for an absent colour would
        ///     assert a value the file does not carry, so it draws nothing.
        ///     <para>
        ///     The hex stays in the cell text. Item 18 requires the number to remain available, and
        ///     it is also what keeps sorting, filtering and cell editing working - all three read
        ///     the aspect and none of them know a renderer exists.
        ///     </para>
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the packed colour off a row, or null when it stores none.</param>
        /// <param name="write">Writes an edited colour back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Colour<TRow>(string header, Func<TRow, int?> read,
            Action<TRow, int>? write = null, int width = 110) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? Hex(read(typed)) : null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToColourInt(value));
                },
                row => Cast<TRow>(row) is TRow typed && read(typed) is int rgb
                    ? DefinitionCellVisual.Swatch(rgb)
                    : DefinitionCellVisual.None);
        }

        /// <summary>
        ///     A colour the cache stores in its own encoding, shown as that encoding with a swatch of
        ///     the colour it resolves to.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="Colour{TRow}"/> because the two read different numbers.
        ///     <c>Colour</c>'s cell text <em>is</em> the packed <c>0xRRGGBB</c>, so editing it writes
        ///     the same number back. Several indexes instead store 16-bit RS HSL, whose RGB is derived
        ///     through a palette lookup that is not invertible - so the editable number has to stay
        ///     the stored one and the swatch has to be told the resolved colour separately. Showing
        ///     RGB in the cell and parsing it back would store a different colour than the one on
        ///     screen and report nothing.
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="stored">Reads the stored value off a row, or null when it stores none.</param>
        /// <param name="resolved">Reads the <c>0xRRGGBB</c> the stored value resolves to.</param>
        /// <param name="write">Writes an edited stored value back, or null for a read-only column.</param>
        /// <param name="digits">Hexadecimal digits the stored encoding occupies.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn EncodedColour<TRow>(string header, Func<TRow, int?> stored,
            Func<TRow, int?> resolved, Action<TRow, int>? write = null, int digits = 4, int width = 110)
            where TRow : class {
            string format = "X" + digits.ToString(CultureInfo.InvariantCulture);

            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed && stored(typed) is int value
                    ? "0x" + value.ToString(format, CultureInfo.InvariantCulture)
                    : null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToColourInt(value));
                },
                row => Cast<TRow>(row) is TRow typed && resolved(typed) is int rgb
                    ? DefinitionCellVisual.Swatch(rgb)
                    : DefinitionCellVisual.None);
        }

        /// <summary>
        ///     A cell whose text is already written out, with a swatch of whatever colour it
        ///     describes.
        /// </summary>
        /// <remarks>
        ///     The third of the swatch factories, and the one for a <b>detail pane</b> rather than a
        ///     list. <see cref="Colour{TRow}"/> and <see cref="EncodedColour{TRow}"/> both own their
        ///     cell text, because in a list the cell is the number and editing it edits the record.
        ///     A detail pane's value column holds one rendered sentence per field - a transparency
        ///     with its inversion spelled out, an outline with "none" beside a zero - and it is the
        ///     record that decided that wording, not the column.
        ///     <para>
        ///     <b>Read only by construction.</b> There is nothing to parse back: the text is prose,
        ///     so a cell editor over it could only fail. A pane using this offers a picker instead,
        ///     which is also the only way to give a colour to a field that currently stores none -
        ///     the list columns cannot, because a stored zero that means "no outline" draws no swatch
        ///     and so has nothing to activate.
        ///     </para>
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the rendered text off a row.</param>
        /// <param name="swatch">Reads the colour to draw, or null when the row describes none.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Swatched<TRow>(string header, Func<TRow, string?> read,
            Func<TRow, int?> swatch, int width = 160) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? read(typed) : null,
                null,
                row => Cast<TRow>(row) is TRow typed && swatch(typed) is int rgb
                    ? DefinitionCellVisual.Swatch(rgb)
                    : DefinitionCellVisual.None);
        }

        /// <summary>An id naming a picture in another index, shown as a tile with the id beside it.</summary>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="read">Reads the id off a row, or null when it names nothing.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Thumbnail<TRow>(string header, int indexId,
            Func<TRow, int?> read, int width = 120) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? (object?) read(typed) : null,
                null,
                row => Cast<TRow>(row) is TRow typed && read(typed) is int id && id >= 0
                    ? DefinitionCellVisual.Thumbnail(indexId, id)
                    : DefinitionCellVisual.None);
        }

        /// <summary>
        ///     An id naming a record in another index, shown as something the user can follow.
        /// </summary>
        /// <remarks>
        ///     The column says which index the number addresses and nothing else. What following it
        ///     <i>does</i> is the host's decision, taken from
        ///     <c>DefinitionListPanel.CellActivated</c>.
        ///     <para>
        ///     <b>A link may still be edited.</b> Several of the measured joins sit on fields that
        ///     were already editable cells, and turning one into a read-only link to make it
        ///     followable would take an edit away to add a jump. The panel is what keeps the two
        ///     apart: a plain click follows a read-only link, and an editable one wants Ctrl, because
        ///     the first click of the double click that starts an edit would otherwise navigate away
        ///     before the second landed.
        ///     </para>
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="indexId">The index the id addresses.</param>
        /// <param name="read">Reads the id off a row, or null when it names nothing.</param>
        /// <param name="write">Writes an edited id back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Link<TRow>(string header, int indexId,
            Func<TRow, int?> read, Action<TRow, int>? write = null, int width = 90) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? (object?) read(typed) : null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToInt(value));
                },
                row => Cast<TRow>(row) is TRow typed && read(typed) is int id && id >= 0
                    ? DefinitionCellVisual.Link(indexId, id)
                    : DefinitionCellVisual.None);
        }

        /// <summary>
        ///     An id naming a record in one group of index 2, shown as something the user can follow.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="Link{TRow}"/> because an index 2 id is not a place until a
        ///     group is named with it: the index is thirty-five unrelated families sharing one index
        ///     and has no id arithmetic, so id 12 is a quest, a map scene icon and a parameter type
        ///     at once. Four of the measured joins land there - the two floor families, quests, map
        ///     scene icons, map elements and parameter types - and every one of them would resolve
        ///     to a different record if the group were dropped.
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="configGroup">The group within index 2 the id is a file of.</param>
        /// <param name="read">Reads the id off a row, or null when it names nothing.</param>
        /// <param name="write">Writes an edited id back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn ConfigLink<TRow>(string header, int configGroup,
            Func<TRow, int?> read, Action<TRow, int>? write = null, int width = 90) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? (object?) read(typed) : null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToInt(value));
                },
                row => Cast<TRow>(row) is TRow typed && read(typed) is int id && id >= 0
                    ? DefinitionCellVisual.ConfigLink(configGroup, id)
                    : DefinitionCellVisual.None);
        }

        /// <summary>A packed colour as the hex the cache stores it in, or null.</summary>
        private static object? Hex(int? packed) {
            return packed.HasValue ? "0x" + packed.Value.ToString("X6", CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        ///     Whatever the cell editor produced, as a packed colour.
        /// </summary>
        /// <remarks>
        ///     Separate from <see cref="ToInt"/> because this column's text is hexadecimal.
        ///     <c>Convert.ToInt32</c> on "0x3C1E0A" throws, and on a bare "3C1E0A" it would read
        ///     decimal and silently store a different colour - which is worse, because the swatch
        ///     would then show the wrong thing rather than reporting a failure.
        /// </remarks>
        /// <param name="value">The editor's value.</param>
        /// <returns>The packed colour.</returns>
        private static int ToColourInt(object? value) {
            if (value == null)
                return 0;

            if (value is string text) {
                string trimmed = text.Trim();
                if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed.Substring(2);
                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                    trimmed = trimmed.Substring(1);

                return int.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>A column that shows a value and cannot be edited.</summary>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn ReadOnly<TRow>(string header, Func<TRow, object?> read, int width = 90)
            where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? read(typed) : null, null);
        }

        /// <summary>A text column, editable when a setter is supplied.</summary>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited string back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Text<TRow>(string header, Func<TRow, string?> read,
            Action<TRow, string>? write = null, int width = 160) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? read(typed) : null,
                write == null ? null : (row, value) => {
                    //A commit against a recycled row is dropped rather than written to the wrong one.
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, value?.ToString() ?? string.Empty);
                });
        }

        /// <summary>A boolean column, editable when a setter is supplied.</summary>
        /// <remarks>
        ///     Separate from <see cref="Number{TRow}"/> because the cell editor for a boolean hands
        ///     back whatever its editor produced - a checkbox yields a <c>bool</c>, an in-place text
        ///     box the strings the user typed - and a setter that cast either way directly would
        ///     throw on the editor it was not written for. The same reason <see cref="Number{TRow}"/>
        ///     converts rather than casts.
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited flag back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Flag<TRow>(string header, Func<TRow, bool> read,
            Action<TRow, bool>? write = null, int width = 90) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? read(typed) : (object?) null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToBool(value));
                });
        }

        /// <summary>An integer column, editable when a setter is supplied.</summary>
        /// <remarks>
        ///     The conversion is here rather than in every setter because the cell editor decides
        ///     the type it hands back - a <c>NumericUpDown</c> yields a <c>decimal</c>, a text box a
        ///     <c>string</c> - and a setter that cast directly would throw on whichever editor it
        ///     was not written for.
        /// </remarks>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="write">Writes an edited number back, or null for a read-only column.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn Number<TRow>(string header, Func<TRow, object?> read,
            Action<TRow, int>? write = null, int width = 90) where TRow : class {
            return new DefinitionColumn(header, width,
                row => Cast<TRow>(row) is TRow typed ? read(typed) : null,
                write == null ? null : (row, value) => {
                    if (Cast<TRow>(row) is TRow typed)
                        write(typed, ToInt(value));
                });
        }

        private static int ToInt(object? value) {
            if (value == null)
                return 0;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Whatever the cell editor produced, as a flag.
        /// </summary>
        /// <remarks>
        ///     A blank cell reads as false rather than throwing: an in-place text box hands back an
        ///     empty string when the user clears it, and <c>Convert.ToBoolean</c> refuses that.
        /// </remarks>
        /// <param name="value">The editor's value.</param>
        /// <returns>The flag.</returns>
        private static bool ToBool(object? value) {
            if (value == null)
                return false;

            if (value is string text) {
                string trimmed = text.Trim();
                return bool.TryParse(trimmed, out bool parsed) ? parsed : trimmed == "1";
            }

            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     The row as its expected type, or <c>null</c> when there is no row.
        /// </summary>
        /// <remarks>
        ///     A null row is a legitimate state, not a defect: ObjectListView evaluates aspect
        ///     getters for rows that are being recycled during a scroll and for cells it is
        ///     measuring before the model is attached. Throwing there surfaced as an
        ///     ArgumentException while simply scrolling a list. The caller renders an empty cell
        ///     instead.
        ///
        ///     A row of the WRONG type still throws, because that can only mean a descriptor wired
        ///     its columns to a different row type than it produces, and silently blanking those
        ///     cells would hide it.
        /// </remarks>
        private static TRow? Cast<TRow>(object? row) where TRow : class {
            if (row == null)
                return null;

            return row as TRow ?? throw new ArgumentException(
                "This column reads a " + typeof(TRow).Name + " but was handed a " +
                row.GetType().Name + ".", nameof(row));
        }
    }

    /// <summary>
    ///     Everything <c>DefinitionListPanel</c> needs to know about one cache index, with no row
    ///     type in the signature.
    /// </summary>
    /// <remarks>
    ///     The panel is driven by this instead of by a switch on the index id. That is the whole
    ///     point: a new index editor is a new descriptor and one registration line, not another arm
    ///     in a method that already knows about every index before it.
    ///     <para>
    ///     Implement <see cref="DefinitionListDescriptor{TRow}"/> rather than this interface - it
    ///     does the casting, so a descriptor never sees an <c>object</c>.
    ///     </para>
    /// </remarks>
    public interface IDefinitionListDescriptor {
        /// <summary>The cache index this describes.</summary>
        int IndexId { get; }

        /// <summary>What one row is called, for the status line. Plural is added by the panel.</summary>
        string RowNoun { get; }

        /// <summary>The columns to show, left to right.</summary>
        IReadOnlyList<DefinitionColumn> Columns { get; }

        /// <summary>Whether <see cref="Encode"/> is implemented, and so whether cells may be edited.</summary>
        bool IsEditable { get; }

        /// <summary>
        ///     Whether a row needs the stored bytes to build, or is fully described by its address.
        /// </summary>
        /// <remarks>
        ///     False buys a listing that costs one reference-table walk instead of one decompression
        ///     per group, and index 7 is why it exists: it declares 63,607 groups of one file, and a
        ///     grid of model ids needs none of their bytes. Reading them anyway would inflate every
        ///     model in the cache to show a column of numbers the table already states.
        ///     <para>
        ///     A descriptor that clears this is handed an <b>empty</b> payload, not a null one, so
        ///     <see cref="Decode"/> keeps one signature - and a descriptor that clears it and then
        ///     reads the payload anyway decodes nothing rather than crashing, which is the failure
        ///     that is easiest to see.
        ///     </para>
        /// </remarks>
        bool ReadsPayload { get; }

        /// <summary>Every row the index holds, as cache addresses.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        IEnumerable<DefinitionAddress> Enumerate(RSCache cache);

        /// <summary>Builds one row from the bytes stored at an address.</summary>
        /// <param name="cache">The open cache, for a descriptor that has to resolve something else to build the row.</param>
        /// <param name="address">Where the payload came from.</param>
        /// <param name="payload">The stored file, positioned at its start.</param>
        /// <returns>The row.</returns>
        object Decode(RSCache cache, DefinitionAddress address, JagStream payload);

        /// <summary>Where a row came from, and so where an edit to it has to be written.</summary>
        /// <param name="row">The row.</param>
        /// <returns>Its address.</returns>
        DefinitionAddress AddressOf(object row);

        /// <summary>Re-encodes a row to the bytes that should be stored for it.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The encoded file.</returns>
        JagStream Encode(object row);
    }

    /// <summary>
    ///     The base every definition-list descriptor derives from, typed on its row.
    /// </summary>
    /// <remarks>
    ///     Read only by default: <see cref="IsEditable"/> is false and <see cref="Encode"/> throws,
    ///     so a descriptor for an index whose format is not reverse engineered cannot accidentally
    ///     offer to write it. Overriding <see cref="Encode"/> alone is not enough - editing turns on
    ///     only when <see cref="IsEditable"/> says so, which keeps the two statements from drifting.
    /// </remarks>
    /// <typeparam name="TRow">The row type this descriptor produces.</typeparam>
    public abstract class DefinitionListDescriptor<TRow> : IDefinitionListDescriptor where TRow : class {
        /// <inheritdoc/>
        public abstract int IndexId { get; }

        /// <inheritdoc/>
        public abstract string RowNoun { get; }

        /// <inheritdoc/>
        public abstract IReadOnlyList<DefinitionColumn> Columns { get; }

        /// <inheritdoc/>
        public virtual bool IsEditable => false;

        /// <inheritdoc/>
        /// <remarks>
        ///     True by default, because every index whose records are modelled has to read them.
        ///     Override it only for a listing whose columns come entirely from the reference table.
        /// </remarks>
        public virtual bool ReadsPayload => true;

        /// <summary>
        ///     Every row the index holds, taken from the reference table.
        /// </summary>
        /// <remarks>
        ///     Table-driven through <see cref="RSCache.EnumerateFiles"/>, never a 0..255 walk that
        ///     catches <see cref="System.IO.FileNotFoundException"/> for the holes - groups really
        ///     are sparse, and the exceptions cost more than the load does.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The addresses to load.</returns>
        public virtual IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach ((int group, int file) in cache.EnumerateFiles(IndexId))
                yield return Address(group, file);
        }

        /// <summary>Builds one row from the bytes stored at an address.</summary>
        /// <param name="cache">The open cache, for a descriptor that has to resolve something else to build the row.</param>
        /// <param name="address">Where the payload came from.</param>
        /// <param name="payload">The stored file, positioned at its start.</param>
        /// <returns>The row.</returns>
        public abstract TRow Decode(RSCache cache, DefinitionAddress address, JagStream payload);

        /// <summary>Where a row came from, and so where an edit to it has to be written.</summary>
        /// <param name="row">The row.</param>
        /// <returns>Its address.</returns>
        public abstract DefinitionAddress AddressOf(TRow row);

        /// <summary>
        ///     Re-encodes a row. Not implemented unless the index's format is understood well enough
        ///     to write it back byte for byte.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The encoded file.</returns>
        /// <exception cref="NotSupportedException">This descriptor is read only.</exception>
        public virtual JagStream Encode(TRow row) {
            throw new NotSupportedException(
                "Index " + IndexId + " has no encoder here, so its rows cannot be written back." +
                " Override Encode and IsEditable together once the format round-trips byte for byte.");
        }

        /// <summary>
        ///     Builds an address, filling in the definition id when this index has an established
        ///     group/file split and leaving it absent when it does not.
        /// </summary>
        /// <remarks>
        ///     Routed through <see cref="CacheAddressing.TryGetFor"/> rather than
        ///     <see cref="CacheAddressing.For"/>: <c>For</c> throws for an index whose split is
        ///     unrecorded, which is the right answer for a caller that needs an id and the wrong one
        ///     for a raw listing that only ever addresses files directly. Name-hashed indexes are
        ///     excluded for the same reason - no arithmetic relates their ids to a group.
        /// </remarks>
        /// <param name="groupId">The group id.</param>
        /// <param name="fileId">The file id within that group.</param>
        /// <returns>The address.</returns>
        protected DefinitionAddress Address(int groupId, int fileId) {
            if (!CacheAddressing.TryGetFor(IndexId, out CacheAddressing addressing))
                return new DefinitionAddress(groupId, fileId);

            if (addressing.Shape == CacheIdShape.NameHashed)
                return new DefinitionAddress(groupId, fileId);

            return new DefinitionAddress(groupId, fileId, addressing.DefinitionId(groupId, fileId));
        }

        object IDefinitionListDescriptor.Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            return Decode(cache, address, payload);
        }

        DefinitionAddress IDefinitionListDescriptor.AddressOf(object row) {
            return AddressOf(Cast(row));
        }

        JagStream IDefinitionListDescriptor.Encode(object row) {
            return Encode(Cast(row));
        }

        private static TRow Cast(object row) {
            return row as TRow ?? throw new ArgumentException(
                "This descriptor produces " + typeof(TRow).Name + " rows but was handed a " +
                (row?.GetType().Name ?? "null") + ".", nameof(row));
        }
    }
}
