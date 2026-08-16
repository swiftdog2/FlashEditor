using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     What a click picks, what a marquee encloses, and which members of a multiple selection
    ///     may be moved independently.
    /// </summary>
    /// <remarks>
    ///     <b>Separated from the canvas so that it can be tested at all.</b> Nothing in the suite
    ///     covers WinForms, so a hit test written inside a <c>UserControl</c> is defended by nothing
    ///     but the eye. Everything here takes plain rectangles and returns plain ids.
    ///     <para>
    ///     <b>Z-order is not a field.</b> Draw order is file-id order within a parent, produced by
    ///     <see cref="InterfaceComponentTree.InDrawOrder"/>, and the caller passes that sequence in.
    ///     Overlap is resolved by position in that sequence and by nothing this class invents - the
    ///     last thing drawn is the thing on top, which is what a click means.
    ///     </para>
    /// </remarks>
    public static class InterfaceHitTest {
        /// <summary>
        ///     The component a click at a point selects, or -1.
        /// </summary>
        /// <remarks>
        ///     <b>A layer only wins when nothing above it was hit.</b> A type-0 component is a
        ///     container, usually spanning everything inside it, so picking one whenever the pointer
        ///     is inside its box would make its children unselectable - which is what the first draft
        ///     of the canvas did. The topmost layer is remembered and returned only if the walk
        ///     reaches the end without finding a leaf.
        /// </remarks>
        /// <param name="drawOrder">The components in paint order, parents before children.</param>
        /// <param name="resolved">Their resolved geometry, keyed by file id.</param>
        /// <param name="x">The point, in canvas coordinates.</param>
        /// <param name="y">The point, in canvas coordinates.</param>
        /// <returns>The component's file id, or -1 when the point is over nothing.</returns>
        public static int TopmostAt(IReadOnlyList<int> drawOrder,
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved, int x, int y) {
            if (drawOrder == null)
                throw new ArgumentNullException(nameof(drawOrder));
            if (resolved == null)
                throw new ArgumentNullException(nameof(resolved));

            int layerHit = -1;

            for (int i = drawOrder.Count - 1; i >= 0; i--) {
                if (!resolved.TryGetValue(drawOrder[i], out InterfaceLayoutNode? node))
                    continue;

                InterfaceRect box = node.Absolute;
                if (x < box.X || y < box.Y || x >= box.Right || y >= box.Bottom)
                    continue;

                if (node.Component.ComponentType == 0) {
                    if (layerHit < 0)
                        layerHit = drawOrder[i];
                    continue;
                }

                return drawOrder[i];
            }

            return layerHit;
        }

        /// <summary>
        ///     The components a marquee encloses, in draw order.
        /// </summary>
        /// <remarks>
        ///     <b>Wholly inside, not merely touching, and the difference decides whether the feature
        ///     is usable.</b> Most interfaces are one root layer covering the whole canvas with
        ///     everything else inside it, so an intersection rule would put that root into every
        ///     marquee ever drawn and every rubber-band would collapse to "the whole interface". The
        ///     containment rule makes a marquee mean "these leaves", and a container is still
        ///     selectable by clicking it.
        ///     <para>
        ///     Returned in draw order rather than in id order, because the caller's first member is
        ///     the one whose geometry drives a subsequent drag and that has to be the bottom-most
        ///     rather than the lowest-numbered.
        ///     </para>
        /// </remarks>
        /// <param name="drawOrder">The components in paint order.</param>
        /// <param name="resolved">Their resolved geometry, keyed by file id.</param>
        /// <param name="marquee">The rubber band, in canvas coordinates.</param>
        /// <returns>The enclosed components, in draw order.</returns>
        public static IReadOnlyList<int> Within(IReadOnlyList<int> drawOrder,
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved, InterfaceRect marquee) {
            if (drawOrder == null)
                throw new ArgumentNullException(nameof(drawOrder));
            if (resolved == null)
                throw new ArgumentNullException(nameof(resolved));

            var caught = new List<int>();

            //An empty band catches nothing. InterfaceRect.IsInside answers true for an empty
            //rectangle - nothing of it can fall outside - so without this a click that moves no
            //pixels would select every zero-sized component in the interface.
            if (marquee.IsEmpty)
                return caught;

            foreach (int fileId in drawOrder) {
                if (!resolved.TryGetValue(fileId, out InterfaceLayoutNode? node))
                    continue;

                InterfaceRect box = node.Absolute;
                if (box.IsEmpty || !box.IsInside(marquee))
                    continue;

                caught.Add(fileId);
            }

            return caught;
        }

        /// <summary>
        ///     The members of a selection that a drag should actually write to.
        /// </summary>
        /// <remarks>
        ///     <b>A selected component that is a descendant of another selected component must not be
        ///     moved.</b> Its own position resolves against its parent, so moving the parent already
        ///     carries it; writing a new base for it as well moves it twice as far as the pointer
        ///     went, and for a proportional mode it moves it somewhere unrelated.
        ///     <para>
        ///     <b>Cycle-proof by construction rather than by a guard.</b> This descends through
        ///     <see cref="InterfaceComponentTree.ChildrenOf"/> with a visited set and never walks a
        ///     parent chain, so work is bounded by the number of edges whatever the topology and no
        ///     depth cap is needed - the format permits a 770-level chain inside the 771-file group
        ///     this cache holds, so a cap would be a tolerance rather than a safeguard.
        ///     </para>
        ///     <para>
        ///     <b>A component that is its own parent is kept.</b> Group 468 file 1 names itself,
        ///     byte-identically in both supported caches. The descent from a component reaches that
        ///     component again, so a test of "was this reached from any selected component" would
        ///     drop it and the drag would silently write nothing. The test is "was it reached from a
        ///     <i>different</i> selected component", which is the question actually being asked.
        ///     </para>
        /// </remarks>
        /// <param name="tree">The interface's tree.</param>
        /// <param name="selection">The selected file ids, in any order.</param>
        /// <returns>The subset to write, in ascending file-id order.</returns>
        public static IReadOnlyList<int> MovableRoots(InterfaceComponentTree tree,
            IEnumerable<int> selection) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (selection == null)
                throw new ArgumentNullException(nameof(selection));

            var selected = new HashSet<int>(selection);
            var carried = new HashSet<int>();

            foreach (int start in selected) {
                var visited = new HashSet<int> { start };
                var pending = new Stack<int>();

                foreach (int child in tree.ChildrenOf(start))
                    pending.Push(child);

                while (pending.Count > 0) {
                    int fileId = pending.Pop();

                    //The visited set is what makes a cycle finite here, exactly as it does in
                    //InDrawOrder. Seeding it with the start is what keeps a self-parenting
                    //component out of its own carried set.
                    if (!visited.Add(fileId))
                        continue;

                    if (selected.Contains(fileId))
                        carried.Add(fileId);

                    foreach (int child in tree.ChildrenOf(fileId))
                        pending.Push(child);
                }
            }

            var movable = new List<int>();
            foreach (int fileId in selected) {
                if (!carried.Contains(fileId))
                    movable.Add(fileId);
            }

            movable.Sort();
            return movable;
        }
    }
}
