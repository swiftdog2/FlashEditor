using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     One substitution slot of a message template: which kind of value fills it, and the words
    ///     that configure it.
    /// </summary>
    /// <remarks>
    ///     Read at Node_Sub46_Sub11.java:140-152. How many words follow the type id is not stated in
    ///     the file - see <see cref="QuickChatSlotType"/>.
    /// </remarks>
    public sealed class QuickChatSlot {
        /// <summary>Binds a slot type to the words stored for it.</summary>
        /// <param name="slotTypeId">The stored slot type id.</param>
        /// <param name="words">The words stored after it, or null for none.</param>
        public QuickChatSlot(int slotTypeId, IEnumerable<int>? words = null) {
            SlotTypeId = slotTypeId;
            Words = words == null ? new List<int>() : new List<int>(words);
        }

        /// <summary>Which kind of value fills this slot.</summary>
        public int SlotTypeId { get; set; }

        /// <summary>
        ///     The 16-bit words stored after the type id.
        /// </summary>
        /// <remarks>
        ///     Their count is decided by the type, so changing <see cref="SlotTypeId"/> without
        ///     matching this list produces a record the client reads a different length from. The
        ///     encoder refuses that rather than writing it.
        /// </remarks>
        public List<int> Words { get; }

        /// <summary>Whether the 637 client has a definition for this slot's type.</summary>
        public bool IsKnownType => QuickChatSlotType.IsKnown(SlotTypeId);
    }

    /// <summary>
    ///     One quick-chat message: a template with substitution slots, and the replies suggested
    ///     once it has been sent.
    /// </summary>
    /// <remarks>
    ///     Group 1 of index 24 or index 25 - the same format in two id namespaces, see
    ///     <see cref="QuickChatBank"/>. Opcode table from <c>Node_Sub46_Sub11.method1578</c>
    ///     (Node_Sub46_Sub11.java:129-173), driven by the loop in <c>method1584</c> (:281-289).
    /// </remarks>
    public sealed class QuickChatMessageDefinition : OpcodeStreamDefinition {
        /// <summary>
        ///     The character in a template that marks a substitution slot.
        /// </summary>
        /// <remarks>
        ///     The client splits the template on it into one more segment than there are slots
        ///     (Node_Sub46_Sub11.java:132) and re-joins them around the filled values
        ///     (<c>method1576</c>, :101-127), indexing the slot arrays by segment position. So the
        ///     count of these and the length of <see cref="Slots"/> are two statements of the same
        ///     thing, and an edit has to move both.
        /// </remarks>
        public const char SlotMarker = '<';

        private byte[] templateBytes = Array.Empty<byte>();

        /// <summary>The message id, which is its file id within the bank's message group.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1. The template, exactly as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Bytes rather than a string because the cp1252 decode is lossy in five byte values -
        ///     see <see cref="QuickChatText"/>. Never null.
        /// </remarks>
        public byte[] TemplateBytes {
            get => templateBytes;
            set => templateBytes = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The template as text; setting it re-encodes <see cref="TemplateBytes"/>.</summary>
        /// <remarks>Kept whole, slot markers included; splitting it is the viewer's job.</remarks>
        public string Template {
            get => QuickChatText.ToText(templateBytes);
            set => templateBytes = QuickChatText.ToBytes(value ?? string.Empty);
        }

        /// <summary>
        ///     Opcode 2. Messages offered as replies once this one has been sent.
        /// </summary>
        /// <remarks>
        ///     Ids address the bank's message group. Self-proving in the data: index 25's message 5
        ///     "How are you?" lists 18, 19, 20 and 21, which are "I'm great!", "I'm good.",
        ///     "I'm okay." and "Meh."
        ///     <para>
        ///     The client tolerates an id its group does not hold, building an empty record from a
        ///     null lookup, so nothing may assert that these resolve. Worth knowing before checking:
        ///     a message group is not densely numbered - index 25's holds ids 0 to 69 with 62 absent
        ///     in both caches - so an id inside the range is not necessarily a file.
        ///     </para>
        /// </remarks>
        public List<int> ResponseIds { get; } = new List<int>();

        /// <summary>Opcode 3. What fills each substitution slot of the template, in order.</summary>
        public List<QuickChatSlot> Slots { get; } = new List<QuickChatSlot>();

        /// <summary>
        ///     Opcode 4. Hides this message from the quick-chat search.
        /// </summary>
        /// <remarks>
        ///     Settled from the flag's only consumer rather than from the opcode: the client clears
        ///     <c>aBoolean6027</c> here (Node_Sub46_Sub11.java:154), and the one thing that reads it
        ///     is <c>JS5Archive.method2759</c> (JS5Archive.java:106), which walks every message of
        ///     both banks looking for a typed substring and skips the ones where it is false.
        ///     <para>
        ///     No record in either cache carries opcode 4, so no sweep defends it - it is
        ///     implemented and covered synthetically for the same reason the unreachable
        ///     reference-table branches are.
        ///     </para>
        /// </remarks>
        public bool HiddenFromSearch {
            get => Opcodes.Has(4);
            set => QuickChatRecord.SetFlag(Opcodes, 4, value);
        }

        /// <summary>
        ///     How many substitution markers the template carries.
        /// </summary>
        /// <remarks>
        ///     Derived from the template, and the number <see cref="Slots"/> is expected to match.
        ///     Deliberately not asserted at decode: it is a property of well-formed content rather
        ///     than of the format, and a decoder that refused a mismatch would refuse a record the
        ///     client loads.
        /// </remarks>
        public int MarkerCount {
            get {
                int markers = 0;
                foreach (byte stored in templateBytes)
                    if (stored == SlotMarker)
                        markers++;
                return markers;
            }
        }

        /// <summary>Reads one message record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public QuickChatMessageDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            templateBytes = Array.Empty<byte>();
            ResponseIds.Clear();
            Slots.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    templateBytes = QuickChatRecord.ReadStoredString(stream);
                    return true;

                case 2: {
                        int count = stream.ReadUnsignedByte();
                        for (int i = 0; i < count; i++)
                            ResponseIds.Add(stream.ReadUnsignedShort());
                        return true;
                    }

                case 3: {
                        int count = stream.ReadUnsignedByte();
                        for (int i = 0; i < count; i++)
                            Slots.Add(ReadSlot(stream));
                        return true;
                    }

                //4 is a bare flag: its presence is its whole payload.
                case 4:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes this message back to the file representation.</summary>
        /// <remarks>
        ///     Replayed in the recorded opcode order. Every message record in both caches happens to
        ///     ascend, but the menu format sharing this bank does not, and an encoder that chose the
        ///     order for itself would rewrite files the user merely opened - which changes the group,
        ///     its CRC, and the reference-table entry of the menu group packed beside it.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            if (Opcodes.Has(1) || templateBytes.Length > 0)
                records.Add(QuickChatRecord.Payload(1, buffer => QuickChatRecord.WriteStoredString(buffer, templateBytes)));

            if (Opcodes.Has(2) || ResponseIds.Count > 0) {
                records.Add(QuickChatRecord.Payload(2, buffer => {
                    QuickChatRecord.RequireByteCount(ResponseIds.Count, 2);
                    buffer.WriteByte((byte) ResponseIds.Count);
                    foreach (int id in ResponseIds) {
                        QuickChatRecord.RequireStoredId(id, "response id");
                        buffer.WriteShort(id);
                    }
                }));
            }

            if (Opcodes.Has(3) || Slots.Count > 0) {
                records.Add(QuickChatRecord.Payload(3, buffer => {
                    QuickChatRecord.RequireByteCount(Slots.Count, 3);
                    buffer.WriteByte((byte) Slots.Count);
                    foreach (QuickChatSlot slot in Slots)
                        WriteSlot(buffer, slot);
                }));
            }

            //4 carries no payload; the recorded stream is the only statement of whether it is set.
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Reads one slot entry: a type id, then the words that type carries.</summary>
        /// <remarks>
        ///     An unknown type consumes no words, which is the client's own behaviour rather than a
        ///     fallback invented here - Node_Sub46_Sub11.java:144 guards the word loop on the lookup
        ///     returning non-null, so such an entry costs two bytes and the record parses on from
        ///     there. The type id is kept even though the client throws it away, or the record could
        ///     not be written back.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the type id.</param>
        /// <returns>The slot.</returns>
        private static QuickChatSlot ReadSlot(JagStream stream) {
            int slotTypeId = stream.ReadUnsignedShort();
            var slot = new QuickChatSlot(slotTypeId);

            int words = QuickChatSlotType.WordCount(slotTypeId);
            for (int word = 0; word < words; word++)
                slot.Words.Add(stream.ReadUnsignedShort());

            return slot;
        }

        /// <summary>Writes one slot, refusing a word list the type cannot describe.</summary>
        /// <remarks>
        ///     The check is what stops an edit producing a record the client reads a different
        ///     length from. Nothing in the file states the word count, so a slot carrying one word
        ///     too many is not a longer slot - it is a shorter one followed by garbage, and every
        ///     field after it shifts.
        /// </remarks>
        /// <param name="buffer">The payload buffer.</param>
        /// <param name="slot">The slot to write.</param>
        private static void WriteSlot(JagStream buffer, QuickChatSlot slot) {
            QuickChatRecord.RequireStoredId(slot.SlotTypeId, "slot type");

            int expected = QuickChatSlotType.WordCount(slot.SlotTypeId);
            if (slot.Words.Count != expected) {
                throw new InvalidOperationException(
                    "Quick-chat slot type " + slot.SlotTypeId + " stores " + expected +
                    " words, so a slot holding " + slot.Words.Count +
                    " cannot be written - the client would read the bytes after it as the next slot.");
            }

            buffer.WriteShort(slot.SlotTypeId);
            foreach (int word in slot.Words) {
                QuickChatRecord.RequireStoredId(word, "slot word");
                buffer.WriteShort(word);
            }
        }
    }
}
