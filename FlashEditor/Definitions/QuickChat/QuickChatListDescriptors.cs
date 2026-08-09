using System;
using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     What the Quick Chat tab needs from a row whichever of the two record families it holds.
    /// </summary>
    /// <remarks>
    ///     Menus and messages share an index and a bank but nothing else - one is a caption and two
    ///     link lists, the other a template and its substitution slots - so this is the smallest
    ///     surface the shared detail pane can be written against.
    /// </remarks>
    public interface IQuickChatListing : IDetailRow {
        /// <summary>Where the record lives in the cache.</summary>
        DefinitionAddress Address { get; }
    }

    /// <summary>
    ///     One quick-chat menu node as a list row.
    /// </summary>
    /// <remarks>
    ///     The global id column is the one worth explaining. A record's stored ids never carry the
    ///     second-bank bit - the client ORs it back on at load
    ///     (<c>Node_Sub46_Sub1.method1531</c>) - so index 25's menu 3 and index 24's menu 3 are
    ///     different records with the same stored id. Showing the folded id is what keeps the two
    ///     banks from appearing to reference each other.
    /// </remarks>
    public sealed class QuickChatMenuListing : IQuickChatListing {
        /// <summary>Binds one decoded menu to where it came from.</summary>
        /// <param name="indexId">The bank it was read from, 24 or 25.</param>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public QuickChatMenuListing(int indexId, DefinitionAddress address, QuickChatMenuDefinition record) {
            IndexId = indexId;
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>The bank this record was read from.</summary>
        public int IndexId { get; }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public QuickChatMenuDefinition Record { get; }

        /// <summary>The menu id, which is its file id within the bank's menu group.</summary>
        public int MenuId => Record.Id;

        /// <summary>The id the client uses once the bank bit is folded back on.</summary>
        public string GlobalId => "0x" + QuickChatBank.GlobalId(IndexId, Record.Id).ToString("X4");

        /// <summary>The caption as text.</summary>
        public string Caption => Record.Caption;

        /// <summary>How many submenus this node offers.</summary>
        public int Submenus => Record.Submenus.Count;

        /// <summary>How many messages this node offers.</summary>
        public int Messages => Record.Messages.Count;

        /// <summary>Whether the record carries the opcode the 637 client has no case for.</summary>
        public string Flag4 => Record.UnknownFlag4 ? "yes" : "no";

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        public string OpcodeOrder => DetailText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Menu " + MenuId + " (" + GlobalId + ") - " +
            (Caption.Length == 0 ? "no caption" : "\"" + Caption + "\"") + " - " +
            Submenus + " submenu(s), " + Messages + " message(s) - opcodes " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Caption (opcode 1)", Caption.Length == 0 ? "not stored" : Caption),
                    new DetailField("Caption bytes", Hex(Record.CaptionBytes))
                };

                for (int i = 0; i < Record.Submenus.Count; i++)
                    fields.Add(Link("Submenu " + i + " (opcode 2)", Record.Submenus[i]));

                for (int i = 0; i < Record.Messages.Count; i++)
                    fields.Add(Link("Message " + i + " (opcode 3)", Record.Messages[i]));

                fields.Add(new DetailField("Opcode 4 flag", Flag4));
                fields.Add(new DetailField("Stored opcode order", OpcodeOrder));
                return fields;
            }
        }

        /// <summary>
        ///     Replaces the caption, but only when the typed text differs from what is displayed.
        /// </summary>
        /// <remarks>
        ///     The guard is what makes this safe to offer at all. The cp1252 decode is lossy in five
        ///     byte values, so passing the displayed string back through the encoder would rewrite
        ///     them as question marks - which is exactly what happens if a user opens the cell editor
        ///     and closes it again without typing. Comparing the text first means an untouched cell
        ///     never reaches <see cref="QuickChatText.ToBytes"/>, and text the user really did type is
        ///     encoded on purpose.
        /// </remarks>
        /// <param name="text">The typed text.</param>
        public void SetCaption(string text) {
            string typed = text ?? string.Empty;
            if (string.Equals(typed, Record.Caption, StringComparison.Ordinal))
                return;
            Record.Caption = typed;
        }

        private static DetailField Link(string name, QuickChatLink link) {
            string shortcut = link.Shortcut == 0
                ? "no shortcut"
                : "shortcut '" + link.ShortcutChar + "' (0x" + link.Shortcut.ToString("X2") + ")";
            return new DetailField(name, "target " + link.TargetId + ", " + shortcut);
        }

        internal static string Hex(byte[] bytes) {
            if (bytes.Length == 0)
                return "none";
            return BitConverter.ToString(bytes).Replace('-', ' ');
        }
    }

    /// <summary>One quick-chat message template as a list row.</summary>
    public sealed class QuickChatMessageListing : IQuickChatListing {
        /// <summary>Binds one decoded message to where it came from.</summary>
        /// <param name="indexId">The bank it was read from, 24 or 25.</param>
        /// <param name="address">The group and file.</param>
        /// <param name="record">The decoded record.</param>
        public QuickChatMessageListing(int indexId, DefinitionAddress address, QuickChatMessageDefinition record) {
            IndexId = indexId;
            Address = address;
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        /// <summary>The bank this record was read from.</summary>
        public int IndexId { get; }

        /// <inheritdoc/>
        public DefinitionAddress Address { get; }

        /// <summary>The decoded record.</summary>
        public QuickChatMessageDefinition Record { get; }

        /// <summary>The message id, which is its file id within the bank's message group.</summary>
        public int MessageId => Record.Id;

        /// <summary>The id the client uses once the bank bit is folded back on.</summary>
        public string GlobalId => "0x" + QuickChatBank.GlobalId(IndexId, Record.Id).ToString("X4");

        /// <summary>The template as text, slot markers included.</summary>
        public string Template => Record.Template;

        /// <summary>
        ///     The marker count beside the slot count, because the two are meant to agree.
        /// </summary>
        /// <remarks>
        ///     Shown together rather than separately: the client indexes the slot arrays by the
        ///     segment the template splits into, so a record where they disagree fills the wrong slot
        ///     or none at all. It is a property of well-formed content rather than of the format, so
        ///     the decoder does not refuse it and this is the only place it becomes visible.
        /// </remarks>
        public string Slots {
            get {
                int markers = Record.MarkerCount;
                int slots = Record.Slots.Count;
                return markers == slots ? slots.ToString() : slots + " for " + markers + " marker(s)";
            }
        }

        /// <summary>How many replies this message suggests.</summary>
        public int Responses => Record.ResponseIds.Count;

        /// <summary>Whether the message is hidden from the quick-chat search.</summary>
        public string Hidden => Record.HiddenFromSearch ? "yes" : "no";

        /// <summary>The opcodes the record stored, in the order it stored them.</summary>
        public string OpcodeOrder => DetailText.Order(Record.Opcodes);

        /// <inheritdoc/>
        public string Summary =>
            "Message " + MessageId + " (" + GlobalId + ") - " +
            (Template.Length == 0 ? "no template" : "\"" + Template + "\"") + " - opcodes " + OpcodeOrder;

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields {
            get {
                var fields = new List<DetailField> {
                    new DetailField("Template (opcode 1)", Template.Length == 0 ? "not stored" : Template),
                    new DetailField("Template bytes", QuickChatMenuListing.Hex(Record.TemplateBytes)),
                    new DetailField("Substitution markers", Record.MarkerCount.ToString()),
                    new DetailField("Responses (opcode 2)", DetailText.Ids(Record.ResponseIds))
                };

                for (int i = 0; i < Record.Slots.Count; i++) {
                    QuickChatSlot slot = Record.Slots[i];
                    string words = slot.Words.Count == 0 ? "no words" : "words " + DetailText.Ids(slot.Words);
                    fields.Add(new DetailField("Slot " + i + " (opcode 3)",
                        "type " + slot.SlotTypeId + (slot.IsKnownType ? "" : " (no client definition)") + ", " + words));
                }

                fields.Add(new DetailField("Hidden from search (opcode 4)", Hidden));
                fields.Add(new DetailField("Stored opcode order", OpcodeOrder));
                return fields;
            }
        }

        /// <summary>Replaces the template, but only when the typed text differs from what is displayed.</summary>
        /// <remarks>See <see cref="QuickChatMenuListing.SetCaption"/> - the same lossy decode applies.</remarks>
        /// <param name="text">The typed text.</param>
        public void SetTemplate(string text) {
            string typed = text ?? string.Empty;
            if (string.Equals(typed, Record.Template, StringComparison.Ordinal))
                return;
            Record.Template = typed;
        }
    }

    /// <summary>
    ///     The menu group of one quick-chat bank as a definition list.
    /// </summary>
    /// <remarks>
    ///     Scoped to a single group rather than the whole index, because index 24 and index 25 each
    ///     hold <b>both</b> record families - group 0 is the menu tree and group 1 the messages
    ///     (Class212.java:66,71 against Class280.java:378,383). The base <c>Enumerate</c> walks every
    ///     file the index declares, which here would feed message payloads to the menu decoder.
    ///     <para>
    ///     Editable in the caption alone. Every other field is a count-prefixed link list, which a
    ///     single cell cannot express, and the caption is guarded against being re-encoded when it was
    ///     not actually typed - see <see cref="QuickChatMenuListing.SetCaption"/>.
    ///     </para>
    /// </remarks>
    public sealed class QuickChatMenuListDescriptor : DefinitionListDescriptor<QuickChatMenuListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every menu record of one bank.</summary>
        /// <param name="indexId">The bank to list, 24 or 25.</param>
        public QuickChatMenuListDescriptor(int indexId) {
            IndexId = RequireBank(indexId);

            columns = new[] {
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Menu", row => row.MenuId, 70),
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Global id", row => row.GlobalId, 90),
                DefinitionColumn.Text<QuickChatMenuListing>("Caption", row => row.Caption,
                    (row, value) => row.SetCaption(value), 320),
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Submenus", row => row.Submenus, 90),
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Messages", row => row.Messages, 90),
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Opcode 4", row => row.Flag4, 80),
                DefinitionColumn.ReadOnly<QuickChatMenuListing>("Opcodes", row => row.OpcodeOrder, 110)
            };
        }

        /// <inheritdoc/>
        public override int IndexId { get; }

        /// <inheritdoc/>
        public override string RowNoun => "menu";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return QuickChatEnumeration.Group(cache, IndexId, QuickChatBank.MenuGroup, Address);
        }

        /// <inheritdoc/>
        public override QuickChatMenuListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new QuickChatMenuDefinition { Id = address.FileId };
            record.Decode(payload);
            return new QuickChatMenuListing(IndexId, address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(QuickChatMenuListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(QuickChatMenuListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }

        internal static int RequireBank(int indexId) {
            if (indexId != RSConstants.QUICK_CHAT_MESSAGES && indexId != RSConstants.QUICK_CHAT_MENU)
                throw new ArgumentOutOfRangeException(nameof(indexId), indexId,
                    "Only indexes 24 and 25 hold quick-chat banks.");
            return indexId;
        }
    }

    /// <summary>
    ///     The message group of one quick-chat bank as a definition list.
    /// </summary>
    /// <remarks>
    ///     Group scoped for the same reason as <see cref="QuickChatMenuListDescriptor"/>, and editable
    ///     in the template alone for the same reason: the responses and the slots are count-prefixed
    ///     runs, and a slot in particular cannot be edited independently of its type, because the type
    ///     is what decides how many words follow it.
    /// </remarks>
    public sealed class QuickChatMessageListDescriptor : DefinitionListDescriptor<QuickChatMessageListing> {
        private readonly IReadOnlyList<DefinitionColumn> columns;

        /// <summary>Lists every message record of one bank.</summary>
        /// <param name="indexId">The bank to list, 24 or 25.</param>
        public QuickChatMessageListDescriptor(int indexId) {
            IndexId = QuickChatMenuListDescriptor.RequireBank(indexId);

            columns = new[] {
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Message", row => row.MessageId, 80),
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Global id", row => row.GlobalId, 90),
                DefinitionColumn.Text<QuickChatMessageListing>("Template", row => row.Template,
                    (row, value) => row.SetTemplate(value), 380),
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Slots", row => row.Slots, 130),
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Responses", row => row.Responses, 90),
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Hidden", row => row.Hidden, 70),
                DefinitionColumn.ReadOnly<QuickChatMessageListing>("Opcodes", row => row.OpcodeOrder, 110)
            };
        }

        /// <inheritdoc/>
        public override int IndexId { get; }

        /// <inheritdoc/>
        public override string RowNoun => "message";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => columns;

        /// <inheritdoc/>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override IEnumerable<DefinitionAddress> Enumerate(RSCache cache) {
            return QuickChatEnumeration.Group(cache, IndexId, QuickChatBank.MessageGroup, Address);
        }

        /// <inheritdoc/>
        public override QuickChatMessageListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            var record = new QuickChatMessageDefinition { Id = address.FileId };
            record.Decode(payload);
            return new QuickChatMessageListing(IndexId, address, record);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(QuickChatMessageListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Address;
        }

        /// <inheritdoc/>
        public override JagStream Encode(QuickChatMessageListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return row.Record.Encode();
        }
    }

    /// <summary>Shared group-scoped enumeration for the two quick-chat descriptors.</summary>
    internal static class QuickChatEnumeration {
        /// <summary>
        ///     Every file one group of a bank declares.
        /// </summary>
        /// <remarks>
        ///     From the reference table rather than a <c>0..count-1</c> walk. Neither bank is densely
        ///     numbered - index 25's message group spans 0 to 69 with 62 absent, in both caches - so a
        ///     counted walk would ask for a file that does not exist and miss the one that does.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="indexId">The bank index.</param>
        /// <param name="groupId">The group within it.</param>
        /// <param name="address">Builds the address, so the descriptor's own id rules apply.</param>
        /// <returns>The addresses to load.</returns>
        internal static IEnumerable<DefinitionAddress> Group(RSCache cache, int indexId, int groupId,
            Func<int, int, DefinitionAddress> address) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            foreach (int file in cache.GetFileIds(indexId, groupId))
                yield return address(groupId, file);
        }
    }
}
