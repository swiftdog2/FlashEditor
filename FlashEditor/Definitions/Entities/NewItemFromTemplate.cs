using System;
using System.Collections.Generic;
using FlashEditor.Cache;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Makes a new item by copying an existing one into the next free item id.
    /// </summary>
    /// <remarks>
    ///     A copy keeps every opcode of its template, in the template's order, except the links out
    ///     to the template's own variants. Opcode 97 names the item's note and 121 its lent copy;
    ///     those variants point back at the template (<c>Class205.java:235-242</c> merges a note or a
    ///     lent copy from the item it names), so a copy that kept the links would claim variants that
    ///     stand for a different item.
    /// </remarks>
    public static class NewItemFromTemplate {
        /// <summary>The link opcodes a copy drops: 97 names the item's note, 121 its lent copy.</summary>
        public static readonly IReadOnlyList<int> DroppedLinkOpcodes = new[] { 97, 121 };

        /// <summary>
        ///     Copies <paramref name="template"/> as item <paramref name="newId"/>, without its note
        ///     and lend links.
        /// </summary>
        /// <remarks>
        ///     The copy is decoded from the template's encoding, so it shares no array, dictionary or
        ///     recorded opcode with the template and an edit to one cannot reach the other.
        /// </remarks>
        /// <param name="template">The item to copy. Not modified.</param>
        /// <param name="newId">The id the copy takes.</param>
        /// <returns>The copy.</returns>
        /// <exception cref="NewItemRefusedException">
        ///     The template is itself a note or a lent copy: the client builds those from the item
        ///     they stand for, so their own record is not an item to copy.
        /// </exception>
        public static ItemDefinition Duplicate(ItemDefinition template, int newId) {
            if (template == null)
                throw new ArgumentNullException(nameof(template));

            if (template.notedTemplateId != 0)
                throw new NewItemRefusedException(
                    "Item " + template.id + " is the note of item " + template.notedId + ", which the client " +
                    "draws from the note template. Copy item " + template.notedId + " instead.");

            if (template.lendTemplateId != 0)
                throw new NewItemRefusedException(
                    "Item " + template.id + " is the lent copy of item " + template.lendId + ", which the " +
                    "client draws from the lend template. Copy item " + template.lendId + " instead.");

            ItemDefinition copy = ItemDefinition.DecodeFromStream(template.Encode());
            copy.id = newId;

            foreach (int opcode in DroppedLinkOpcodes)
                copy.Opcodes.Remove(opcode);
            copy.notedId = 0;
            copy.lendId = 0;

            return copy;
        }
    }

    /// <summary>A new item the editor declined to make, with the reason in its message.</summary>
    public sealed class NewItemRefusedException : InvalidOperationException {
        /// <summary>Creates the refusal.</summary>
        /// <param name="message">Why no item was made, in words for the user.</param>
        public NewItemRefusedException(string message) : base(message) { }
    }
}
