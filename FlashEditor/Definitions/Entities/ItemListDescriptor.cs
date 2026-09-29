using System;
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
            UShort("InvModel", row => row.inventoryModelId, (row, value) => row.inventoryModelId = value, 90),
            UShort("ManModel1", row => row.maleWearModel1, (row, value) => row.maleWearModel1 = value, 95),
            UShort("ManModel2", row => row.maleWearModel2, (row, value) => row.maleWearModel2 = value, 95),
            UShort("ManModel3", row => row.maleWearModel3, (row, value) => row.maleWearModel3 = value, 95),
            UShort("Female1", row => row.femaleWearModel1, (row, value) => row.femaleWearModel1 = value, 85),
            UShort("Female2", row => row.femaleWearModel2, (row, value) => row.femaleWearModel2 = value, 85),
            UShort("Female3", row => row.femaleWearModel3, (row, value) => row.femaleWearModel3 = value, 85),
            UShort("Zoom", row => row.modelZoom, (row, value) => row.modelZoom = value, 70),
            UShort("Rotate1", row => row.modelRotation1, (row, value) => row.modelRotation1 = value, 85),
            UShort("Rotate2", row => row.modelRotation2, (row, value) => row.modelRotation2 = value, 85),
            UShort("Zan2d", row => row.zan2d, (row, value) => row.zan2d = value, 70),
            Short("OffsetX", row => row.modelOffsetX, (row, value) => row.modelOffsetX = value, 75),
            Short("OffsetY", row => row.modelOffsetY, (row, value) => row.modelOffsetY = value, 75),
            DefinitionColumn.Text<ItemDefinition>("ManOffset",
                row => ItemFieldText.FormatOffset(row.manWearXOffset, row.manWearYOffset, row.manWearZOffset),
                (row, value) => (row.manWearXOffset, row.manWearYOffset, row.manWearZOffset) =
                    ItemFieldText.ParseOffset(value), 100),
            DefinitionColumn.Text<ItemDefinition>("FemaleOffset",
                row => ItemFieldText.FormatOffset(row.womanWearXOffset, row.womanWearYOffset, row.womanWearZOffset),
                (row, value) => (row.womanWearXOffset, row.womanWearYOffset, row.womanWearZOffset) =
                    ItemFieldText.ParseOffset(value), 100),
            DefinitionColumn.Text<ItemDefinition>("Ground", row => ItemFieldText.FormatOptions(row.groundOptions),
                (row, value) => row.groundOptions = ItemFieldText.ParseOptions(value), 140),
            DefinitionColumn.Text<ItemDefinition>("Inventory", row => ItemFieldText.FormatOptions(row.inventoryOptions),
                (row, value) => row.inventoryOptions = ItemFieldText.ParseOptions(value), 140),
            DefinitionColumn.Text<ItemDefinition>("Recolours",
                row => ItemFieldText.FormatRecolours(row.originalModelColors, row.modifiedModelColors),
                (row, value) => (row.originalModelColors, row.modifiedModelColors) =
                    ItemFieldText.ParseRecolours(value) ?? (null, null), 140),
            DefinitionColumn.Text<ItemDefinition>("Params", row => ItemFieldText.FormatParams(row.itemParams),
                (row, value) => row.itemParams = ItemFieldText.ParseParams(value), 200),
            DefinitionColumn.Number<ItemDefinition>("Value", row => row.value,
                (row, value) => row.value = value, 85),
            //A flag rather than a number: opcode 11 carries no payload, so any value but set or clear
            //would be shown and then lost on save.
            DefinitionColumn.Flag<ItemDefinition>("Stack", row => row.stackable == 1,
                (row, value) => row.stackable = value ? 1 : 0, 70),
            DefinitionColumn.Flag<ItemDefinition>("Members", row => row.membersOnly,
                (row, value) => row.membersOnly = value, 90)
        };

        /// <summary>A column over a field the record stores as an unsigned short.</summary>
        /// <remarks>Refuses what the short cannot hold rather than letting the encoder wrap it.</remarks>
        private static DefinitionColumn UShort(string header, Func<ItemDefinition, int> read,
            Action<ItemDefinition, int> write, int width) {
            return Ranged(header, read, write, width, 0, ushort.MaxValue);
        }

        /// <summary>A column over a field the record stores as a signed short.</summary>
        private static DefinitionColumn Short(string header, Func<ItemDefinition, int> read,
            Action<ItemDefinition, int> write, int width) {
            return Ranged(header, read, write, width, short.MinValue, short.MaxValue);
        }

        private static DefinitionColumn Ranged(string header, Func<ItemDefinition, int> read,
            Action<ItemDefinition, int> write, int width, int min, int max) {
            return DefinitionColumn.Number<ItemDefinition>(header, row => read(row), (row, value) => {
                if (value < min || value > max)
                    throw new FormatException(header + " is stored in two bytes, so it must be from " + min +
                                              " to " + max + "; " + value + " cannot be saved.");
                write(row, value);
            }, width);
        }

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
