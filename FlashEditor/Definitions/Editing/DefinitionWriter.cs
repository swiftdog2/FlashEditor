using FlashEditor.Cache;
using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Editing {
    /// <summary>
    ///     Stages an edited definition into the cache at the address its id implies.
    /// </summary>
    /// <remarks>
    ///     This lives outside the form so the write behind a grid edit can be exercised without
    ///     building one. A commit that only exists as the body of a <c>CellEditFinished</c> handler
    ///     is reachable by nothing but a double click, which is how index 18's write path came to be
    ///     complete, correct and never once executed.
    ///     <para>
    ///     The id split comes from <see cref="CacheAddressing"/> rather than from a literal at the
    ///     call site. Writing a definition to the wrong slot overwrites a different definition and
    ///     reports success, so the one place the split is stated is the only place it should be
    ///     spelled.
    ///     </para>
    /// </remarks>
    public static class DefinitionWriter {
        /// <summary>
        ///     Writes <paramref name="encoded"/> into the slot <paramref name="definitionId"/> names,
        ///     unless the cache already holds exactly those bytes.
        /// </summary>
        /// <remarks>
        ///     The comparison is against what the cache holds now, not against a snapshot taken when
        ///     the edit started. A snapshot only knows about the edit in front of it, where the
        ///     stored bytes also settle an edit that put a field back the way it was. Re-encoding
        ///     rewrites the stored bytes and therefore the archive CRC, which drags in the
        ///     reference-table entry of every definition packed alongside this one.
        ///     <para>
        ///     A slot the cache cannot read is treated as a difference rather than as an error, so a
        ///     definition can be written into a group that does not yet carry it.
        ///     </para>
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="indexId">The index the definition lives in.</param>
        /// <param name="definitionId">The definition id, which that index's addressing splits.</param>
        /// <param name="encoded">The definition's encoded bytes.</param>
        /// <returns>Whether anything was staged.</returns>
        public static bool Save(RSCache cache, int indexId, int definitionId, byte[] encoded) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (encoded == null)
                throw new ArgumentNullException(nameof(encoded));

            CacheAddressing addressing = CacheAddressing.For(indexId);
            int groupId = addressing.GroupOf(definitionId);
            int fileId = addressing.FileOf(definitionId);

            if (MatchesStoredBytes(cache, indexId, groupId, fileId, encoded))
                return false;

            cache.WriteFile(indexId, groupId, fileId, new JagStream(encoded));
            return true;
        }

        /// <summary>Whether the cache already stores exactly these bytes at this address.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="indexId">The index id.</param>
        /// <param name="groupId">The group within the index.</param>
        /// <param name="fileId">The file within the group.</param>
        /// <param name="encoded">The candidate bytes.</param>
        /// <returns>True when a write would change nothing.</returns>
        private static bool MatchesStoredBytes(RSCache cache, int indexId, int groupId, int fileId,
                                               byte[] encoded) {
            try {
                return cache.ReadFileBytes(indexId, groupId, fileId).AsSpan().SequenceEqual(encoded);
            }
            catch (Exception) {
                //An unreadable slot is one to write, not one to fail on: a group missing from the
                //table, or a file id it does not declare, both surface here and both are cases the
                //write path is expected to create.
                return false;
            }
        }
    }
}
