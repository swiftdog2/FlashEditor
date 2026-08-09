using System;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using Xunit;

namespace FlashEditor.Tests.Definitions {
    /// <summary>
    ///     The four entity descriptors, checked without a cache.
    /// </summary>
    /// <remarks>
    ///     Everything here is a statement the descriptors make about themselves - which index they
    ///     address, whether they can be written, and how a row's id folds back into a group and a
    ///     file. None of it needs bytes, and all of it is what the page depends on to write an edit
    ///     to the right place.
    ///     <para>
    ///     The addressing is the part worth pinning. Items and objects page 256 ids to a group and
    ///     NPCs page 128, and folding an NPC id with <c>* 256</c> names a different NPC for every
    ///     group above zero - a defect this project has already had once, invisible because the only
    ///     caller overwrote the id on the next line.
    ///     </para>
    /// </remarks>
    public sealed class EntityListDescriptorTests {
        /// <summary>Each descriptor addresses the index its page claims it does.</summary>
        [Fact]
        public void EachDescriptorNamesItsOwnIndex() {
            Assert.Equal(RSConstants.ITEM_DEFINITIONS_INDEX, new ItemListDescriptor().IndexId);
            Assert.Equal(RSConstants.NPC_DEFINITIONS_INDEX, new NPCListDescriptor().IndexId);
            Assert.Equal(RSConstants.OBJECTS_DEFINITIONS_INDEX, new ObjectListDescriptor().IndexId);
            Assert.Equal(RSConstants.MODELS_INDEX, new ModelListDescriptor().IndexId);
        }

        /// <summary>
        ///     The three definition families are editable and the model listing is not.
        /// </summary>
        /// <remarks>
        ///     Not a restatement of the code. Editing turns on only where a byte-identity sweep says
        ///     the index re-encodes to what it was read from, and index 7 has no such sweep - so a
        ///     descriptor that started offering to write models would be offering to corrupt them,
        ///     and nothing else in the suite would notice.
        /// </remarks>
        [Fact]
        public void OnlyTheIndexesWithAByteIdentitySweepAreEditable() {
            Assert.True(new ItemListDescriptor().IsEditable);
            Assert.True(new NPCListDescriptor().IsEditable);
            Assert.True(new ObjectListDescriptor().IsEditable);
            Assert.False(new ModelListDescriptor().IsEditable);
        }

        /// <summary>
        ///     Only the model listing opts out of reading payloads.
        /// </summary>
        /// <remarks>
        ///     The opt-out is what keeps the page affordable: index 7 declares over 63,000 groups of
        ///     one file, and reading them to fill a column of ids would inflate every model in the
        ///     cache. It has to stay off for that one and on for everything else - a descriptor that
        ///     cleared it by accident would show a grid of empty rows, because it would be handed an
        ///     empty payload to decode.
        /// </remarks>
        [Fact]
        public void OnlyTheModelListingSkipsThePayload() {
            Assert.True(new ItemListDescriptor().ReadsPayload);
            Assert.True(new NPCListDescriptor().ReadsPayload);
            Assert.True(new ObjectListDescriptor().ReadsPayload);
            Assert.False(new ModelListDescriptor().ReadsPayload);
        }

        /// <summary>
        ///     An item's id folds back into the address the cache stores it at.
        /// </summary>
        /// <remarks>
        ///     Checked against <see cref="CacheAddressing"/> rather than against <c>id / 256</c>
        ///     written out again here, which would only prove the test and the code agree.
        /// </remarks>
        [Theory]
        [InlineData(0)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(20426)]
        public void AnItemRowAddressesTheFileItCameFrom(int id) {
            var row = new ItemDefinition { id = id };
            DefinitionAddress address = new ItemListDescriptor().AddressOf(row);

            CacheAddressing addressing = CacheAddressing.For(RSConstants.ITEM_DEFINITIONS_INDEX);
            Assert.Equal(addressing.GroupOf(id), address.GroupId);
            Assert.Equal(addressing.FileOf(id), address.FileId);
            Assert.Equal(id, address.DefinitionId);
        }

        /// <summary>
        ///     An NPC's id folds back through the 128-wide paging rather than the 256-wide one.
        /// </summary>
        /// <remarks>
        ///     127 and 128 are the pair that tells the two apart: under a 256-wide fold both land in
        ///     group 0, and under the real one 128 is the first file of group 1.
        /// </remarks>
        [Theory]
        [InlineData(0)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(13358)]
        public void AnNpcRowAddressesTheFileItCameFrom(int id) {
            var row = new NPCDefinition { id = id };
            DefinitionAddress address = new NPCListDescriptor().AddressOf(row);

            CacheAddressing addressing = CacheAddressing.For(RSConstants.NPC_DEFINITIONS_INDEX);
            Assert.Equal(addressing.GroupOf(id), address.GroupId);
            Assert.Equal(addressing.FileOf(id), address.FileId);
            Assert.Equal(id, address.DefinitionId);
        }

