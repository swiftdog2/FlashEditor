using System.Windows.Forms;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     The Map tab's selection keys, as a key-to-command table that needs no window.
    /// </summary>
    public sealed class MapKeyCommandsTests
    {
        [Theory]
        [InlineData(Keys.Escape, MapKeyCommand.CancelDragOrClearSelection)]
        [InlineData(Keys.Control | Keys.D, MapKeyCommand.ClearSelection)]
        [InlineData(Keys.Control | Keys.Up, MapKeyCommand.RaiseSelection)]
        [InlineData(Keys.Control | Keys.Down, MapKeyCommand.LowerSelection)]
        [InlineData(Keys.Enter, MapKeyCommand.FillSelection)]
        public void KeyMap_MapsTheSelectionKeys(Keys key, MapKeyCommand expected)
        {
            Assert.Equal(expected, MapKeyCommands.For(key));
        }

        /// <summary>
        ///     Keys owned by something else stay with it: the canvas pans on a bare arrow, the
        ///     palette arms tools on letters and owns Ctrl+Z / Ctrl+Y, and a plain D no longer
        ///     clears anything.
        /// </summary>
        [Theory]
        [InlineData(Keys.Up)]
        [InlineData(Keys.Down)]
        [InlineData(Keys.Left)]
        [InlineData(Keys.D)]
        [InlineData(Keys.G)]
        [InlineData(Keys.Control | Keys.Z)]
        [InlineData(Keys.Control | Keys.Y)]
        [InlineData(Keys.Shift | Keys.Escape)]
        [InlineData(Keys.Control | Keys.Shift | Keys.Up)]
        [InlineData(Keys.Control | Keys.Enter)]
        public void KeyMap_LeavesEveryOtherKeyAlone(Keys key)
        {
            Assert.Equal(MapKeyCommand.None, MapKeyCommands.For(key));
        }
    }
}
