using System;

namespace FlashEditor.Definitions.Compression {
    /// <summary>
    ///     One record of the chat table as a grid row: a data value, its stored bit length, and the
    ///     codeword the table derives for it.
    /// </summary>
    /// <remarks>
    ///     Holds the table rather than a snapshot of the value's fields. Changing one bit length
    ///     re-derives the codewords of an unpredictable number of the other 255 records, so a row
    ///     that had copied its codeword out would show a stale one for every value the edit moved.
    /// </remarks>
    public sealed class HuffmanEntryRow {
        private readonly HuffmanTable _table;

        /// <summary>Binds a row to one data value of a table.</summary>
        /// <param name="table">The table the row reads through.</param>
        /// <param name="value">The data value, which is the record index.</param>
        public HuffmanEntryRow(HuffmanTable table, int value) {
            _table = table ?? throw new ArgumentNullException(nameof(table));
            Value = value;
        }

        /// <summary>The data value this record encodes.</summary>
        public int Value { get; }

        /// <summary>The byte in hex, since the value is a byte rather than a definition id.</summary>
        public string Hex => "0x" + Value.ToString("X2");

        /// <summary>
        ///     What the byte prints as, or a placeholder when it does not print.
        /// </summary>
        /// <remarks>
        ///     Printable ASCII only. The band above 0x7F is the client's modified cp1252 rather than
        ///     Latin-1, so rendering it as a code point would put the wrong glyph beside a length.
        /// </remarks>
        public string Character => Value >= 0x20 && Value < 0x7F ? ((char) Value).ToString() : ".";

        /// <summary>The stored bit length, and the only editable thing in the record.</summary>
        public int BitLength {
            get => _table.BitLengthOf(Value);
            set => _table.SetBitLength(Value, value);
        }

        /// <summary>The derived codeword, most significant bit first.</summary>
        public string Codeword => _table.CodewordBits(Value);

        /// <summary>
        ///     Whether this value can be sent at all.
        /// </summary>
        /// <remarks>
        ///     A zero length means no codeword, and the client throws rather than skipping the
        ///     character (<c>Class213.java:296-298</c>), so zeroing a length quietly makes every
        ///     message containing that byte unsendable. Shown as a column so the consequence is
        ///     visible before the save rather than after it.
        /// </remarks>
        public bool Encodable => _table.BitLengthOf(Value) > 0;
    }
}
