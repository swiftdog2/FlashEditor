using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     One node of the quick-chat menu tree: a caption, the submenus under it, and the messages
    ///     it offers.
    /// </summary>
    /// <remarks>
    ///     Group 0 of index 24 or index 25 - the same format in two id namespaces, see
    ///     <see cref="QuickChatBank"/>. Opcode table from <c>Node_Sub46_Sub1.method1527</c>
    ///     (Node_Sub46_Sub1.java:34-67), driven by the loop in <c>method1532</c> (:136-142).
    ///     <para>
    ///     Which of the two lists is which is proven rather than inferred from the opcode order.
    ///     Index 24's opcode 2 ids span exactly its menu group's id range and its opcode 3 ids span
    ///     exactly its message group's; index 25's tree says the same thing row by row, where menu
    ///     "General" lists (3,'r') (4,'h') (5,'g') (6,'m') (7,'s') (8,'b') against menu records
    ///     named "Responses", "Hello", "Goodbye", "Mood", "Smileys", "Banter".
    ///     </para>
    /// </remarks>
    public sealed class QuickChatMenuDefinition : OpcodeStreamDefinition {
        private byte[] captionBytes = Array.Empty<byte>();

        /// <summary>The menu id, which is its file id within the bank's menu group.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1. The caption, exactly as the file stores it.
        /// </summary>
        /// <remarks>
        ///     Bytes rather than a string because the cp1252 decode is lossy in five byte values and
        ///     re-encoding a decoded string would silently replace them - see
        ///     <see cref="QuickChatText"/>. Never null.
        /// </remarks>
        public byte[] CaptionBytes {
            get => captionBytes;
            set => captionBytes = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>The caption as text; setting it re-encodes <see cref="CaptionBytes"/>.</summary>
        public string Caption {
            get => QuickChatText.ToText(captionBytes);
            set => captionBytes = QuickChatText.ToBytes(value ?? string.Empty);
        }

        /// <summary>
        ///     Opcode 2. The submenus reachable from this node, each with its shortcut key.
        /// </summary>
        /// <remarks>
        ///     Targets address the bank's <b>menu</b> group. <c>method1528</c>
        ///     (Node_Sub46_Sub1.java:69-86) searches this list by the pressed character and returns
        ///     the id, which is what makes it the submenu list rather than the message list.
        /// </remarks>
        public List<QuickChatLink> Submenus { get; } = new List<QuickChatLink>();

        /// <summary>
        ///     Opcode 3. The messages this node offers, each with its shortcut key.
        /// </summary>
        /// <remarks>
        ///     Targets address the bank's <b>message</b> group; searched by <c>method1529</c>
        ///     (:88-105). Every entry in both caches stores a shortcut byte of zero, so this list is
        ///     chosen by position rather than by key.
        /// </remarks>
        public List<QuickChatLink> Messages { get; } = new List<QuickChatLink>();

        /// <summary>
        ///     Opcode 4. A bare flag whose meaning is unknown.
        /// </summary>
        /// <remarks>
        ///     <c>method1527</c> tests only 1, 2 and 3, so the 637 client falls through opcode 4
        ///     reading nothing and the record still parses to its terminator. It does occur in the
        ///     639 data - index 25's menu group, file 1, the root "Quick Chat" node, and nowhere
        ///     else - which is the item-opcode-131 case: the data vetoes the client, the handler
        ///     stays, and the name does not guess at a meaning one occurrence cannot establish.
        ///     <para>
        ///     Backed by the recorded opcode stream rather than by a field, so clearing it drops the
        ///     opcode instead of leaving one for the replay to put back.
        ///     </para>
        /// </remarks>
        public bool UnknownFlag4 {
            get => Opcodes.Has(4);
            set => QuickChatRecord.SetFlag(Opcodes, 4, value);
        }

        /// <summary>Reads one menu record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public QuickChatMenuDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            captionBytes = Array.Empty<byte>();
            Submenus.Clear();
            Messages.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            switch (opcode) {
                case 1:
                    captionBytes = QuickChatRecord.ReadStoredString(stream);
                    return true;

                case 2:
                    ReadLinks(stream, Submenus);
                    return true;

                case 3:
                    ReadLinks(stream, Messages);
                    return true;

                //4 is a bare flag: its presence is its whole payload.
                case 4:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Writes this menu record back to the file representation.</summary>
        /// <remarks>
        ///     Replayed in the recorded opcode order rather than emitted ascending, because one
        ///     record in the 639 data is not ascending: index 25's menu file 1 stores opcode 4 ahead
        ///     of the caption. A fixed ascending encoder reproduces every other record in both banks
        ///     and corrupts that one - which is enough to rewrite the group, its CRC, and the
        ///     reference-table entry of the message group packed beside it.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            /* The "carried it" arm keeps an opcode a record stored at a value indistinguishable
               from absent - an empty caption, or a list with a count of zero - rather than
               shortening a file nobody edited. */
            if (Opcodes.Has(1) || captionBytes.Length > 0)
                records.Add(QuickChatRecord.Payload(1, buffer => QuickChatRecord.WriteStoredString(buffer, captionBytes)));

            if (Opcodes.Has(2) || Submenus.Count > 0)
                records.Add(QuickChatRecord.Payload(2, buffer => WriteLinks(buffer, Submenus, 2)));

            if (Opcodes.Has(3) || Messages.Count > 0)
                records.Add(QuickChatRecord.Payload(3, buffer => WriteLinks(buffer, Messages, 3)));

            //4 carries no payload, so the recorded stream is the only statement of whether it is
            //set - which is exactly what UnknownFlag4 reads and writes.
            return Opcodes.Replay(records, appendInAscendingOrder: true);
        }

        /// <summary>Reads a link list: a byte count, then that many id and shortcut pairs.</summary>
        /// <param name="stream">The stream, positioned at the count.</param>
        /// <param name="into">The list to fill.</param>
        private static void ReadLinks(JagStream stream, List<QuickChatLink> into) {
            int count = stream.ReadUnsignedByte();
            for (int i = 0; i < count; i++) {
                int targetId = stream.ReadUnsignedShort();
                into.Add(new QuickChatLink(targetId, (byte) stream.ReadUnsignedByte()));
            }
        }

        /// <summary>Writes a link list back in the shape the client reads.</summary>
        /// <param name="buffer">The payload buffer.</param>
        /// <param name="links">The links to write.</param>
        /// <param name="opcode">The opcode being written, so a failure names it.</param>
        private static void WriteLinks(JagStream buffer, List<QuickChatLink> links, int opcode) {
            QuickChatRecord.RequireByteCount(links.Count, opcode);
            buffer.WriteByte((byte) links.Count);

            foreach (QuickChatLink link in links) {
                QuickChatRecord.RequireStoredId(link.TargetId, "link target");
                buffer.WriteShort(link.TargetId);
                buffer.WriteByte(link.Shortcut);
            }
        }
    }
}
