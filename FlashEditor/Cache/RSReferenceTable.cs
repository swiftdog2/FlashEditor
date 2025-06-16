using FlashEditor.Cache.CheckSum;
using static FlashEditor.utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlashEditor.utils;

namespace FlashEditor.cache {
    ///<summary>
    ///A<seealso cref="RSReferenceTable" /> holds details for all the files with a single type,
    ///such as checksums, versions and archive members. There are also optional
    ///fields for identifier hashes and whirlpool digests.
    /// </summary>
    public class RSReferenceTable {
        public const int FLAG_IDENTIFIERS = 0x01;
        public const int FLAG_WHIRLPOOL = 0x02;
        public const int FLAG_SIZES = 0x04;
        public const int FLAG_HASH = 0x08;

        public bool hasIdentifiers;
        public bool usesWhirlpool;
        public bool entryHashes;
        public bool sizes;

        internal SortedDictionary<int, RSEntry> entries = new SortedDictionary<int, RSEntry>();

        public int version;
        public int format;
        public int flags;

        public int validArchivesCount;
        public int[] validArchiveIds;
        public int type;

        internal RSIdentifiers identifiers;

        /// <summary>
        /// Updates CRC, XTEA flag and version for a single group,
        /// then marks the table dirty by incrementing <see cref="version"/>.
        /// </summary>
        public void UpdateGroup(int groupId, uint crc, bool usesXtea, int versionInc = 1)
        {
            if (!entries.TryGetValue(groupId, out var e))
                return;

            e.SetCrc((int)crc);
            e.UsesXtea = usesXtea;
            e.SetVersion(e.GetVersion() + versionInc);

            // bump reference-table version so the client notices the change
            version++;
        }

        /// <summary>
        /// Gets the maximum number of entries in this table.
        /// </summary>
        /// <returns>The maximum number of entries</returns>
        public int Capacity() {
            if(entries.Count == 0)
                return 0;
            return entries.Keys.Last() + 1;
        }

        /// <summary>
        /// Returns the specified entry
        /// </summary>
        /// <param name="id">The entry index</param>
        /// <returns>The entry at the index <paramref name="id"/></returns>
        internal RSEntry GetEntry(int id) {
            if(!entries.ContainsKey(id))
                return null;
            return entries[id];
        }

        public void PutEntry(int containerId, RSEntry entry)
        {
            if (entries.ContainsKey(containerId))
                entries[containerId] = entry;
            else
                entries.Add(containerId, entry);
        }


        /// <summary>
        /// Returns the number of entries in the reference table
        /// </summary>
        /// <returns>The number of archives</returns>
        internal int GetEntryTotal() {
            return entries.Count;
        }

        /// <summary>
        /// Return the reference table version
        /// </summary>
        /// <returns>The reference table version</returns>
        public virtual int GetVersion() {
            return version;
        }

        internal SortedDictionary<int, RSEntry> GetEntries() {
            return entries;
        }

        internal void SetType(int type) {
            this.type = type;
        }
    }
}