        /// <summary>An object's id folds back into the address the cache stores it at.</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(255)]
        [InlineData(256)]
        [InlineData(56198)]
        public void AnObjectRowAddressesTheFileItCameFrom(int id) {
            var row = new ObjectDefinition { id = id };
            DefinitionAddress address = new ObjectListDescriptor().AddressOf(row);

            CacheAddressing addressing = CacheAddressing.For(RSConstants.OBJECTS_DEFINITIONS_INDEX);
            Assert.Equal(addressing.GroupOf(id), address.GroupId);
            Assert.Equal(addressing.FileOf(id), address.FileId);
            Assert.Equal(id, address.DefinitionId);
        }

        /// <summary>
        ///     A model row carries its address rather than deriving one.
        /// </summary>
        /// <remarks>
        ///     Index 7 is one group per id, and <see cref="CacheAddressing.FileOf"/> refuses to answer
        ///     for such an index because the file id is declared by the reference table rather than
        ///     computed. A descriptor that derived it would assume 0, and index 23 is the case that
        ///     proves a single-file group's id is not always 0.
        /// </remarks>
        [Fact]
        public void AModelRowCarriesItsAddressRatherThanDerivingOne() {
            var row = new ModelListing(new DefinitionAddress(4711, 3, 4711));
            DefinitionAddress address = new ModelListDescriptor().AddressOf(row);

            Assert.Equal(4711, address.GroupId);
            Assert.Equal(3, address.FileId);
            Assert.Equal(4711, row.ModelId);
            Assert.Equal(3, row.FileId);
        }

        /// <summary>
        ///     Writing a model is refused, so the read-only grid cannot become a write path by accident.
        /// </summary>
        [Fact]
        public void EncodingAModelIsRefused() {
            IDefinitionListDescriptor descriptor = new ModelListDescriptor();
            var row = new ModelListing(new DefinitionAddress(1, 0, 1));

            Assert.Throws<NotSupportedException>(() => descriptor.Encode(row));
        }

        /// <summary>
        ///     A flag column takes whatever the cell editor hands back.
        /// </summary>
        /// <remarks>
        ///     ObjectListView decides that type itself: a checkbox editor yields a <c>bool</c> and an
        ///     in-place text box yields the string the user typed, so a setter written for one throws
        ///     on the other. Clearing the cell yields an empty string, which
        ///     <c>Convert.ToBoolean</c> refuses outright.
        /// </remarks>
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData("True", true)]
        [InlineData("false", false)]
        [InlineData("1", true)]
        [InlineData("0", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void AFlagColumnTakesWhateverTheCellEditorHandsBack(object? edited, bool expected) {
            var row = new ItemDefinition { membersOnly = !expected };
            DefinitionColumn column = DefinitionColumn.Flag<ItemDefinition>("Members",
                item => item.membersOnly, (item, value) => item.membersOnly = value);

            column.Write!(row, edited);

            Assert.Equal(expected, row.membersOnly);
            Assert.Equal(expected, column.Read(row));
        }

        /// <summary>A flag column renders an empty cell for the null row a scroll recycles.</summary>
        /// <remarks>
        ///     ObjectListView evaluates aspect getters for rows being recycled during a scroll and for
        ///     cells it measures before a model is attached. Throwing there surfaced as an
        ///     ArgumentException while simply scrolling a list.
        /// </remarks>
        [Fact]
        public void AFlagColumnRendersAnEmptyCellForANullRow() {
            DefinitionColumn column = DefinitionColumn.Flag<ItemDefinition>("Members",
                item => item.membersOnly);

            Assert.Null(column.Read(null!));
        }

        /// <summary>
        ///     Every entity column reads its own row type and refuses another.
        /// </summary>
        /// <remarks>
        ///     A row of the wrong type can only mean a descriptor wired its columns to something it
        ///     does not produce, and blanking those cells would hide it. This is the one case that
        ///     must still throw after the null-row case above is made safe.
        /// </remarks>
        [Fact]
        public void AnEntityColumnRefusesARowFromAnotherFamily() {
            DefinitionColumn column = new ItemListDescriptor().Columns.First();

            Assert.Throws<ArgumentException>(() => column.Read(new ObjectDefinition()));
        }
    }
}
