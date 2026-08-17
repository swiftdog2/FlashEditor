using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Interfaces.Layout;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     What a structural edit to an interface would change, worked out before anything is written.
    /// </summary>
    /// <remarks>
    ///     Separate from applying it because the interesting part is entirely the renumbering, and a
    ///     plan can be inspected, tested and shown to a user where a mutation in place cannot.
    /// </remarks>
    public sealed class InterfaceStructureEdit {
        internal InterfaceStructureEdit(IReadOnlyDictionary<int, int> renumbering,
            IReadOnlyList<int> removed, int inserted, IReadOnlyList<string> warnings) {
            Renumbering = renumbering;
            Removed = removed;
            Inserted = inserted;
            Warnings = warnings;
        }

        /// <summary>
        ///     Old file id to new file id, for every component that moves.
        /// </summary>
        /// <remarks>
        ///     A component whose id does not change is absent rather than mapped to itself, so the
        ///     count of this is the size of the change.
        /// </remarks>
        public IReadOnlyDictionary<int, int> Renumbering { get; }

        /// <summary>The file ids that cease to exist, in ascending order.</summary>
        public IReadOnlyList<int> Removed { get; }

        /// <summary>The file id a newly created component takes, or -1.</summary>
        public int Inserted { get; }

        /// <summary>
        ///     What this edit breaks that it cannot fix.
        /// </summary>
        /// <remarks>
        ///     Always shown to the user before the edit is applied. Every entry here is a reference
        ///     from <b>outside</b> the group, which this plan can detect and cannot repair.
        /// </remarks>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Whether anything actually changes.</summary>
        public bool IsEmpty => Renumbering.Count == 0 && Removed.Count == 0 && Inserted < 0;
    }

    /// <summary>
    ///     Creating, deleting and reordering interface components, and the renumbering each forces.
    /// </summary>
    /// <remarks>
    ///     <b>Draw order is not a field, so reordering is renumbering.</b> The client draws a
    ///     parent's children in file-id order, so "send to back" means giving a component a lower id
    ///     than its siblings - which means moving every sibling between the two, which means every
    ///     component whose stored parent id pointed at one of those has to be repointed. That
    ///     cascade is why this is a planner rather than three one-line methods.
    ///     <para>
    ///     <b>File ids are dense, 0 to n-1, in every group in this cache, and the client depends on
    ///     it.</b> <c>VersionTable</c> stores <c>maxFileId + 1</c> as the file count and discards the
    ///     explicit id list whenever the two agree, so a group with a hole would be read with a file
    ///     count that does not match its contents. Every operation here therefore closes up the
    ///     numbering rather than leaving a gap.
    ///     </para>
    ///     <para>
    ///     <b>The references this cannot fix are the important ones.</b> A component is addressed
    ///     from outside its group as <c>(group &lt;&lt; 16) | fileId</c>: CS2 scripts in index 12
    ///     address components that way, and so do hook arguments in other interfaces. Renumbering
    ///     silently re-points every one of them at a different component. Nothing in this project
    ///     can rewrite compiled CS2, so the planner <i>finds</i> those references and reports them,
    ///     and the decision to proceed is the user's rather than one taken quietly on their behalf.
    ///     </para>
    /// </remarks>
    public static class InterfaceComponentEdits {
        /// <summary>
        ///     Moves a component to a new position among its siblings.
        /// </summary>
        /// <remarks>
        ///     Positions are stated among the siblings rather than as a target file id, because a
        ///     sibling list is what draw order means and a file id is an implementation of it.
        /// </remarks>
        /// <param name="tree">The interface's current structure.</param>
        /// <param name="fileId">The component to move.</param>
        /// <param name="newPositionAmongSiblings">Where it should sit, zero based.</param>
        /// <returns>The plan.</returns>
        public static InterfaceStructureEdit PlanReorder(InterfaceComponentTree tree, int fileId,
            int newPositionAmongSiblings) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (!tree.Components.ContainsKey(fileId))
                throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "No such component.");

            List<int> order = DrawOrderWithin(tree, ParentOf(tree, fileId));
            int from = order.IndexOf(fileId);
            int to = Math.Clamp(newPositionAmongSiblings, 0, order.Count - 1);

            if (from == to)
                return Nothing();

            /* Only the ids the siblings already occupy are redistributed. A reorder must not renumber
               anything outside this parent, or moving one button would shuffle the whole interface. */
            var slots = new List<int>(order);
            slots.Sort();

            order.RemoveAt(from);
            order.Insert(to, fileId);

            var renumbering = new Dictionary<int, int>();
            for (int i = 0; i < order.Count; i++) {
                if (order[i] != slots[i])
                    renumbering[order[i]] = slots[i];
            }

            return new InterfaceStructureEdit(renumbering, Array.Empty<int>(), -1,
                ExternalReferenceWarnings(tree, renumbering.Keys));
        }

        /// <summary>
        ///     Deletes a component and everything beneath it.
        /// </summary>
        /// <remarks>
        ///     <b>The subtree goes with it, and that is not a policy choice.</b> A child's parent
        ///     field holds a file id; leaving the children behind would point them at whichever
        ///     component inherits the deleted id after renumbering, which is a different component
        ///     that would then draw them. Orphaning them by clearing the parent would be worse still
        ///     - they would become roots and draw at the interface origin.
        /// </remarks>
        /// <param name="tree">The interface's current structure.</param>
        /// <param name="fileId">The component to delete.</param>
        /// <returns>The plan.</returns>
        public static InterfaceStructureEdit PlanDelete(InterfaceComponentTree tree, int fileId) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));
            if (!tree.Components.ContainsKey(fileId))
                throw new ArgumentOutOfRangeException(nameof(fileId), fileId, "No such component.");

            var doomed = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(fileId);

            //An explicit stack and a visited set, because the parent graph in this index is not
            //guaranteed acyclic - one component in both supported caches is its own parent.
            while (pending.Count > 0) {
                int next = pending.Pop();
                if (!doomed.Add(next))
                    continue;

                foreach (int child in tree.ChildrenOf(next))
                    pending.Push(child);
            }

            var removed = doomed.ToList();
            removed.Sort();

            var renumbering = new Dictionary<int, int>();
            int newId = 0;

            foreach (int id in tree.Components.Keys.OrderBy(id => id)) {
                if (doomed.Contains(id))
                    continue;

                if (id != newId)
                    renumbering[id] = newId;
                newId++;
            }

            var affected = new List<int>(renumbering.Keys);
            affected.AddRange(removed);

            return new InterfaceStructureEdit(renumbering, removed, -1,
                ExternalReferenceWarnings(tree, affected));
        }

        /// <summary>
        ///     Adds a component as a child of another, at a position among its siblings.
        /// </summary>
        /// <remarks>
        ///     Appending at the end is the only insertion that renumbers nothing, so it is worth
        ///     preferring where the position does not matter - the plan says how much a given
        ///     position costs.
        /// </remarks>
        /// <param name="tree">The interface's current structure.</param>
        /// <param name="parentId">The parent, or <see cref="InterfaceComponentDefinition.NoParent"/> for a root.</param>
        /// <param name="positionAmongSiblings">Where it should sit, zero based, or -1 to append.</param>
        /// <returns>The plan.</returns>
        public static InterfaceStructureEdit PlanInsert(InterfaceComponentTree tree, int parentId,
            int positionAmongSiblings) {
            if (tree == null)
                throw new ArgumentNullException(nameof(tree));

            if (parentId != InterfaceComponentDefinition.NoParent
                && !tree.Components.ContainsKey(parentId)) {
                throw new ArgumentOutOfRangeException(nameof(parentId), parentId, "No such parent.");
            }

            List<int> siblings = DrawOrderWithin(tree, parentId);
            int count = tree.Components.Count;

            //Appending to the last parent in the group takes the next free id and moves nothing.
            if (positionAmongSiblings < 0 || siblings.Count == 0 || positionAmongSiblings >= siblings.Count) {
                return new InterfaceStructureEdit(new Dictionary<int, int>(), Array.Empty<int>(),
                    count, Array.Empty<string>());
            }

            int takes = siblings[positionAmongSiblings];

            var renumbering = new Dictionary<int, int>();
            foreach (int id in tree.Components.Keys.OrderBy(id => id)) {
                if (id >= takes)
                    renumbering[id] = id + 1;
            }

            return new InterfaceStructureEdit(renumbering, Array.Empty<int>(), takes,
                ExternalReferenceWarnings(tree, renumbering.Keys));
        }

        /// <summary>
        ///     A newly created component, in the shape the editor gives one.
        /// </summary>
        /// <remarks>
        ///     <b>A filled rectangle rather than a layer</b>, which is what a default-constructed
        ///     component is. A layer draws nothing at all, so a created one would appear in the tree
        ///     and nowhere on the canvas - an edit the user cannot tell from one that failed.
        ///     <para>
        ///     A mid grey rather than black, for the same reason: zero is a real stored colour on a
        ///     rectangle, so a new component drawing in the colour an unset field would produce is
        ///     one nobody can distinguish from a decode fault.
        ///     </para>
        ///     <para>
        ///     Here rather than in the panel so that what the editor writes into a cache can be
        ///     asserted without a window. It is a starting point and not a template: every field it
        ///     sets is editable afterwards through the grid, the canvas or the field pane.
        ///     </para>
        /// </remarks>
        /// <param name="groupId">The interface it belongs to.</param>
        /// <param name="fileId">The file id it will take.</param>
        /// <param name="parentId">
        ///     Its parent's file id, or <see cref="InterfaceComponentDefinition.NoParent"/> for a
        ///     root.
        /// </param>
        /// <returns>The component.</returns>
        public static InterfaceComponentDefinition NewComponent(int groupId, int fileId, int parentId) {
            return new InterfaceComponentDefinition(groupId, fileId) {
                RawParentId = parentId,
                ComponentType = 3,
                BaseWidth = 64,
                BaseHeight = 32,
                Colour = 0x808080,
                RectangleFilledByte = 1
            };
        }

        /// <summary>
        ///     Repoints every stored parent reference through a renumbering.
        /// </summary>
        /// <remarks>
        ///     The half of a structural edit that is easy to forget and impossible to see afterwards:
        ///     the components move, the tree looks right, and every parent field still names the id
        ///     its component used to have. Applied to the definitions themselves, so a caller does
        ///     the renaming and this does the repair.
        /// </remarks>
        /// <param name="components">The components, after they have been renumbered.</param>
        /// <param name="renumbering">Old file id to new file id.</param>
        public static void RepointParents(IEnumerable<InterfaceComponentDefinition> components,
            IReadOnlyDictionary<int, int> renumbering) {
            if (components == null)
                throw new ArgumentNullException(nameof(components));
            if (renumbering == null)
                throw new ArgumentNullException(nameof(renumbering));

            foreach (InterfaceComponentDefinition component in components) {
                if (component.RawParentId == InterfaceComponentDefinition.NoParent)
                    continue;

                if (renumbering.TryGetValue(component.RawParentId, out int moved))
                    component.RawParentId = moved;
            }
        }

        private static InterfaceStructureEdit Nothing() {
            return new InterfaceStructureEdit(new Dictionary<int, int>(), Array.Empty<int>(), -1,
                Array.Empty<string>());
        }

        private static int ParentOf(InterfaceComponentTree tree, int fileId) {
            return tree.Components[fileId].RawParentId;
        }

        /// <summary>
        ///     A parent's children in draw order, or the group's roots.
        /// </summary>
        private static List<int> DrawOrderWithin(InterfaceComponentTree tree, int parentId) {
            if (parentId == InterfaceComponentDefinition.NoParent)
                return new List<int>(tree.Roots);

            return new List<int>(tree.ChildrenOf(parentId));
        }

        /// <summary>
        ///     Which moved components are referred to from outside their own group.
        /// </summary>
        /// <remarks>
        ///     Only the references this project can see are reported, and it says so: a component's
        ///     own hook arrays carry folded component ids, so an interface that refers to itself is
        ///     detectable here. A CS2 script in index 12 that addresses the same component is not,
        ///     because finding it means scanning 4,149 compiled scripts for a constant - worth
        ///     doing, and a separate piece of work. The warning names the limit rather than implying
        ///     the list is complete.
        /// </remarks>
        private static IReadOnlyList<string> ExternalReferenceWarnings(InterfaceComponentTree tree,
            IEnumerable<int> movedOrRemoved) {
            var affected = new HashSet<int>(movedOrRemoved);
            if (affected.Count == 0)
                return Array.Empty<string>();

            var warnings = new List<string>();
            var referenced = new SortedSet<int>();

            foreach (InterfaceComponentDefinition component in tree.Components.Values) {
                foreach (InterfaceScriptOperand[] hook in component.Hooks) {
                    foreach (InterfaceScriptOperand operand in hook) {
                        if (operand.TypeByte != InterfaceScriptOperand.IntegerType)
                            continue;

                        //A folded component id: the high half is a group and the low half a file.
                        int group = operand.Integer >> 16;
                        int file = operand.Integer & 0xFFFF;

                        if (group == tree.GroupId && affected.Contains(file))
                            referenced.Add(file);
                    }
                }
            }

            if (referenced.Count > 0) {
                warnings.Add("Hook arguments in this interface address components " +
                    string.Join(", ", referenced) + " by id, and this edit changes those ids.");
            }

            warnings.Add("Components are addressed from outside this interface as " +
                "(interface << 16) | component, so CS2 scripts in index 12 and hooks in other " +
                "interfaces may name the ids this edit moves. Those cannot be found or repaired " +
                "from here.");

            return warnings;
        }
    }
}
