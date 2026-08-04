using System;
using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Enums {
    /// <summary>
    ///     One enum from index 17 as a list row.
    /// </summary>
    /// <remarks>
    ///     Most of the index is unallocated: a file that holds a single terminator byte is a slot
    ///     with no enum in it, and there are far more of those than there are enums. They are still
    ///     rows, because they are still files the reference table declares and an editor that hid
    ///     them would misreport the id space - but <see cref="IsEmpty"/> is a column so the two are
    ///     never confused.
    /// </remarks>
    public sealed class EnumListing {
        /// <summary>Binds one decoded enum to where it came from.</summary>
        /// <param name="address">The group and file, and the enum id they carry.</param>
        /// <param name="record">The decoded enum.</param>
        public EnumListing(DefinitionAddress address, EnumDefinition record) {
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>Where the enum lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded enum, which the detail pane reads its entries from.</summary>
        public EnumDefinition Record { get; }

        /// <summary>The enum id, which is <c>(group &lt;&lt; 8) | file</c>.</summary>
        public int EnumId => Record.Id;

        /// <summary>The bank of 256 ids this enum sits in.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>The file within that bank.</summary>
        public int FileId => Address.FileId;

        /// <summary>Whether the file is an unallocated slot rather than an enum.</summary>
        public bool IsEmpty => Record.IsEmpty;

        /// <summary>How many key/value pairs the table holds.</summary>
        public int EntryCount => Record.Entries.Count;

        /// <summary>
        ///     The key type as the client would show it, with the stored byte beside it.
        /// </summary>
        /// <remarks>
        ///     Both, because the mapping is not the identity and is not reversible: one value type in
        ///     this cache is <c>0xAB</c>. A column showing only the character would present a byte
        ///     the editor cannot recover from that character.
        /// </remarks>
        public string KeyType => DescribeType(Record.KeyTypeByte, Record.KeyTypeChar);

        /// <summary>The value type, in the same form as <see cref="KeyType"/>.</summary>
        public string ValueType => DescribeType(Record.ValueTypeByte, Record.ValueTypeChar);

        /// <summary>Whether the table's values are strings (opcode 5) rather than ints (opcode 6).</summary>
        /// <remarks>
        ///     Taken from which opcode carried the table rather than from the value type char, which
        ///     is a label the client never sizes a read from.
        /// </remarks>
        public string ValueShape => Record.Entries.Count == 0
            ? string.Empty
            : Record.ValuesAreStrings ? "string" : "int";

        /// <summary>What a string-valued lookup answers for a key the table lacks.</summary>
        public string DefaultString {
            get => Record.DefaultString;
            set => Record.DefaultString = value;
        }

        /// <summary>What an int-valued lookup answers for a key the table lacks.</summary>
        public int DefaultInt {
            get => Record.DefaultInt;
            set => Record.DefaultInt = value;
        }

        /// <summary>The opcodes the file stored, in the order it stored them.</summary>
        /// <remarks>
        ///     Worth a column on this index. Only four orders occur and every one writes the default
        ///     after the table, which is the opposite of what an encoder emitting ascending opcodes
        ///     would produce - so the stored order is the thing a reader has to be able to see.
        /// </remarks>
        public string OpcodeOrder {
            get {
                var parts = new List<string>(Record.Opcodes.Count);
                for (int i = 0; i < Record.Opcodes.Count; i++)
                    parts.Add(Record.Opcodes[i].Opcode.ToString());
                return string.Join(",", parts);
            }
        }

        private static string DescribeType(int raw, char mapped) {
            if (raw <= 0)
                return string.Empty;
            return mapped == '\0' ? "0x" + raw.ToString("X2") : mapped + " (0x" + raw.ToString("X2") + ")";
        }
    }

    /// <summary>
    ///     Index 17 as a definition list: one row per enum, decoded and re-encodable.
    /// </summary>
    /// <remarks>
    ///     The index constant is <c>CLIENTSCRIPT_SETTINGS</c> and is a misnomer - the client's own
    ///     field for the store is <c>enumFileStore</c> (Node_Sub10_Sub24.java:9). An enum id splits
    ///     256 to a group (Class29.java:237-238), which <see cref="CacheAddressing"/> already records,
    ///     so the row's id comes from there rather than being folded here.
    ///     <para>
    ///     <b>Editable, but only in the four scalar fields.</b> The table itself is not a cell: it is
    ///     a variable number of key/value pairs and belongs in the detail pane beside the list. The
    ///     defaults and the two type bytes are single values with a single meaning, and
    ///     <see cref="EnumDefinition.Encode"/> replays the stored opcode order around them, so an
    ///     edit to one of those rewrites that field and nothing else. An edit that introduces an
    ///     opcode the file never carried appends it in ascending order rather than after the table,
    ///     which is a difference from how the shipped files are written - it only reaches a file the
    ///     user has deliberately changed.
    ///     </para>
    /// </remarks>
    public sealed class EnumListDescriptor : DefinitionListDescriptor<EnumListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every enum the index declares.</summary>
        public EnumListDescriptor() {
            columns = new[] {
                DefinitionColumn.ReadOnly<EnumListing>("Enum", row => row.EnumId, 80),
                DefinitionColumn.ReadOnly<EnumListing>("Group", row => row.GroupId, 70),
                DefinitionColumn.ReadOnly<EnumListing>("File", row => row.FileId, 60),
                DefinitionColumn.ReadOnly<EnumListing>("Entries", row => row.EntryCount, 80),
                DefinitionColumn.ReadOnly<EnumListing>("Values", row => row.ValueShape, 70),
                DefinitionColumn.Text<EnumListing>("Key type", row => row.KeyType,
                    (row, value) => row.Record.KeyTypeByte = ParseTypeByte(value, row.Record.KeyTypeByte), 110),
                DefinitionColumn.Text<EnumListing>("Value type", row => row.ValueType,
                    (row, value) => row.Record.ValueTypeByte = ParseTypeByte(value, row.Record.ValueTypeByte), 110),
                DefinitionColumn.Text<EnumListing>("Default string", row => row.DefaultString,
                    (row, value) => row.DefaultString = value, 180),
                DefinitionColumn.Number<EnumListing>("Default int", row => row.DefaultInt,
                    (row, value) => row.DefaultInt = value, 100),
                DefinitionColumn.ReadOnly<EnumListing>("Opcodes", row => row.OpcodeOrder, 110)
            };
        }

        /// <inheritdoc/>
        public override int IndexId => RSConstants.CLIENTSCRIPT_SETTINGS;

        /// <inheritdoc/>
        public override string RowNoun => "enum";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override EnumListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new EnumDefinition { Id = address.DefinitionId };
            record.Decode(payload);
            return new EnumListing(address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(EnumListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(EnumListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        /// <summary>
        ///     Reads a type byte back out of the cell that displays it.
        /// </summary>
        /// <remarks>
        ///     Both spellings the column writes are accepted - a bare <c>0xAB</c> and a character
        ///     with the byte in brackets after it - and an unparseable cell leaves the stored byte
        ///     alone. Refusing rather than substituting matters here: the display is lossy for the
        ///     0x80-0x9F range, so guessing a byte back from a character would silently rewrite a
        ///     field the user did not touch.
        /// </remarks>
        /// <param name="text">The cell's text.</param>
        /// <param name="current">What the record holds now.</param>
        /// <returns>The byte to store.</returns>
        private static int ParseTypeByte(string? text, int current) {
            if (string.IsNullOrWhiteSpace(text))
                return current;

            string trimmed = text.Trim();

            int open = trimmed.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (open >= 0) {
                string digits = trimmed.Substring(open + 2).TrimEnd(')', ' ');
                if (int.TryParse(digits, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed is > 0 and <= 0xFF)
                    return parsed;
                return current;
            }

            //A single character is only unambiguous below 0x80, where the client's remap is the
            //identity. Anything above it has to be typed as a byte.
            return trimmed.Length == 1 && trimmed[0] > 0 && trimmed[0] < 0x80 ? trimmed[0] : current;
        }
    }
}
