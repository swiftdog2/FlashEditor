using FlashEditor.Definitions.Interfaces;
using FlashEditor.Definitions.Interfaces.Layout;
using Xunit;

namespace FlashEditor.Tests.Definitions.Interfaces {
    /// <summary>
    ///     What a snapped drag lands on, and that it is the pixel being snapped rather than the
    ///     stored base.
    /// </summary>
    /// <remarks>
    ///     The last test in this file is the one that matters most. Snapping is easy to write in the
    ///     wrong place - pulling the stored number onto a multiple of four looks identical for a
    ///     mode-0 component and is wrong for every other mode, because four of the six positioning
    ///     modes do not store a pixel at all.
    /// </remarks>
    public sealed class InterfaceSnapTests {
        private static readonly InterfaceRect[] Nothing = new InterfaceRect[0];

        /// <summary>With nothing near, a drag falls onto the grid.</summary>
        [Fact]
        public void SnapMove_FallsBackToTheGridWhenNothingIsNear() {
            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(7, 9, 20, 10), Nothing, InterfaceSnapSettings.Default);

            Assert.Equal(8, snapped.X);
            Assert.Equal(8, snapped.Y);

            //A grid landing is not a guide. There is no line to draw, and marking one would tell the
            //user a component caught on something when it caught on nothing.
            Assert.False(snapped.HasGuideX);
            Assert.False(snapped.HasGuideY);
        }

        /// <summary>
        ///     A nearby edge wins even when the grid is nearer.
        /// </summary>
        /// <remarks>
        ///     The discriminating case for the "grid is a fallback, not a competitor" rule. The
        ///     moving rectangle starts on a grid line, so the grid asks for a shift of zero and the
        ///     edge asks for two - and the edge still has to win, or an author lining a caption up
        ///     with the box above it would find the pitch silently overriding them.
        /// </remarks>
        [Fact]
        public void SnapMove_PrefersAnEdgeToTheGridEvenWhenTheGridIsNearer() {
            var targets = new[] { new InterfaceRect(10, 0, 40, 40) };

            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(12, 100, 20, 10), targets, InterfaceSnapSettings.Default);

