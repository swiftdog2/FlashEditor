using FlashEditor.Cache.CheckSum;
using static FlashEditor.utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.utils;

namespace FlashEditor.cache
{
    /// <summary>
    /// Static helpers for encoding and decoding <see cref="RSReferenceTable"/> payloads.
    /// </summary>
    public static class ReferenceTableCodec
    {
        /// <summary>
        ///     Parses a reference-table container payload into an <see cref="RSReferenceTable"/> instance.
        /// </summary>
        /// <param name="stream">Positioned at the start of the container payload.</param>
        /// <returns>The populated reference table.</returns>
        public static RSReferenceTable Decode(JagStream stream)
        {
            Debug("Decoding reference table", LOG_DETAIL.ADVANCED);

            RSReferenceTable table = new RSReferenceTable();

            /* ── Table header ───────────────────────────────────────── */
            table.format = stream.ReadByte();
            if (table.format >= 6)
                table.version = stream.ReadInt();

            table.flags = stream.ReadByte();
            table.hasIdentifiers = (table.flags & RSReferenceTable.FLAG_IDENTIFIERS) != 0;
            table.usesWhirlpool = (table.flags & RSReferenceTable.FLAG_WHIRLPOOL) != 0;
            table.entryHashes  = (table.flags & RSReferenceTable.FLAG_HASH) != 0;
            table.sizes        = (table.flags & RSReferenceTable.FLAG_SIZES) != 0;

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
                table.GetEntries().Add(lastArchiveId, new RSEntry(i));
            }

            /* ── Optional 32-bit identifier hashes ─────────────────── */
            int[] identifiersTmp = new int[table.GetEntries().Keys.Max() + 1];
            if (table.hasIdentifiers)
            {
                foreach (var kv in table.GetEntries())
                {
                    int ident = stream.ReadInt();
                    identifiersTmp[kv.Key] = ident;
                    kv.Value.SetIdentifier(ident);
                }
            }
            table.identifiers = new RSIdentifiers(identifiersTmp);

            /* ── CRC-32 for each archive (always) ───────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
                table.GetEntries()[table.validArchiveIds[i]].SetCrc(stream.ReadInt());

            /* ── Archive versions (always) ──────────────────────────── */
            foreach (var kv in table.GetEntries())
                kv.Value.SetVersion(stream.ReadInt());

            /* ── Archive-flags (format 7+)  bit-0 ⇢ XTEA ───────────── */
            if (table.format >= 7)
            {
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    byte flagByte = (byte) stream.ReadByte();
                    table.GetEntries()[table.validArchiveIds[i]].UsesXtea = (flagByte & 0x01) != 0;
                }
            }

            /* ── Optional entry hash (32-bit) ───────────────────────── */
            if (table.entryHashes)
                foreach (var kv in table.GetEntries())
                    kv.Value.SetHash(stream.ReadInt());

            /* ── Optional Whirlpool digests (64 bytes each) ─────────── */
            if (table.usesWhirlpool)
            {
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    Span<byte> whirl = stackalloc byte[64];
                    stream.Read(whirl);
                    table.GetEntries()[table.validArchiveIds[i]].SetWhirlpool(whirl);
                }
            }

            /* ── Optional compressed / uncompressed sizes ───────────── */
            if (table.sizes)
            {
                foreach (var kv in table.GetEntries())
                {
                    kv.Value.compressed = stream.ReadInt();
                    kv.Value.uncompressed = stream.ReadInt();
                }
            }

            /* ── Child counts (one 16-bit per archive) ──────────────── */
            foreach (var kv in table.GetEntries())
                kv.Value.SetValidFileIds(new int[stream.ReadUnsignedShort()]);

            /* ── Child IDs, delta-encoded ───────────────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
            {
                RSEntry entry = table.GetEntries()[table.validArchiveIds[i]];
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
                    RSEntry entry = table.GetEntries()[table.validArchiveIds[i]];
                    foreach (int fileId in entry.GetValidFileIds())
                        entry.GetChildEntries()[fileId].SetHash(stream.ReadInt());
                }
            }

            return table;
        }

        /// <summary>
        /// Writes the provided table to a new <see cref="JagStream"/>.
        /// </summary>
        public static JagStream Encode(RSReferenceTable table)
        {
            Debug("Encoding Reference Table " + table.type);

            JagStream stream = new JagStream();
            var sb = new System.Text.StringBuilder();

            Debug("\tOUT Table version: " + table.version + " | Format: " + table.format + " | Flags: " + (table.flags == 1 ? "Y" : "N") + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"), LOG_DETAIL.ADVANCED);

            stream.WriteByte((byte)table.format);

            if (table.format >= 6)
                stream.WriteInteger(table.version);

            stream.WriteByte((byte)table.flags);
            stream.WriteShort(table.GetEntries().Count);

            int last = 0;
            foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
            {
                int delta = kvp.Key - last;
                last = kvp.Key;
                stream.WriteShort(delta);
            }

            if (table.hasIdentifiers)
            {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                {
                    int ident = kvp.Value.GetIdentifier();
                    sb.Clear();
                    sb.Append('\t').Append('-').Append(ident);
                    Debug(sb.ToString());
                    stream.WriteInteger(ident);
                }
            }

            Debug("Writing CRCs", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
            {
                int crc = kvp.Value.GetCrc();
                sb.Clear();
                sb.Append('\t').Append('|').Append(crc);
                Debug(sb.ToString());
                stream.WriteInteger(kvp.Value.GetCrc());
            }

            if (table.entryHashes)
                foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                {
                    int hash = kvp.Value.CalculateHash();
                    stream.WriteInteger(hash);
                    sb.Clear();
                    sb.Append('\t').Append('|').Append(hash);
                    Debug(sb.ToString());
                }

            if (table.usesWhirlpool)
            {
                Debug("Writing whirlpool hash", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                {
                    byte[] whirl = kvp.Value.GetWhirlpool();
                    PrintByteArray(whirl);
                    stream.Write(kvp.Value.GetWhirlpool(), 0, 64);
                }
            }

            if (table.sizes)
            {
                foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                {
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
            foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
            {
                int version = kvp.Value.GetVersion();
                stream.WriteInteger(kvp.Value.GetVersion());
                sb.Clear();
                sb.Append('\t').Append('|').Append(version);
                Debug(sb.ToString());
            }

            Debug("Writing number of non-null child entries", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
            {
                int nnce = kvp.Value.GetChildEntries().Count;
                stream.WriteShort(nnce);
                sb.Clear();
                sb.Append('\t').Append('|').Append(nnce);
                Debug(sb.ToString());
            }

            Debug("Writing child IDs", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
            {
                last = 0;
                for (int id = 0; id < kvp.Value.GetChildEntries().Count; id++)
                {
                    int delta = id - last;
                    stream.WriteShort(delta);
                    last = id;
                }
            }

            if (table.hasIdentifiers)
            {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSEntry> kvp in table.GetEntries())
                    foreach (KeyValuePair<int, RSChildEntry> child in kvp.Value.GetChildEntries())
                    {
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
    }
}
