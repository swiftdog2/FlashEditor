using FlashEditor.Cache.CheckSum;
using static FlashEditor.utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;

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

        internal SortedDictionary<int, RSEntry> entries = new SortedDictionary<int, RSEntry>();
        public int version;
        public int format;
        public int flags;
        public bool named;
        public bool usesWhirlpool;

        public int validArchivesCount;
        public int[] validArchiveIds;
        public int type;

        internal JagStream stream;
        private RSIdentifiers identifiers;

        /// <summary>
        /// Constructs a reference table from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the reference table Container data</param>
        /// <returns>The reference table</returns>
        internal static RSReferenceTable Decode(JagStream stream) {
            Debug("Decoding reference table", LOG_DETAIL.ADVANCED);

            //Create a new Reference Table
            RSReferenceTable table = new RSReferenceTable();

            table.format = stream.ReadUnsignedByte();

            if(table.format >= 6)
                table.version = stream.ReadInt();

            table.flags = stream.ReadUnsignedByte();
            table.validArchivesCount = stream.ReadUnsignedShort();
            table.named = (FLAG_IDENTIFIERS & table.flags) != 0;
            table.usesWhirlpool = (FLAG_WHIRLPOOL & table.flags) != 0;

            Debug("Table version: " + table.version + " | Flags: " + (table.flags == 1 ? "Y" : "N") + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"), LOG_DETAIL.ADVANCED);

            table.validArchiveIds = new int[table.validArchivesCount];

            int k = 0, lastArchiveId = 0;

            for(int index = 0; index < table.validArchivesCount; index++) {
                int archiveId = lastArchiveId += stream.ReadUnsignedShort();
                table.validArchiveIds[index] = archiveId;
                table.entries.Add(archiveId, new RSEntry(k++));
            }

            //If named, set the name hash for the archive
            if(table.named)
                for(int index = 0; index < table.validArchivesCount; index++)
                    table.entries[table.validArchiveIds[index]].SetNameHash(stream.ReadInt());

            //If the archive uses whirlpool, set the whirlpool hash
            if(table.usesWhirlpool) {
                for(int index = 0; index < table.validArchivesCount; index++) {
                    byte[] whirpool = new byte[64];
                    stream.Read(whirpool, 0, 64);
                    table.entries[table.validArchiveIds[index]].SetWhirlpool(whirpool);
                }
            }

            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetCrc(stream.ReadInt());

            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetVersion(stream.ReadInt());

            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetValidFileIds(new int[stream.ReadUnsignedShort()]);

            for(int index = 0; index < table.validArchivesCount; index++) {
                int lastFileId = 0;
                int biggestFileId = 0;
                RSEntry entry = table.entries[table.validArchiveIds[index]];
                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++) {
                    int fileId = lastFileId += stream.ReadUnsignedShort();
                    if(fileId > biggestFileId)
                        biggestFileId = fileId;
                    entry.GetValidFileIds()[index2] = fileId;
                }
                entry.SetFiles(new SortedDictionary<int?, RSChildEntry>());
                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                    entry.GetChildEntries()[entry.GetValidFileIds()[index2]] = new RSChildEntry();
            }

            if(table.named) {
                for(int index = 0; index < table.validArchivesCount; index++) {
                    RSEntry entry = table.entries[table.validArchiveIds[index]];
                    for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                        entry.GetChildEntries()[entry.GetValidFileIds()[index2]].SetNameHash(stream.ReadInt());
                }
            }

            return table;
        }

        internal void PutEntry(int containerId, RSEntry entry) {
            if(entries.ContainsKey(containerId))
                entries[containerId] = entry;
            else
                entries.Add(containerId, entry);
        }

        internal void UpdateStream(JagStream stream) {
            this.stream = stream;
        }

        /// <summary>
        /// Writes the RSReferenceTable
        /// </summary>
        /// <returns>The reference table</returns>
        internal JagStream Encode() {
            Debug("Encoding RSReferenceTable " + type);

            JagStream stream = new JagStream();

            stream.WriteByte((byte) format);

            if(format >= 6)
                stream.WriteInteger(version);

            stream.WriteByte((byte) flags);
            stream.WriteShort(entries.Count);

            int last = 0;
            foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                int delta = kvp.Key - last;
                last = kvp.Key;
                stream.WriteShort(delta);
            }

            if(named)
                foreach(KeyValuePair<int, RSEntry> kvp in entries)
                    stream.WriteInteger(kvp.Value.GetNameHash());

            if(usesWhirlpool)
                foreach(KeyValuePair<int, RSEntry> kvp in entries)
                    stream.Write(kvp.Value.GetWhirlpool(), 0, 64);

            foreach(KeyValuePair<int, RSEntry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetCrc());

            foreach(KeyValuePair<int, RSEntry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetVersion());

            foreach(KeyValuePair<int, RSEntry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetValidFileIds().Length);

            foreach(KeyValuePair<int, RSEntry> kvp in entries)
                for(int k = 0; k < kvp.Value.GetValidFileIds().Length; k++)
                    stream.WriteShort(kvp.Value.GetValidFileIds()[k]);

            for(int index = 0; index < validArchivesCount; index++) {
                RSEntry entry = entries[validArchiveIds[index]];
                for(int k = 0; k < entry.GetValidFileIds().Length; k++)
                    stream.WriteShort(entry.GetValidFileIds()[k]);
            }

            if(named)
                foreach(KeyValuePair<int, RSEntry> kvp in entries)
                    for(int k = 0; k < kvp.Value.GetValidFileIds().Length; k++)
                        stream.WriteInteger(kvp.Value.GetChildEntries()[k].CalculateNameHash());

            return stream.Flip();
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
