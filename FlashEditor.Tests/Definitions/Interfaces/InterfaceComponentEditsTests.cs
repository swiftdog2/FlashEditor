using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     The renumbering a structural edit forces, and the references it has to repair.
    /// </summary>
    /// <remarks>
    ///     These operations are destructive and their damage is invisible: a reorder that forgets to
    ///     repoint a parent leaves a tree that still looks right and draws the wrong components
    ///     inside the wrong containers. So the assertions are all about the invariant rather than
    ///     about the numbers - after any plan is applied, every component must still have the same
    ///     parent it had before, by identity rather than by id.
    /// </remarks>
    public sealed class InterfaceComponentEditsTests {
        /// <summary>A layer with children, inside a group with a second root.</summary>
        private static InterfaceComponentTree Build(params (int FileId, int Parent)[] shape) {
            var components = new List<InterfaceComponentDefinition>();

            foreach ((int fileId, int parent) in shape) {
                components.Add(new InterfaceComponentDefinition(7, fileId) {
                    ComponentType = 0,
                    RawParentId = parent
                });
            }

            return InterfaceComponentTree.Build(7, components);
        }

        private const int Root = InterfaceComponentDefinition.NoParent;

        /// <summary>
        ///     Applies a plan the way the editor would, and hands back the new structure.
        /// </summary>
        /// <remarks>
        ///     Renumber first, then repoint. Doing it the other way round repoints against ids that
        ///     have already moved, which is the single most likely way to implement this wrong.
        /// </remarks>
        private static Dictionary<int, InterfaceComponentDefinition> Apply(
            InterfaceComponentTree tree, InterfaceStructureEdit plan) {
            var kept = tree.Components.Values
                .Where(c => !plan.Removed.Contains(c.FileId))
                .ToList();

            var moved = new Dictionary<int, InterfaceComponentDefinition>();
            foreach (InterfaceComponentDefinition component in kept) {
                int newId = plan.Renumbering.TryGetValue(component.FileId, out int mapped)
                    ? mapped
                    : component.FileId;

                var copy = new InterfaceComponentDefinition(7, newId) {
                    ComponentType = component.ComponentType,
                    RawParentId = component.RawParentId
                };
                moved[newId] = copy;
            }

            InterfaceComponentEdits.RepointParents(moved.Values, plan.Renumbering);
            return moved;
        }

        [Fact]
        public void ReorderingAChildRedistributesOnlyItsSiblingsIds() {
            InterfaceComponentTree tree = Build(
                (0, Root), (1, 0), (2, 0), (3, 0), (4, Root), (5, 4));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanReorder(tree, 3, 0);

            //3 goes to the front of 1,2,3 so it takes id 1, and 1 and 2 shuffle up.
            Assert.Equal(1, plan.Renumbering[3]);
            Assert.Equal(2, plan.Renumbering[1]);
            Assert.Equal(3, plan.Renumbering[2]);

            //Nothing outside that parent moves. Moving one button must not shuffle the interface.
            Assert.DoesNotContain(0, plan.Renumbering.Keys);
            Assert.DoesNotContain(4, plan.Renumbering.Keys);
            Assert.DoesNotContain(5, plan.Renumbering.Keys);
        }

        /// <summary>
        ///     After a reorder, every component still sits under the same parent it did before.
        /// </summary>
        /// <remarks>
        ///     The invariant the whole unit exists for. A plan that renumbered correctly and failed
        ///     to repoint would pass the id assertions above and fail this one.
        /// </remarks>
        [Fact]
        public void AfterAReorderEveryComponentKeepsTheParentItHad() {
            InterfaceComponentTree tree = Build(
                (0, Root), (1, 0), (2, 0), (3, 0), (4, 1), (5, 1));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanReorder(tree, 3, 0);
            Dictionary<int, InterfaceComponentDefinition> after = Apply(tree, plan);

            //1 became 2, so 4 and 5 - which were its children - must now name 2.
            int oneIsNow = plan.Renumbering[1];
            Assert.Equal(oneIsNow, after[plan.Renumbering.GetValueOrDefault(4, 4)].RawParentId);
            Assert.Equal(oneIsNow, after[plan.Renumbering.GetValueOrDefault(5, 5)].RawParentId);
        }

        [Fact]
        public void DeletingAComponentTakesItsWholeSubtree() {
            InterfaceComponentTree tree = Build(
                (0, Root), (1, 0), (2, 1), (3, 2), (4, Root));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, 1);

            Assert.Equal(new[] { 1, 2, 3 }, plan.Removed);
        }

        /// <summary>
        ///     A delete closes the numbering up, because the client reads a group's file count as
        ///     the highest id plus one.
        /// </summary>
        [Fact]
        public void DeletingClosesTheNumberingRatherThanLeavingAHole() {
            InterfaceComponentTree tree = Build(
                (0, Root), (1, Root), (2, Root), (3, Root));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, 1);
            Dictionary<int, InterfaceComponentDefinition> after = Apply(tree, plan);

            Assert.Equal(new[] { 0, 1, 2 }, after.Keys.OrderBy(id => id));
        }

        /// <summary>
        ///     A delete repoints the survivors, so a surviving child still names its surviving parent.
        /// </summary>
        [Fact]
        public void AfterADeleteASurvivingChildStillNamesItsParent() {
            InterfaceComponentTree tree = Build(
                (0, Root), (1, Root), (2, Root), (3, 2));

            //Removing 1 pulls 2 down to 1 and 3 down to 2; 3 must follow its parent.
            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, 1);
            Dictionary<int, InterfaceComponentDefinition> after = Apply(tree, plan);

            Assert.Equal(1, plan.Renumbering[2]);
            Assert.Equal(2, plan.Renumbering[3]);
            Assert.Equal(1, after[2].RawParentId);
        }

        /// <summary>Appending renumbers nothing, which is why it is worth preferring.</summary>
        [Fact]
        public void AppendingTakesTheNextIdAndMovesNothing() {
            InterfaceComponentTree tree = Build((0, Root), (1, 0), (2, 0));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanInsert(tree, 0, -1);

            Assert.Equal(3, plan.Inserted);
            Assert.Empty(plan.Renumbering);
        }

        /// <summary>Inserting in the middle pushes every later id up by one, and repoints.</summary>
        [Fact]
        public void InsertingInTheMiddlePushesLaterIdsUpAndKeepsParents() {
            InterfaceComponentTree tree = Build((0, Root), (1, 0), (2, 0), (3, 2));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanInsert(tree, 0, 1);

            Assert.Equal(2, plan.Inserted);
            Assert.Equal(3, plan.Renumbering[2]);
            Assert.Equal(4, plan.Renumbering[3]);

            Dictionary<int, InterfaceComponentDefinition> after = Apply(tree, plan);
            Assert.Equal(3, after[4].RawParentId);
        }

        /// <summary>
        ///     A component that is its own parent does not hang the delete planner.
        /// </summary>
        /// <remarks>
        ///     Group 468 file 1 in both supported caches. A subtree walk without a visited set
        ///     recurses forever on it, and a delete that hangs the editor on a real interface is a
        ///     worse failure than one that refuses.
        /// </remarks>
        [Fact]
        public void DeletingASelfParentedComponentTerminates() {
            InterfaceComponentTree tree = Build((0, Root), (1, 1));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanDelete(tree, 1);

            Assert.Equal(new[] { 1 }, plan.Removed);
        }

        /// <summary>Moving a component to where it already is changes nothing at all.</summary>
        [Fact]
        public void AReorderToTheSamePositionIsEmpty() {
            InterfaceComponentTree tree = Build((0, Root), (1, 0), (2, 0));

            Assert.True(InterfaceComponentEdits.PlanReorder(tree, 1, 0).IsEmpty);
        }

        /// <summary>
        ///     Every structural edit warns that ids are addressed from outside the interface.
        /// </summary>
        /// <remarks>
        ///     Not a nicety. A component is addressed as (interface &lt;&lt; 16) | file from CS2 in
        ///     index 12 and from hooks in other interfaces, and renumbering silently re-points all of
        ///     them at a different component. Nothing here can rewrite compiled CS2, so the only
        ///     honest behaviour is to say so before the edit rather than after.
        /// </remarks>
        [Fact]
        public void AnyRenumberingWarnsAboutReferencesFromOutsideTheInterface() {
            InterfaceComponentTree tree = Build((0, Root), (1, 0), (2, 0));

            InterfaceStructureEdit plan = InterfaceComponentEdits.PlanReorder(tree, 2, 0);

            Assert.NotEmpty(plan.Warnings);
            Assert.Contains(plan.Warnings, w => w.Contains("outside this interface"));
        }

        /// <summary>An edit that renumbers nothing does not warn about anything.</summary>
        [Fact]
        public void AnEditThatMovesNothingDoesNotWarn() {
            InterfaceComponentTree tree = Build((0, Root), (1, 0));

            Assert.Empty(InterfaceComponentEdits.PlanInsert(tree, 0, -1).Warnings);
        }
    }
}
