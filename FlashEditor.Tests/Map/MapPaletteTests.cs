using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FlashEditor.Map;
using FlashEditor.UI;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     The Map tab's tool palette as built: its order, its groups and what its tooltips say.
    /// </summary>
    /// <remarks>
    ///     Read off the strip the panel actually constructs rather than off a table beside it, so a
    ///     button added outside the table is caught too. Icons stand in for the buttons' identity,
    ///     which is sound only because the second test requires every icon to be distinct.
    /// </remarks>
    public sealed class MapPaletteTests
    {
        private const string Separator = "|";

        [Fact]
        public void Palette_IsGroupedInspectSelectPaintTerrainObjectsHistoryLive()
        {
            using var panel = new MapEditorPanel();

            string[] expected = {
                nameof(EditorIcon.Pointer), nameof(EditorIcon.Eyedropper), Separator,
                nameof(EditorIcon.SelectRectangle), nameof(EditorIcon.SelectFreehand),
                nameof(EditorIcon.SelectSimilar), nameof(EditorIcon.FillArea),
                nameof(EditorIcon.ClearSelection), Separator,
                nameof(EditorIcon.Underlay), nameof(EditorIcon.Overlay),
                nameof(EditorIcon.OverlayShape), nameof(EditorIcon.OverlayRotate), Separator,
                nameof(EditorIcon.Raise), nameof(EditorIcon.Lower), nameof(EditorIcon.Block), Separator,
                nameof(EditorIcon.Add), nameof(EditorIcon.Rotate), nameof(EditorIcon.Shape),
                nameof(EditorIcon.Remove), Separator,
                nameof(EditorIcon.Undo), nameof(EditorIcon.Redo), Separator,
                nameof(EditorIcon.Link)
            };

            Assert.Equal(expected, Layout(panel.Palette));
        }

        [Fact]
        public void Palette_GivesEveryButtonADistinctIcon()
        {
            using var panel = new MapEditorPanel();

            List<EditorIcon> icons = panel.Palette.Tools.Select(tool => tool.Icon).ToList();
            List<EditorIcon> repeated = icons.GroupBy(icon => icon).Where(g => g.Count() > 1)
                .Select(g => g.Key).ToList();

            Assert.Empty(repeated);
        }

        /// <summary>
        ///     The keys that are not a button's own shortcut have nowhere else to be found, so the
        ///     tooltip of the button they stand in for has to name them.
        /// </summary>
        [Theory]
        [InlineData(EditorIcon.FillArea, "(G)", "Enter")]
        [InlineData(EditorIcon.ClearSelection, "(Ctrl+D)", "Esc")]
        [InlineData(EditorIcon.Raise, "(R)", "Ctrl+Up")]
        [InlineData(EditorIcon.Lower, "(F)", "Ctrl+Down")]
        [InlineData(EditorIcon.OverlayShape, "(S)", null)]
        [InlineData(EditorIcon.OverlayRotate, "(T)", null)]
        [InlineData(EditorIcon.Rotate, "(K)", null)]
        [InlineData(EditorIcon.Shape, "(J)", null)]
        [InlineData(EditorIcon.Remove, "(X)", null)]
        public void Palette_TooltipsNameTheirKeys(EditorIcon icon, string shortcut, string? alsoMentions)
        {
            using var panel = new MapEditorPanel();

            string tooltip = panel.Palette.Tools.Single(tool => tool.Icon == icon).ToolTipText;

            Assert.EndsWith(shortcut, tooltip);
            if (alsoMentions != null)
                Assert.Contains(alsoMentions, tooltip);
        }

        private static List<string> Layout(ToolStrip strip)
        {
            var layout = new List<string>();
            foreach (ToolStripItem item in strip.Items)
                layout.Add(item is EditorToolButton button ? button.Icon.ToString() : Separator);
            return layout;
        }
    }
}
