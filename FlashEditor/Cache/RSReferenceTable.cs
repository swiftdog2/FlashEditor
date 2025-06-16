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
        ///     Parses a reference-table container payload into an <see cref="RSReferenceTable"/> instance.
        ///     Handles formats 5 → 7.  Archive-flags (bit-0 ⇢ XTEA) are present only in format 7+
        ///     and are read <em>after</em> the per-archive version integers (matching on-disk order).
        /// </summary>
        /// <param name="stream">
        ///     A <see cref="JagStream"/> positioned at the start of the container’s payload
        ///     (i.e. immediately after the 5- or 9-byte container header).
        /// </param>
        /// <returns>The populated reference table.</returns>
        public static RSReferenceTable Decode(JagStream stream)
        {
            Debug("Decoding reference table", LOG_DETAIL.ADVANCED);

            RSReferenceTable table = new RSReferenceTable();

            /* ── Table header ───────────────────────────────────────── */
            table.format = stream.ReadUnsignedByte();
            if (table.format >= 6)
                table.version = stream.ReadInt();

            table.flags = stream.ReadUnsignedByte();
            table.hasIdentifiers = (table.flags & FLAG_IDENTIFIERS) != 0;
            table.usesWhirlpool = (table.flags & FLAG_WHIRLPOOL) != 0;
            table.entryHashes = (table.flags & FLAG_HASH) != 0;
            table.sizes = (table.flags & FLAG_SIZES) != 0;

            table.validArchivesCount = stream.ReadUnsignedShort();

            Debug($"Table v{table.version} | fmt {table.format} | flags {DebugUtil.ToBitString(table.flags)} | " +
                  $"archives {table.validArchivesCount}", LOG_DETAIL.ADVANCED);

            /* ── Delta-encoded archive IDs ──────────────────────────── */
            table.validArchiveIds = new int[table.validArchivesCount];

            int lastArchiveId = 0;
            for (int i = 0; i < table.validArchivesCount; i++)
            {
                lastArchiveId += stream.ReadUnsignedShort();
                table.validArchiveIds[i] = lastArchiveId;
                table.entries.Add(lastArchiveId, new RSEntry(i));
            }

            /* ── Optional 32-bit identifier hashes ─────────────────── */
            int[] identifiersTmp = new int[table.entries.Keys.Max() + 1];
            if (table.hasIdentifiers)
            {
                foreach (var kv in table.entries)
                {
                    int ident = stream.ReadInt();
                    identifiersTmp[kv.Key] = ident;
                    kv.Value.SetIdentifier(ident);
                }
            }
            table.identifiers = new RSIdentifiers(identifiersTmp);

            /* ── CRC-32 for each archive (always) ───────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
                table.entries[table.validArchiveIds[i]].SetCrc(stream.ReadInt());

            /* ── Archive versions (always) ──────────────────────────── */
            foreach (var kv in table.entries)
                kv.Value.SetVersion(stream.ReadInt());

            /* ── Archive-flags (format 7+)  bit-0 ⇢ XTEA ───────────── */
            if (table.format >= 7)
            {
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    byte flagByte = stream.ReadUnsignedByte();
                    table.entries[table.validArchiveIds[i]].UsesXtea = (flagByte & 0x01) != 0;
                    /*  Further bits can be stored if future client revisions use them. */
                }
            }

            /* ── Optional entry hash (32-bit) ───────────────────────── */
            if (table.entryHashes)
                foreach (var kv in table.entries)
                    kv.Value.SetHash(stream.ReadInt());

            /* ── Optional Whirlpool digests (64 bytes each) ─────────── */
            if (table.usesWhirlpool)
            {
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    Span<byte> whirl = stackalloc byte[64];
                    stream.Read(whirl);
                    table.entries[table.validArchiveIds[i]].SetWhirlpool(whirl);
                }
            }

            /* ── Optional compressed / uncompressed sizes ───────────── */
            if (table.sizes)
            {
                foreach (var kv in table.entries)
                {
                    kv.Value.compressed = stream.ReadInt();
                    kv.Value.uncompressed = stream.ReadInt();
                }
            }

            /* ── Child counts (one 16-bit per archive) ──────────────── */
            foreach (var kv in table.entries)
                kv.Value.SetValidFileIds(new int[stream.ReadUnsignedShort()]);

            /* ── Child IDs, delta-encoded ───────────────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
            {
                RSEntry entry = table.entries[table.validArchiveIds[i]];
                int lastFileId = 0;

                for (int j = 0; j < entry.GetValidFileIds().Length; j++)
                {
                    lastFileId += stream.ReadUnsignedShort();
                    entry.GetValidFileIds()[j] = lastFileId;
                }

                entry.SetChildEntries(new SortedDictionary<int, RSChildEntry>());
                foreach (int id in entry.GetValidFileIds())
                    entry.GetChildEntries()[id] = new RSChildEntry();
            }

            /* ── Optional per-child hashes when identifiers present ─── */
            if (table.hasIdentifiers)
            {
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    RSEntry entry = table.entries[table.validArchiveIds[i]];
                    foreach (int fileId in entry.GetValidFileIds())
                        entry.GetChildEntries()[fileId].SetHash(stream.ReadInt());
                }
            }

            return table;
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
