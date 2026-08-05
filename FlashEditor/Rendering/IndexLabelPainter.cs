using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing;
using System;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Draws the index labels with GDI+, over the top of the GL surface.
    /// </summary>
    /// <remarks>
    ///     GDI+ rather than a texture atlas in the GL pipeline. Text on the GPU means a glyph atlas, a
    ///     second shader and a second vertex format, and the labels are a handful of short strings
    ///     drawn once per frame over a control that already has a <c>Graphics</c> - so the whole of
    ///     that machinery would buy nothing.
    ///     <para>
    ///     Kept apart from <see cref="FaceLabelLayout"/> so the positions can be asserted without a
    ///     device context. This half is the half no test can reach, which is the reason it does as
    ///     little as possible: it decides nothing, and every position it draws at was computed
    ///     somewhere testable.
    ///     </para>
    /// </remarks>
    public static class IndexLabelPainter
    {
        /// <summary>Pixels of backdrop around the text on every side.</summary>
        public const int BackdropPadding = 3;

        /// <summary>Colour of a face label.</summary>
        /// <remarks>
        ///     Amber, matching <see cref="OverlayGeometry.HighlightColour"/> - the highlight and the
        ///     face label refer to the same triangle, and being the same colour is what says so.
        /// </remarks>
        public static Color FaceColour => Color.FromArgb(255, 255, 184, 38);

        /// <summary>Colour of a vertex label.</summary>
        /// <remarks>
        ///     Blue, matching <see cref="OverlayGeometry.WireframeColour"/>. Deliberately far from
        ///     <see cref="FaceColour"/>, because telling a face label from a vertex label at a glance
        ///     is what the overlay is for and text position alone is not enough on a small face.
        /// </remarks>
        public static Color VertexColour => Color.FromArgb(255, 150, 190, 255);

        /// <summary>Colour of the backdrop drawn behind each label.</summary>
        /// <remarks>
        ///     Mostly-opaque black. Without a backdrop a light label over light geometry is unreadable,
        ///     and the geometry under the cursor is exactly where the highlight has just made it
        ///     lighter. Not fully opaque, so the shape underneath still shows through.
        /// </remarks>
        public static Color BackdropColour => Color.FromArgb(190, 0, 0, 0);

        /// <summary>Draws a set of labels, each centred on its own pixel.</summary>
        /// <remarks>
        ///     Restores the caller's <see cref="SmoothingMode"/> rather than leaving it on. This is
        ///     drawn into a <c>Graphics</c> the control owns, and silently changing its state would
        ///     affect whatever paints next.
        ///     <para>
        ///     The brushes are created per call rather than cached in statics. A <see cref="Brush"/>
        ///     is a GDI+ handle, and one held in a static outlives the control it was used on; this
        ///     runs once a frame over at most four labels, which is not worth the leak.
        ///     </para>
        /// </remarks>
        /// <param name="graphics">The surface to draw on.</param>
        /// <param name="labels">The labels, from <see cref="FaceLabelLayout.Build"/>.</param>
        /// <param name="font">The font to measure and draw with.</param>
        /// <exception cref="ArgumentNullException">Any argument is null.</exception>
        public static void Paint(Graphics graphics, IReadOnlyList<IndexLabel> labels, Font font)
        {
            if (graphics == null)
            {
                throw new ArgumentNullException(nameof(graphics));
            }

            if (labels == null)
            {
                throw new ArgumentNullException(nameof(labels));
            }

            if (font == null)
            {
                throw new ArgumentNullException(nameof(font));
            }

            //Nothing hovered. Returning before touching the smoothing mode keeps the common case free.
            if (labels.Count == 0)
            {
                return;
            }

            SmoothingMode callersSmoothing = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using SolidBrush backdrop = new SolidBrush(BackdropColour);
            using SolidBrush faceBrush = new SolidBrush(FaceColour);
            using SolidBrush vertexBrush = new SolidBrush(VertexColour);

            foreach (IndexLabel label in labels)
            {
                SizeF size = graphics.MeasureString(label.Text, font);

                //The layout gives the point the label should be centred on, so the top left is half
                //the measured size back from it. Doing that here rather than in the layout keeps the
                //layout free of anything that needs a device context to measure.
                float left = label.Pixel.X - size.Width / 2f;
                float top = label.Pixel.Y - size.Height / 2f;

                graphics.FillRectangle(backdrop,
                    left - BackdropPadding, top - BackdropPadding,
                    size.Width + BackdropPadding * 2, size.Height + BackdropPadding * 2);

                graphics.DrawString(label.Text, font,
                    label.Kind == IndexLabelKind.Face ? faceBrush : vertexBrush, left, top);
            }

            graphics.SmoothingMode = callersSmoothing;
        }
    }
}
