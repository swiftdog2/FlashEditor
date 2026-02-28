using System;

namespace FlashEditor.cache {
    /// <summary>
    /// Represents a single file entry within an archive (group).
    /// Inherits identifiers and metadata from <see cref="RSEntry"/>.
    /// </summary>
    public class RSChildEntry : RSEntry {
        public RSChildEntry() : base() {
        }

        public RSChildEntry(int index) : base(index) {

        }
    }
}
