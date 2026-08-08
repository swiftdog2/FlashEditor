using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     Shows one sprite frame at a stated magnification, on the canvas it is placed within.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The list tile is for finding a sprite and this is for judging one, which is why it starts
    ///     at 1:1 and magnifies by whole numbers only. A thumbnail of a 400x200 sprite is not
    ///     something an edit can be checked against, and a fractionally scaled one is worse than
    ///     none because it invents pixels that are not in the file.
    ///     </para>
    ///     <para>
    ///     <b>Both rectangles are drawn.</b> A frame is a sub-rectangle of the set's canvas placed at
    ///     an offset, and it routinely does not reach the canvas edge - so cropping to the frame's
    ///     own pixels hides the offset, which is a stored field that an edit can get wrong. The
    ///     canvas is outlined solid, the frame within it dashed, and the picture drawn over the
    ///     checkerboard between them.
    ///     </para>
    /// </remarks>
    public sealed class SpriteCanvas : Panel {
        /// <summary>The largest magnification the zoom control offers.</summary>
        public const int MaximumZoom = 16;

        private static readonly Color Surround = Color.FromArgb(0xFF, 0x28, 0x28, 0x28);
        private static readonly Color CanvasEdge = Color.FromArgb(0xFF, 0x30, 0x30, 0x30);
        private static readonly Color FrameEdge = Color.FromArgb(0xFF, 0xE0, 0x30, 0x30);

        private Bitmap? picture;
        private Rectangle frame;
        private int zoom = 1;
        private bool outlineFrame = true;
        private string emptyText = "No sprite selected";

        /// <summary>Creates an empty canvas.</summary>
        public SpriteCanvas() {
            AutoScroll = true;
            BackColor = Surround;
            DoubleBuffered = true;
            //ResizeRedraw, because the checkerboard and the centring are both measured from the
            //client area and a resize would otherwise leave the previous layout painted.
            ResizeRedraw = true;
        }

        /// <summary>
        ///     The whole-number magnification the picture is drawn at.
        /// </summary>
        /// <remarks>
        ///     Whole numbers only. Anything else resamples pixel art into a picture the cache does
        ///     not contain, which is the one thing a preview used to check an edit must never do.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Zoom {
            get => zoom;
            set {
                int clamped = Math.Clamp(value, 1, MaximumZoom);
                if (clamped == zoom)
                    return;
                zoom = clamped;
                Rescale();
            }
        }

        /// <summary>Whether the frame's own sub-rectangle is outlined within the canvas.</summary>
        /// <remarks>
        ///     Settable because the outline sits on top of the picture, so judging a pixel at the
        ///     frame's edge means being able to take it away.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool OutlineFrame {
            get => outlineFrame;
            set {
                if (outlineFrame == value)
                    return;
                outlineFrame = value;
                Invalidate();
            }
        }

        /// <summary>What the pane says when it has nothing to draw.</summary>
        /// <remarks>
        ///     Stated by the caller rather than fixed, because "nothing selected" and "this set
        ///     stores no pixels" are different facts and only one of them is about the user.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string EmptyText {
            get => emptyText;
            set {
                emptyText = value ?? string.Empty;
                if (picture == null)
                    Invalidate();
            }
        }

        /// <summary>
        ///     Puts a rasterised frame on the canvas.
        /// </summary>
        /// <remarks>
        ///     The bitmap is owned by the caller and is only referenced here, so whoever built it has
        ///     to keep it alive until the next call and release it afterwards. The pane deliberately
        ///     does not take ownership: the same frame is on screen in the list at the same time.
        /// </remarks>
        /// <param name="canvasPicture">The frame drawn on its canvas, or null to clear.</param>
        /// <param name="frameWithinCanvas">The frame's stored sub-rectangle within that canvas.</param>
        public void ShowFrame(Bitmap? canvasPicture, Rectangle frameWithinCanvas) {
            picture = canvasPicture;
            frame = frameWithinCanvas;
            Rescale();
        }

        /// <summary>Paints the surround, the checkerboard, the picture and the two outlines.</summary>
        /// <param name="e">The paint data.</param>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            Graphics graphics = e.Graphics;

            if (picture == null) {
                using var ink = new SolidBrush(Color.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
                using var format = new StringFormat {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                graphics.DrawString(emptyText, Font, ink, ClientRectangle, format);
                return;
            }

            Rectangle drawn = DrawnBounds();

            //The checkerboard is painted under the picture only, not across the whole viewport, so
            //the canvas extent is visible before a single pixel has been drawn.
            SpritePainter.PaintCheckerboard(graphics, drawn, Math.Max(4, 4 * zoom));
            SpritePainter.DrawSprite(graphics, picture, drawn, true);

            using (var edge = new Pen(CanvasEdge))
                graphics.DrawRectangle(edge, drawn.X, drawn.Y, drawn.Width - 1, drawn.Height - 1);

            if (!outlineFrame || frame.Width <= 0 || frame.Height <= 0)
                return;

            //Dashed and in a colour no sprite palette entry can be confused with, because this is
            //an annotation over the artwork rather than part of it.
            using var framePen = new Pen(FrameEdge) { DashStyle = DashStyle.Dash };
            graphics.DrawRectangle(framePen,
                drawn.X + frame.X * zoom, drawn.Y + frame.Y * zoom,
                Math.Max(1, frame.Width * zoom - 1), Math.Max(1, frame.Height * zoom - 1));
        }

        /// <summary>
        ///     Where the magnified picture sits, centred while it is smaller than the viewport.
        /// </summary>
        /// <remarks>
        ///     <see cref="ScrollableControl.AutoScrollPosition"/> reads back negative, which is what
        ///     turns it into the offset a paint has to add.
        /// </remarks>
        /// <returns>The destination rectangle in client coordinates.</returns>
        private Rectangle DrawnBounds() {
            int width = picture!.Width * zoom;
            int height = picture.Height * zoom;

            int x = Math.Max(0, (ClientSize.Width - width) / 2) + AutoScrollPosition.X;
            int y = Math.Max(0, (ClientSize.Height - height) / 2) + AutoScrollPosition.Y;

            return new Rectangle(x, y, width, height);
        }

        private void Rescale() {
            AutoScrollMinSize = picture == null
                ? Size.Empty
                : new Size(picture.Width * zoom, picture.Height * zoom);
            Invalidate();
        }
    }
}
