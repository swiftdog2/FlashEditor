using System;
using FlashEditor.Cache;
using FlashEditor.Definitions.Config;
using FlashEditor.Definitions.Editing;
using FlashEditor.Export;
using FlashEditor.UI;
using Xunit;

namespace FlashEditor.Tests.UI {
    /// <summary>
    ///     What a link has to carry for it to land on the record it names.
    /// </summary>
    /// <remarks>
    ///     None of this needs a cache. The failure being pinned is that an index 2 reference stated
    ///     as an id alone resolves to a different record depending on which family the Config tab
    ///     was left showing - which is a mistake in the <i>shape</i> of the reference, and shows up
    ///     as a link that lands somewhere plausible and wrong rather than as one that fails.
    /// </remarks>
    public sealed class CrossNavigationTests {
        /// <summary>A row type for the column factories to read.</summary>
        private sealed class Row {
            internal Row(int id) {
                Id = id;
            }

            internal int Id { get; }

            internal int Written { get; set; } = -1;
        }

        /// <summary>A row of a type no column here is wired to.</summary>
        private sealed class OtherRow {
        }

        [Fact]
        public void SameIdInTwoConfigGroupsIsTwoDifferentPlaces() {
            var quest = new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.Quest);
            var icon = new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.MapSceneIcon);

