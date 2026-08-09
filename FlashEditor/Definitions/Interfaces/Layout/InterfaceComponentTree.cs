using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     Why a component is not reachable from any root of its interface.
    /// </summary>
    public enum InterfaceParentage {
        /// <summary>Its parent field says it is a root, so the interface itself is its parent.</summary>
        Root,

        /// <summary>It is a child of a component that exists in the group.</summary>
        Child,

        /// <summary>Its parent field names a file the group does not hold.</summary>
        Dangling,

        /// <summary>
        ///     It is inside a parent cycle, so no root reaches it.
        /// </summary>
        /// <remarks>
        ///     Not hypothetical. Group 468 file 1 names itself as its parent, byte-identically in
        ///     both supported caches. See <see cref="InterfaceComponentTree"/>.
        /// </remarks>
        Cyclic
    }

    /// <summary>
    ///     One interface's components arranged by parent, in draw order.
    /// </summary>
    /// <remarks>
    ///     <b>Draw order is file-id order within a parent, and it is not a stored field.</b> The
    ///     client walks the group's component array in index order and draws each child as it
    ///     reaches it, so "send to back" is a renumber rather than a property edit - which is why
    ///     reordering has to fix up every sibling's parent reference and is scheduled as its own
    ///     item.
    ///     <para>
    ///     <b>Cycles are survived by construction, not by a guard, and they are real.</b> Group 468
    ///     file 1 stores its own file id as its parent, identically in the vanilla b639 capture and
    ///     in the repack. Nothing here recurses and nothing walks a parent chain: the build is a
    ///     single pass that buckets children by parent, and every traversal is an explicit stack
    ///     with a visited set. Work is therefore bounded by the number of edges whatever the
    ///     topology, and a component inside a cycle simply never gets visited from a root - which is
    ///     also exactly what the client does with it.
    ///     </para>
    ///     <para>
    ///     <b>No depth cap.</b> A cap is a tolerance dressed as a safety net, and the format permits
    ///     a 770-level chain inside the 771-file group this cache actually contains. The visited set
    ///     is what makes a cap unnecessary.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceComponentTree {
        private readonly Dictionary<int, InterfaceComponentDefinition> byFileId;
        private readonly Dictionary<int, List<int>> childrenByParent;
        private readonly Dictionary<int, InterfaceParentage> parentage;
        private readonly List<int> roots;

        private InterfaceComponentTree(int groupId,
            Dictionary<int, InterfaceComponentDefinition> byFileId,
            Dictionary<int, List<int>> childrenByParent,
            Dictionary<int, InterfaceParentage> parentage,
            List<int> roots) {
            GroupId = groupId;
            this.byFileId = byFileId;
            this.childrenByParent = childrenByParent;
            this.parentage = parentage;
            this.roots = roots;
        }

        /// <summary>The interface this tree describes.</summary>
        public int GroupId { get; }

        /// <summary>Every component in the group, by file id.</summary>
        public IReadOnlyDictionary<int, InterfaceComponentDefinition> Components => byFileId;

        /// <summary>The components whose parent field says they are roots, in file-id order.</summary>
        public IReadOnlyList<int> Roots => roots;

        /// <summary>
        ///     Arranges one interface's components.
        /// </summary>
        /// <remarks>
        ///     Builds a child-list index in one pass rather than scanning the whole array per
        ///     parent. The client does the latter, in two separate passes
        ///     (<c>Class224_Sub2.method2837</c> for layout and
        ///     <c>Node_Sub10_Sub24.method1077</c> for drawing), which is quadratic in the group's
        ///     size - about 600,000 comparisons for the 771-file group this cache holds. An editor
        ///     redrawing on every mouse move cannot pay that.
        /// </remarks>
        /// <param name="groupId">The interface id.</param>
        /// <param name="components">Its components, in any order.</param>
        /// <returns>The tree.</returns>
        public static InterfaceComponentTree Build(int groupId,
            IEnumerable<InterfaceComponentDefinition> components) {
            if (components == null)
                throw new ArgumentNullException(nameof(components));

            var byFileId = new Dictionary<int, InterfaceComponentDefinition>();
            foreach (InterfaceComponentDefinition component in components) {
                if (component == null)
                    continue;

                //Last one wins rather than throwing: a caller handing over two records for one file
                //id has a problem upstream, and refusing to build the tree hides the whole group
                //rather than the one row.
                byFileId[component.FileId] = component;
            }

            var childrenByParent = new Dictionary<int, List<int>>();
            var parentage = new Dictionary<int, InterfaceParentage>(byFileId.Count);
            var roots = new List<int>();

            foreach (KeyValuePair<int, InterfaceComponentDefinition> entry in byFileId) {
                int fileId = entry.Key;
                int raw = entry.Value.RawParentId;

                if (raw == InterfaceComponentDefinition.NoParent) {
                    roots.Add(fileId);
                    parentage[fileId] = InterfaceParentage.Root;
                    continue;
                }

                if (!byFileId.ContainsKey(raw)) {
                    parentage[fileId] = InterfaceParentage.Dangling;
                    continue;
                }

                if (!childrenByParent.TryGetValue(raw, out List<int>? siblings)) {
                    siblings = new List<int>();
                    childrenByParent[raw] = siblings;
                }

                siblings.Add(fileId);
                parentage[fileId] = InterfaceParentage.Child;
            }

            roots.Sort();
            foreach (List<int> siblings in childrenByParent.Values)
                siblings.Sort();

            var tree = new InterfaceComponentTree(groupId, byFileId, childrenByParent, parentage, roots);
            tree.MarkUnreachable();
            return tree;
        }

        /// <summary>The children of a component, in draw order.</summary>
        /// <param name="fileId">The parent's file id.</param>
        /// <returns>Its children, ascending by file id, or empty.</returns>
        public IReadOnlyList<int> ChildrenOf(int fileId) {
            return childrenByParent.TryGetValue(fileId, out List<int>? children)
                ? children
                : Array.Empty<int>();
        }

        /// <summary>How a component is attached, or is not.</summary>
        /// <param name="fileId">The component's file id.</param>
        /// <returns>Its parentage.</returns>
        public InterfaceParentage ParentageOf(int fileId) {
            return parentage.TryGetValue(fileId, out InterfaceParentage how)
                ? how
                : InterfaceParentage.Dangling;
        }

        /// <summary>
        ///     Every component reachable from a root, in the order the client would draw them.
        /// </summary>
        /// <remarks>
        ///     Pre-order: a parent is yielded before its children, because that is paint order and a
        ///     canvas consuming this can draw straight down the sequence. An explicit stack rather
        ///     than recursion, so a 770-deep chain cannot overflow one.
        /// </remarks>
        /// <returns>The file ids, in draw order.</returns>
        public IEnumerable<int> InDrawOrder() {
            var visited = new HashSet<int>();
            var pending = new Stack<int>();

            for (int i = roots.Count - 1; i >= 0; i--)
                pending.Push(roots[i]);

            while (pending.Count > 0) {
                int fileId = pending.Pop();

                //The visited set is what makes a cycle finite. It also stops a component that is
                //somehow reachable twice being drawn twice.
                if (!visited.Add(fileId))
                    continue;

                yield return fileId;

                IReadOnlyList<int> children = ChildrenOf(fileId);
                for (int i = children.Count - 1; i >= 0; i--)
                    pending.Push(children[i]);
            }
        }

        /// <summary>
        ///     Reclassifies as <see cref="InterfaceParentage.Cyclic"/> every component that has a
        ///     parent in the group but is not reachable from any root.
        /// </summary>
        /// <remarks>
        ///     Done by subtracting the reachable set from the child set rather than by hunting for
        ///     cycles. Any component with a resolvable parent that a root cannot reach is, by
        ///     definition, in or below a cycle - there is nowhere else for it to be - and computing
        ///     it this way costs one traversal instead of a chain walk per component.
        /// </remarks>
        private void MarkUnreachable() {
            var reachable = new HashSet<int>();
            foreach (int fileId in InDrawOrder())
                reachable.Add(fileId);

            foreach (int fileId in byFileId.Keys) {
                if (parentage[fileId] == InterfaceParentage.Child && !reachable.Contains(fileId))
                    parentage[fileId] = InterfaceParentage.Cyclic;
            }
        }
    }
}
