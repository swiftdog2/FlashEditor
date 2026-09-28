using System;

namespace FlashEditor.Definitions.Editing {
    /// <summary>The file a definition commit staged into the cache's in-memory overlay.</summary>
    public sealed class DefinitionStagedEventArgs : EventArgs {
        /// <summary>Creates the arguments.</summary>
        /// <param name="indexId">The cache index written.</param>
        /// <param name="address">The group and file written.</param>
        /// <param name="payload">The bytes staged, exactly as the file now holds them.</param>
        public DefinitionStagedEventArgs(int indexId, DefinitionAddress address, byte[] payload) {
            IndexId = indexId;
            Address = address;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        /// <summary>The cache index written.</summary>
        public int IndexId { get; }

        /// <summary>The group and file written.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The bytes staged. Shared with the caller, so a handler that keeps them must not mutate them.</summary>
        public byte[] Payload { get; }
    }
}
