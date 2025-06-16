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

        private RSIdentifiers identifiers;

        /// <summary>
        /// Constructs a reference table from the stream data
        /// </summary>
        /// <param name="stream">The stream containing the reference table Container data</param>
        /// <returns>The reference table</returns>
        public static RSReferenceTable Decode(JagStream stream) {
            Debug("Decoding reference table", LOG_DETAIL.ADVANCED);

            RSReferenceTable table = new RSReferenceTable();

            table.format = stream.ReadUnsignedByte();

            //If the table is versioned
            if(table.format >= 6)
                table.version = stream.ReadInt();

            //Calculate the flags for this table
            table.flags = stream.ReadUnsignedByte();
            table.hasIdentifiers = (FLAG_IDENTIFIERS & table.flags) != 0;
            table.usesWhirlpool = (FLAG_WHIRLPOOL & table.flags) != 0;
            table.entryHashes = (FLAG_HASH & table.flags) != 0;
            table.sizes = (FLAG_SIZES & table.flags) != 0;

            table.validArchivesCount = stream.ReadUnsignedShort();

            Debug("Table version: " + table.version + " | Format: " + table.format + " | Flags: " + DebugUtil.ToBitString(table.flags) + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"), LOG_DETAIL.ADVANCED);

            table.validArchiveIds = new int[table.validArchivesCount];

            int k = 0, lastArchiveId = 0;

            //Read the delta-encoded archive IDs
            for(int index = 0; index < table.validArchivesCount; index++) {
                int val = stream.ReadUnsignedShort();
                int archiveId = lastArchiveId += val;
                table.validArchiveIds[index] = archiveId;
                table.entries.Add(archiveId, new RSEntry(k++));
            }

            //Identifier hashers
            int[] identifiersArray = new int[table.GetEntries().Keys.Max() + 1];
            if(table.hasIdentifiers) {
                foreach(KeyValuePair<int, RSEntry> kvp in table.GetEntries()) {
                    int identifier = stream.ReadInt();
                    identifiersArray[kvp.Key] = identifier;
                    kvp.Value.SetIdentifier(identifier);
                }
            }
            table.identifiers = new RSIdentifiers(identifiersArray);

            //CRC checksums
            for(int index = 0; index < table.validArchivesCount; index++)
                table.entries[table.validArchiveIds[index]].SetCrc(stream.ReadInt());

            //Read the entry hash if present
            if(table.entryHashes)
                foreach(KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                    kvp.Value.SetHash(stream.ReadInt());

            //If the archive uses whirlpool, set the whirlpool hash
            if(table.usesWhirlpool) {
                for(int index = 0; index < table.validArchivesCount; index++) {
                    Span<byte> whirpool = stackalloc byte[64];
                    stream.Read(whirpool);
                    table.entries[table.validArchiveIds[index]].SetWhirlpool(whirpool);
                }
            }

            //Read the sizes of the archive
            if(table.sizes) {
                foreach(KeyValuePair<int, RSEntry> kvp in table.entries) {
                    kvp.Value.compressed = stream.ReadInt();
                    kvp.Value.uncompressed = stream.ReadInt();
                }
            }

            //Versions
            foreach(KeyValuePair<int, RSEntry> kvp in table.entries)
                kvp.Value.SetVersion(stream.ReadInt());

            //Child sizes
            foreach(KeyValuePair<int, RSEntry> kvp in table.entries)
                kvp.Value.SetValidFileIds(new int[stream.ReadUnsignedShort()]);

            //Child IDs
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
                entry.SetChildEntries(new SortedDictionary<int, RSChildEntry>());
                for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                    entry.GetChildEntries()[entry.GetValidFileIds()[index2]] = new RSChildEntry();
            }

            if(table.hasIdentifiers) {
                for(int index = 0; index < table.validArchivesCount; index++) {
                    RSEntry entry = table.entries[table.validArchiveIds[index]];
                    for(int index2 = 0; index2 < entry.GetValidFileIds().Length; index2++)
                        entry.GetChildEntries()[entry.GetValidFileIds()[index2]].SetHash(stream.ReadInt());
                }
            }

            return table;
        }

        public void PutEntry(int containerId, RSEntry entry) {
            if(entries.ContainsKey(containerId))
                entries[containerId] = entry;
            else
                entries.Add(containerId, entry);
        }

        /// <summary>
        /// Writes the RSReferenceTable
        /// </summary>
        /// <returns>The reference table</returns>
        public JagStream Encode() {
            Debug("Encoding Reference Table " + type);

            JagStream stream = new JagStream();

            var sb = new StringBuilder();

            Debug("\tOUT Table version: " + version + " | Format: " + format + " | Flags: " + (flags == 1 ? "Y" : "N") + " | Archives: " + validArchivesCount + " | Whirl: " + (usesWhirlpool ? "Y" : "N"), LOG_DETAIL.ADVANCED);

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
                //Debug("Delta: " + delta + ", key: " + kvp.Key + ", last: " + last);
            }

            if(hasIdentifiers) {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                    int ident = kvp.Value.GetIdentifier();
                    sb.Clear();
                    sb.Append('\t').Append('-').Append(ident);
                    Debug(sb.ToString());
                    stream.WriteInteger(ident);
                }
            }

            Debug("Writing CRCs", LOG_DETAIL.INSANE);
            foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                int crc = kvp.Value.GetCrc();
                sb.Clear();
                sb.Append('\t').Append('|').Append(crc);
                Debug(sb.ToString());
                stream.WriteInteger(kvp.Value.GetCrc());
            }

            if(entryHashes)
                foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                    int hash = kvp.Value.CalculateHash();
                    stream.WriteInteger(hash);
                    sb.Clear();
                    sb.Append('\t').Append('|').Append(hash);
                    Debug(sb.ToString());
                }

            if(usesWhirlpool) {
                Debug("Writing whirlpool hash", LOG_DETAIL.INSANE);
                foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                    byte[] whirl = kvp.Value.GetWhirlpool();
                    PrintByteArray(whirl);
                    stream.Write(kvp.Value.GetWhirlpool(), 0, 64);
                }
            }

            //The sizes of the archive
            if(sizes) {
                foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                    //Needs to be updated to recalculate these values!!
                    int comp = kvp.Value.compressed;
                    int uncomp = kvp.Value.uncompressed;
                    stream.WriteInteger(kvp.Value.compressed);
                    stream.WriteInteger(kvp.Value.uncompressed);
                    sb.Clear();
                    sb.Append("\t|comp: ");
                    sb.Append(comp);
                    sb.Append(", uncomp: ");
                    sb.Append(uncomp);
                    Debug(sb.ToString());
                }
            }

            Debug("Writing versions", LOG_DETAIL.INSANE);
            foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                int version = kvp.Value.GetVersion();
                stream.WriteInteger(kvp.Value.GetVersion());
                sb.Clear();
                sb.Append('\t').Append('|').Append(version);
                Debug(sb.ToString());
            }

            Debug("Writing number of non-null child entries", LOG_DETAIL.INSANE);
            foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                int nnce = kvp.Value.GetChildEntries().Count;
                stream.WriteShort(nnce);
                sb.Clear();
                sb.Append('\t').Append('|').Append(nnce);
                Debug(sb.ToString());
            }

            Debug("Writing child IDs", LOG_DETAIL.INSANE);
            foreach(KeyValuePair<int, RSEntry> kvp in entries) {
                last = 0;
                for(int id = 0; id < kvp.Value.GetChildEntries().Count; id++) {
                    int delta = id - last;
                    stream.WriteShort(delta);
                    //Debug("\t|" + delta, LOG_DETAIL.INSANE);
                    last = id;
                }
            }

            if(hasIdentifiers) {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach(KeyValuePair<int, RSEntry> kvp in entries)
                    foreach(KeyValuePair<int, RSChildEntry> child in kvp.Value.GetChildEntries()) {
                        int childIdent = child.Value.GetIdentifier();
                        stream.WriteInteger(childIdent);
                        sb.Clear();
                        sb.Append('\t').Append('|').Append(childIdent);
                        Debug(sb.ToString());
                    }
            }

            Debug("...finished, stream len: " + stream.Length);
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
