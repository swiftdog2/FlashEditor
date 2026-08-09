using System;
using System.Collections.Generic;
using System.IO;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     The nineteen material columns, in the order the file stores them.
    /// </summary>
    /// <remarks>
    ///     The order is the format, read off the client's own reader: <c>Class260.java:114-208</c>
    ///     runs nineteen separate passes over the whole texture range, so a field's position in this
    ///     enum is where its bytes are and swapping two members silently mis-reads every record.
    ///     <para>
    ///     It is an enum rather than a comment because <see cref="TextureDefinition"/> reports which
    ///     column an edit touched, and a column that had to be named by an integer literal at each
    ///     of nineteen setters would drift from the layout the moment either changed.
    ///     </para>
    /// </remarks>
    public enum MaterialColumn {
        /// <summary>Pass 1, one byte, stored inverted (<c>Class260.java:116</c>).</summary>
        Field1825 = 0,

        /// <summary>Pass 2, one byte (<c>Class260.java:121</c>).</summary>
        Field1822,

        /// <summary>Pass 3, one byte (<c>Class260.java:126</c>).</summary>
        Field1833,

        /// <summary>Pass 4, one signed byte (<c>Class260.java:131</c>).</summary>
        Field1829,

        /// <summary>Pass 5, one signed byte (<c>Class260.java:136</c>).</summary>
        Field1830,

        /// <summary>Pass 6, one signed byte (<c>Class260.java:141</c>).</summary>
        Field1820,

        /// <summary>Pass 7, one signed byte (<c>Class260.java:146</c>).</summary>
        Field1816,

        /// <summary>Pass 8, two bytes (<c>Class260.java:151</c>).</summary>
        Field1831,

        /// <summary>Pass 9, one signed byte (<c>Class260.java:156</c>).</summary>
        Field1823,

        /// <summary>Pass 10, one signed byte (<c>Class260.java:161</c>).</summary>
        Field1837,

        /// <summary>Pass 11, one byte (<c>Class260.java:166</c>).</summary>
        Field1827,

        /// <summary>Pass 12, one byte (<c>Class260.java:171</c>).</summary>
        Field1824,

        /// <summary>Pass 13, one signed byte (<c>Class260.java:176</c>).</summary>
        Field1832,

        /// <summary>Pass 14, one byte (<c>Class260.java:181</c>).</summary>
        Field1826,

        /// <summary>Pass 15, one byte (<c>Class260.java:186</c>).</summary>
        Field1819,

        /// <summary>Pass 16, one byte (<c>Class260.java:191</c>).</summary>
        Field1817,

        /// <summary>Pass 17, one unsigned byte (<c>Class260.java:196</c>).</summary>
        Field1821,

        /// <summary>Pass 18, four bytes (<c>Class260.java:201</c>).</summary>
        Field1835,

        /// <summary>Pass 19, one unsigned byte (<c>Class260.java:206</c>).</summary>
        Field1818
    }

    /// <summary>
    ///     Index 26 in one object: the column-major material table the client wraps in
    ///     <c>Class260</c>, and the write path that puts an edited one back.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The whole index is one file.</b> <c>InterfaceSettings.java:244</c> builds
    ///     <c>new Class260(idx26, idx9, idx8)</c> and <c>Class260.java:106</c> reads
    ///     <c>getChildFromFolder(0, 0)</c>, so there is no per-texture record to address - a save
    ///     rewrites the entire table or none of it.
    ///     </para>
    ///     <para>
    ///     <b>The layout is column-major, not record-major.</b> A u16 count, one existence byte per
    ///     slot, then nineteen columns each holding one entry per <em>present</em> texture. A record
    ///     is therefore 23 bytes scattered across nineteen places in the file, which is why this type
    ///     carries the split rather than leaving it to a caller.
    ///     </para>
    ///     <para>
    ///     <b>Stored bytes beat recomputed ones, per column.</b> Every present texture keeps the 23
    ///     bytes it was decoded from, and a column is re-encoded from its field only when that field
    ///     was actually assigned a different value - see
    ///     <see cref="TextureDefinition.IsColumnDirty"/>. Three of the nineteen columns decode
    ///     many-to-one and cannot be recomputed: the boolean columns collapse every byte other than
    ///     the one they test to <c>false</c>, and the existence column collapses everything that is
    ///     not 1 to "absent". A per-column profile of the repack's records found no instance of
    ///     either - every boolean column strictly {0,1}, every existence byte 1 - so an encoder that
    ///     rebuilt those columns would sweep perfectly clean, and nothing here would say otherwise
    ///     until a cache that did hold one was opened. Per-column granularity is also what stops an
    ///     edit to one field rewriting a sibling's aliased byte.
    ///     </para>
    ///     <para>
    ///     <b>The count and the existence column are stored state, not derived.</b> They are replayed
    ///     from what was read rather than recomputed from whatever the editor is holding: the texture
    ///     dictionary also carries entries merged in from index 9, and deriving the table's shape from
    ///     it would grow index 26 by every texture that has a graph but no material record.
    ///     </para>
    /// </remarks>
    public sealed class MaterialTable {
        /// <summary>Bytes one present texture contributes, across all nineteen columns.</summary>
        /// <remarks>
        ///     Held to by exact consumption rather than asserted from the client alone:
        ///     <c>RealCacheMaterialTests</c> requires the file to be exactly
        ///     <c>2 + count + present * 23</c> bytes and the decoder to stop on its last byte, which
        ///     is what fixes all nineteen column widths at once.
        /// </remarks>
        public const int BytesPerRecord = 23;

        /// <summary>Columns the format defines.</summary>
        public const int ColumnCount = 19;

        /// <summary>
        ///     The file id the whole table is stored as within its group.
        /// </summary>
        /// <remarks>
        ///     Index 26 is <c>CacheAddressing.SingleGroup(0)</c>, so a definition id of 0 addresses
        ///     group 0 file 0 - the pair <c>Class260.java:106</c> asks for. Spelled through the
        ///     addressing rather than as two literals at the write site.
        /// </remarks>
        public const int WholeTableDefinitionId = 0;

        /// <summary>Width in bytes of each column, indexed by <see cref="MaterialColumn"/>.</summary>
        private static readonly int[] ColumnWidths =
            { 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 1, 4, 1 };

        /// <summary>Where each column starts within a record's 23 bytes.</summary>
        private static readonly int[] ColumnOffsets = BuildOffsets();

        /// <summary>The existence byte for every slot, exactly as stored.</summary>
        private readonly byte[] _existence;

        /// <summary>One entry per slot, null where the slot holds no texture.</summary>
        private readonly TextureDefinition[] _slots;

        private MaterialTable(int count) {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "A texture count is non-negative.");

            Count = count;
            _existence = new byte[count];
            _slots = new TextureDefinition[count];
        }

        /// <summary>Texture slots the table declares, present or not.</summary>
        /// <remarks>
        ///     The client sizes its own array from this and indexes it by texture id, so it is the
        ///     id space rather than a population - <c>Class260.java:108</c>.
        /// </remarks>
        public int Count { get; }

        /// <summary>Every slot in id order, null where the slot is absent.</summary>
        public IReadOnlyList<TextureDefinition> Slots => _slots;

        /// <summary>
        ///     Whether re-encoding this table would produce anything other than the bytes it was
        ///     read from.
        /// </summary>
        /// <remarks>
        ///     False for a table nobody has edited, which is what lets a save write nothing at all.
        ///     A present record with no stored bytes counts as dirty: it was built rather than
        ///     decoded, so there is nothing to replay for it.
        /// </remarks>
        public bool IsDirty {
            get {
                foreach (TextureDefinition def in _slots) {
                    if (def == null)
                        continue;
                    if (def.IsDirty || def.StoredRecord == null)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Where a column starts within a record's 23 bytes.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The byte offset.</returns>
        public static int OffsetOf(MaterialColumn column) => ColumnOffsets[(int) column];

        /// <summary>How many bytes a column stores per present texture.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The width in bytes.</returns>
        public static int WidthOf(MaterialColumn column) => ColumnWidths[(int) column];

        /// <summary>
        ///     Decodes the whole table, keeping every record's bytes alongside its fields.
        /// </summary>
        /// <remarks>
        ///     The bytes are read into each record's own buffer first and the fields derived from
        ///     that buffer afterwards, so the stored copy is a capture rather than a reconstruction -
        ///     a field whose decode throws information away still re-encodes to what it came from.
        ///     <para>
        ///     Nothing past the last column is consumed. The file has no trailer in either supported
        ///     cache, and reading whatever remained would make any file length look correct.
        ///     </para>
        /// </remarks>
        /// <param name="stream">The file, positioned at its start.</param>
        /// <returns>The decoded table.</returns>
        /// <exception cref="EndOfStreamException">The file is shorter than the count it declares.</exception>
        public static MaterialTable Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var table = new MaterialTable(stream.ReadUnsignedShort());
            var rows = new byte[table.Count][];

            for (int slot = 0; slot < table.Count; slot++) {
                byte existence = (byte) stream.ReadUnsignedByte();
                table._existence[slot] = existence;

                //The client's own test, and the reason the byte is kept rather than a bool: it
                //accepts exactly 1 and treats everything else as an empty slot (Class260.java:110).
                if (existence != 1)
                    continue;

                table._slots[slot] = new TextureDefinition { id = slot };
                rows[slot] = new byte[BytesPerRecord];
            }

            for (int column = 0; column < ColumnCount; column++) {
                int offset = ColumnOffsets[column];
                int width = ColumnWidths[column];

                for (int slot = 0; slot < table.Count; slot++) {
                    if (table._slots[slot] == null)
                        continue;

                    if (stream.Read(rows[slot], offset, width) != width)
                        throw new EndOfStreamException(
                            "Index 26 declares " + table.Count + " textures but ran out of bytes in column " +
                            (MaterialColumn) column + " at texture " + slot + ".");
                }
            }

            for (int slot = 0; slot < table.Count; slot++) {
                TextureDefinition def = table._slots[slot];
                if (def == null)
                    continue;

                Unpack(def, rows[slot]);
                def.StoredRecord = rows[slot];
                def.MarkClean();
            }

            return table;
        }

        /// <summary>Reads the material table out of an open cache.</summary>
        /// <param name="cache">The open cache.</param>
        /// <returns>The decoded table.</returns>
        public static MaterialTable Load(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            CacheAddressing addressing = CacheAddressing.For(RSConstants.MATERIALS);
            byte[] stored = cache.ReadFileBytes(RSConstants.MATERIALS,
                addressing.GroupOf(WholeTableDefinitionId), addressing.FileOf(WholeTableDefinitionId));
            return Decode(new JagStream(stored));
        }

        /// <summary>
        ///     Builds a table from loose definitions, for encoding a set that was never decoded.
        /// </summary>
        /// <remarks>
        ///     The slot is the dictionary key rather than <see cref="TextureDefinition.id"/>, because
        ///     the key is what every reader looks the definition up by. This shape has no stored
        ///     bytes at all, so it always encodes from fields.
        /// </remarks>
        /// <param name="definitions">The definitions, keyed by texture id.</param>
        /// <returns>A table spanning slot 0 to the highest key.</returns>
        public static MaterialTable FromDefinitions(IEnumerable<KeyValuePair<int, TextureDefinition>> definitions) {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));

            var present = new List<KeyValuePair<int, TextureDefinition>>();
            int count = 0;

            foreach (KeyValuePair<int, TextureDefinition> entry in definitions) {
                if (entry.Value == null)
                    continue;
                if (entry.Key < 0)
                    throw new ArgumentOutOfRangeException(nameof(definitions), entry.Key,
                        "A texture id is a slot in the material table, so it cannot be negative.");

                present.Add(entry);
                if (entry.Key >= count)
                    count = entry.Key + 1;
            }

            var table = new MaterialTable(count);
            foreach (KeyValuePair<int, TextureDefinition> entry in present) {
                table._slots[entry.Key] = entry.Value;
                table._existence[entry.Key] = 1;
            }

            return table;
        }

        /// <summary>
        ///     Encodes the table, replaying stored bytes for every column nobody edited.
        /// </summary>
        /// <remarks>
        ///     This is the write path. An untouched table comes back byte for byte, which is what
        ///     keeps a save that changed nothing from rewriting the archive and its CRC; an edited
        ///     column is written from its field, which is what makes an edit take effect at all.
        /// </remarks>
        /// <returns>The bytes to store, ready to read.</returns>
        public JagStream Encode() => WriteBlob(BuildRows(true));

        /// <summary>
        ///     Encodes every column of every record from its field, ignoring what was stored.
        /// </summary>
        /// <remarks>
        ///     Deliberately lossy where the format is not canonical, and useful precisely for that:
        ///     comparing it against <see cref="Encode"/> is how a caller can tell whether a cache
        ///     holds a byte the field round trip cannot reproduce.
        /// </remarks>
        /// <returns>The bytes to store, ready to read.</returns>
        public JagStream EncodeFromFields() => WriteBlob(BuildRows(false));

        /// <summary>
        ///     Stages the table into the cache, unless encoding it changes nothing.
        /// </summary>
        /// <remarks>
        ///     Goes through <see cref="DefinitionWriter"/> so the comparison is against the bytes the
        ///     cache holds now rather than against this table's own idea of dirtiness - an edit that
        ///     was undone before saving must not rewrite the archive. The stored bytes are adopted
        ///     either way, so a second save after a successful one writes nothing.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>Whether anything was staged.</returns>
        public bool SaveTo(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            byte[][] rows = BuildRows(true);
            bool staged = DefinitionWriter.Save(cache, RSConstants.MATERIALS, WholeTableDefinitionId,
                WriteBlob(rows).ToArray());
            Adopt(rows);
            return staged;
        }

        /// <summary>
        ///     Takes the bytes just written as the new stored state, so nothing reads as edited twice.
        /// </summary>
        /// <param name="rows">The rows that were encoded, indexed by slot.</param>
        private void Adopt(byte[][] rows) {
            for (int slot = 0; slot < Count; slot++) {
                TextureDefinition def = _slots[slot];
                if (def == null)
                    continue;

                def.StoredRecord = rows[slot];
                def.MarkClean();
            }
        }

        /// <summary>
        ///     Lays every present record out as its 23 stored bytes.
        /// </summary>
        /// <param name="preferStored">
        ///     Whether a column that was not edited keeps the bytes it was decoded from.
        /// </param>
        /// <returns>One row per slot, null where the slot is absent.</returns>
        private byte[][] BuildRows(bool preferStored) {
            var rows = new byte[Count][];

            for (int slot = 0; slot < Count; slot++) {
                TextureDefinition def = _slots[slot];
                if (def == null)
                    continue;

                var row = new byte[BytesPerRecord];
                byte[]? stored = preferStored ? def.StoredRecord : null;
                if (stored != null)
                    Array.Copy(stored, row, BytesPerRecord);

                for (int column = 0; column < ColumnCount; column++)
                    if (stored == null || def.IsColumnDirty((MaterialColumn) column))
                        Pack(def, (MaterialColumn) column, row);

                rows[slot] = row;
            }

            return rows;
        }

        /// <summary>Writes the count, the existence column and then the nineteen field columns.</summary>
        /// <param name="rows">One row per slot, null where the slot is absent.</param>
        /// <returns>The encoded file, ready to read.</returns>
        private JagStream WriteBlob(byte[][] rows) {
            //The count is a u16 and everything downstream is sized from it, so a table that no
            //longer fits has to say so rather than wrap and produce a file that parses as a
            //much smaller one.
            if (Count > 0xFFFF)
                throw new InvalidDataException(
                    "A material table holds at most 65535 textures; this one has " + Count + ".");

            int present = 0;
            foreach (byte[] row in rows)
                if (row != null)
                    present++;

            var stream = new JagStream(2 + Count + present * BytesPerRecord);
            stream.WriteShort(Count);

            for (int slot = 0; slot < Count; slot++)
                stream.WriteByte(_existence[slot]);

            for (int column = 0; column < ColumnCount; column++) {
                int offset = ColumnOffsets[column];
                int width = ColumnWidths[column];

                for (int slot = 0; slot < Count; slot++)
                    if (rows[slot] != null)
                        stream.Write(rows[slot], offset, width);
            }

            return stream.Flip();
        }

        /// <summary>
        ///     Writes one column of one record into its row.
        /// </summary>
        /// <remarks>
        ///     The single field-to-byte mapping in this codec. <see cref="Unpack"/> is its inverse and
        ///     the two are the only places a column's width or signedness is spelled.
        /// </remarks>
        /// <param name="def">The record.</param>
        /// <param name="column">The column to write.</param>
        /// <param name="row">The record's 23 bytes.</param>
        private static void Pack(TextureDefinition def, MaterialColumn column, byte[] row) {
            int at = ColumnOffsets[(int) column];

            switch (column) {
                //Inverted: the client's test is byte == 0 (Class260.java:116).
                case MaterialColumn.Field1825: row[at] = (byte) (def.field1825 ? 0 : 1); break;
                case MaterialColumn.Field1822: row[at] = (byte) (def.field1822 ? 1 : 0); break;
                case MaterialColumn.Field1833: row[at] = (byte) (def.field1833 ? 1 : 0); break;
                case MaterialColumn.Field1829: row[at] = unchecked((byte) def.field1829); break;
                case MaterialColumn.Field1830: row[at] = unchecked((byte) def.field1830); break;
                case MaterialColumn.Field1820: row[at] = unchecked((byte) def.field1820); break;
                case MaterialColumn.Field1816: row[at] = unchecked((byte) def.field1816); break;

                case MaterialColumn.Field1831:
                    row[at] = (byte) (def.field1831 >> 8);
                    row[at + 1] = (byte) def.field1831;
                    break;

                case MaterialColumn.Field1823: row[at] = unchecked((byte) def.field1823); break;
                case MaterialColumn.Field1837: row[at] = unchecked((byte) def.field1837); break;
                case MaterialColumn.Field1827: row[at] = (byte) (def.field1827 ? 1 : 0); break;
                case MaterialColumn.Field1824: row[at] = (byte) (def.field1824 ? 1 : 0); break;
                case MaterialColumn.Field1832: row[at] = unchecked((byte) def.field1832); break;
                case MaterialColumn.Field1826: row[at] = (byte) (def.field1826 ? 1 : 0); break;
                case MaterialColumn.Field1819: row[at] = (byte) (def.field1819 ? 1 : 0); break;
                case MaterialColumn.Field1817: row[at] = (byte) (def.field1817 ? 1 : 0); break;
                case MaterialColumn.Field1821: row[at] = (byte) def.field1821; break;

                case MaterialColumn.Field1835:
                    row[at] = (byte) (def.field1835 >> 24);
                    row[at + 1] = (byte) (def.field1835 >> 16);
                    row[at + 2] = (byte) (def.field1835 >> 8);
                    row[at + 3] = (byte) def.field1835;
                    break;

                case MaterialColumn.Field1818: row[at] = (byte) def.field1818; break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(column), column,
                        "The material table has no such column.");
            }
        }

        /// <summary>
        ///     Reads every field of one record out of its stored bytes.
        /// </summary>
        /// <remarks>
        ///     Assigns through the properties, so a caller that hands in bytes other than the stored
        ///     ones gets a record that knows it changed. <see cref="Decode"/> clears that afterwards,
        ///     because for it the bytes and the fields agree by construction.
        /// </remarks>
        /// <param name="def">The record to fill.</param>
        /// <param name="row">The record's 23 bytes.</param>
        private static void Unpack(TextureDefinition def, byte[] row) {
            def.field1825 = row[ColumnOffsets[(int) MaterialColumn.Field1825]] == 0;
            def.field1822 = row[ColumnOffsets[(int) MaterialColumn.Field1822]] == 1;
            def.field1833 = row[ColumnOffsets[(int) MaterialColumn.Field1833]] == 1;
            def.field1829 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1829]]);
            def.field1830 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1830]]);
            def.field1820 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1820]]);
            def.field1816 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1816]]);

            int hsl = ColumnOffsets[(int) MaterialColumn.Field1831];
            def.field1831 = (row[hsl] << 8) | row[hsl + 1];

            def.field1823 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1823]]);
            def.field1837 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1837]]);
            def.field1827 = row[ColumnOffsets[(int) MaterialColumn.Field1827]] == 1;
            def.field1824 = row[ColumnOffsets[(int) MaterialColumn.Field1824]] == 1;
            def.field1832 = unchecked((sbyte) row[ColumnOffsets[(int) MaterialColumn.Field1832]]);
            def.field1826 = row[ColumnOffsets[(int) MaterialColumn.Field1826]] == 1;
            def.field1819 = row[ColumnOffsets[(int) MaterialColumn.Field1819]] == 1;
            def.field1817 = row[ColumnOffsets[(int) MaterialColumn.Field1817]] == 1;
            def.field1821 = row[ColumnOffsets[(int) MaterialColumn.Field1821]];

            int tint = ColumnOffsets[(int) MaterialColumn.Field1835];
            def.field1835 = (row[tint] << 24) | (row[tint + 1] << 16) | (row[tint + 2] << 8) | row[tint + 3];

            def.field1818 = row[ColumnOffsets[(int) MaterialColumn.Field1818]];
        }

        /// <summary>Runs the column widths into a start offset per column.</summary>
        /// <returns>The offsets, indexed by <see cref="MaterialColumn"/>.</returns>
        private static int[] BuildOffsets() {
            var offsets = new int[ColumnCount];
            int running = 0;

            for (int column = 0; column < ColumnCount; column++) {
                offsets[column] = running;
                running += ColumnWidths[column];
            }

            if (running != BytesPerRecord)
                throw new InvalidOperationException(
                    "The material columns sum to " + running + " bytes but a record is " + BytesPerRecord + ".");

            return offsets;
        }
    }
}
