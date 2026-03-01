using System;

namespace FlashEditor.cache {
    /// <summary>
    /// Represents a single file entry within an archive.
    /// Inherits identifiers and metadata from <see cref="RSArchiveEntry"/>.
    /// </summary>
    public class RSFileEntry : RSArchiveEntry {
        public RSFileEntry() : base() {
        }

        public RSFileEntry(int index) : base(index) {

        }
    }
}
