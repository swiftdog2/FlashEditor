using FlashEditor.Cache.CheckSum;
using FlashEditor.utils;
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

        internal JagStream stream;
        private RSIdentifiers identifiers;

        /// <summary>
        /// Constructs a reference table from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the reference table Container data</param>
        /// <returns>The reference table</returns>
        internal static RSReferenceTable Decode(JagStream stream) {
            DebugUtil.Debug("Decoding reference table");

            //Create a new Reference Table
            RSReferenceTable table = new RSReferenceTable();

            //Read header
            table.format = stream.ReadUnsignedByte();
            if(table.format >= 6)
                table.version = stream.ReadInt();
            table.flags = stream.ReadUnsignedByte();

            table.named = (FLAG_IDENTIFIERS & table.flags) != 0;
            table.usesWhirlpool = (FLAG_WHIRLPOOL & table.flags) != 0;

            table.validArchivesCount = stream.ReadUnsignedShort(); // table.format >= 7 ? stream.ReadSmart() : stream.ReadUnsignedShort();
            table.validArchiveIds = new int[table.validArchivesCount];

            DebugUtil.Debug("Table version: " + table.version + " | Flags: " + table.flags + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"));

            int k = 0, lastArchiveId = 0, size = -1;

            for(int index = 0; index < table.validArchivesCount; index++) {
                int archiveId = lastArchiveId += stream.ReadUnsignedShort();
                table.validArchiveIds[index] = archiveId;
                 table.entries.Add(table.validArchiveIds[index], new RSEntry(k++));

                if(archiveId >= size)
                    size = archiveId;
            }

            //If named, set the name hash for the archive

            /*
            if(table.named)
                for(int index = 0; index < table.validArchivesCount; index++)
                    table.entries[table.validArchiveIds[index]].SetNameHash(stream.ReadInt());
            */

            //Read the identifiers if present AKA name hashes
            //FYI this was/is? extremely fucky lol
            int[] identifiersArray = new int[table.validArchivesCount];
            if(table.named) {
                DebugUtil.Debug("Identifiers hash detected. archive ids: " + table.validArchiveIds.Length);
                for(int id = 0; id < table.validArchivesCount; id++) {
                    int identifier = stream.ReadInt();
                    //DebugUtil.Debug("id: " + id + ", valid:" + table.validArchiveIds.Length + ", entries:" + table.entries.Count + ", idents:" + identifiersArray.Length);

                    identifiersArray[id] = identifier;
                    if(table.entries.ContainsKey(id))
                        table.entries[id].identifier = identifier;
                    else
                        DebugUtil.Debug("Missing entry " + id);
                }
            }
            DebugUtil.Debug("Finished reading identifiers");
            table.identifiers = new RSIdentifiers(identifiersArray);

            //Read the whirlpool digests if present
            if(table.usesWhirlpool) {
                DebugUtil.Debug("Uses whirlpool...");
                foreach(int id in table.validArchiveIds) {
                    byte[] whirlpool = new byte[64];
                    stream.Read(whirlpool, 0, 64);
                    table.entries[id].SetWhirlpool(whirlpool);
                }
            }

            //Read the CRC Checksums
            foreach(int id in table.validArchiveIds)
                table.entries[id].SetCrc(stream.ReadInt());

            /*
            //Read another hash if present
           
            if((table.flags & FLAG_HASH) != 0) {
                DebugUtil.Debug("Special hash detected.");
                foreach(int id in table.validArchiveIds)
                    table.entries[id].hash = stream.ReadInt();
            }*/

            /*
            //Read the sizes of the archive
            if((table.flags & FLAG_SIZES) != 0) {
                DebugUtil.Debug("Reading sizes of archives");
                foreach(int id in table.validArchiveIds) {
                    table.entries[id].compressed = stream.ReadInt();
                    table.entries[id].uncompressed = stream.ReadInt();
                }
            }*/

            //Versions
            foreach(int id in table.validArchiveIds)
                table.entries[id].SetVersion(stream.ReadInt());

            //Read the child sizes (validfileid lengths)
            foreach(int id in table.validArchiveIds)
                table.entries[id].SetValidFileIds(new int[stream.ReadUnsignedShort() /*table.format >= 7 ? stream.ReadSmart() : stream.ReadUnsignedShort()*/]);

            //Read the child ids
            foreach(int id in table.validArchiveIds) {
                int lastFileId = 0; //accumulator
                RSEntry entry = table.entries[id];
                int[] fileIds = entry.GetValidFileIds();

                //
                for(int index2 = 0; index2 < fileIds.Length; index2++) {
                    int fileId = lastFileId += stream.ReadUnsignedShort();
                    fileIds[index2] = fileId;
                }

                int x = 0;
                foreach(int child in fileIds)
                    entry.GetEntries().Add(child, new RSChildEntry(x++));
            }

            //If the table is named
            if(table.named) {
                //Read the child identifiers (name hashes) also
                foreach(int id in table.validArchiveIds) {
                    RSEntry entry = table.entries[id];
                    int[] fileIds = entry.GetValidFileIds();

                    //Read the name hashes for the entries
                    foreach(int fileId in fileIds)
                        entry.GetEntries()[fileId].SetNameHash(stream.ReadInt());
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

            foreach(KeyValuePair<int, RSEntry> kvp in entries)
                stream.WriteShort(kvp.Key);

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
                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                    stream.WriteShort(entry.GetValidFileIds()[index2]);
            }

            if(named)
                foreach(KeyValuePair<int, RSEntry> kvp in entries)
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
    }
}
