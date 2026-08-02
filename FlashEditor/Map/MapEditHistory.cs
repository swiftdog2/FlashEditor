using System;
using System.Collections.Generic;

namespace FlashEditor.Map {
    /// <summary>An undo and redo stack for map edits.</summary>
    /// <remarks>
    ///     Applying a new edit discards the redo stack, which is the behaviour every editor has and
    ///     the only one that keeps the history a single linear sequence.
    /// </remarks>
    public sealed class MapEditHistory {
        private readonly List<IMapEdit> done = new List<IMapEdit>();
        private readonly List<IMapEdit> undone = new List<IMapEdit>();

        /// <summary>Raised after any change to the history.</summary>
        public event EventHandler Changed;

        /// <summary>Whether there is anything to undo.</summary>
        public bool CanUndo => done.Count > 0;

        /// <summary>Whether there is anything to redo.</summary>
        public bool CanRedo => undone.Count > 0;

        /// <summary>Edits currently applied.</summary>
        public int Count => done.Count;

        /// <summary>What undo would reverse, or <c>null</c>.</summary>
        public string NextUndoDescription => CanUndo ? done[done.Count - 1].Description : null;

        /// <summary>What redo would reapply, or <c>null</c>.</summary>
        public string NextRedoDescription => CanRedo ? undone[undone.Count - 1].Description : null;

        /// <summary>Applies an edit and pushes it onto the undo stack.</summary>
        /// <param name="edit">The edit to apply.</param>
        public void Apply(IMapEdit edit) {
            if (edit == null) throw new ArgumentNullException(nameof(edit));

            edit.Apply();
            done.Add(edit);
            undone.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Reverses the most recent edit.</summary>
        /// <returns>The edit undone, or <c>null</c> when there was nothing to undo.</returns>
        public IMapEdit Undo() {
            if (!CanUndo)
                return null;

            IMapEdit edit = done[done.Count - 1];
            done.RemoveAt(done.Count - 1);
            edit.Undo();
            undone.Add(edit);
            Changed?.Invoke(this, EventArgs.Empty);
            return edit;
        }

        /// <summary>Reapplies the most recently undone edit.</summary>
        /// <returns>The edit redone, or <c>null</c> when there was nothing to redo.</returns>
        public IMapEdit Redo() {
            if (!CanRedo)
                return null;

            IMapEdit edit = undone[undone.Count - 1];
            undone.RemoveAt(undone.Count - 1);
            edit.Apply();
            done.Add(edit);
            Changed?.Invoke(this, EventArgs.Empty);
            return edit;
        }

        /// <summary>
        ///     Forgets all history without reversing anything.
        /// </summary>
        /// <remarks>
        ///     For use after a save, when the applied state has become the baseline. It does not
        ///     revert, so calling it with unsaved edits pending makes them permanent.
        /// </remarks>
        public void Clear() {
            done.Clear();
            undone.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
