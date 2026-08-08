using System.Collections.Generic;
using FlashEditor.cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.Entities {
    /// <summary>
    ///     Index 16's object definitions as an editable list.
    /// </summary>
    /// <remarks>
    ///     Replaces the bespoke arm the Objects tab carried inside <c>Editor.LoadEditorTab</c>, which
    ///     read each record through <c>RSCache.GetObjectDefinition</c> and so re-inflated each group
    ///     once per file - 56,199 group decodes where <c>DefinitionListPanel</c> does 224 for the
    ///     same bytes - and called <c>SetObjects</c> from inside <c>DoWork</c>.
    ///     <para>
    ///     56,199 records over 224 groups in both caches. The addressing matters here more than
    ///     anywhere: 64 of those 224 groups are short, so a page size derived from group 0's file
    ///     count names the wrong definition for every group after the first short one.
    ///     <see cref="CacheAddressing"/> is what states the split once.
    ///     </para>
    /// </remarks>
    public sealed class ObjectListDescriptor : DefinitionListDescriptor<ObjectDefinition> {
        private static readonly IReadOnlyList<DefinitionColumn> ObjectColumns = new[] {
            //The id is the address, not a field. See ItemListDescriptor.
            DefinitionColumn.ReadOnly<ObjectDefinition>("ID", row => row.id, 70),
            DefinitionColumn.Text<ObjectDefinition>("Name", row => row.name,
                (row, value) => row.name = value, 190),
            DefinitionColumn.Number<ObjectDefinition>("SizeX", row => row.sizeX,
                (row, value) => row.sizeX = (byte) value, 70),
            DefinitionColumn.Number<ObjectDefinition>("SizeY", row => row.sizeY,
                (row, value) => row.sizeY = (byte) value, 70),
            DefinitionColumn.Flag<ObjectDefinition>("Walkable", row => row.walkable,
                (row, value) => row.walkable = value, 90),
            DefinitionColumn.Flag<ObjectDefinition>("Clipped", row => row.isClipped,
                (row, value) => row.isClipped = value, 85),
            DefinitionColumn.Number<ObjectDefinition>("Sound", row => row.ambientSoundId,
                (row, value) => row.ambientSoundId = value, 80),
            DefinitionColumn.Number<ObjectDefinition>("MorphVar", row => row.morphVarbit,
                (row, value) => row.morphVarbit = value, 95)
        };

        /// <inheritdoc/>
        public override int IndexId => RSConstants.OBJECTS_DEFINITIONS_INDEX;

        /// <inheritdoc/>
        public override string RowNoun => "object";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns => ObjectColumns;

        /// <summary>Editable, because index 16 re-encodes byte for byte over all 56,199 records.</summary>
        public override bool IsEditable => true;

        /// <inheritdoc/>
        public override ObjectDefinition Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            ObjectDefinition definition = ObjectDefinition.DecodeFromStream(payload);
            definition.id = address.DefinitionId;
            return definition;
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(ObjectDefinition row) {
            CacheAddressing addressing = CacheAddressing.For(IndexId);
            return new DefinitionAddress(addressing.GroupOf(row.id), addressing.FileOf(row.id), row.id);
        }

        /// <inheritdoc/>
        public override JagStream Encode(ObjectDefinition row) {
            return row.Encode();
        }
    }
}
