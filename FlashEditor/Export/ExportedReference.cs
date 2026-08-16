using System;

namespace FlashEditor.Export {
    /// <summary>
    ///     One id in a record, resolved to what it addresses.
    /// </summary>
    /// <remarks>
    ///     The raw id is kept beside the resolution rather than replaced by it. A resolution is an
    ///     interpretation - it can be wrong, and the export is read only, so nothing downstream may
    ///     be forced to trust it to recover what the file actually stored.
    /// </remarks>
    public sealed class ExportedReference {
        /// <summary>Binds a stored id to what it addresses.</summary>
        /// <param name="field">The field on the record that holds the id.</param>
        /// <param name="join">The join this resolution comes from, named as the work list names it.</param>
        /// <param name="id">The id exactly as the record stores it.</param>
        /// <param name="targetIndex">The index the id addresses.</param>
        /// <param name="targetGroup">The group within that index.</param>
        /// <param name="targetFile">The file within that group.</param>
        /// <param name="exists">Whether that index's reference table declares the file.</param>
        /// <param name="detail">What the target holds, in a line, or null when nothing was decoded.</param>
        /// <param name="identifier">The target group's name hash, or null when its index carries none.</param>
        public ExportedReference(string field, string join, int id, int targetIndex, int targetGroup,
            int targetFile, bool exists, string? detail, int? identifier) {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Join = join ?? throw new ArgumentNullException(nameof(join));
            Id = id;
            TargetIndex = targetIndex;
            TargetGroup = targetGroup;
            TargetFile = targetFile;
            Exists = exists;
            Detail = detail;
            Identifier = identifier;
        }

        /// <summary>The field on the record that holds the id.</summary>
        public string Field { get; }

        /// <summary>
        ///     The join this resolution comes from.
        /// </summary>
        /// <remarks>
        ///     Written into the export so a reader can tell which relation produced a row and check
        ///     it against the measured list, rather than having to trust that every resolution in the
        ///     file came from the same place.
        /// </remarks>
        public string Join { get; }

        /// <summary>The id exactly as the record stores it.</summary>
        public int Id { get; }

        /// <summary>The index the id addresses.</summary>
        public int TargetIndex { get; }

        /// <summary>The group within that index.</summary>
        public int TargetGroup { get; }

        /// <summary>The file within that group.</summary>
        public int TargetFile { get; }

        /// <summary>
        ///     Whether the target index's reference table declares that file.
        /// </summary>
        /// <remarks>
        ///     Taken from the table rather than by reading the bytes, because the client gates every
        ///     read on the table: a group the table does not declare is unreachable in game whatever
        ///     its bytes say, so "the table declares it" is the definition of a reference that
        ///     resolves.
        /// </remarks>
        public bool Exists { get; }

        /// <summary>What the target holds, in one line, or null when nothing about it was decoded.</summary>
        public string? Detail { get; }

        /// <summary>
        ///     The target group's name hash, or null when its index carries no identifiers.
        /// </summary>
        /// <remarks>
        ///     The hash rather than a name. Group and file names hash with Java's
        ///     <c>String.hashCode</c>, which is one way, so a name can only be recovered by hashing a
        ///     candidate and comparing - and a name printed here that came from anywhere else would
        ///     be a claim this export cannot support.
        /// </remarks>
        public int? Identifier { get; }
    }
}
