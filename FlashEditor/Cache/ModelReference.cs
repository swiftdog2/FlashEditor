using System;

namespace FlashEditor.cache
{
    /// <summary>
    /// Lightweight reference to a model within the cache.
    /// </summary>
    internal readonly struct ModelReference
    {
        /// <summary>Archive identifier within the models index.</summary>
        public int ArchiveId { get; }

        /// <summary>File identifier within the archive.</summary>
        public int FileId { get; }

        /// <summary>
        /// Combined model identifier (<c>archiveId &lt;&lt; 8 | fileId</c>).
        /// </summary>
        public int ModelID => ArchiveId;

        /// <summary>Create a new model reference.</summary>
        /// <param name="archiveId">Archive identifier.</param>
        /// <param name="fileId">File identifier.</param>
        public ModelReference(int archiveId, int fileId)
        {
            ArchiveId = archiveId;
            FileId = fileId;
        }
    }
}
