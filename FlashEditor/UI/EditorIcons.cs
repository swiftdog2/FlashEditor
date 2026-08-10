using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace FlashEditor.UI {
    /// <summary>
    ///     Draws the editor's line icons, tinted, and remembers what it has drawn.
    /// </summary>
    /// <remarks>
    ///     <b>Why the icons are drawn rather than shipped.</b> The brief is monochrome line icons
    ///     <i>tinted from the theme</i>, and tinting is the one thing a raster cannot do without a
    ///     second asset. The application has two chrome surfaces
    ///     (<see cref="EditorSurface"/>), so shipping pictures would mean two files per icon and a
    ///     third for anything drawn over cache content. It would also reintroduce the
    ///     runtime-missing-file failure the shader glob in the csproj exists to prevent: a picture
    ///     that fails to copy is an exception when a tab opens, where a missing painter here is a
    ///     compile error.
    ///     <para>
    ///     <b>Drawing them does not buy sharpness, and nobody should think it does.</b> The process
    ///     is pinned DPI-unaware (<c>FlashEditorForm.cs:46</c>), so on a scaled display Windows
    ///     bitmap-stretches the whole window after it is painted. A vector drawn at request is
    ///     stretched exactly as hard as a raster would be. The reason to draw is recolourability.
    ///     </para>
    ///     <para>
    ///     <b>Threading.</b> <see cref="Render(EditorIcon, Color, int)"/> touches no control and no
    ///     <c>ImageList</c>, so it is safe from a worker - the shape
    ///     <c>SpritePainter.RenderTile</c> already uses. The cache is locked because two workers
    ///     decoding different indexes can both ask for the same icon.
    ///     </para>
    ///     <para>
    ///     <b>The pixel grid is the craft risk at this size.</b> Axis-aligned strokes are drawn with
    ///     <see cref="SmoothingMode.None"/> so a 1px line lands on one row of pixels rather than
    ///     two grey ones; diagonals and curves are drawn with <see cref="SmoothingMode.AntiAlias"/>
    ///     because unsmoothed they read as staircases. Mixing the two within one icon is deliberate
    ///     and normal. Nothing in the test suite can see any of this - it is an eyeball check at
    ///     native resolution, and <c>CLAUDE.md</c> is explicit that a downscaled screenshot is not
    ///     evidence about a small control.
    ///     </para>
    /// </remarks>
    public static class EditorIcons {
        /// <summary>The grid every painter draws on, before any scaling.</summary>
        /// <remarks>
        ///     Painters are written against a fixed 16x16 box so their coordinates can be read as
        ///     pixels. A request for another side scales that box rather than each painter carrying
        ///     its own arithmetic, which is what keeps stroke weights consistent across the set.
        /// </remarks>
        private const int DesignSide = 16;

        private static readonly Dictionary<(EditorIcon Icon, int Argb, int Side), Image> Cache = new();
        private static readonly object CacheLock = new();

        static EditorIcons() {
            //A theme change re-tints everything, so nothing already drawn is still correct.
            EditorTheme.Changed += (_, _) => Invalidate();
        }

        /// <summary>
        ///     The icon at the given side, in the given ink.
        /// </summary>
        /// <remarks>
        ///     The returned image is <b>shared and cached</b>. Callers must not dispose it and must
        ///     not draw into it. That is the opposite of the usual GDI contract and it is deliberate:
        ///     a tool strip repaints on every mouse move, and rendering thirty icons per paint to
        ///     hand each one to a caller that disposes it would be the whole cost of the strip.
        /// </remarks>
        /// <param name="icon">Which icon.</param>
        /// <param name="ink">The colour to draw it in.</param>
        /// <param name="side">The side in pixels.</param>
        /// <returns>The rendered icon, owned by this class.</returns>
        public static Image Render(EditorIcon icon, Color ink, int side) {
            if (side <= 0)
                throw new ArgumentOutOfRangeException(nameof(side), side, "An icon needs a positive side.");

            var key = (icon, ink.ToArgb(), side);

            lock (CacheLock) {
                if (Cache.TryGetValue(key, out Image? cached))
                    return cached;

                var bitmap = new Bitmap(side, side);
                using (Graphics graphics = Graphics.FromImage(bitmap)) {
                    graphics.Clear(Color.Transparent);
                    Draw(graphics, icon, new Rectangle(0, 0, side, side), ink);
                }

                Cache[key] = bitmap;
                return bitmap;
            }
        }

        /// <summary>The icon tinted for a surface, at <see cref="EditorTheme.IconSide"/>.</summary>
        /// <param name="icon">Which icon.</param>
        /// <param name="surface">The surface it will sit on.</param>
        /// <returns>The rendered icon, owned by this class.</returns>
        public static Image Render(EditorIcon icon, EditorSurface surface) {
            return Render(icon, EditorTheme.Ink(surface), EditorTheme.IconSide);
        }

        /// <summary>
        ///     Paints an icon straight onto a surface, for one drawn over cache content.
        /// </summary>
        /// <remarks>
        ///     Uncached, because an icon over content is drawn once per frame at a position that
        ///     moves. Anything on chrome should go through <see cref="Render(EditorIcon, Color, int)"/>.
        /// </remarks>
        /// <param name="graphics">Where to draw.</param>
        /// <param name="icon">Which icon.</param>
        /// <param name="bounds">The box to draw it in.</param>
        /// <param name="ink">The colour to draw it in.</param>
        public static void Draw(Graphics graphics, EditorIcon icon, Rectangle bounds, Color ink) {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            GraphicsState state = graphics.Save();
            try {
                graphics.TranslateTransform(bounds.X, bounds.Y);

                float scale = Math.Min(bounds.Width, bounds.Height) / (float) DesignSide;
                if (Math.Abs(scale - 1f) > 0.001f)
                    graphics.ScaleTransform(scale, scale);

                using var pen = new Pen(ink, 1f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
                using var brush = new SolidBrush(ink);

                Paint(icon, graphics, pen, brush);
            }
            finally {
                graphics.Restore(state);
            }
        }

        /// <summary>Drops every rendered icon, for a theme change.</summary>
        public static void Invalidate() {
            lock (CacheLock) {
                foreach (Image image in Cache.Values)
                    image.Dispose();

                Cache.Clear();
            }
        }

        /// <summary>
        ///     Draws one icon on the 16x16 design grid.
        /// </summary>
        /// <remarks>
        ///     Every painter is built from the primitives below rather than from raw
        ///     <see cref="Graphics"/> calls. That is what makes thirty icons read as one set: the
        ///     stroke weight, the inset and the corner treatment are stated once each and cannot
        ///     drift per icon.
        /// </remarks>
        /// <param name="icon">Which icon.</param>
        /// <param name="g">The surface, already translated and scaled to the design grid.</param>
        /// <param name="pen">A 1px pen in the ink.</param>
        /// <param name="brush">A solid brush in the ink.</param>
        private static void Paint(EditorIcon icon, Graphics g, Pen pen, SolidBrush brush) {
            switch (icon) {
                case EditorIcon.Back:
                    Chevron(g, pen, 9.5f, 4f, 6f, 8f, 9.5f, 12f);
                    Line(g, pen, 6, 8, 13, 8);
                    break;

                case EditorIcon.Forward:
                    Chevron(g, pen, 6.5f, 4f, 10f, 8f, 6.5f, 12f);
                    Line(g, pen, 3, 8, 10, 8);
                    break;

                case EditorIcon.Link:
                    //A box with an arrow leaving it: "this id names something elsewhere".
                    Line(g, pen, 2, 6, 2, 13);
                    Line(g, pen, 2, 13, 9, 13);
                    Line(g, pen, 9, 13, 9, 9);
                    Line(g, pen, 2, 6, 6, 6);
                    Smooth(g, () => {
                        g.DrawLine(pen, 7f, 8.5f, 13f, 2.5f);
                        Arrowhead(g, pen, 13f, 2.5f, 1f, -1f);
                    });
                    break;

                case EditorIcon.Search:
                    Lens(g, pen);
                    break;

                case EditorIcon.Info:
                    Smooth(g, () => g.DrawEllipse(pen, 1.5f, 1.5f, 13f, 13f));
                    Dot(g, brush, 8, 4);
                    Line(g, pen, 8, 7, 8, 12);
                    break;

                case EditorIcon.Warning:
                    Smooth(g, () => {
                        g.DrawPolygon(pen, new[] {
                            new PointF(8f, 1.5f), new PointF(15f, 14f), new PointF(1f, 14f)
                        });
                    });
                    Line(g, pen, 8, 6, 8, 10);
                    Dot(g, brush, 8, 12);
                    break;

                case EditorIcon.Expand:
                    //Points right: a tree node whose children are not shown.
                    Chevron(g, pen, 6f, 4f, 10f, 8f, 6f, 12f);
                    break;

                case EditorIcon.Collapse:
                    //The same chevron rotated a quarter turn, so the pair reads as one control.
                    Chevron(g, pen, 4f, 6f, 8f, 10f, 12f, 6f);
                    break;

                case EditorIcon.Refresh:
                    Smooth(g, () => {
                        /* The gap is on the right, spanning 3 o'clock. The head sits at the 320
                           degree end and points along the direction of travel there, which for a
                           clockwise sweep is down and to the right. */
                        g.DrawArc(pen, 2.5f, 2.5f, 11f, 11f, 40f, 280f);
                        Arrowhead(g, pen, 12.2f, 4.5f, 0.64f, 0.77f);
                    });
                    break;

                case EditorIcon.Add:
                    Line(g, pen, 8, 3, 8, 12);
                    Line(g, pen, 3, 8, 12, 8);
                    break;

                case EditorIcon.Remove:
                    Line(g, pen, 3, 8, 12, 8);
                    break;

                case EditorIcon.Duplicate:
                    Box(g, pen, 2, 2, 9, 9);
                    Box(g, pen, 5, 5, 9, 9);
                    break;

                case EditorIcon.MoveUp:
                    Line(g, pen, 8, 3, 8, 13);
                    Smooth(g, () => {
                        Arrowhead(g, pen, 8f, 2.5f, 0f, -1f);
                    });
                    break;

                case EditorIcon.MoveDown:
                    Line(g, pen, 8, 2, 8, 12);
                    Smooth(g, () => {
                        Arrowhead(g, pen, 8f, 13.5f, 0f, 1f);
                    });
                    break;

                /* Undo and Redo are one shape mirrored: the top half of a circle, a short tail
                   dropping from the end the stroke starts at, and the head on the other tip
                   pointing down into the gap. Drawn as a pair on purpose - they sit next to each
                   other on every toolbar that has them, and two independently drawn curves at
                   16px read as two unrelated squiggles. */
                case EditorIcon.Undo:
                    Smooth(g, () => {
                        g.DrawArc(pen, 3.5f, 5f, 9f, 9f, 180f, 180f);
                        g.DrawLine(pen, 12.5f, 9.5f, 12.5f, 12.5f);
                        Arrowhead(g, pen, 3.5f, 9.5f, 0f, 1f);
                    });
                    break;

                case EditorIcon.Redo:
                    Smooth(g, () => {
                        g.DrawArc(pen, 3.5f, 5f, 9f, 9f, 180f, 180f);
                        g.DrawLine(pen, 3.5f, 9.5f, 3.5f, 12.5f);
                        Arrowhead(g, pen, 12.5f, 9.5f, 0f, 1f);
                    });
                    break;

                case EditorIcon.Colour:
                    //A filled swatch with an outline, because an unfilled one reads as "image".
                    g.FillRectangle(brush, 3, 3, 10, 10);
                    break;

                case EditorIcon.Image:
                    Box(g, pen, 2, 3, 12, 10);
                    Dot(g, brush, 5, 6);
                    Smooth(g, () => {
                        g.DrawLines(pen, new[] {
                            new PointF(3f, 12f), new PointF(6.5f, 8.5f),
                            new PointF(9f, 11f), new PointF(11.5f, 8.5f), new PointF(13f, 10f)
                        });
                    });
                    break;

                case EditorIcon.Model:
                    //A wireframe cube in isometric, which is how the viewer presents one.
                    Smooth(g, () => {
                        g.DrawPolygon(pen, new[] {
                            new PointF(8f, 1.5f), new PointF(14f, 5f),
                            new PointF(14f, 11f), new PointF(8f, 14.5f),
                            new PointF(2f, 11f), new PointF(2f, 5f)
                        });
                        g.DrawLine(pen, 2f, 5f, 8f, 8.5f);
                        g.DrawLine(pen, 14f, 5f, 8f, 8.5f);
                        g.DrawLine(pen, 8f, 8.5f, 8f, 14.5f);
                    });
                    break;

                case EditorIcon.Font:
                    //A serif A, drawn as strokes rather than set as text, so it does not depend on
                    //a font being installed and does not change shape with the system's.
                    Smooth(g, () => {
                        g.DrawLine(pen, 3f, 13f, 8f, 3f);
                        g.DrawLine(pen, 8f, 3f, 13f, 13f);
                    });
                    Line(g, pen, 5, 10, 11, 10);
                    break;

                case EditorIcon.Animation:
                    //Frames on a strip, the timeline the animation tab wants to become.
                    Box(g, pen, 1, 4, 14, 8);
                    Line(g, pen, 6, 4, 6, 12);
                    Line(g, pen, 10, 4, 10, 12);
                    break;

                case EditorIcon.Sound:
                    Line(g, pen, 3, 6, 3, 10);
                    Line(g, pen, 6, 3, 6, 13);
                    Line(g, pen, 9, 5, 9, 11);
                    Line(g, pen, 12, 7, 12, 9);
                    break;

                case EditorIcon.Texture:
                    /* A hatched swatch, not a grid. A grid is what Grid means, and a texture drawn
                       as one sat two cells from it on the contact sheet and read as the same
                       thing. Hatching says "a surface with a pattern on it", which is what a
                       texture slot is. */
                    Box(g, pen, 2, 2, 12, 12);
                    Smooth(g, () => {
                        g.DrawLine(pen, 3f, 9f, 9f, 3f);
                        g.DrawLine(pen, 3f, 13f, 13f, 3f);
                        g.DrawLine(pen, 7f, 13f, 13f, 7f);
                    });
                    break;

                case EditorIcon.Script:
                    Box(g, pen, 3, 2, 10, 12);
                    Line(g, pen, 5, 5, 10, 5);
                    Line(g, pen, 5, 8, 10, 8);
                    Line(g, pen, 5, 11, 8, 11);
                    break;

                /* The lens is centred on (6.5, 6.5), so the glyph inside it is drawn on 6 and runs
                   4..9 - five pixels each way. The first draft used a two and three pixel cross
                   and it was unreadable at 16px against the lens outline around it. */
                case EditorIcon.ZoomIn:
                    Lens(g, pen);
                    Line(g, pen, 4, 6, 9, 6);
                    Line(g, pen, 6, 4, 6, 9);
                    break;

                case EditorIcon.ZoomOut:
                    Lens(g, pen);
                    Line(g, pen, 4, 6, 9, 6);
                    break;

                case EditorIcon.Grid:
                    Box(g, pen, 2, 2, 12, 12);
                    Line(g, pen, 6, 2, 6, 13);
                    Line(g, pen, 10, 2, 10, 13);
                    Line(g, pen, 2, 6, 13, 6);
                    Line(g, pen, 2, 10, 13, 10);
                    break;

                case EditorIcon.Visible:
                    Eye(g, pen);
                    Smooth(g, () => g.DrawEllipse(pen, 6f, 6f, 4f, 4f));
                    break;

                case EditorIcon.Hidden:
                    Eye(g, pen);
                    Smooth(g, () => g.DrawLine(pen, 2.5f, 13.5f, 13.5f, 2.5f));
                    break;

                case EditorIcon.Pointer:
                    Smooth(g, () => {
                        g.FillPolygon(brush, new[] {
                            new PointF(4f, 2f), new PointF(4f, 13f), new PointF(7f, 10f),
                            new PointF(9f, 14f), new PointF(11f, 13f), new PointF(9f, 9.2f),
                            new PointF(12.5f, 9f)
                        });
                    });
                    break;

                case EditorIcon.Move:
                    Line(g, pen, 8, 2, 8, 13);
                    Line(g, pen, 2, 8, 13, 8);
                    Smooth(g, () => {
                        Arrowhead(g, pen, 8f, 1.5f, 0f, -1f);
                        Arrowhead(g, pen, 8f, 13.5f, 0f, 1f);
                        Arrowhead(g, pen, 1.5f, 8f, -1f, 0f);
                        Arrowhead(g, pen, 13.5f, 8f, 1f, 0f);
                    });
                    break;

                case EditorIcon.Resize:
                    Box(g, pen, 2, 2, 8, 8);
                    Smooth(g, () => {
                        g.DrawLine(pen, 8f, 8f, 13.5f, 13.5f);
                        Arrowhead(g, pen, 13.5f, 13.5f, 1f, 1f);
                    });
                    break;

                case EditorIcon.Eyedropper:
                    /* A tapering barrel to a point at the lower left, with a solid bulb at the
                       upper right. The first draft outlined the bulb as a diamond and the whole
                       thing read as a hammer. Filling it is what makes it a dropper. */
                    Smooth(g, () => {
                        g.FillPolygon(brush, new[] {
                            new PointF(10f, 2f), new PointF(14f, 6f),
                            new PointF(11.5f, 8.5f), new PointF(7.5f, 4.5f)
                        });
                        g.DrawLine(pen, 8.5f, 5.5f, 3f, 11f);
                        g.DrawLine(pen, 10.5f, 7.5f, 5f, 13f);
                        g.DrawLine(pen, 3f, 11f, 5f, 13f);
                        g.DrawLine(pen, 3f, 11f, 1.8f, 14.2f);
                        g.DrawLine(pen, 5f, 13f, 1.8f, 14.2f);
                    });
                    break;

                /* The transport four are the only solid glyphs in the set apart from the pointer
                   and the dropper bulb, and that is deliberate rather than a lapse. Outlined at
                   16px a play triangle is a 1px wireframe arrow that reads as Forward, which is
                   already in the set two rows away; filled, it reads as a play button on sight.
                   The same argument makes the pause bars solid: two 1px rectangles side by side
                   are four vertical lines, which is Sound. */

                case EditorIcon.Play:
                    /* Back edge on 5 rather than on 4.5, and the same goes for every coordinate in
                       these four. A vertical edge on a half pixel is anti-aliased into a column of
                       50% grey, which at 16px is a quarter of the glyph's width spent looking
                       smudged - the first draft put all four back edges on halves and the contact
                       sheet showed a grey seam down each one. Only the sloped edges need the
                       smoothing. */
                    PlayHead(g, brush, 5f, 13f, 3f, 13f);
                    break;

                case EditorIcon.Pause:
                    /* Two on, two off, two on. A 1px gap closes up against the anti-aliased edges
                       either side of it and the pair reads as one thick bar. */
                    Bar(g, brush, 5, 3, 2, 10);
                    Bar(g, brush, 9, 3, 2, 10);
                    break;

                /* Previous and next are one shape mirrored about x = 8, for the reason Undo and
                   Redo are: they sit next to each other on the same strip, and two independently
                   placed pairs read as two unrelated marks.
                   The trailing head's tip lands exactly on the leading head's back edge rather than
                   half a pixel short of it. Short of it, the sliver between them anti-aliases to a
                   grey column and the pair reads as one blob with a crease; touching, the notch is
                   cut by the sloped edges themselves and stays a notch at 16px. */
                case EditorIcon.PreviousTrack:
                    PlayHead(g, brush, 8f, 3f, 3f, 13f);
                    PlayHead(g, brush, 13f, 8f, 3f, 13f);
                    break;

                case EditorIcon.NextTrack:
                    PlayHead(g, brush, 8f, 13f, 3f, 13f);
                    PlayHead(g, brush, 3f, 8f, 3f, 13f);
                    break;

                default:
                    //A named icon with no painter draws a box rather than nothing, so the gap is
                    //visible on screen instead of showing as a tool that lost its picture.
                    Box(g, pen, 2, 2, 12, 12);
                    break;
            }
        }

        /// <summary>
        ///     An axis-aligned 1px stroke, landing on exactly one row or column of pixels.
        /// </summary>
        /// <remarks>
        ///     The half-pixel offset is the whole point. GDI+ centres a 1px pen on the coordinate,
        ///     so a line at an integer y covers half of row y-1 and half of row y and renders as two
        ///     grey rows; at y + 0.5 it covers row y alone. Smoothing is forced off for the same
        ///     reason.
        /// </remarks>
        private static void Line(Graphics g, Pen pen, int x0, int y0, int x1, int y1) {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            g.DrawLine(pen, x0 + 0.5f, y0 + 0.5f, x1 + 0.5f, y1 + 0.5f);
            g.SmoothingMode = previous;
        }

        /// <summary>A 1px rectangle outline on the pixel grid.</summary>
        private static void Box(Graphics g, Pen pen, int x, int y, int width, int height) {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(pen, x + 0.5f, y + 0.5f, width - 1f, height - 1f);
            g.SmoothingMode = previous;
        }

        /// <summary>A 2x2 mark, which is the smallest dot that reads as deliberate at this size.</summary>
        private static void Dot(Graphics g, Brush brush, int x, int y) {
            Bar(g, brush, x, y, 2, 2);
        }

        /// <summary>
        ///     A solid axis-aligned block on the pixel grid.
        /// </summary>
        /// <remarks>
        ///     Integer coordinates and no half-pixel offset, which is the opposite of
        ///     <see cref="Line"/> and correct for the same reason: a 1px pen is centred on its
        ///     coordinate and a fill is not, so a fill from x to x + width covers exactly those
        ///     columns. Smoothing is forced off so the edges stay on the grid.
        /// </remarks>
        private static void Bar(Graphics g, Brush brush, int x, int y, int width, int height) {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(brush, x, y, width, height);
            g.SmoothingMode = previous;
        }

        /// <summary>
        ///     A solid triangle pointing left or right, the transport set's one shape.
        /// </summary>
        /// <remarks>
        ///     Stated as the flat edge's x, the tip's x and the two ends of the flat edge, for the
        ///     reason <see cref="Chevron"/> takes three explicit points: a form taking an origin
        ///     and a signed reach lets a caller silently draw the mirror of what it meant, and a
        ///     next button pointing backwards is the kind of defect that ships. Here the direction
        ///     is not a sign but a consequence of which of two named coordinates is larger, so a
        ///     caller cannot state it and mean the other thing.
        /// </remarks>
        /// <param name="g">The surface.</param>
        /// <param name="brush">The ink.</param>
        /// <param name="backX">Where the flat edge stands.</param>
        /// <param name="tipX">Where the point is; left or right of <paramref name="backX"/>.</param>
        /// <param name="top">The flat edge's upper end.</param>
        /// <param name="bottom">The flat edge's lower end.</param>
        private static void PlayHead(Graphics g, Brush brush,
            float backX, float tipX, float top, float bottom) {
            Smooth(g, () => g.FillPolygon(brush, new[] {
                new PointF(backX, top),
                new PointF(tipX, (top + bottom) / 2f),
                new PointF(backX, bottom)
            }));
        }

        /// <summary>
        ///     A chevron through three explicit points.
        /// </summary>
        /// <remarks>
        ///     Stated as three points rather than as an origin plus a signed reach and spread. The
        ///     signed form was written first and produced a chevron whose arms ran off the left of
        ///     the design grid when the spread was negative, which rendered as a tick and shipped
        ///     as far as the first contact sheet. Three points cannot be got wrong silently: they
        ///     are legible as coordinates in the caller.
        /// </remarks>
        private static void Chevron(Graphics g, Pen pen,
            float x0, float y0, float x1, float y1, float x2, float y2) {
            Smooth(g, () => g.DrawLines(pen, new[] {
                new PointF(x0, y0), new PointF(x1, y1), new PointF(x2, y2)
            }));
        }

        /// <summary>
        ///     A magnifier: the lens and its handle, shared by find and the two zooms.
        /// </summary>
        /// <remarks>
        ///     One shape for all three so that a glyph placed inside the lens is the only thing
        ///     that distinguishes them, which is what makes the three read as a family.
        /// </remarks>
        private static void Lens(Graphics g, Pen pen) {
            Smooth(g, () => {
                g.DrawEllipse(pen, 2.5f, 2.5f, 8f, 8f);
                g.DrawLine(pen, 10.5f, 10.5f, 13.5f, 13.5f);
            });
        }

        /// <summary>
        ///     The eye outline shared by the shown and hidden marks.
        /// </summary>
        /// <remarks>
        ///     Two lid curves meeting at a point on each side, which is what makes an eye rather
        ///     than a circle. Both earlier attempts failed in an instructive way and are recorded
        ///     so a third does not repeat them: <c>DrawClosedCurve</c> at tension zero is a
        ///     polygon and drew a diamond; two arcs sharing one bounding box are two halves of the
        ///     same circle and drew a ring. A lens needs two arcs of circles whose centres are
        ///     apart, and expressing that as curves through three points is the legible way to say
        ///     it.
        /// </remarks>
        private static void Eye(Graphics g, Pen pen) {
            Smooth(g, () => {
                g.DrawCurve(pen, new[] {
                    new PointF(1.5f, 8f), new PointF(8f, 3.2f), new PointF(14.5f, 8f)
                }, 0.7f);
                g.DrawCurve(pen, new[] {
                    new PointF(1.5f, 8f), new PointF(8f, 12.8f), new PointF(14.5f, 8f)
                }, 0.7f);
            });
        }

        /// <summary>
        ///     A small open arrowhead at a point, opening away from a direction.
        /// </summary>
        /// <remarks>
        ///     Two strokes rather than a filled triangle. A filled head at 16px turns into a blob
        ///     three pixels across and stops matching the 1px strokes it terminates.
        /// </remarks>
        private static void Arrowhead(Graphics g, Pen pen, float x, float y, float dx, float dy) {
            /* 4.0, not the 3.2 the first draft used. At 3.2 the barbs were three pixels long and
               disappeared into the stroke they terminate, so the undo and redo arcs read as plain
               humps with no direction at all. */
            const float Reach = 4f;

            float length = (float) Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0f)
                return;

            dx /= length;
            dy /= length;

            //The two barbs are the direction rotated by plus and minus 40 degrees, reversed.
            const double Angle = 40.0 * Math.PI / 180.0;
            float cos = (float) Math.Cos(Angle);
            float sin = (float) Math.Sin(Angle);

            float leftX = -(dx * cos - dy * sin);
            float leftY = -(dx * sin + dy * cos);
            float rightX = -(dx * cos + dy * sin);
            float rightY = -(-dx * sin + dy * cos);

            g.DrawLine(pen, x, y, x + leftX * Reach, y + leftY * Reach);
            g.DrawLine(pen, x, y, x + rightX * Reach, y + rightY * Reach);
        }

        /// <summary>Runs a drawing action with anti-aliasing on, and puts the mode back.</summary>
        private static void Smooth(Graphics g, Action draw) {
            SmoothingMode previousSmoothing = g.SmoothingMode;
            PixelOffsetMode previousOffset = g.PixelOffsetMode;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            try {
                draw();
            }
            finally {
                g.SmoothingMode = previousSmoothing;
                g.PixelOffsetMode = previousOffset;
            }
        }
    }
}