            Assert.Equal(10, snapped.X);
            Assert.Equal(10, snapped.GuideX);
        }

        /// <summary>An edge further away than the threshold does not catch.</summary>
        /// <remarks>
        ///     The target is deliberately narrow. A wide one offers a centre line as well as two
        ///     edges, and a 40-pixel box at x=10 has its centre at exactly 30 - which is where a
        ///     20-wide component dragged to x=20 has its own, so that pairing would catch at zero
        ///     distance and the test would be asserting the opposite of what it says.
        /// </remarks>
        [Fact]
        public void SnapMove_IgnoresAnEdgeBeyondTheThreshold() {
            var targets = new[] { new InterfaceRect(10, 0, 4, 40) };

            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(20, 100, 20, 10), targets, InterfaceSnapSettings.Default);

            //Twenty is already on the grid, so the fallback moves it nowhere and the point is that
            //the edge ten pixels away did not drag it back.
            Assert.Equal(20, snapped.X);
            Assert.False(snapped.HasGuideX);
        }

        /// <summary>The far edge of the moving rectangle catches too, not only its near edge.</summary>
        [Fact]
        public void SnapMove_AlignsTheMovingRectanglesFarEdge() {
            var targets = new[] { new InterfaceRect(100, 0, 50, 20) };

            //Right edge at 47 + 50 == 97, three short of the target's left edge at 100.
            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(47, 200, 50, 10), targets, InterfaceSnapSettings.Default);

            Assert.Equal(50, snapped.X);
            Assert.Equal(100, snapped.GuideX);
        }

        /// <summary>Centres catch as well as edges, which is how a caption lands under a box.</summary>
        [Fact]
        public void SnapMove_AlignsCentres() {
            var targets = new[] { new InterfaceRect(100, 0, 100, 20) };

            //The target's centre is 150; the moving rectangle's is 138 + 10 == 148.
            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(138, 200, 20, 10), targets, InterfaceSnapSettings.Default);

            Assert.Equal(140, snapped.X);
            Assert.Equal(150, snapped.GuideX);
        }

        /// <summary>Snapping off leaves the drag exactly where the pointer put it.</summary>
        [Fact]
        public void SnapMove_DoesNothingWhenItIsOff() {
            var targets = new[] { new InterfaceRect(10, 10, 40, 40) };

            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(11, 11, 20, 10), targets, InterfaceSnapSettings.Off);

            Assert.Equal(11, snapped.X);
            Assert.Equal(11, snapped.Y);
            Assert.False(snapped.HasGuideX);
        }

        /// <summary>
        ///     The grid floors towards negative infinity rather than truncating towards zero.
        /// </summary>
        /// <remarks>
        ///     A resolved position genuinely goes negative in this cache. Truncating division would
        ///     snap -3 to 0 while snapping 3 to 4, which puts a four-pixel dead zone across the
        ///     origin that only shows up on a component dragged off the left of the screen.
        /// </remarks>
        [Fact]
        public void SnapMove_SnapsNegativePositionsToTheNearerGridLine() {
            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(-3, -3, 20, 10), Nothing, InterfaceSnapSettings.Default);

            Assert.Equal(-4, snapped.X);
            Assert.Equal(-4, snapped.Y);
        }

        /// <summary>A resize catches its far edge on what is near it.</summary>
        [Fact]
        public void SnapResize_PullsTheMovingEdgeOntoAnAlignment() {
            var targets = new[] { new InterfaceRect(100, 0, 50, 200) };

            InterfaceSnapResult snapped = InterfaceSnap.SnapResize(
                new InterfaceRect(10, 20, 88, 40), new InterfaceRect(0, 0, 88, 40),
                targets, InterfaceSnapSettings.Default);

            //10 + 88 == 98, two short of the target's left edge.
            Assert.Equal(90, snapped.X);
            Assert.Equal(100, snapped.GuideX);
        }

        /// <summary>
        ///     A snapped extent is clamped at zero.
        /// </summary>
        /// <remarks>
        ///     The format permits a negative extent and the resolver reproduces one, but nothing
        ///     should be able to <i>create</i> one by dragging past the opposite corner - that is a
        ///     mis-drag rather than an intent.
        /// </remarks>
        [Fact]
        public void SnapResize_NeverProducesANegativeExtent() {
            InterfaceSnapResult snapped = InterfaceSnap.SnapResize(
                new InterfaceRect(0, 0, 0, 0), new InterfaceRect(0, 0, -20, -20),
                Nothing, InterfaceSnapSettings.Default);

            Assert.Equal(0, snapped.X);
            Assert.Equal(0, snapped.Y);
        }

        /// <summary>
        ///     A snapped pixel survives the mode inversion, and adding the same delta to the base
        ///     does not.
        /// </summary>
        /// <remarks>
        ///     <b>This is the test the whole design exists for.</b> Positioning mode 2 resolves as
        ///     <c>parent - own - base</c>, so the stored number runs backwards against the screen. A
        ///     snap that pulled the stored base four pixels left would move the component four pixels
        ///     <i>right</i>, and a mode-3 component - which stores a Q0.14 fraction - would move by
        ///     about a two-hundredth of what was asked. Snapping the wanted pixel first and inverting
        ///     afterwards is what makes the answer the same for every mode.
        /// </remarks>
        [Fact]
        public void ASnappedPixelSurvivesTheModeInversion() {
            const int parentWidth = 400;

            var component = new InterfaceComponentDefinition(0, 0) {
                ComponentType = 3,
                XMode = 2,
                BaseWidth = 40,
                BaseHeight = 10,
                BasePositionX = 100
            };

            //Where the pointer asked for, and where the snap actually put it.
            InterfaceSnapResult snapped = InterfaceSnap.SnapMove(
                new InterfaceRect(101, 0, 40, 10), Nothing, InterfaceSnapSettings.Default);
            Assert.Equal(100, snapped.X);

            component.BasePositionX = InterfaceLayoutResolver.BaseForPosition(
                component.XMode, snapped.X, parentWidth, 40);

            (int x, _) = InterfaceLayoutResolver.ResolvePosition(component, parentWidth, 200, 40, 10);
            Assert.Equal(100, x);

            //And the base that produced it is 260, not 100 - the number stored is nothing like the
            //pixel, which is exactly why a delta added to the base would have gone the wrong way.
            Assert.Equal(260, component.BasePositionX);
        }
    }
}
