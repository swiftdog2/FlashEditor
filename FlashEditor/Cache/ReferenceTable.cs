using FlashEditor.utils;
using System.Collections.Generic;
using System.Linq;

namespace FlashEditor.cache {
    ///<summary>
    ///A<seealso cref="ReferenceTable" /> holds details for all the files with a single type,
    ///such as checksums, versions and archive members. There are also optional
    ///fields for identifier hashes and whirlpool digests.
    /// </summary>
    public class ReferenceTable {
        public const int FLAG_IDENTIFIERS = 0x01;
        public const int FLAG_WHIRLPOOL = 0x02;
        public const int FLAG_SIZES = 0x04;
        public const int FLAG_HASH = 0x08;

        internal SortedDictionary<int, Entry> entries = new SortedDictionary<int, Entry>();
        public int version;
        public int format;
        public int flags;
        public bool named;
        public bool usesWhirlpool;
        public int validArchivesCount;
        public int[] validArchiveIds;

        internal JagStream stream;

        /// <summary>
        /// Constructs a reference table from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the reference table Container data</param>
        /// <returns>The reference table</returns>
        internal static ReferenceTable Decode(JagStream stream) {
            DebugUtil.Debug("Decoding reference table");

            //Create a new Reference Table
            ReferenceTable table = new ReferenceTable();

            table.format = stream.ReadUnsignedByte();

            if(table.format >= 6)
                table.version = stream.ReadInt();

            table.flags = stream.ReadUnsignedByte();
            table.validArchivesCount = stream.ReadUnsignedShort();
            table.named = (FLAG_IDENTIFIERS & table.flags) != 0;
            table.usesWhirlpool = (FLAG_WHIRLPOOL & table.flags) != 0;

            DebugUtil.Debug("Table version: " + table.version + " | Flags: " + (table.flags == 1 ? "Y" : "N") + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"));

            table.validArchiveIds = new int[table.validArchivesCount];

            int k = 0, lastArchiveId = 0;

            for(int index = 0; index < table.validArchivesCount; index++) {
                int archiveId = lastArchiveId += stream.ReadUnsignedShort();
                table.validArchiveIds[index] = archiveId;
                table.entries.Add(archiveId, new Entry(k++));
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

            //crc
            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetCrc(stream.ReadInt());

            //versions
            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetVersion(stream.ReadInt());

            //validfileid lengths
            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetValidFileIds(new int[stream.ReadUnsignedShort()]);


            for(int index = 0; index < table.validArchivesCount; index++) {
                int lastFileId = 0;
                Entry entry = table.entries[table.validArchiveIds[index]];

                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++) {
                    int fileId = lastFileId += stream.ReadUnsignedShort();
                    entry.GetValidFileIds()[index2] = fileId;
                }

                entry.SetFiles(new SortedDictionary<int?, ChildEntry>());

                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                    entry.GetEntries()[entry.GetValidFileIds()[index2]] = new ChildEntry();
            }

            if(table.named) {
                for(int index = 0; index < table.validArchivesCount; index++) {
                    Entry entry = table.entries[table.validArchiveIds[index]];
                    for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                        entry.GetEntries()[entry.GetValidFileIds()[index2]].SetNameHash(stream.ReadInt());
                }
            }

            return table;
        }

        internal void UpdateStream(JagStream stream) {
            this.stream = stream;
        }

        /// <summary>
        /// Writes the RSReferenceTable
        /// </summary>
        /// <returns>The reference table</returns>
        internal JagStream Encode() {
            JagStream stream = new JagStream();

            stream.WriteByte((byte) format);

            if(format >= 6)
                stream.WriteInteger(version);

            stream.WriteByte((byte) flags);
            stream.WriteShort(entries.Count);

            foreach(KeyValuePair<int, Entry> kvp in entries)
                stream.WriteShort(kvp.Key);

            if(named)
                foreach(KeyValuePair<int, Entry> kvp in entries)
                    stream.WriteInteger(kvp.Value.GetNameHash());

            if(usesWhirlpool)
                foreach(KeyValuePair<int, Entry> kvp in entries)
                    stream.Write(kvp.Value.GetWhirlpool(), 0, 64);

            foreach(KeyValuePair<int, Entry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetCrc());

            foreach(KeyValuePair<int, Entry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetVersion());

            foreach(KeyValuePair<int, Entry> kvp in entries)
                stream.WriteInteger(kvp.Value.GetValidFileIds().Length);

            foreach(KeyValuePair<int, Entry> kvp in entries)
                for(int k = 0; k < kvp.Value.GetValidFileIds().Length; k++)
                    stream.WriteShort(kvp.Value.GetValidFileIds()[k]);

            for(int index = 0; index < validArchivesCount; index++) {
                Entry entry = entries[validArchiveIds[index]];
                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                    stream.WriteShort(entry.GetValidFileIds()[index2]);
            }

            if(named)
                foreach(KeyValuePair<int, Entry> kvp in entries)
                    for(int index2 = 0; index2 < kvp.Value.GetValidFileIds().Length; index2++) {
                        //Should actually recalculate the name hash when entries are edited
                        stream.WriteInteger(kvp.Value.GetEntries()[index2].CalculateNameHash());
                    }
            return stream.Flip();
        }

        /// <summary>
        /// Gets the maximum number of entries in this table.
        /// </summary>
        /// <returns>The maximum number of entries</returns>
        public int Capacity() {
            if(entries.Count == 0)
                return 0;
            return (int) entries.Keys.Last() + 1;
        }

        /// <summary>
        /// Returns the specified entry
        /// </summary>
        /// <param name="id">The entry index</param>
        /// <returns>The entry at the index <paramref name="id"/></returns>
        internal Entry GetEntry(int id) {
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

        internal SortedDictionary<int, Entry> GetEntries() {
            return entries;
        }
    }
}
