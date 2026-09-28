using System;

namespace FlashEditor.Definitions.Editing {
    /// <summary>The file a definition commit staged into the cache's in-memory overlay.</summary>
    public sealed class DefinitionStagedEventArgs : EventArgs {
        /// <summary>Creates the arguments.</summary>
        /// <param name="indexId">The cache index written.</param>
        /// <param name="address">The group and file written.</param>
        public DefinitionStagedEventArgs(int indexId, DefinitionAddress address) {
            IndexId = indexId;
            Address = address;
        }

        /// <summary>The cache index written.</summary>
        public int IndexId { get; }

        /// <summary>The group and file written.</summary>
        public DefinitionAddress Address { get; }
    }
}
