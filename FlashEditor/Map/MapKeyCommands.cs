using System.Windows.Forms;

namespace FlashEditor.Map {
    /// <summary>What a Map tab key does to the selection, beyond arming a tool.</summary>
    public enum MapKeyCommand {
        /// <summary>The key is not a selection command.</summary>
        None,

        /// <summary>Esc: abandon the selection drag in progress, or clear the selection when there is none.</summary>
        CancelDragOrClearSelection,

        /// <summary>Ctrl+D: empty the selection.</summary>
        ClearSelection,

        /// <summary>Ctrl+Up: raise every selected tile one height step, whatever tool is armed.</summary>
        RaiseSelection,

        /// <summary>Ctrl+Down: lower every selected tile one height step, whatever tool is armed.</summary>
        LowerSelection,

        /// <summary>Enter: apply the armed tool to every selected tile, as G does.</summary>
        FillSelection
    }

    /// <summary>
    ///     The Map tab's selection keys.
    /// </summary>
    /// <remarks>
    ///     Matched on the exact combination, modifiers included, so a key that carries an extra
    ///     modifier falls through to whoever else wants it. Bare arrows in particular must fall
    ///     through: the canvas pans on them.
    /// </remarks>
    public static class MapKeyCommands {
        /// <summary>The command a key combination issues.</summary>
        /// <param name="keyData">The key and its modifiers, as <c>ProcessCmdKey</c> receives them.</param>
        /// <returns>The command, or <see cref="MapKeyCommand.None"/>.</returns>
        public static MapKeyCommand For(Keys keyData) {
            switch (keyData) {
                case Keys.Escape: return MapKeyCommand.CancelDragOrClearSelection;
                case Keys.Control | Keys.D: return MapKeyCommand.ClearSelection;
                case Keys.Control | Keys.Up: return MapKeyCommand.RaiseSelection;
                case Keys.Control | Keys.Down: return MapKeyCommand.LowerSelection;
                case Keys.Enter: return MapKeyCommand.FillSelection;
                default: return MapKeyCommand.None;
            }
        }
    }
}
