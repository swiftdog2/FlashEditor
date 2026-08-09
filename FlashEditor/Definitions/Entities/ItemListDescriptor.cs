using System.Collections.Generic;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Index 19's item definitions as an editable list.
    /// </summary>
    /// <remarks>
    ///     This replaces the bespoke arm the Items tab used to carry inside <c>Editor.LoadEditorTab</c>.
    ///     Two things change with it beyond the tidying. The old arm read every definition through
    ///     <c>RSCache.GetItemDefinition</c>, which goes through <c>RSCache.ReadFile</c> and releases
    ///     the container the moment it has handed back one file - so it re-inflated each group once
    ///     per file it holds, 20,427 group decodes in the vanilla capture where
    ///     <c>DefinitionListPanel</c> does 80 for the same bytes. And it called
    ///     <c>ObjectListView.SetObjects</c> from inside <c>DoWork</c>, which is cross-thread control
    ///     access that happened to survive.
    ///     <para>
    ///     The row count is a property of the cache rather than of the format: index 19 declares
    ///     20,427 files in the vanilla capture and 20,470 in the repack, which is why nothing here
    ///     writes one down and the enumeration is table-driven.
    ///     </para>
    /// </remarks>
    public sealed class ItemListDescriptor : DefinitionListDescriptor<ItemDefinition> {
        private static readonly IReadOnlyList<DefinitionColumn> ItemColumns = new[] {
            /* The id is the address, not a field: the commit folds it back into a group and a file
               through CacheAddressing, so an edited id would write the definition over a different
               item. Read only for the same reason the NPC and object grids made theirs read only. */
            DefinitionColumn.ReadOnly<ItemDefinition>("ID", row => row.id, 70),
            DefinitionColumn.Text<ItemDefinition>("Name", row => row.name,
                (row, value) => row.name = value, 190),
            DefinitionColumn.Number<ItemDefinition>("InvModel", row => row.inventoryModelId,
                (row, value) => row.inventoryModelId = value, 90),
            DefinitionColumn.Number<ItemDefinition>("ManModel1", row => row.maleWearModel1,
                (row, value) => row.maleWearModel1 = value, 95),
            DefinitionColumn.Number<ItemDefinition>("ManModel2", row => row.maleWearModel2,
                (row, value) => row.maleWearModel2 = value, 95),
            DefinitionColumn.Number<ItemDefinition>("Female1", row => row.femaleWearModel1,
                (row, value) => row.femaleWearModel1 = value, 85),
            DefinitionColumn.Number<ItemDefinition>("Female2", row => row.femaleWearModel2,
                (row, value) => row.femaleWearModel2 = value, 85),
            DefinitionColumn.Number<ItemDefinition>("Rotate1", row => row.modelRotation1,
                (row, value) => row.modelRotation1 = value, 85),
            DefinitionColumn.Number<ItemDefinition>("Rotate2", row => row.modelRotation2,
                (row, value) => row.modelRotation2 = value, 85),
            DefinitionColumn.Number<ItemDefinition>("Value", row => row.value,
                (row, value) => row.value = value, 85),
            DefinitionColumn.Number<ItemDefinition>("Stack", row => row.stackable,
                (row, value) => row.stackable = value, 70),
            /* Stored as a byte in the record, so the setter narrows rather than the column carrying
               a byte: the cell editor decides the type it hands back and DefinitionColumn.Number is
               the one place that conversion is written. */
            DefinitionColumn.Number<ItemDefinition>("Slot", row => row.equipSlotId,
                (row, value) => row.equipSlotId = (byte) value, 70),
            DefinitionColumn.Number<ItemDefinition>("EquipId", row => row.equipId,
                (row, value) => row.equipId = (byte) value, 85),
            DefinitionColumn.Flag<ItemDefinition>("Members", row => row.membersOnly,
                (row, value) => row.membersOnly = value, 90)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.ITEM_DEFINITIONS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "item";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => ItemColumns;

        /// <summary>
        ///     Editable, because index 19 re-encodes byte for byte.
        /// </summary>
        /// <remarks>
        ///     Pinned by the item byte-identity sweep: every record the reference table declares
        ///     re-encodes to the bytes it was read from. That is what makes an in-place edit safe -
        ///     without it, saving a record nobody touched would rewrite it.
        /// </remarks>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override ItemDefinition Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            ItemDefinition definition = ItemDefinition.DecodeFromStream(payload);

            //The id is the address rather than a stored field, so it is applied here rather than
            //hoped for out of the record.
            definition.id = address.DefinitionId;
            return definition;
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ItemDefinition row) {
            CacheAddressing addressing = CacheAddressing.For(IndexId);
            return new DefinitionAddress(addressing.GroupOf(row.id), addressing.FileOf(row.id), row.id);
        }

        /// <inheritdoc/>
        public override JagStream Encode(ItemDefinition row) {
            return row.Encode();
        }
    }
}
