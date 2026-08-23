using System;
using FlashEditor.Cache.CheckSum;
using FlashEditor.Cache.Util;
using static FlashEditor.Utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FlashEditor.Utils;

namespace FlashEditor.Cache
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

        /// <summary>How many archives the table declares.</summary>
        /// <remarks>
        /// A view over <see cref="archiveEntries"/> rather than a stored count, for the same
        /// reason <see cref="hasIdentifiers"/> is a view over <see cref="flags"/>. It was a field
        /// set once by <see cref="ReferenceTableCodec.Decode"/> and never refreshed by
        /// <see cref="PutArchiveEntry"/>, so a write that added a group left it describing the
        /// table as it arrived from disk - and it is bound to a grid column, so the count on
        /// screen would simply have been wrong. <see cref="ReferenceTableCodec.Encode"/> already
        /// wrote the count from the dictionary, so the two could disagree about the same table.
        /// </remarks>
        public int validArchivesCount => archiveEntries.Count;

        /// <summary>The declared archive ids, ascending.</summary>
        /// <remarks>
        /// Computed for the reason given on <see cref="validArchivesCount"/>. Allocates per call,
        /// so a loop wanting it more than once should hold it in a local.
        /// </remarks>
        public int[] validArchiveIds => archiveEntries.Keys.ToArray();

        public int indexId;

        private RSIdentifiers? identifierIndex;
        private readonly object identifierIndexLock = new object();

        /// <summary>
        ///     The group-level name hash to archive id lookup behind <see cref="GetArchiveId"/>.
        /// </summary>
        /// <remarks>
        ///     Built on first use and dropped by <see cref="PutArchiveEntry"/>, for the same reason
        ///     <see cref="Names"/> is. It used to be assigned once by
        ///     <see cref="ReferenceTableCodec.Decode"/> and never rebuilt, so after any write that
        ///     added a group this answered from the table as it arrived from disk while
        ///     <see cref="Names"/> answered from the table as it now stands - two name lookups over
        ///     one table, disagreeing, with <c>MapSquareLoader.ResolveGroup</c>,
        ///     <c>WorldMapNaming</c> and <c>HuffmanTable</c> reading the stale one.
        ///     <para>
        ///     Dropped rather than rebuilt eagerly: a batch of map-square writes would otherwise
        ///     rebuild index 5's 5203-entry table once per square with nothing reading it in
        ///     between.
        ///     </para>
        /// </remarks>
        internal RSIdentifiers identifiers {
            get {
                lock (identifierIndexLock)
                    return identifierIndex ??= BuildIdentifiers();
            }
        }

        /// <summary>
        ///     Builds the dense identifier array <see cref="RSIdentifiers"/> indexes.
        /// </summary>
        /// <remarks>
        ///     Sized to the highest declared id plus one and indexed by archive id, so an id the
        ///     table does not declare keeps a zero. A table with the identifiers flag clear is
        ///     built all-zero rather than from the entries, which sit at -1: that is what the
        ///     decoder did, and changing it here would change what <see cref="GetArchiveId"/>
        ///     answers on every index that carries no names.
        /// </remarks>
        private RSIdentifiers BuildIdentifiers() {
            int[] dense = archiveEntries.Count == 0
                ? Array.Empty<int>()
                : new int[archiveEntries.Keys.Last() + 1];

            if (hasIdentifiers)
                foreach (KeyValuePair<int, RSArchiveEntry> entry in archiveEntries)
                    dense[entry.Key] = entry.Value.GetIdentifier();

            return new RSIdentifiers(dense);
        }

        private CacheNameIndex? nameIndex;
        private readonly object nameIndexLock = new object();

        /// <summary>
        ///     The table's names, at both the group and the file level.
        /// </summary>
        /// <remarks>
        ///     <see cref="GetArchiveId"/> only ever reached the group level, so the per-file
        ///     identifiers this codec has always decoded were unreachable. This is the lookup the
        ///     client actually uses - it resolves a group name and then a file name inside it - and
        ///     it is shared machinery rather than an index-31 detail: indexes 3, 5, 23, 30, 31, 32
        ///     and 33 all set the identifiers flag.
        ///     <para>
        ///     Built on first use and dropped whenever an entry is added, because a write that
        ///     introduces a group or a file introduces a name with it.
        ///     </para>
        /// </remarks>
        public CacheNameIndex Names {
            get {
                lock (nameIndexLock)
                    return nameIndex ??= CacheNameIndex.Build(this);
            }
        }

        /// <summary>
        ///     Resolves a group name to its archive id through the table's identifier map.
        /// </summary>
        /// <remarks>
        ///     This is the only way to address index 5. A map square is found by hashing
        ///     <c>m50_50</c>; the <c>map_index.dat</c> mechanism the client ships is dead code and
        ///     does not agree with this cache.
        /// </remarks>
        /// <param name="name">The group name, case-insensitive.</param>
        /// <returns>The archive id, or -1 when the table has no group of that name.</returns>
        public int GetArchiveId(string name)
        {
            if (name == null)
                return -1;
            return identifiers.getFile(NameHasher.GetNameHash(name));
        }

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

            //A write can add a group, add a file to one, or replace an entry outright, and each of
            //those changes what the table names. Dropped rather than patched: the rebuild costs one
            //pass over entries that are already in memory, and a patch would have to know which of
            //the three happened.
            lock (nameIndexLock)
                nameIndex = null;

            //The group-level lookup is keyed on the same identifiers, so it goes with it. Both are
            //dropped here rather than at the staging layer because this is the single funnel every
            //entry enters a table through, whatever order a caller sets things in.
            lock (identifierIndexLock)
                identifierIndex = null;
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
