using FlashEditor.Cache.CheckSum;
using static FlashEditor.Utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlashEditor.Utils;

namespace FlashEditor.cache
{
    ///<summary>
    ///A<seealso cref="RSReferenceTable" /> holds details for all the archives within a single index,
    ///such as checksums, versions and file members. There are also optional
    ///fields for identifier hashes and whirlpool digests.
    /// </summary>
    public class RSReferenceTable
    {
        public const int FLAG_IDENTIFIERS = 0x01;
        public const int FLAG_WHIRLPOOL = 0x02;
        public const int FLAG_SIZES = 0x04;
        public const int FLAG_HASH = 0x08;

        internal SortedDictionary<int, RSArchiveEntry> archiveEntries = new SortedDictionary<int, RSArchiveEntry>();

        public int version;
        public int format;

        /// <summary>
        /// The raw table flags byte. This is the only place the flag state lives.
        /// </summary>
        /// <remarks>
        /// <see cref="ReferenceTableCodec.Encode"/> writes this byte to the wire but decides
        /// which optional blocks follow it from the four bools below. Were those stored
        /// separately they could drift out of step with the byte, and the table would then
        /// declare one shape and carry another - shifting every field after the disagreement,
        /// exactly the failure the format-7 archive-flags byte had. They are views over this
        /// field so the two cannot disagree, the same way
        /// <see cref="RSArchiveEntry.UsesXtea"/> is a view over
        /// <see cref="RSArchiveEntry.ArchiveFlags"/>.
        /// </remarks>
        public int flags;

        /// <summary>Whether the table carries a 32-bit name hash per archive and per file.</summary>
        public bool hasIdentifiers => (flags & FLAG_IDENTIFIERS) != 0;

        /// <summary>Whether the table carries a 64-byte whirlpool digest per archive.</summary>
        public bool usesWhirlpool => (flags & FLAG_WHIRLPOOL) != 0;

        /// <summary>Whether the table carries a 32-bit hash per archive.</summary>
        public bool entryHashes => (flags & FLAG_HASH) != 0;

        /// <summary>Whether the table carries a compressed and an uncompressed size per archive.</summary>
        public bool sizes => (flags & FLAG_SIZES) != 0;

        public int validArchivesCount;
        public int[] validArchiveIds;
        public int indexId;

        internal RSIdentifiers identifiers;

        /// <summary>
        /// Updates CRC, XTEA flag and version for a single archive,
        /// then marks the table dirty by incrementing <see cref="version"/>.
        /// </summary>
        public void UpdateGroup(int groupId, uint crc, bool usesXtea, int versionInc = 1)
        {
            if (!archiveEntries.TryGetValue(groupId, out var e))
                return;

            e.SetCrc((int)crc);
            e.UsesXtea = usesXtea;
            e.SetVersion(e.GetVersion() + versionInc);

            // bump reference-table version so the client notices the change
            version++;
        }

        /// <summary>
        /// Gets the maximum number of archive entries in this table.
        /// </summary>
        /// <returns>The maximum number of archive entries</returns>
        public int Capacity()
        {
            if (archiveEntries.Count == 0)
                return 0;
            return archiveEntries.Keys.Last() + 1;
        }

        /// <summary>
        /// Returns the specified archive entry
        /// </summary>
        /// <param name="id">The archive id</param>
        /// <returns>The archive entry at <paramref name="id"/></returns>
        internal RSArchiveEntry GetArchiveEntry(int id)
        {
            if (!archiveEntries.ContainsKey(id))
                return null;
            return archiveEntries[id];
        }

        public void PutArchiveEntry(int archiveId, RSArchiveEntry entry)
        {
            if (archiveEntries.ContainsKey(archiveId))
                archiveEntries[archiveId] = entry;
            else
                archiveEntries.Add(archiveId, entry);
        }


        /// <summary>
        /// Returns the number of archive entries in the reference table
        /// </summary>
        /// <returns>The number of archives</returns>
        internal int GetArchiveCount()
        {
            return archiveEntries.Count;
        }

        /// <summary>
        /// Return the reference table version
        /// </summary>
        /// <returns>The reference table version</returns>
        public virtual int GetVersion()
        {
            return version;
        }

        internal SortedDictionary<int, RSArchiveEntry> GetArchiveEntries()
        {
            return archiveEntries;
        }

        internal void SetIndexId(int indexId)
        {
            this.indexId = indexId;
        }
    }
}
