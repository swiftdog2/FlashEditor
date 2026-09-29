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

        /// <summary>
        ///     Raised after an edit is applied, undone or redone, with that edit, before
        ///     <see cref="Changed"/>. Not raised by <see cref="Clear"/>, which changes no square.
        /// </summary>
        /// <remarks>
        ///     Raised on the thread that stepped the history, which for the Map tab is inside the
        ///     square store's exclusive section, so a handler must not wait on anything that takes it.
        /// </remarks>
        public event EventHandler<IMapEdit>? MapChanged;

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
            MapChanged?.Invoke(this, edit);
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
            MapChanged?.Invoke(this, edit);
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
            MapChanged?.Invoke(this, edit);
            Changed?.Invoke(this, EventArgs.Empty);
            return edit;
        }

        /// <summary>
        ///     Forgets all history without reversing anything.
        /// </summary>
        /// <remarks>
        ///     For when the squares the edits refer to are gone, as on binding a different cache.
        ///     Not for a save: the edits stay valid after one, and an undo then re-dirties its
        ///     square. It does not revert, so calling it with unsaved edits pending makes them
        ///     permanent.
        /// </remarks>
        public void Clear() {
            done.Clear();
            undone.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