            Assert.NotEqual(quest, icon);
        }

        /// <summary>
        ///     A place that states a group is not the same place as one that leaves it to the index.
        /// </summary>
        /// <remarks>
        ///     They would be equal if the group were dropped from the comparison, and the back stack
        ///     refuses to record a move to where it already is - so a jump from "index 2" to "index
        ///     2, group 35, id 12" would be silently discarded.
        /// </remarks>
        [Fact]
        public void AGroupedPlaceIsNotTheUngroupedOne() {
            Assert.NotEqual(new EditorLocation(RSConstants.CONFIG, 12),
                new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.Quest));
        }

        [Fact]
        public void APlaceNamesItsGroupInWords() {
            Assert.Equal("index 2, group 35, id 12",
                new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.Quest).ToString());
            Assert.Equal("index 9, id 17", new EditorLocation(RSConstants.TEXTURES, 17).ToString());
            Assert.Equal("index 9", new EditorLocation(RSConstants.TEXTURES).ToString());
        }

        [Fact]
        public void TheBackStackRoundTripsAConfigGroupPlace() {
            var navigator = new EditorNavigator();
            var quest = new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.Quest);
            var icon = new EditorLocation(RSConstants.CONFIG, 12, ConfigGroup.MapSceneIcon);

            navigator.GoTo(quest);
            navigator.GoTo(icon);

            Assert.True(navigator.GoBack());
            Assert.Equal(quest, navigator.Current);
        }

        [Fact]
        public void AConfigLinkCarriesItsGroupAndAPlainLinkDoesNot() {
            DefinitionColumn config = DefinitionColumn.ConfigLink<Row>("Quest", ConfigGroup.Quest,
                row => row.Id);
            DefinitionColumn plain = DefinitionColumn.Link<Row>("Texture", RSConstants.TEXTURES,
                row => row.Id);

            DefinitionCellVisual configVisual = config.Visual!(new Row(12));
            DefinitionCellVisual plainVisual = plain.Visual!(new Row(12));

            Assert.Equal(DefinitionCellArt.Link, configVisual.Art);
            Assert.Equal(RSConstants.CONFIG, configVisual.IndexId);
            Assert.Equal(ConfigGroup.Quest, configVisual.GroupId);

            Assert.Equal(RSConstants.TEXTURES, plainVisual.IndexId);
            Assert.Equal(-1, plainVisual.GroupId);
        }

        /// <summary>
        ///     Two links reading the same number for different indexes are different targets.
        /// </summary>
        /// <remarks>
        ///     The count shown before navigating is keyed on what a cell addresses rather than on
        ///     the number in it, and this is the case that separates the two.
        /// </remarks>
        [Fact]
        public void TwoLinksToDifferentIndexesWithTheSameIdAreNotEqual() {
            Assert.NotEqual(DefinitionCellVisual.Link(RSConstants.TEXTURES, 12),
                DefinitionCellVisual.Link(RSConstants.SPRITES_INDEX, 12));
            Assert.NotEqual(DefinitionCellVisual.ConfigLink(ConfigGroup.Quest, 12),
                DefinitionCellVisual.ConfigLink(ConfigGroup.MapSceneIcon, 12));
        }

        /// <summary>An id that names nothing draws nothing.</summary>
        /// <remarks>
        ///     Several of the joined fields store -1 for "no reference", and a link drawn for one
        ///     would offer to navigate to record -1.
        /// </remarks>
        [Fact]
        public void ALinkDrawsNothingForAnIdThatNamesNothing() {
            DefinitionColumn column = DefinitionColumn.Link<Row>("Model", RSConstants.MODELS_INDEX,
                row => row.Id < 0 ? null : row.Id);

            Assert.Equal(DefinitionCellArt.None, column.Visual!(new Row(-1)).Art);
            Assert.Equal(DefinitionCellArt.Link, column.Visual!(new Row(0)).Art);
        }

        /// <summary>
        ///     A recycled row draws an empty cell; a row of the wrong type still throws.
        /// </summary>
        /// <remarks>
        ///     ObjectListView hands a null model to an aspect getter for rows being recycled during
        ///     a scroll. A wrong type can only mean a descriptor wired its columns to a row type it
        ///     does not produce, and blanking that would hide it.
        /// </remarks>
        [Fact]
        public void ALinkToleratesANullRowAndRefusesTheWrongType() {
            DefinitionColumn column = DefinitionColumn.ConfigLink<Row>("Quest", ConfigGroup.Quest,
                row => row.Id);

            Assert.Equal(DefinitionCellArt.None, column.Visual!(null!).Art);
            Assert.Null(column.Read(null!));
            Assert.Throws<ArgumentException>(() => column.Visual!(new OtherRow()));
        }

        /// <summary>
        ///     A link may be editable, because several of the joins sit on fields that already were.
        /// </summary>
        [Fact]
        public void ALinkIsEditableOnlyWhenItIsGivenASetter() {
            DefinitionColumn readOnly = DefinitionColumn.Link<Row>("Model", RSConstants.MODELS_INDEX,
                row => row.Id);
            DefinitionColumn editable = DefinitionColumn.Link<Row>("Model", RSConstants.MODELS_INDEX,
                row => row.Id, (row, value) => row.Written = value);

            Assert.False(readOnly.IsEditable);
            Assert.True(editable.IsEditable);

            var row = new Row(3);

            //Through a string, because the in-place cell editor hands back whatever its editor
            //produced and a text box produces text.
            editable.Write!(row, "17");
            Assert.Equal(17, row.Written);
        }

        [Fact]
        public void AConfigReferenceIsDescribedByItsGroupRatherThanItsIndex() {
            var reference = new ExportedReference("quests[0]", "item opcode 132 -> config group 35",
                12, RSConstants.CONFIG, ConfigGroup.Quest, 12, true, "\"Cook's Assistant\"", null);

            string described = CacheReferencePreview.Describe(reference);

            Assert.Contains("config group 35", described, StringComparison.Ordinal);
            Assert.Contains("Cook's Assistant", described, StringComparison.Ordinal);
        }

        /// <summary>A dangling id says so rather than coming back empty.</summary>
        /// <remarks>
        ///     Finding one is a real result in this cache. An empty preview would read identically
        ///     to one that failed, which is the distinction the whole line exists to make.
        /// </remarks>
        [Fact]
        public void AReferenceTheTableDoesNotDeclareSaysSo() {
            var reference = new ExportedReference("modelId", "spot animation model -> index 7",
                99999, RSConstants.MODELS_INDEX, 99999, 0, false, null, null);

            Assert.Contains("does not declare it", CacheReferencePreview.Describe(reference),
                StringComparison.Ordinal);
        }
    }
}
