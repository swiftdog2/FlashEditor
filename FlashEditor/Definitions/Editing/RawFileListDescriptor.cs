using System;
using System.Collections.Generic;
using FlashEditor.cache;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     One addressable file in an index, described without decoding it.
    /// </summary>
    /// <remarks>
    ///     Everything here comes from the reference table and the stored payload's length, so it is
    ///     true of any index whatever its record format turns out to be.
    /// </remarks>
    public sealed class RawFileListing {
        /// <summary>Describes one file.</summary>
        /// <param name="address">Where the file lives.</param>
        /// <param name="sizeBytes">The decoded payload length.</param>
        /// <param name="groupNameHash">The group's name hash, or -1 when the table carries none.</param>
        /// <param name="fileNameHash">The file's name hash, or -1 when the table carries none.</param>
        public RawFileListing(DefinitionAddress address, int sizeBytes, int groupNameHash, int fileNameHash) {
            Address = address;
            SizeBytes = sizeBytes;
            GroupNameHash = groupNameHash;
            FileNameHash = fileNameHash;
        }

        /// <summary>Where the file lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The group (archive) that holds it.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>The file id within that group.</summary>
        public int FileId => Address.FileId;

        /// <summary>
        ///     The length of the stored file after the group is decompressed and split.
        /// </summary>
        /// <remarks>
        ///     Not the compressed size. The reference table's <c>FLAG_SIZES</c> pair is per group and
        ///     is set on no table in this cache anyway, so a per-file size can only come from
        ///     actually decoding the group.
        /// </remarks>
        public int SizeBytes { get; }

        /// <summary>
        ///     The group's name hash, or -1 when none is recorded.
        /// </summary>
        /// <remarks>
        ///     The hash, not a name. Recovering the name means finding a string that hashes to it,
        ///     which is a dictionary attack rather than a lookup - so the honest column is the number
        ///     the table actually holds.
        ///     <para>
        ///     -1 covers two cases that the format does not distinguish: a table with no identifiers
        ///     block at all, and an entry the block marks as unnamed, which it spells as -1. Index 3
        ///     carries identifiers and still leaves entries at -1, so the second case is the common
        ///     one there.
        ///     </para>
        /// </remarks>
        public int GroupNameHash { get; }

        /// <summary>The file's name hash, or -1 when none is recorded. See <see cref="GroupNameHash"/>.</summary>
        public int FileNameHash { get; }
    }

    /// <summary>
    ///     Lists an index's groups and files without claiming to understand their contents.
    /// </summary>
    /// <remarks>
    ///     For an index whose record format is not reverse engineered. Index 3 is the case this was
    ///     written for: it has been declared in the editor's tab list from the start and renders an
    ///     empty page, because there is no decoder to put behind it. A raw addressable listing is
    ///     what can honestly be shown, and it is genuinely useful - it says what exists, how big it
    ///     is and what the table names it.
    ///     <para>
    ///     Read only, and it must stay that way. There is no encoder, so
    ///     <see cref="DefinitionListDescriptor{TRow}.IsEditable"/> is left false and the panel
    ///     switches cell editing off entirely.
    ///     </para>
    /// </remarks>
    public sealed class RawFileListDescriptor : DefinitionListDescriptor<RawFileListing> {
        private readonly int indexId;
        private readonly string rowNoun;
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists the files of one index.</summary>
        /// <param name="indexId">The index to list.</param>
        /// <param name="rowNoun">What one row is called in the status line, singular.</param>
        public RawFileListDescriptor(int indexId, string rowNoun) {
            if (indexId < 0)
                throw new ArgumentOutOfRangeException(nameof(indexId), indexId, "Index ids are non-negative.");

            this.indexId = indexId;
            this.rowNoun = rowNoun ?? throw new ArgumentNullException(nameof(rowNoun));

            columns = new[] {
                DefinitionColumn.ReadOnly<RawFileListing>("Group", row => row.GroupId, 80),
                DefinitionColumn.ReadOnly<RawFileListing>("File", row => row.FileId, 80),
                DefinitionColumn.ReadOnly<RawFileListing>("Size", row => row.SizeBytes, 90),
                DefinitionColumn.ReadOnly<RawFileListing>("Group name hash", row => HashOrNothing(row.GroupNameHash), 140),
                DefinitionColumn.ReadOnly<RawFileListing>("File name hash", row => HashOrNothing(row.FileNameHash), 140)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => indexId;

        /// <inheritdoc/>
        public override string RowNoun => rowNoun;

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override RawFileListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            RSArchiveEntry? group = cache.GetReferenceTable(indexId).GetArchiveEntry(address.GroupId);
            RSFileEntry? file = group?.GetFileEntry(address.FileId);

            return new RawFileListing(address, payload.Length,
                group?.GetIdentifier() ?? -1,
                file?.GetIdentifier() ?? -1);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(RawFileListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            //Carried on the row rather than derived. An index listed this way is one whose id
            //arithmetic is not established, so there is nothing to derive it from.
            return row.Address;
        }

        /// <summary>
        ///     An unnamed entry reads as an empty cell rather than as the number -1.
        /// </summary>
        /// <remarks>
        ///     -1 is the format's own marker for "no name", not a hash anybody could look up, and a
        ///     column full of them sorts as if it held real values.
        /// </remarks>
        private static object? HashOrNothing(int hash) {
            return hash == -1 ? null : hash;
        }
    }
}
