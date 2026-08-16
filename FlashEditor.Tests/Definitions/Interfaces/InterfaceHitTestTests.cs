using System.Collections.Generic;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     What a click picks, what a marquee catches, and which of a multiple selection may move.
    /// </summary>
    /// <remarks>
    ///     None of this touches a byte, so no sweep can defend it: a wrong answer here shows only as
    ///     a canvas that selects the wrong thing or a drag that moves a component twice as far as the
    ///     pointer went. These are the tests instead.
    ///     <para>
    ///     Everything is built from real geometry through <see cref="InterfaceLayoutResolver"/>
    ///     rather than from hand-made nodes, because the thing being asserted is how the hit test and
    ///     the resolver fit together and a fixture that bypassed the resolver could not show that.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceHitTestTests {
        /// <summary>A component at a fixed pixel rectangle, which is positioning and sizing mode 0.</summary>
        private static InterfaceComponentDefinition At(int fileId, int type, int x, int y,
            int width, int height, int parent = InterfaceComponentDefinition.NoParent) {
            return new InterfaceComponentDefinition(0, fileId) {
                ComponentType = type,
                BasePositionX = x,
                BasePositionY = y,
                BaseWidth = width,
                BaseHeight = height,
                RawParentId = parent
            };
        }

        private static (InterfaceComponentTree Tree, List<int> DrawOrder,
            IReadOnlyDictionary<int, InterfaceLayoutNode> Resolved)
            Build(params InterfaceComponentDefinition[] components) {
            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, components);
            var order = new List<int>(tree.InDrawOrder());
            return (tree, order,
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas));
        }

        /// <summary>
        ///     Two overlapping leaves: the later file id wins, because that is the one drawn on top.
        /// </summary>
        /// <remarks>
        ///     Z-order is not a field in this format. Draw order is file-id order within a parent, so
        ///     the only correct tie-break is position in the draw sequence - anything the hit test
        ///     invented would disagree with what the user can see.
        /// </remarks>
        [Fact]
        public void TopmostAt_ResolvesOverlapByDrawOrderAndNotByAnythingElse() {
            var (_, order, resolved) = Build(
                At(1, 3, 0, 0, 100, 100),
                At(2, 3, 50, 50, 100, 100));

            Assert.Equal(2, InterfaceHitTest.TopmostAt(order, resolved, 60, 60));
            Assert.Equal(1, InterfaceHitTest.TopmostAt(order, resolved, 10, 10));
            Assert.Equal(2, InterfaceHitTest.TopmostAt(order, resolved, 140, 140));
            Assert.Equal(-1, InterfaceHitTest.TopmostAt(order, resolved, 400, 400));
        }

        /// <summary>
        ///     A layer is picked only where nothing inside it was hit.
        /// </summary>
        /// <remarks>
        ///     A container usually spans everything within it, so picking one whenever the pointer is
        ///     inside its box makes its children unselectable - and every interface in this cache is
        ///     a root layer with everything else inside it.
        /// </remarks>
        [Fact]
        public void TopmostAt_PrefersALeafToTheLayerHoldingIt() {
            var (_, order, resolved) = Build(
                At(1, 0, 0, 0, 200, 200),
                At(2, 3, 10, 10, 20, 20, 1));

            Assert.Equal(2, InterfaceHitTest.TopmostAt(order, resolved, 15, 15));
            Assert.Equal(1, InterfaceHitTest.TopmostAt(order, resolved, 150, 150));
        }

        /// <summary>
        ///     A marquee catches what it wholly encloses and never what it merely touches.
        /// </summary>
        /// <remarks>
        ///     The rule matters more than it sounds. With an intersection rule the root layer, which
        ///     covers the whole canvas in almost every interface here, would be caught by every band
        ///     ever drawn and a marquee would always mean "the whole interface".
        /// </remarks>
        [Fact]
        public void Within_CatchesOnlyWhatItWhollyEncloses() {
            var (_, order, resolved) = Build(
                At(1, 0, 0, 0, 765, 503),
                At(2, 3, 10, 10, 20, 20, 1),
                At(3, 3, 40, 10, 20, 20, 1),
                At(4, 3, 200, 10, 20, 20, 1));

            IReadOnlyList<int> caught = InterfaceHitTest.Within(order, resolved,
                new InterfaceRect(0, 0, 100, 100));

            Assert.Equal(new[] { 2, 3 }, caught);
        }

        /// <summary>A band that encloses nothing, and a band of no size, catch nothing.</summary>
        /// <remarks>
        ///     The zero-size case is the one worth pinning: <see cref="InterfaceRect.IsInside"/>
        ///     answers true for an empty rectangle, because no pixel of it can fall outside, so a
        ///     click that moved no pixels would otherwise select every zero-sized component in the
        ///     interface.
        /// </remarks>
        [Fact]
        public void Within_CatchesNothingForAnEmptyBand() {
            var (_, order, resolved) = Build(
                At(1, 3, 10, 10, 0, 0),
                At(2, 3, 40, 10, 20, 20));

            Assert.Empty(InterfaceHitTest.Within(order, resolved, new InterfaceRect(0, 0, 0, 0)));
            Assert.Empty(InterfaceHitTest.Within(order, resolved, new InterfaceRect(300, 300, 50, 50)));
        }

        /// <summary>The caught ids come back in draw order, not in id order.</summary>
        [Fact]
        public void Within_ReturnsCaughtComponentsInDrawOrder() {
            var (_, order, resolved) = Build(
                At(1, 0, 0, 0, 300, 300),
                At(5, 3, 10, 10, 10, 10, 1),
                At(3, 3, 30, 10, 10, 10, 1),
                At(9, 3, 50, 10, 10, 10, 1));

            //Draw order is a parent before its children and then ascending within the parent, which
            //is exactly what the canvas paints and therefore what "topmost" has to mean.
            Assert.Equal(new[] { 3, 5, 9 },
                InterfaceHitTest.Within(order, resolved, new InterfaceRect(5, 5, 100, 100)));
        }

        /// <summary>
        ///     A selected component whose selected ancestor already carries it is not written to.
        /// </summary>
        /// <remarks>
        ///     Its position resolves against its parent, so moving the parent already moves it.
        ///     Writing a base for it as well moves it twice as far as the pointer went.
        /// </remarks>
        [Fact]
        public void MovableRoots_DropsADescendantOfAnotherSelectedComponent() {
            var (tree, _, _) = Build(
                At(1, 0, 0, 0, 300, 300),
                At(2, 0, 10, 10, 100, 100, 1),
                At(3, 3, 10, 10, 10, 10, 2),
                At(4, 3, 200, 10, 10, 10));

            Assert.Equal(new[] { 1, 4 },
                InterfaceHitTest.MovableRoots(tree, new[] { 1, 2, 3, 4 }));

            //And a selection holding only the deep one keeps it, because nothing above it is held.
            Assert.Equal(new[] { 3 }, InterfaceHitTest.MovableRoots(tree, new[] { 3 }));
        }

        /// <summary>
        ///     A component that is its own parent terminates and is still movable.
        /// </summary>
        /// <remarks>
        ///     <b>Not hypothetical.</b> Group 468 file 1 stores its own file id as its parent,
        ///     byte-identically in the vanilla b639 capture and in the repack. A "was this reached
        ///     from any selected component" test would drop it - it reaches itself - and the drag
        ///     would silently write nothing.
        /// </remarks>
        [Fact]
        public void MovableRoots_KeepsAComponentThatIsItsOwnParent() {
            var (tree, _, _) = Build(
                At(1, 0, 0, 0, 300, 300, 1),
                At(2, 3, 10, 10, 10, 10, 1));

            Assert.Equal(new[] { 1 }, InterfaceHitTest.MovableRoots(tree, new[] { 1 }));

            //And with the child held too, the child is dropped while the self-parenting one stays.
            Assert.Equal(new[] { 1 }, InterfaceHitTest.MovableRoots(tree, new[] { 1, 2 }));
        }

        /// <summary>
        ///     A longer parent cycle terminates rather than running until the stack does.
        /// </summary>
        /// <remarks>
        ///     There is deliberately no depth cap anywhere in this family, because the format permits
        ///     a 770-level chain inside the 771-file group this cache holds. Termination comes from
        ///     the visited set, which is what this asserts.
        /// </remarks>
        [Fact]
        public void MovableRoots_TerminatesOnAThreeComponentCycle() {
            var (tree, _, _) = Build(
                At(1, 0, 0, 0, 300, 300, 3),
                At(2, 0, 0, 0, 300, 300, 1),
                At(3, 0, 0, 0, 300, 300, 2));

            //Every one of the three is inside the cycle, so each is reached from the other two and
            //only the one the walk starts from survives its own descent.
            Assert.Empty(InterfaceHitTest.MovableRoots(tree, new[] { 1, 2, 3 }));
            Assert.Equal(new[] { 2 }, InterfaceHitTest.MovableRoots(tree, new[] { 2 }));
        }

        /// <summary>An empty selection moves nothing rather than throwing.</summary>
        [Fact]
        public void MovableRoots_AnswersNothingForAnEmptySelection() {
            var (tree, _, _) = Build(At(1, 3, 0, 0, 10, 10));

            Assert.Empty(InterfaceHitTest.MovableRoots(tree, new int[0]));
        }
    }
}
