using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>How far a drag is allowed to be pulled, and onto what.</summary>
    /// <remarks>
    ///     Two independent sources rather than one. An edge snap is what an author wants when they
    ///     are lining a caption up with the box above it; a grid step is what they want when there is
    ///     nothing to line up against. Turning either off by setting it to zero is meaningful, so
    ///     neither is derived from the other.
    /// </remarks>
    public readonly struct InterfaceSnapSettings {
        /// <summary>Snapping disabled, which is what a drag does when the toggle is off.</summary>
        public static readonly InterfaceSnapSettings Off = new InterfaceSnapSettings(0, 0);

        /// <summary>
        ///     The default: align to another component's edge or centre within four pixels, and fall
        ///     back to a four-pixel grid.
        /// </summary>
        /// <remarks>
        ///     Four rather than eight because interface components in this cache are small - the
        ///     median component is under 40 pixels on its shorter axis - and a threshold that is a
        ///     large fraction of the thing being dragged reads as the editor refusing to put it where
        ///     the pointer is.
        /// </remarks>
        public static readonly InterfaceSnapSettings Default = new InterfaceSnapSettings(4, 4);

        /// <summary>States both distances.</summary>
        /// <param name="gridStep">The grid pitch in pixels, or 0 for no grid.</param>
        /// <param name="edgeThreshold">How near an edge has to be to catch, or 0 for no edge snap.</param>
        public InterfaceSnapSettings(int gridStep, int edgeThreshold) {
            GridStep = Math.Max(0, gridStep);
            EdgeThreshold = Math.Max(0, edgeThreshold);
        }

        /// <summary>The grid pitch in pixels. Zero means no grid.</summary>
        public int GridStep { get; }

        /// <summary>How near an edge has to be to catch. Zero means no edge snap.</summary>
        public int EdgeThreshold { get; }

        /// <summary>Whether either source can move anything.</summary>
        public bool Enabled => GridStep > 0 || EdgeThreshold > 0;
    }

    /// <summary>Where a snapped drag landed, and the line it landed on.</summary>
    /// <remarks>
    ///     The guides are carried back so the canvas can draw them. A snap that moves a component
    ///     without saying what it caught on reads as the drag being wrong rather than as the editor
    ///     helping, which is the usual complaint about snapping in any editor that hides it.
    /// </remarks>
    public readonly struct InterfaceSnapResult {
        internal InterfaceSnapResult(int x, int y, int guideX, int guideY) {
            X = x;
            Y = y;
            GuideX = guideX;
            GuideY = guideY;
        }

        /// <summary>The snapped value on the horizontal axis.</summary>
        public int X { get; }

        /// <summary>The snapped value on the vertical axis.</summary>
        public int Y { get; }

        /// <summary>
        ///     The vertical line the drag caught on, or <see cref="int.MinValue"/> for none.
        /// </summary>
        /// <remarks>
        ///     A sentinel rather than a nullable, because zero is a real canvas coordinate and the
        ///     left edge of the screen is one of the commonest things to snap to.
        /// </remarks>
        public int GuideX { get; }

        /// <summary>The horizontal line the drag caught on, or <see cref="int.MinValue"/> for none.</summary>
        public int GuideY { get; }

        /// <summary>Whether a vertical guide was caught.</summary>
        public bool HasGuideX => GuideX != int.MinValue;

        /// <summary>Whether a horizontal guide was caught.</summary>
        public bool HasGuideY => GuideY != int.MinValue;
    }

    /// <summary>
    ///     Pulls a dragged rectangle onto the edges around it, or onto a grid.
    /// </summary>
    /// <remarks>
    ///     <b>Snapping happens in resolved pixels, before the mode is inverted, and that ordering is
    ///     the whole design.</b> A component's stored base is not a pixel on four of the six
    ///     positioning modes - three of them store a Q0.14 fraction of the parent and one measures
    ///     from the far edge - so snapping the stored number would move a mode-2 component away from
    ///     the line it was meant to catch and move a mode-3 component by about a two-hundredth of the
    ///     distance. The wanted pixel is snapped here, and
    ///     <see cref="InterfaceLayoutResolver.BaseForPosition"/> then inverts whatever this returns.
    ///     <para>
    ///     <b>The shift modes cannot represent every pixel, so a snapped drag may still not land
    ///     exactly on the guide.</b> That is inherent to the format rather than a defect here: on a
    ///     narrow parent one unit of base is more than a pixel. The canvas re-resolves after every
    ///     edit and shows where the component actually went, which is the only honest thing to show.
    ///     </para>
    /// </remarks>
    public static class InterfaceSnap {
        /// <summary>
        ///     The position a moving rectangle should take, pulled onto whatever is near it.
        /// </summary>
        /// <remarks>
        ///     Each axis is decided independently, and on each the nearest candidate within the
        ///     threshold wins. Three edges of the moving rectangle are offered - near, centre and far
        ///     - against the same three of every target, so a box lines up left to left, centred on a
        ///     centre, or right to right without the caller having to say which.
        ///     <para>
        ///     The grid is a fallback rather than a competitor. An edge that is one pixel from a
        ///     sibling and three from a grid line should catch the sibling, and running both and
        ///     taking the nearer would make the grid pitch silently override alignment.
        ///     </para>
        /// </remarks>
        /// <param name="wanted">Where the pointer would put the rectangle, in canvas coordinates.</param>
        /// <param name="targets">The rectangles to align against, excluding the ones being dragged.</param>
        /// <param name="settings">How far a drag may be pulled.</param>
        /// <returns>The snapped top-left corner and the guides it caught.</returns>
        public static InterfaceSnapResult SnapMove(InterfaceRect wanted,
            IReadOnlyList<InterfaceRect> targets, InterfaceSnapSettings settings) {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            if (!settings.Enabled)
                return new InterfaceSnapResult(wanted.X, wanted.Y, int.MinValue, int.MinValue);

            (int deltaX, int guideX) = SnapAxis(wanted.X, wanted.Width, targets, settings, true);
            (int deltaY, int guideY) = SnapAxis(wanted.Y, wanted.Height, targets, settings, false);

            return new InterfaceSnapResult(wanted.X + deltaX, wanted.Y + deltaY, guideX, guideY);
        }

        /// <summary>
        ///     The extent a resizing rectangle should take, with its far edge pulled onto what is
        ///     near it.
        /// </summary>
        /// <remarks>
        ///     Only the far edge moves, because the canvas offers one grip and it is the bottom-right
        ///     corner. Snapping the near edge as well would move a component the user is sizing.
        ///     <para>
        ///     A snapped extent is clamped at zero. The format permits a negative extent and the
        ///     resolver reproduces one, but nothing should be able to <i>create</i> one by dragging
        ///     past the opposite corner.
        ///     </para>
        /// </remarks>
        /// <param name="fixedCorner">The top-left corner, which does not move.</param>
        /// <param name="wanted">The extents the pointer asks for.</param>
        /// <param name="targets">The rectangles to align against.</param>
        /// <param name="settings">How far a drag may be pulled.</param>
        /// <returns>The snapped extents, carried in <c>X</c> and <c>Y</c>, and the guides caught.</returns>
        public static InterfaceSnapResult SnapResize(InterfaceRect fixedCorner, InterfaceRect wanted,
            IReadOnlyList<InterfaceRect> targets, InterfaceSnapSettings settings) {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            if (!settings.Enabled)
                return new InterfaceSnapResult(wanted.Width, wanted.Height, int.MinValue, int.MinValue);

            (int deltaX, int guideX) = SnapEdge(fixedCorner.X + wanted.Width, targets, settings, true);
            (int deltaY, int guideY) = SnapEdge(fixedCorner.Y + wanted.Height, targets, settings, false);

            return new InterfaceSnapResult(Math.Max(0, wanted.Width + deltaX),
                Math.Max(0, wanted.Height + deltaY), guideX, guideY);
        }

        /// <summary>The lines a target rectangle offers on one axis: near edge, centre, far edge.</summary>
        /// <param name="target">The target.</param>
        /// <param name="horizontal">Whether the horizontal axis is wanted.</param>
        /// <returns>The three lines.</returns>
        private static (int Near, int Centre, int Far) LinesOf(InterfaceRect target, bool horizontal) {
            return horizontal
                ? (target.X, target.X + target.Width / 2, target.Right)
                : (target.Y, target.Y + target.Height / 2, target.Bottom);
        }

        /// <summary>
        ///     How far one axis of a moving rectangle has to shift to catch something.
        /// </summary>
        /// <param name="start">The moving rectangle's near edge.</param>
        /// <param name="extent">Its extent on this axis.</param>
        /// <param name="targets">The rectangles to align against.</param>
        /// <param name="settings">How far a drag may be pulled.</param>
        /// <param name="horizontal">Whether the horizontal axis is being decided.</param>
        /// <returns>The shift to apply, and the line caught or <see cref="int.MinValue"/>.</returns>
        private static (int Delta, int Guide) SnapAxis(int start, int extent,
            IReadOnlyList<InterfaceRect> targets, InterfaceSnapSettings settings, bool horizontal) {
            int best = int.MaxValue;
            int guide = int.MinValue;

            if (settings.EdgeThreshold > 0) {
                //The moving rectangle's own three lines, in the same order the targets offer theirs.
                int[] moving = { start, start + extent / 2, start + extent };

                foreach (InterfaceRect target in targets) {
                    if (target.IsEmpty)
                        continue;

                    (int near, int centre, int far) = LinesOf(target, horizontal);
                    int[] lines = { near, centre, far };

                    foreach (int line in lines) {
                        foreach (int edge in moving) {
                            int delta = line - edge;
                            if (Math.Abs(delta) > settings.EdgeThreshold || Math.Abs(delta) >= Math.Abs(best))
                                continue;

                            best = delta;
                            guide = line;
                        }
                    }
                }
            }

            if (best != int.MaxValue)
                return (best, guide);

            //The grid is only consulted where nothing was near, so alignment always beats pitch.
            return settings.GridStep > 0
                ? (Nearest(start, settings.GridStep) - start, int.MinValue)
                : (0, int.MinValue);
        }

        /// <summary>
        ///     How far a single moving edge has to shift to catch something.
        /// </summary>
        /// <remarks>
        ///     A resize offers only the edge under the grip, so the three-lines-each pairing a move
        ///     uses does not apply: a component's far edge lining up with a sibling's centre is a
        ///     real alignment, but its far edge lining up with its own centre is not a thing that can
        ///     happen.
        /// </remarks>
        /// <param name="edge">The moving edge.</param>
        /// <param name="targets">The rectangles to align against.</param>
        /// <param name="settings">How far a drag may be pulled.</param>
        /// <param name="horizontal">Whether the horizontal axis is being decided.</param>
        /// <returns>The shift to apply, and the line caught or <see cref="int.MinValue"/>.</returns>
        private static (int Delta, int Guide) SnapEdge(int edge, IReadOnlyList<InterfaceRect> targets,
            InterfaceSnapSettings settings, bool horizontal) {
            int best = int.MaxValue;
            int guide = int.MinValue;

            if (settings.EdgeThreshold > 0) {
                foreach (InterfaceRect target in targets) {
                    if (target.IsEmpty)
                        continue;

                    (int near, int centre, int far) = LinesOf(target, horizontal);
                    int[] lines = { near, centre, far };

                    foreach (int line in lines) {
                        int delta = line - edge;
                        if (Math.Abs(delta) > settings.EdgeThreshold || Math.Abs(delta) >= Math.Abs(best))
                            continue;

                        best = delta;
                        guide = line;
                    }
                }
            }

            if (best != int.MaxValue)
                return (best, guide);

            return settings.GridStep > 0
                ? (Nearest(edge, settings.GridStep) - edge, int.MinValue)
                : (0, int.MinValue);
        }

        /// <summary>
        ///     The nearest multiple of a step.
        /// </summary>
        /// <remarks>
        ///     Written for negative inputs as well as positive ones. A component's resolved position
        ///     goes negative in this cache - 117 of them have a negative base on a shift-mode axis -
        ///     and <c>value / step * step</c> truncates towards zero, which would snap -5 to 0 while
        ///     snapping 5 to 4 on a four-pixel grid.
        /// </remarks>
        /// <param name="value">The value.</param>
        /// <param name="step">The pitch, which must be positive.</param>
        /// <returns>The nearest multiple.</returns>
        private static int Nearest(int value, int step) {
            int down = (int) Math.Floor(value / (double) step) * step;
            return value - down < step - (value - down) ? down : down + step;
        }
    }
}
