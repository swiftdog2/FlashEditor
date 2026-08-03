using System;
using System.Collections.Generic;
using System.Globalization;
using FlashEditor.cache;

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
        private DefinitionColumn(string header, int width, Func<object, object?> read, Action<object, object?>? write) {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Width = width;
            Read = read ?? throw new ArgumentNullException(nameof(read));
            Write = write;
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

        /// <summary>A column that shows a value and cannot be edited.</summary>
        /// <typeparam name="TRow">The row type this column reads.</typeparam>
        /// <param name="header">The column heading.</param>
        /// <param name="read">Reads the value off a row.</param>
        /// <param name="width">The column width.</param>
        /// <returns>The column.</returns>
        public static DefinitionColumn ReadOnly<TRow>(string header, Func<TRow, object?> read, int width = 90)
            where TRow : class {
            return new DefinitionColumn(header, width, row => read(Cast<TRow>(row)), null);
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
                row => read(Cast<TRow>(row)),
                write == null ? null : (row, value) => write(Cast<TRow>(row), value?.ToString() ?? string.Empty));
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
                row => read(Cast<TRow>(row)),
                write == null ? null : (row, value) => write(Cast<TRow>(row), ToInt(value)));
        }

        private static int ToInt(object? value) {
            if (value == null)
                return 0;
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        private static TRow Cast<TRow>(object row) where TRow : class {
            return row as TRow ?? throw new ArgumentException(
                "This column reads a " + typeof(TRow).Name + " but was handed a " +
                (row?.GetType().Name ?? "null") + ".", nameof(row));
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
