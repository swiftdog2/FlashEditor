using FlashEditor.Cache.CheckSum;
using static FlashEditor.Utils.DebugUtil;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Utils;

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

            //hasIdentifiers, usesWhirlpool, entryHashes and sizes all read off this byte
            table.flags = stream.ReadByte();

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
                table.GetArchiveEntries().Add(lastArchiveId, new RSArchiveEntry(i));
            }

            /* ── Optional 32-bit identifier hashes ─────────────────── */
            int[] identifiersTmp = new int[table.GetArchiveEntries().Keys.Max() + 1];
            if (table.hasIdentifiers)
            {
                foreach (var kv in table.GetArchiveEntries())
                {
                    int ident = stream.ReadInt();
                    identifiersTmp[kv.Key] = ident;
                    kv.Value.SetIdentifier(ident);
                }
            }
            table.identifiers = new RSIdentifiers(identifiersTmp);

            /* ── CRC-32 for each archive (always) ───────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
                table.GetArchiveEntries()[table.validArchiveIds[i]].SetCrc(stream.ReadInt());

            /* ── Optional entry hash (32-bit) ───────────────────────── */
            if (table.entryHashes)
                foreach (var kv in table.GetArchiveEntries())
                    kv.Value.SetHash(stream.ReadInt());

            /* ── Optional Whirlpool digests (64 bytes each) ─────────── */
            if (table.usesWhirlpool)
            {
                /* One buffer for the whole loop. A stackalloc inside the loop body is not
                   reclaimed until Decode returns, and validArchivesCount is an unbounded
                   16-bit field read from the file, so a corrupt table could burn up to
                   65535 * 64 bytes (~4 MB) of stack and kill the process.

                   Clear() before each Read preserves the old semantics exactly: JagStream.Read
                   returns a short count at EOF, and each fresh stackalloc was zero-initialised,
                   so the unread tail must stay zero rather than carry the previous archive's
                   digest forward. SetWhirlpool copies the span into the entry's own byte[64]. */
                Span<byte> whirl = stackalloc byte[64];
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    whirl.Clear();
                    stream.Read(whirl);
                    table.GetArchiveEntries()[table.validArchiveIds[i]].SetWhirlpool(whirl);
                }
            }

            /* ── Optional compressed / uncompressed sizes ───────────── */
            if (table.sizes)
            {
                foreach (var kv in table.GetArchiveEntries())
                {
                    kv.Value.compressed = stream.ReadInt();
                    kv.Value.uncompressed = stream.ReadInt();
                }
            }

            /* ── Archive versions (always) ──────────────────────────── */
            foreach (var kv in table.GetArchiveEntries())
                kv.Value.SetVersion(stream.ReadInt());

            /* ── Archive-flags (format 7+)  bit-0 ⇢ XTEA ───────────── */
            if (table.format >= 7)
            {
                //Keep the whole byte, not just the XTEA bit - see RSArchiveEntry.ArchiveFlags.
                for (int i = 0; i < table.validArchivesCount; i++)
                    table.GetArchiveEntries()[table.validArchiveIds[i]].ArchiveFlags = (byte) stream.ReadByte();
            }

            /* ── File counts (one 16-bit per archive) ──────────────── */
            foreach (var kv in table.GetArchiveEntries())
                kv.Value.SetValidFileIds(new int[stream.ReadUnsignedShort()]);

            /* ── File IDs, delta-encoded ───────────────────────────── */
            for (int i = 0; i < table.validArchivesCount; i++)
            {
                RSArchiveEntry entry = table.GetArchiveEntries()[table.validArchiveIds[i]];
                int lastFileId = 0;

                for (int j = 0; j < entry.GetValidFileIds().Length; j++)
                {
                    lastFileId += stream.ReadUnsignedShort();
                    entry.GetValidFileIds()[j] = lastFileId;
                }

                entry.SetFileEntries(new SortedDictionary<int, RSFileEntry>());
                foreach (int id in entry.GetValidFileIds())
                    entry.GetFileEntries()[id] = new RSFileEntry();
            }

            /* ── Optional per-file identifiers when identifiers present ─── */
            if (table.hasIdentifiers)
            {
                //These are name hashes, the per-file counterpart of the archive identifiers
                //read above, and Encode writes them back from GetIdentifier. Storing them in
                //the separate hash field would drop every file name on the next encode.
                for (int i = 0; i < table.validArchivesCount; i++)
                {
                    RSArchiveEntry entry = table.GetArchiveEntries()[table.validArchiveIds[i]];
                    foreach (int fileId in entry.GetValidFileIds())
                        entry.GetFileEntries()[fileId].SetIdentifier(stream.ReadInt());
                }
            }

            return table;
        }

        /// <summary>
        /// Writes the provided table to a new <see cref="JagStream"/>.
        /// </summary>
        public static JagStream Encode(RSReferenceTable table)
        {
            Debug("Encoding Reference Table " + table.indexId);

            JagStream stream = new JagStream();
            var sb = new System.Text.StringBuilder();

            Debug("\tOUT Table version: " + table.version + " | Format: " + table.format + " | Flags: " + (table.flags == 1 ? "Y" : "N") + " | Archives: " + table.validArchivesCount + " | Whirl: " + (table.usesWhirlpool ? "Y" : "N"), LOG_DETAIL.ADVANCED);

            stream.WriteByte((byte)table.format);

            if (table.format >= 6)
                stream.WriteInteger(table.version);

            stream.WriteByte((byte)table.flags);
            stream.WriteShort(table.GetArchiveEntries().Count);

            int last = 0;
            foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
            {
                int delta = kvp.Key - last;
                last = kvp.Key;
                stream.WriteShort(delta);
            }

            if (table.hasIdentifiers)
            {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
                {
                    int ident = kvp.Value.GetIdentifier();
                    sb.Clear();
                    sb.Append('\t').Append('-').Append(ident);
                    Debug(sb.ToString());
                    stream.WriteInteger(ident);
                }
            }

            Debug("Writing CRCs", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
            {
                int crc = kvp.Value.GetCrc();
                sb.Clear();
                sb.Append('\t').Append('|').Append(crc);
                Debug(sb.ToString());
                stream.WriteInteger(kvp.Value.GetCrc());
            }

            //Write back the hash that was read off the wire. CalculateHash() digests the
            //entry's own stream, which the codec never populates, so calling it here
            //would replace every shipped hash with the digest of zero bytes.
            if (table.entryHashes)
            {
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
                {
                    int hash = (int)kvp.Value.GetHash();
                    stream.WriteInteger(hash);
                    sb.Clear();
                    sb.Append('\t').Append('|').Append(hash);
                    Debug(sb.ToString());
                }
            }

            if (table.usesWhirlpool)
            {
                Debug("Writing whirlpool hash", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
                {
                    byte[] whirl = kvp.Value.GetWhirlpool();
                    PrintByteArray(whirl);
                    stream.Write(kvp.Value.GetWhirlpool(), 0, 64);
                }
            }

            if (table.sizes)
            {
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
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
            foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
            {
                int version = kvp.Value.GetVersion();
                stream.WriteInteger(kvp.Value.GetVersion());
                sb.Clear();
                sb.Append('\t').Append('|').Append(version);
                Debug(sb.ToString());
            }

            //Per-archive flags byte, bit 0 being the XTEA marker. Decode reads this for
            //format 7+, so omitting it here shifts every field after it and corrupts the
            //table - and RSCache re-encodes the table on every edit. The raw byte goes back
            //out whole - see RSArchiveEntry.ArchiveFlags.
            if (table.format >= 7)
            {
                Debug("Writing archive flags", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
                    stream.WriteByte(kvp.Value.ArchiveFlags);
            }

            Debug("Writing number of non-null file entries", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
            {
                int nnce = kvp.Value.GetFileEntries().Count;
                stream.WriteShort(nnce);
                sb.Clear();
                sb.Append('\t').Append('|').Append(nnce);
                Debug(sb.ToString());
            }

            Debug("Writing file IDs", LOG_DETAIL.INSANE);
            foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
            {
                //Delta over the actual file ids. Walking an ordinal counter instead would
                //renumber every archive's files to 0..n-1 and lose sparse ids entirely.
                last = 0;
                foreach (int fileId in kvp.Value.GetFileEntries().Keys)
                {
                    stream.WriteShort(fileId - last);
                    last = fileId;
                }
            }

            if (table.hasIdentifiers)
            {
                Debug("Writing identifiers", LOG_DETAIL.INSANE);
                foreach (KeyValuePair<int, RSArchiveEntry> kvp in table.GetArchiveEntries())
                    foreach (KeyValuePair<int, RSFileEntry> file in kvp.Value.GetFileEntries())
                    {
                        int fileIdent = file.Value.GetIdentifier();
                        stream.WriteInteger(fileIdent);
                        sb.Clear();
                        sb.Append('\t').Append('|').Append(fileIdent);
                        Debug(sb.ToString());
                    }
            }

            Debug("...finished, stream len: " + stream.Length);
            return stream.Flip();
        }
    }
}
