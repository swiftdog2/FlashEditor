using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Index 18's NPC definitions as an editable list.
    /// </summary>
    /// <remarks>
    ///     Replaces the bespoke arm the NPCs tab carried inside <c>Editor.LoadEditorTab</c>, which
    ///     read each record through <c>RSCache.GetNPCDefinition</c> and so re-inflated each group
    ///     once per file - 13,359 group decodes where <c>DefinitionListPanel</c> does 106 for the
    ///     same bytes - and called <c>SetObjects</c> from inside <c>DoWork</c>.
    ///     <para>
    ///     13,359 records over 106 groups, and that figure holds in both caches, so it is a property
    ///     of build 639 rather than of one cache. The enumeration is still table-driven: the old arm
    ///     walked 106 groups x 128 slots and reported 13,568, counting 209 slots that were never
    ///     allocated as NPCs.
    ///     </para>
    /// </remarks>
    public sealed class NPCListDescriptor : DefinitionListDescriptor<NPCDefinition> {
        private static readonly IReadOnlyList<DefinitionColumn> NpcColumns = new[] {
            //The id is the address, not a field. See ItemListDescriptor.
            DefinitionColumn.ReadOnly<NPCDefinition>("ID", row => row.id, 70),
            DefinitionColumn.Text<NPCDefinition>("Name", row => row.name,
                (row, value) => row.name = value, 190),
            DefinitionColumn.Number<NPCDefinition>("Size", row => row.size,
                (row, value) => row.size = value, 65),
            DefinitionColumn.Number<NPCDefinition>("Level", row => row.level,
                (row, value) => row.level = value, 65),
            /* Opcode 127. This is the only route from an NPC to the animations it plays: it names a
               record in index 2 group 32, which holds the idle, walk, run and turn animation ids.
               The entity page reads it to fill its animation selector. */
            DefinitionColumn.Number<NPCDefinition>("Render", row => row.renderTypeID,
                (row, value) => row.renderTypeID = value, 80),
            DefinitionColumn.Flag<NPCDefinition>("Clickable", row => row.clickable,
                (row, value) => row.clickable = value, 95),
            DefinitionColumn.Flag<NPCDefinition>("Minidot", row => row.drawMinimapDot,
                (row, value) => row.drawMinimapDot = value, 85),
            DefinitionColumn.Number<NPCDefinition>("Rotation", row => row.rotation,
                (row, value) => row.rotation = value, 85),
            DefinitionColumn.Number<NPCDefinition>("Ambient", row => row.ambient,
                (row, value) => row.ambient = value, 80),
            DefinitionColumn.Number<NPCDefinition>("Contrast", row => row.contrast,
                (row, value) => row.contrast = value, 85),
            DefinitionColumn.Number<NPCDefinition>("AtkCursor", row => row.attackOpCursor,
                (row, value) => row.attackOpCursor = value, 95),
            DefinitionColumn.Flag<NPCDefinition>("VisiblePrio", row => row.visiblePriority,
                (row, value) => row.visiblePriority = value, 105),
            /* A joined view of an array, so it is read only: writing it back would need the string
               split into ids, and a typo would silently drop a model from the NPC rather than
               failing. The models themselves are edited by picking the NPC and reading the ids. */
            DefinitionColumn.ReadOnly<NPCDefinition>("Model IDs", row => row.ModelIdList, 180)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.NPC_DEFINITIONS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "NPC";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => NpcColumns;

        /// <summary>Editable, because index 18 re-encodes byte for byte over all 13,359 records.</summary>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override NPCDefinition Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            NPCDefinition definition = new NPCDefinition(payload);
            definition.SetId(address.DefinitionId);
            return definition;
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(NPCDefinition row) {
            /* NPCs page 128 ids to a group rather than 256, and CacheAddressing is what states that
               once. Folding the id with * 256 here would name a different NPC for every group above
               zero, which is a defect this project has already had once. */
            CacheAddressing addressing = CacheAddressing.For(IndexId);
            return new DefinitionAddress(addressing.GroupOf(row.id), addressing.FileOf(row.id), row.id);
        }

        /// <inheritdoc/>
        public override JagStream Encode(NPCDefinition row) {
            return row.Encode();
        }
    }
}
