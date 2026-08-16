using System;
using FlashEditor.IO;

namespace FlashEditor.Cache {
    /// <summary>
    ///     One file as a group rewrite states it: where it sits, what it holds, and the name hash
    ///     the reference table should record against it.
    /// </summary>
    /// <remarks>
    ///     <b>The identifier is stated rather than inherited, and that is deliberate.</b>
    ///     <see cref="RSCache.WriteGroup"/> replaces a group's whole file set, so it cannot carry a
    ///     name across on the caller's behalf - after a renumbering, the file that used to be id 5
    ///     is a different id and nothing in the bytes says which one it became. A caller that knows
    ///     the mapping is the only thing that can move the hashes with the files, and one that
    ///     leaves this at <see cref="Unnamed"/> on a table carrying identifiers is asking for every
    ///     file in the group to be marked unnamed.
    ///     <para>
    ///     <see cref="Unnamed"/> is <c>-1</c> because that is the client's own sentinel:
    ///     <c>VersionTable.java:145-147</c> pre-fills the identifier array with -1 and overwrites it
    ///     only for the entries a table declares, so a stored -1 is how the format says "no name".
    ///     Zero is a real hash and means nothing of the sort.
    ///     </para>
    /// </remarks>
    public sealed class RSGroupFile {
        /// <summary>The identifier value that means the file carries no name.</summary>
        public const int Unnamed = -1;

        /// <summary>States one file of a group rewrite.</summary>
        /// <param name="fileId">The file id it takes within the group.</param>
        /// <param name="data">Its payload.</param>
        /// <param name="identifier">Its name hash, or <see cref="Unnamed"/>.</param>
        public RSGroupFile(int fileId, JagStream data, int identifier = Unnamed) {
            FileId = fileId;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Identifier = identifier;
        }

        /// <summary>The file id it takes within the group.</summary>
        public int FileId { get; }

        /// <summary>Its payload.</summary>
        public JagStream Data { get; }

        /// <summary>Its name hash, or <see cref="Unnamed"/>.</summary>
        public int Identifier { get; }
    }
}
