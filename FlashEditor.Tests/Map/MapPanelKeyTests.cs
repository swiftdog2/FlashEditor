using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using FlashEditor.Map;
using FlashEditor.Tests.Cache.RealCache;
using FlashEditor.Utils;
using Xunit;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     The Map tab's selection keys, driven through the panel.
    /// </summary>
    /// <remarks>
    ///     Nothing here commits, so the panel is bound to the shared read-only cache: edits stay in
    ///     the squares the panel's own store decoded and never reach the file store.
    /// </remarks>
    public sealed class MapPanelKeyTests : IClassFixture<RealCacheFixture>
    {
        private const int RegionX = 50, RegionY = 50;
        private const int BaseX = RegionX * MapRegion.WIDTH, BaseY = RegionY * MapRegion.HEIGHT;

        private readonly RealCacheFixture _fixture;

        public MapPanelKeyTests(RealCacheFixture fixture)
        {
            _fixture = fixture;
            DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
        }

        [Theory]
        [InlineData(Keys.Escape)]
        [InlineData(Keys.Control | Keys.D)]
        public void ClearKeys_EmptyTheSelection(Keys key)
        {
            using var panel = Sized();
            panel.Selection.Apply(MapSelection.RectangleTiles(BaseX, BaseY, BaseX + 3, BaseY + 3),
                MapSelectionMode.Replace);

            Assert.True(panel.HandleKey(key));
            Assert.True(panel.Selection.IsEmpty);
        }

        [Fact]
        public void PlainD_NoLongerClearsTheSelection()
        {
            using var panel = Sized();
            panel.Selection.Apply(MapSelection.RectangleTiles(BaseX, BaseY, BaseX + 3, BaseY + 3),
                MapSelectionMode.Replace);

            Assert.False(panel.HandleKey(Keys.D));
            Assert.Equal(16, panel.Selection.Count);
        }

        [RealCacheFact]
        public void CtrlUpAndDown_StepEverySelectedTile_AsOneUndoEntry_WhateverToolIsArmed()
        {
            using var panel = Bound();
            MapRegion square = panel.Store!.GetOrLoad(RegionX, RegionY);

            //Three by two, local (10,10)..(12,11), with Inspect still armed.
            var selected = MapSelection.RectangleTiles(10, 10, 12, 11).ToArray();
            panel.Selection.Apply(selected.Select(t => (BaseX + t.WorldX, BaseY + t.WorldY)),
                MapSelectionMode.Replace);

            int[] original = selected.Select(t => square.GetTileHeight(0, t.WorldX, t.WorldY)).ToArray();
            int[] raised = selected.Select(t => MapAreaEdits.StepHeight(square, 0, t.WorldX, t.WorldY, +1)).ToArray();
            int[] lowered = selected.Select(t => MapAreaEdits.StepHeight(square, 0, t.WorldX, t.WorldY, -1)).ToArray();
            int outside = square.GetTileHeight(0, 13, 10);

            Assert.NotEqual(original, raised);

            Assert.True(panel.HandleKey(Keys.Control | Keys.Up));
            Assert.Equal(raised, Heights(square, selected));
            Assert.Equal(outside, square.GetTileHeight(0, 13, 10));

            panel.Undo();
            Assert.Equal(original, Heights(square, selected));
            Assert.False(panel.CanUndo, "the raise was more than one undo entry");

            Assert.True(panel.HandleKey(Keys.Control | Keys.Down));
            Assert.Equal(lowered, Heights(square, selected));
        }

        [RealCacheFact]
        public void CtrlUp_WithNothingSelected_ChangesNothing_AndIsStillConsumed()
        {
            using var panel = Bound();

            Assert.True(panel.HandleKey(Keys.Control | Keys.Up));
            Assert.False(panel.CanUndo);
        }

        [RealCacheFact]
        public void Enter_FillsTheSelectionWithTheArmedTool()
        {
            using var panel = Bound();
            MapRegion square = panel.Store!.GetOrLoad(RegionX, RegionY);

            var selected = MapSelection.RectangleTiles(20, 20, 21, 21).ToArray();
            panel.Selection.Apply(selected.Select(t => (BaseX + t.WorldX, BaseY + t.WorldY)),
                MapSelectionMode.Replace);
            int[] raised = selected.Select(t => MapAreaEdits.StepHeight(square, 0, t.WorldX, t.WorldY, +1)).ToArray();

            Assert.True(panel.Palette.HandleShortcut(Keys.R));
            Assert.True(panel.HandleKey(Keys.Enter));

            Assert.Equal(raised, Heights(square, selected));
        }

        private MapEditorPanel Bound()
        {
            var panel = Sized();
            panel.Bind(_fixture.OpenCache(), RealCacheLocator.Directory);

            //Area edits are refused below the editing zoom, which a panel with no window starts at.
            panel.View.Camera.SetPixelsPerTile(8);
            return panel;
        }

        //Arming a tool that shows an option group on a zero-sized panel never finishes laying out.
        private static MapEditorPanel Sized() => new MapEditorPanel { Size = new Size(1600, 900) };

        private static int[] Heights(MapRegion square, (int X, int Y)[] tiles) =>
            tiles.Select(t => square.GetTileHeight(0, t.X, t.Y)).ToArray();
    }
}
