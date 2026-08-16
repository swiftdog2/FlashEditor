using System;
using System.Collections.Generic;

namespace FlashEditor.UI {
    /// <summary>
    ///     One place in the cache: an index and, where there is one, a record within it.
    /// </summary>
    public readonly struct EditorLocation : IEquatable<EditorLocation> {
        /// <summary>A place.</summary>
        /// <param name="indexId">The cache index.</param>
        /// <param name="recordId">The record, or -1 for the index itself.</param>
        /// <param name="groupId">
        ///     The group the record sits in, for an index whose ids do not name one, or -1.
        /// </param>
        public EditorLocation(int indexId, int recordId = -1, int groupId = -1) {
            IndexId = indexId;
            RecordId = recordId;
            GroupId = groupId;
        }

        /// <summary>The cache index.</summary>
        public int IndexId { get; }

        /// <summary>The record within it, or -1.</summary>
        public int RecordId { get; }

        /// <summary>
        ///     The group the record sits in, or -1 when the index's own arithmetic derives it.
        /// </summary>
        /// <remarks>
        ///     <b>Index 2 is why this exists.</b> It is thirty-five unrelated config families sharing
        ///     one index and has no id arithmetic at all, so "config record 12" is not a place -
        ///     twelve is a quest, a map scene icon and a parameter type all at once, and which one is
        ///     decided by the group. Every other index derives its group from the record id through
        ///     <c>CacheAddressing</c> and leaves this -1 rather than restating what the arithmetic
        ///     already says.
        /// </remarks>
        public int GroupId { get; }

        /// <summary>Whether this names a particular record rather than a whole index.</summary>
        public bool HasRecord => RecordId >= 0;

        /// <summary>Whether the place states its own group rather than deriving one.</summary>
        public bool HasGroup => GroupId >= 0;

        /// <inheritdoc/>
        public bool Equals(EditorLocation other) {
            return IndexId == other.IndexId && RecordId == other.RecordId && GroupId == other.GroupId;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) {
            return obj is EditorLocation other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode() {
            return HashCode.Combine(IndexId, RecordId, GroupId);
        }

        /// <summary>The place in words, for a status line and a tooltip.</summary>
        /// <returns>The description.</returns>
        public override string ToString() {
            string where = "index " + IndexId;
            if (HasGroup)
                where += ", group " + GroupId;
            return HasRecord ? where + ", id " + RecordId : where;
        }
    }

    /// <summary>
    ///     Where the user has been, so following a reference can be undone.
    /// </summary>
    /// <remarks>
    ///     <b>Navigation without a way back is worse than none.</b> This cache is almost entirely
    ///     ids pointing at other ids, so answering one question routinely means following three or
    ///     four references - and a user who cannot get home will stop following them. The back stack
    ///     is therefore part of the feature rather than a refinement of it.
    ///     <para>
    ///     <b>Deliberately knows nothing about tabs.</b> It records places in the <i>cache</i>, and
    ///     the form turns a place into a tab and a row. That keeps the history correct across a tab
    ///     that has not loaded yet, a record that no longer exists, and a cache that has been
    ///     reopened underneath it - none of which the history should have an opinion about.
    ///     </para>
    ///     <para>
    ///     Modelled on a browser rather than on an undo stack: going back and then somewhere new
    ///     discards the forward history, because the alternative is a forward button that resumes a
    ///     journey the user has already abandoned.
    ///     </para>
    /// </remarks>
    public sealed class EditorNavigator {
        /// <summary>
        ///     How many places are remembered in each direction.
        /// </summary>
        /// <remarks>
        ///     Bounded because nothing ever clears it otherwise, and a session that follows links
        ///     for an hour would hold every place it visited. Fifty is far past the point where a
        ///     user would use the button rather than navigate directly.
        /// </remarks>
        private const int Limit = 50;

        private readonly List<EditorLocation> back = new();
        private readonly List<EditorLocation> forward = new();

        private bool navigating;

        /// <summary>Raised when the form should show a place.</summary>
        public event EventHandler<EditorLocation>? Navigated;

        /// <summary>Raised when <see cref="CanGoBack"/> or <see cref="CanGoForward"/> changes.</summary>
        public event EventHandler? HistoryChanged;

        /// <summary>Where the user is now, or null before anything has been visited.</summary>
        public EditorLocation? Current { get; private set; }

        /// <summary>Whether there is anywhere to go back to.</summary>
        public bool CanGoBack => back.Count > 0;

        /// <summary>Whether a back has been taken that can be undone.</summary>
        public bool CanGoForward => forward.Count > 0;

        /// <summary>
        ///     Goes somewhere, remembering where the user was.
        /// </summary>
        /// <remarks>
        ///     Navigating to where you already are does nothing at all, so a double click on a link
        ///     does not fill the history with the same place twice.
        /// </remarks>
        /// <param name="location">Where to go.</param>
        public void GoTo(EditorLocation location) {
            if (Current.HasValue && Current.Value.Equals(location))
                return;

            if (Current.HasValue)
                Push(back, Current.Value);

            forward.Clear();
            Arrive(location);
        }

        /// <summary>
        ///     Records where the user is without treating it as a jump.
        /// </summary>
        /// <remarks>
        ///     For a selection the user made themselves - clicking a row, switching a tab. It has to
        ///     be recorded or the first Back after browsing would return to wherever the last
        ///     <i>link</i> was followed from, which is not where the user came from.
        ///     <para>
        ///     It deliberately does not raise <see cref="Navigated"/>: the form is already showing
        ///     this place, and telling it to go there would fight the user's own selection.
        ///     </para>
        /// </remarks>
        /// <param name="location">Where the user now is.</param>
        public void RecordVisit(EditorLocation location) {
            if (navigating || (Current.HasValue && Current.Value.Equals(location)))
                return;

            if (Current.HasValue)
                Push(back, Current.Value);

            forward.Clear();
            Current = location;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Goes back one place, if there is one.</summary>
        /// <returns>Whether it moved.</returns>
        public bool GoBack() {
            if (back.Count == 0)
                return false;

            EditorLocation destination = Take(back);
            if (Current.HasValue)
                Push(forward, Current.Value);

            Arrive(destination);
            return true;
        }

        /// <summary>Undoes a back, if there is one.</summary>
        /// <returns>Whether it moved.</returns>
        public bool GoForward() {
            if (forward.Count == 0)
                return false;

            EditorLocation destination = Take(forward);
            if (Current.HasValue)
                Push(back, Current.Value);

            Arrive(destination);
            return true;
        }

        /// <summary>Forgets everywhere, for a cache being closed or reopened.</summary>
        /// <remarks>
        ///     A history kept across a reopen would offer to return to a record id that means
        ///     something different, or nothing, in the cache now open.
        /// </remarks>
        public void Clear() {
            back.Clear();
            forward.Clear();
            Current = null;
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        ///     Publishes a destination, guarding against the form navigating back at us.
        /// </summary>
        /// <remarks>
        ///     The form answers <see cref="Navigated"/> by selecting a tab and a row, and selecting
        ///     a row is exactly what calls <see cref="RecordVisit"/>. Without the guard, one Back
        ///     would record the place it just arrived at as a fresh visit, push the place it came
        ///     from onto the back stack again, and the button would never make progress.
        /// </remarks>
        private void Arrive(EditorLocation location) {
            Current = location;

            navigating = true;
            try {
                Navigated?.Invoke(this, location);
            }
            finally {
                navigating = false;
            }

            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void Push(List<EditorLocation> stack, EditorLocation location) {
            stack.Add(location);

            //Oldest first, because the far end of a long history is the part nobody returns to.
            if (stack.Count > Limit)
                stack.RemoveAt(0);
        }

        private static EditorLocation Take(List<EditorLocation> stack) {
            EditorLocation top = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return top;
        }
    }
}
