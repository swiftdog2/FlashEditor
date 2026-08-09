using System.Collections.Generic;
using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     The layout resolver's arms, one at a time, against hand-computed values.
    /// </summary>
    /// <remarks>
    ///     This code touches no bytes, so no byte-identity sweep can defend it and a wrong branch
    ///     would show only as a canvas that draws things in plausible but wrong places. These are
    ///     the tests instead.
    ///     <para>
    ///     <b>Every expected value here is computed from the client's expression by hand and stated
    ///     with its working, never taken from a document.</b> The specification this was built from
    ///     got one of these constants exactly inverted - it demanded -1211 where the client produces
    ///     -1212, which is precisely the output of the wrong implementation the test existed to
    ///     catch - and three independent reviewers caught it before it was written. That is the
    ///     failure mode <c>CLAUDE.md</c> records as having already shipped twice: a hand-built test
    ///     that asserts the bug rather than catching it.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceLayoutResolverTests {
        private static InterfaceComponentDefinition Component(int type = 3) {
            return new InterfaceComponentDefinition(0, 0) { ComponentType = type };
        }

        private static (int Width, int Height) Size(InterfaceComponentDefinition component,
            int parentWidth, int parentHeight, (int Width, int Height)? previous = null) {
            return InterfaceLayoutResolver.ResolveSize(component, parentWidth, parentHeight,
                previous ?? (0, 0), (1, 1), out _);
        }

        [Fact]
        public void WidthMode0_TakesTheBaseWidthAndIgnoresTheParent() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 0;
            component.BaseWidth = 120;

            Assert.Equal(120, Size(component, 765, 503).Width);
            Assert.Equal(120, Size(component, 1, 1).Width);
        }

        [Fact]
        public void WidthMode1_IsTheParentLessTheBase() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 1;
            component.BaseWidth = 65;

            Assert.Equal(700, Size(component, 765, 503).Width);
        }

        /// <summary>
        ///     Mode 1 can produce a negative extent, and the resolver must not clamp it.
        /// </summary>
        /// <remarks>
        ///     Nothing in the client clamps <c>parent - base</c>. A component wider than its parent
        ///     resolves negative, the client then draws nothing for it, and an editor that clamped to
        ///     zero would show a zero-width component where the format holds a negative one - which
        ///     hides an authoring mistake the editor exists to reveal.
        /// </remarks>
        [Fact]
        public void WidthMode1_ProducesANegativeExtentRatherThanClamping() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 1;
            component.BaseWidth = 900;

            Assert.Equal(-135, Size(component, 765, 503).Width);
        }

        /// <summary>Mode 2 reads the base as a Q0.14 fraction of the parent: 16384 is 100%.</summary>
        [Fact]
        public void WidthMode2_TreatsTheBaseAsAFourteenBitFractionOfTheParent() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 2;

            component.BaseWidth = 16384;
            Assert.Equal(765, Size(component, 765, 503).Width);

            component.BaseWidth = 8192;
            Assert.Equal(382, Size(component, 765, 503).Width);   //(765 * 8192) >> 14 == 382

            component.BaseWidth = 4096;
            Assert.Equal(191, Size(component, 765, 503).Width);   //(765 * 4096) >> 14 == 191
        }

        /// <summary>
        ///     16391 is the modal proportional base in this cache and resolves to the same pixel as
        ///     16384.
        /// </summary>
        /// <remarks>
        ///     Worth pinning because it looks like a corruption and is not. 1,067 of the 2,251
        ///     width-mode-2 components store 16391 rather than 16384, and a reader who assumed the
        ///     base is capped at 100% would take that as evidence against the Q0.14 reading. It is
        ///     100% plus seven fourteen-bit ticks, and against a 765-wide parent the shift discards
        ///     all seven.
        /// </remarks>
        [Fact]
        public void WidthMode2_At16391_ResolvesToTheSamePixelAs16384() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 2;

            component.BaseWidth = 16384;
            int atExactly100Percent = Size(component, 765, 503).Width;

            component.BaseWidth = 16391;
            Assert.Equal(atExactly100Percent, Size(component, 765, 503).Width);
        }

        /// <summary>
        ///     Mode 3 is not an arm at all: the extent keeps whatever value it already had.
        /// </summary>
        /// <remarks>
        ///     <c>Class253.java:321</c> and <c>:336</c> are bare <c>if</c>s with no <c>else</c>, so a
        ///     mode the client does not name leaves the field untouched. Defended by nothing in the
        ///     data - sizing modes 3 and 4 occur zero times in either supported cache - so this test
        ///     is the only thing standing under the branch.
        /// </remarks>
        [Fact]
        public void SizingModesOtherThan0_1_2And4_LeaveTheExtentUnchanged() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 3;
            component.HeightMode = 3;
            component.BaseWidth = 999;
            component.BaseHeight = 999;

            (int width, int height) = Size(component, 765, 503, (44, 55));

            Assert.Equal(44, width);
            Assert.Equal(55, height);
        }

        /// <summary>
        ///     Mode 4 on both axes recomputes width from the stale height, then height from the new
        ///     width, and on a first pass that is 0 by 0.
        /// </summary>
        /// <remarks>
        ///     The order at <c>Class253.java:343</c> then <c>:347</c> is load-bearing and the result
        ///     looks like a defect. It is what the client computes, and a resolver that "fixed" it
        ///     would disagree with the client for every component CS2 puts in this state.
        /// </remarks>
        [Fact]
        public void BothAxesInAspectMode_CollapseToZeroOnAFirstPass() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 4;
            component.HeightMode = 4;

            (int width, int height) = Size(component, 765, 503);

            Assert.Equal(0, width);
            Assert.Equal(0, height);
        }

        /// <summary>A zero aspect denominator is reported, not thrown, and leaves the extent alone.</summary>
        /// <remarks>
        ///     The client throws <c>ArithmeticException</c> at <c>Class253.java:344</c>. Diverged
        ///     from deliberately: leaving the extent at its previous value is the client's own idiom
        ///     for an arm it cannot compute, on the very same method, and an exception here would
        ///     stop the editor displaying a whole interface for one record.
        /// </remarks>
        [Fact]
        public void ADegenerateAspectRatioIsReportedRatherThanThrown() {
            InterfaceComponentDefinition component = Component();
            component.WidthMode = 4;

            (int width, _) = InterfaceLayoutResolver.ResolveSize(component, 765, 503, (44, 55),
                (0, 0), out InterfaceLayoutDiagnostics diagnostics);

            Assert.Equal(44, width);
            Assert.True(diagnostics.HasFlag(InterfaceLayoutDiagnostics.DegenerateAspect));
        }

        [Fact]
        public void XMode0_IsTheBasePosition() {
            InterfaceComponentDefinition component = Component();
            component.XMode = 0;
            component.BasePositionX = 37;

            Assert.Equal(37, InterfaceLayoutResolver.ResolvePosition(component, 765, 503, 10, 10).X);
        }

        [Fact]
        public void XMode1_CentresTheComponentAndThenOffsetsIt() {
            InterfaceComponentDefinition component = Component();
            component.XMode = 1;
            component.BasePositionX = 5;

            //5 + (765 - 65) / 2 = 5 + 350
            Assert.Equal(355, InterfaceLayoutResolver.ResolvePosition(component, 765, 503, 65, 10).X);
        }

        [Fact]
        public void XMode2_MeasuresTheBaseFromTheRightEdge() {
            InterfaceComponentDefinition component = Component();
            component.XMode = 2;
            component.BasePositionX = 20;

            //765 - 65 - 20
            Assert.Equal(680, InterfaceLayoutResolver.ResolvePosition(component, 765, 503, 65, 10).X);
        }

        /// <summary>
        ///     A negative base under a shift mode floors, and this is the test the specification got
        ///     backwards.
        /// </summary>
        /// <remarks>
        ///     <c>KeyStroke.java:44</c> is <c>y * i &gt;&gt; 14</c>, an arithmetic shift, which floors
        ///     toward negative infinity. C#'s <c>/</c> truncates toward zero. The two differ for every
        ///     negative numerator that is not an exact multiple of 16384, and this cache has 117
        ///     components with a negative base position on a shift-mode axis, so the difference is
        ///     live rather than theoretical.
        ///     <para>
        ///     The working, because a bare constant here is exactly how the specification went wrong:
        ///     -25945 * 765 = -19,847,925. 16384 * 1212 = 19,857,408 and 16384 * 1211 = 19,841,024,
        ///     so -19,847,925 lies between -16384*1212 and -16384*1211. Flooring gives <b>-1212</b>;
        ///     truncating gives -1211. <b>-1211 is the wrong answer</b>, and a test asserting it
        ///     would pass against a resolver using <c>/ 16384</c> and fail against a correct one.
        ///     </para>
        /// </remarks>
        [Fact]
        public void ANegativeBaseUnderAShiftMode_FloorsRatherThanTruncating() {
            InterfaceComponentDefinition component = Component();
            component.XMode = 3;
            component.BasePositionX = -25945;

            int x = InterfaceLayoutResolver.ResolvePosition(component, 765, 503, 10, 10).X;

            Assert.Equal(-1212, x);
            Assert.NotEqual(-25945 * 765 / 16384, x);
        }

        /// <summary>
        ///     Every mode byte outside 0..4 takes the last arm, not a default of zero.
        /// </summary>
        /// <remarks>
        ///     The mode is read as an unclamped signed byte (<c>RSInterface.java:1053-1056</c>) and
        ///     only the CS2 setter clamps it. A <c>switch</c> with a <c>case 5</c> and no
        ///     <c>default</c> would leave a stored 6 or -128 at the origin, silently.
        /// </remarks>
        [Theory]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(127)]
        [InlineData(-128)]
        public void APositionModeOutside0To4_TakesTheCatchAllArm(int mode) {
            InterfaceComponentDefinition component = Component();
            component.XMode = (sbyte) mode;
            component.BasePositionX = 8192;

            //765 - 65 - ((765 * 8192) >> 14) = 700 - 382
            Assert.Equal(318, InterfaceLayoutResolver.ResolvePosition(component, 765, 503, 65, 10).X);
        }

        /// <summary>
        ///     A line's clip runs one pixel further right and further down than its rectangle.
        /// </summary>
        /// <remarks>
        ///     <c>Node_Sub10_Sub24.java:197-200</c>, corroborated by the independently written
        ///     hit-test pass at <c>client.java:733-736</c>. Both caches hold 367 type-9 components,
        ///     and a resolver without this arm clips the last pixel off every one of them - a defect
        ///     no test that does not draw could otherwise see.
        /// </remarks>
        [Fact]
        public void ALineComponentsClipExtendsOnePixelPastItsRectangle() {
            var line = new InterfaceComponentDefinition(0, 0) { ComponentType = 9 };
            line.WidthMode = 0;
            line.HeightMode = 0;
            line.BaseWidth = 40;
            line.BaseHeight = 0;

            var rectangle = new InterfaceComponentDefinition(0, 1) { ComponentType = 3 };
            rectangle.WidthMode = 0;
            rectangle.HeightMode = 0;
            rectangle.BaseWidth = 40;
            rectangle.BaseHeight = 0;

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { line, rectangle });
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            Assert.Equal(1, resolved[0].Clip.Height);
            Assert.Equal(41, resolved[0].Clip.Width);

            //The same geometry as a rectangle clips to nothing, because its height really is zero.
            Assert.True(resolved[1].Clip.IsEmpty);
        }

        /// <summary>A type-2 component inherits its parent's clip rather than its own box.</summary>
        [Fact]
        public void ATypeTwoComponentInheritsTheClipUnchanged() {
            var passthrough = new InterfaceComponentDefinition(0, 0) { ComponentType = 2 };
            passthrough.WidthMode = 0;
            passthrough.HeightMode = 0;
            passthrough.BaseWidth = 10;
            passthrough.BaseHeight = 10;

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { passthrough });
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            Assert.Equal(InterfaceRect.FixedModeCanvas, resolved[0].Clip);
        }

        /// <summary>A scrolling layer offers its children the scroll extent, not its visible box.</summary>
        /// <remarks>
        ///     <c>Class63.java:104-106</c>. Without it every proportional child of a scrolling layer
        ///     piles into the visible fraction of it.
        /// </remarks>
        [Fact]
        public void AScrollingLayerOffersItsChildrenTheScrollExtent() {
            var layer = new InterfaceComponentDefinition(0, 0) { ComponentType = 0 };
            layer.WidthMode = 0;
            layer.HeightMode = 0;
            layer.BaseWidth = 200;
            layer.BaseHeight = 100;
            layer.ScrollMaxHorizontal = 600;

            var child = new InterfaceComponentDefinition(0, 1) { ComponentType = 3 };
            child.WidthMode = 2;
            child.BaseWidth = 16384;
            child.RawParentId = 0;

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { layer, child });
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            Assert.Equal(600, resolved[1].Relative.Width);
        }

        /// <summary>
        ///     A component that is its own parent terminates, and is reported rather than dropped.
        /// </summary>
        /// <remarks>
        ///     Not a hypothetical: index 3 group 468 file 1 stores its own file id as its parent,
        ///     byte-identically in both supported caches. The specification asserted that no cycle
        ///     exists, which would have failed on the first run against either cache.
        /// </remarks>
        [Fact]
        public void AComponentThatIsItsOwnParent_TerminatesAndIsReportedAsCyclic() {
            var root = new InterfaceComponentDefinition(0, 0) { ComponentType = 0 };
            root.WidthMode = 0;
            root.HeightMode = 0;

            var selfParented = new InterfaceComponentDefinition(0, 1) { ComponentType = 3 };
            selfParented.RawParentId = 1;

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { root, selfParented });

            Assert.Equal(InterfaceParentage.Cyclic, tree.ParentageOf(1));
            Assert.DoesNotContain(1, tree.InDrawOrder());

            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            Assert.True(resolved.ContainsKey(1));
            Assert.False(resolved[1].IsDrawn);
            Assert.True(resolved[1].Diagnostics.HasFlag(InterfaceLayoutDiagnostics.CyclicParent));
        }

        /// <summary>A two-component cycle terminates as well, not just a self-reference.</summary>
        [Fact]
        public void ATwoComponentCycle_Terminates() {
            var first = new InterfaceComponentDefinition(0, 0) { ComponentType = 0, RawParentId = 1 };
            var second = new InterfaceComponentDefinition(0, 1) { ComponentType = 0, RawParentId = 0 };

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { first, second });

            Assert.Empty(tree.Roots);
            Assert.Equal(InterfaceParentage.Cyclic, tree.ParentageOf(0));
            Assert.Equal(InterfaceParentage.Cyclic, tree.ParentageOf(1));
            Assert.Equal(2, InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas).Count);
        }

        /// <summary>A parent field naming a file the group does not hold is reported, not dropped.</summary>
        [Fact]
        public void ADanglingParentIsReportedAndTheComponentStillResolves() {
            var orphan = new InterfaceComponentDefinition(0, 0) { ComponentType = 3, RawParentId = 900 };

            InterfaceComponentTree tree = InterfaceComponentTree.Build(0, new[] { orphan });
            IReadOnlyDictionary<int, InterfaceLayoutNode> resolved =
                InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas);

            Assert.Equal(InterfaceParentage.Dangling, tree.ParentageOf(0));
            Assert.True(resolved[0].Diagnostics.HasFlag(InterfaceLayoutDiagnostics.DanglingParent));
            Assert.False(resolved[0].IsDrawn);
        }

        /// <summary>Children are walked in file-id order, because that is the client's draw order.</summary>
        /// <remarks>
        ///     Z-order is not a stored field. The client draws a parent's children in array index
        ///     order, so "send to back" is a renumber, and anything that presented children in the
        ///     order they happened to decode would show a different stacking from the game.
        /// </remarks>
        [Fact]
        public void ChildrenAreWalkedInFileIdOrderWhateverOrderTheyArrivedIn() {
            var layer = new InterfaceComponentDefinition(0, 0) { ComponentType = 0 };
            var third = new InterfaceComponentDefinition(0, 3) { ComponentType = 3, RawParentId = 0 };
            var first = new InterfaceComponentDefinition(0, 1) { ComponentType = 3, RawParentId = 0 };
            var second = new InterfaceComponentDefinition(0, 2) { ComponentType = 3, RawParentId = 0 };

            InterfaceComponentTree tree =
                InterfaceComponentTree.Build(0, new[] { third, second, layer, first });

            Assert.Equal(new[] { 1, 2, 3 }, tree.ChildrenOf(0));
            Assert.Equal(new[] { 0, 1, 2, 3 }, tree.InDrawOrder());
        }
    }
}
