using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Enums {
    /// <summary>
    ///     One entry of an enum: an int32 key and the value stored against it.
    /// </summary>
    /// <remarks>
    ///     Both value shapes are carried on the same type because an enum's table is either wholly
    ///     strings (opcode 5) or wholly ints (opcode 6), never a mixture, and which one applies is a
    ///     property of the definition rather than of the entry. Splitting it into two entry types
    ///     would make every consumer branch on a distinction the wire format states once.
    /// </remarks>
    public sealed class EnumEntry {
        /// <summary>Creates an entry with the key and both value slots unset.</summary>
        public EnumEntry() {
        }

        /// <summary>Creates a string-valued entry.</summary>
        /// <param name="key">The int32 key.</param>
        /// <param name="text">The string value.</param>
        public EnumEntry(int key, string text) {
            Key = key;
            Text = text;
        }

        /// <summary>Creates an int-valued entry.</summary>
        /// <param name="key">The int32 key.</param>
        /// <param name="number">The int32 value.</param>
        public EnumEntry(int key, int number) {
            Key = key;
            Number = number;
        }

        /// <summary>
        ///     The key, always an int32 on the wire whatever <see cref="EnumDefinition.KeyTypeByte"/>
        ///     claims.
        /// </summary>
        /// <remarks>
        ///     GameConfig.java:95 reads it with <c>readInt</c> in both table arms, so the key type
        ///     char is a semantic label the client never uses to size a read.
        /// </remarks>
        public int Key { get; set; }

        /// <summary>The value when the table is a string table, otherwise unused.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The value when the table is an int table, otherwise unused.</summary>
        public int Number { get; set; }
    }

    /// <summary>
    ///     One enum from JS5 index 17: a key/value table plus the defaults returned for a key the
    ///     table does not hold.
    /// </summary>
    /// <remarks>
    ///     The index constant is <see cref="cache.RSConstants.CLIENTSCRIPT_SETTINGS"/>, which is a
    ///     misnomer - the client's own field for the store is <c>enumFileStore</c>
    ///     (Node_Sub10_Sub24.java:9, opened at InterfaceSettings.java:173) and its only consumers are
    ///     <c>Class29.getEnum</c> and <c>getEnumData</c>. An enum id splits into
    ///     <c>group = id &gt;&gt;&gt; 8</c> and <c>file = id &amp; 0xFF</c> (Class29.java:237-238), so
    ///     a group is a bank of 256 enum ids and a file is one whole enum.
    ///     <para>
    ///     Opcode table from <c>GameConfig.extractEnumData</c> (GameConfig.java:78-123), driven by
    ///     the loop in <c>loadEnum</c> (:148-160). That loop has no default arm: an unrecognised
    ///     opcode consumes nothing and the next payload byte is read as an opcode, so the client
    ///     cannot detect a desync. This decoder refuses instead, through the base class.
    ///     </para>
    /// </remarks>
    public sealed class EnumDefinition : OpcodeStreamDefinition {
        /// <summary>What <c>getEnumData</c> answers for a missing key when no opcode 3 was stored.</summary>
        /// <remarks>The literal the client initialises the field to, GameConfig.java:56.</remarks>
        public const string AbsentDefaultString = "null";

        /// <summary>The enum id, which is <c>(group &lt;&lt; 8) | file</c>.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1. The key type, as the raw byte the file stores.
        /// </summary>
        /// <remarks>
        ///     Kept as a byte rather than a char because the client's mapping is not the identity:
        ///     <c>Class64_Sub7.method576</c> sends 0x80-0x9F through cp1252 and turns its five
        ///     unassigned slots into '?', which is one way and would lose the stored byte on the
        ///     first save. <see cref="KeyTypeChar"/> is the display form.
        /// </remarks>
        public int KeyTypeByte { get; set; }

        /// <summary>Opcode 2. The value type, as the raw byte the file stores.</summary>
        /// <remarks>See <see cref="KeyTypeByte"/> for why this is a byte.</remarks>
        public int ValueTypeByte { get; set; }

        /// <summary>The key type as the client would display it.</summary>
        public char KeyTypeChar => TypeChar(KeyTypeByte);

        /// <summary>The value type as the client would display it.</summary>
        public char ValueTypeChar => TypeChar(ValueTypeByte);

        /// <summary>Opcode 3. What a string-valued lookup answers for a key the table lacks.</summary>
        public string DefaultString { get; set; } = AbsentDefaultString;

        /// <summary>Opcode 4. What an int-valued lookup answers for a key the table lacks.</summary>
        public int DefaultInt { get; set; }

        /// <summary>
        ///     Whether <see cref="Entries"/> holds strings (opcode 5) rather than ints (opcode 6).
        /// </summary>
        /// <remarks>
        ///     Decided by which opcode carried the table, not by <see cref="ValueTypeByte"/>. The
        ///     type char is a label - 22 distinct ones occur across this index - while the wire
        ///     shape has exactly two forms.
        /// </remarks>
        public bool ValuesAreStrings { get; set; }

        /// <summary>Opcodes 5 and 6. The table, in the order the file stores it.</summary>
        /// <remarks>
        ///     A list rather than a dictionary: the format does not require keys to be unique or
        ///     sorted, and folding it into a dictionary would silently re-order and de-duplicate a
        ///     table nobody edited.
        /// </remarks>
        public List<EnumEntry> Entries { get; } = new List<EnumEntry>();

        /// <summary>
        ///     Whether this file is an unallocated enum slot - a single terminator byte.
        /// </summary>
        /// <remarks>
        ///     Most of the index is: index 17 declares 3558 files and only a minority carry an
        ///     opcode. Treating those as "no enum here" and skipping them loses three quarters of
        ///     the index the moment anything writes the group back.
        /// </remarks>
        public bool IsEmpty => Opcodes.Count == 0;

        /// <summary>Reads one enum from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public EnumDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            Entries.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    KeyTypeByte = stream.ReadUnsignedByte();
                    return true;

                case 2:
                    ValueTypeByte = stream.ReadUnsignedByte();
                    return true;

                case 3:
                    DefaultString = stream.ReadJagexString();
                    return true;

                case 4:
                    DefaultInt = stream.ReadInt();
                    return true;

                case 5:
                case 6: {
                        int count = stream.ReadUnsignedShort();
                        Entries.Clear();
                        ValuesAreStrings = opcode == 5;
                        for (int i = 0; i < count; i++) {
                            int key = stream.ReadInt();
                            Entries.Add(ValuesAreStrings
                                ? new EnumEntry(key, stream.ReadJagexString())
                                : new EnumEntry(key, stream.ReadInt()));
                        }
                        return true;
                    }

                default:
                    return false;
            }
        }

        /// <summary>Writes this enum back to the file representation.</summary>
        /// <remarks>
        ///     The opcode order is replayed rather than chosen. Only four orders occur across the
        ///     whole index and every one of them writes the default (3 or 4) <em>after</em> the
        ///     table (5 or 6), so an encoder emitting ascending opcodes - the obvious choice -
        ///     reproduces none of the enums that carry both.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            /* Each block emits when the file carried the opcode OR when the field has moved off
               what the client assumes in its absence. The first arm is what keeps an opcode whose
               payload happens to equal the default - a stored default of "null" or 0 - rather than
               dropping it and shortening a file nobody edited. */
            if (Opcodes.Has(1) || KeyTypeByte != 0)
                records.Add(Payload(1, buffer => buffer.WriteByte((byte) KeyTypeByte)));
            if (Opcodes.Has(2) || ValueTypeByte != 0)
                records.Add(Payload(2, buffer => buffer.WriteByte((byte) ValueTypeByte)));
            if (Opcodes.Has(3) || !string.Equals(DefaultString, AbsentDefaultString, StringComparison.Ordinal))
                records.Add(Payload(3, buffer => buffer.WriteJagexString(DefaultString ?? AbsentDefaultString)));
            if (Opcodes.Has(4) || DefaultInt != 0)
                records.Add(Payload(4, buffer => buffer.WriteInteger(DefaultInt)));

            if (Opcodes.Has(5) || Opcodes.Has(6) || Entries.Count > 0) {
                records.Add(Payload(ValuesAreStrings ? 5 : 6, buffer => {
                    buffer.WriteShort(Entries.Count);
                    foreach (EnumEntry entry in Entries) {
                        buffer.WriteInteger(entry.Key);
                        if (ValuesAreStrings)
                            buffer.WriteJagexString(entry.Text ?? string.Empty);
                        else
                            buffer.WriteInteger(entry.Number);
                    }
                }));
            }

            /* Ascending order for anything the file did not carry. The blocks above build the list
               in opcode order already, but the table emits as 5 or 6 depending on the value shape,
               so without the sort a newly added int table would land ahead of a newly added
               default rather than behind it. */
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Builds one opcode's payload into its own buffer.</summary>
        /// <param name="opcode">The opcode the payload belongs to.</param>
        /// <param name="write">Writes the payload.</param>
        /// <returns>The opcode paired with its bytes.</returns>
        private static KeyValuePair<int, byte[]> Payload(int opcode, Action<JagStream> write) {
            var buffer = new JagStream();
            write(buffer);
            return new KeyValuePair<int, byte[]>(opcode, buffer.Flip().ToArray());
        }

        /// <summary>
        ///     Maps a stored type byte to the character the client shows for it.
        /// </summary>
        /// <remarks>
        ///     Routed through <see cref="JagStream.ReadJagexString"/> rather than against a private
        ///     copy of the cp1252 table, so the two cannot drift: <c>Class64_Sub7.method576</c> and
        ///     <c>RSBuffer.readString</c> apply the same remap, and that reader is already proven
        ///     against it. A zero byte has no character - the client throws for it - and is reported
        ///     as NUL rather than as '?'.
        /// </remarks>
        /// <param name="raw">The stored byte.</param>
        /// <returns>The character, or NUL when the byte is zero.</returns>
        private static char TypeChar(int raw) {
            if (raw <= 0)
                return '\0';

            string mapped = new JagStream(new byte[] { (byte) raw, 0 }).ReadJagexString();
            return mapped.Length == 1 ? mapped[0] : '?';
        }
    }
}
