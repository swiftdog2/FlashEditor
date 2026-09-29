using System;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

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

            copy.Opcodes.Remove(97);
            copy.notedId = 0;
            copy.Opcodes.Remove(121);
            copy.lendId = 0;

            return copy;
        }

        /// <summary>
        ///     The id the next new item takes: one past the highest item the reference table declares.
        /// </summary>
        /// <remarks>
        ///     Past the highest rather than into a gap, because the server builds its equipment index
        ///     from the item ids in order, so an id taken below the highest would renumber every
        ///     wearable item after it.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <returns>The next item id.</returns>
        public static int NextItemId(RSCache cache) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            int highest = -1;
            foreach ((int group, int file) in cache.EnumerateFiles(RSConstants.ITEM_DEFINITIONS_INDEX))
                highest = Math.Max(highest, addressing.DefinitionId(group, file));
            return highest + 1;
        }

        /// <summary>
        ///     Refuses an item id that server code treats specially whatever the item is.
        /// </summary>
        /// <param name="itemId">The id a new item would take.</param>
        /// <exception cref="NewItemRefusedException">The id is <see cref="ReservedItemId"/>.</exception>
        public static void RefuseReservedId(int itemId) {
            if (itemId == ReservedItemId)
                throw new NewItemRefusedException(
                    "The next item id is " + ReservedItemId + ", and the HydraScape server hard-codes " +
                    ReservedItemId + " as a full helmet (EquipmentTypes.isFullHelmet), so any item made " +
                    "there hides the wearer's head. No item was made.");
        }

        /// <summary>The item id <c>EquipmentTypes.isFullHelmet</c> in the HydraScape server names.</summary>
        public const int ReservedItemId = 20474;

        /// <summary>
        ///     Copies item <paramref name="templateId"/> into <see cref="NextItemId"/> and stages it.
        /// </summary>
        /// <remarks>
        ///     Staged like any edit: nothing reaches the disk until the cache is saved. A copy that
        ///     lands in a group the table already declares is a new file in it; one past a full group
        ///     creates the next group. Every refusal is raised before anything is staged.
        /// </remarks>
        /// <param name="cache">The open cache to stage into.</param>
        /// <param name="templateId">The item to copy.</param>
        /// <returns>The staged copy, whose <see cref="ItemDefinition.id"/> is the id it took.</returns>
        /// <exception cref="NewItemRefusedException">
        ///     The next id is <see cref="ReservedItemId"/>, or the template is a note or a lent copy.
        /// </exception>
        public static ItemDefinition Stage(RSCache cache, int templateId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            int itemId = NextItemId(cache);
            RefuseReservedId(itemId);

            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            ItemDefinition template = ItemDefinition.DecodeFromStream(new JagStream(cache.ReadFileBytes(
                RSConstants.ITEM_DEFINITIONS_INDEX, addressing.GroupOf(templateId), addressing.FileOf(templateId))));
            template.id = templateId;

            ItemDefinition copy = Duplicate(template, itemId);
            int group = addressing.GroupOf(itemId);
            int file = addressing.FileOf(itemId);

            if (file == 0)
                cache.CreateGroup(RSConstants.ITEM_DEFINITIONS_INDEX, group, new[] { new RSGroupFile(file, copy.Encode()) });
            else
                DefinitionWriter.Save(cache, RSConstants.ITEM_DEFINITIONS_INDEX, itemId, copy.Encode().ToArray());

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
