using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     Draws an interface as the stored record describes it.
    /// </summary>
    /// <remarks>
    ///     <b>This is deliberately not what the game shows, and the surface says so.</b> The if3
    ///     format carries no per-state appearance at all: a component stores one colour, one sprite
    ///     id and one font, and hover, pressed and selected are produced entirely at runtime by CS2
    ///     scripts fired from twenty hook slots. Item counts, inventory contents and every dynamic
    ///     child are also runtime constructions the file knows nothing about. So a canvas rendering
    ///     the stored record alone shows a bank window with nothing selected, no item icons and no
    ///     counts - and that is what the format <i>is</i>, not a defect in the drawing.
    ///     <para>
    ///     <b>Models are not drawn, and are marked rather than skipped.</b> The only path to model
    ///     pixels in this application is OpenGL on the one UI-thread context; there is no CPU
    ///     rasteriser and no offscreen path. A type-6 component gets a hatched placeholder carrying
    ///     its model id, because an empty rectangle where the client draws a character reads as a
    ///     rendering failure and this is a stated limit.
    ///     </para>
    ///     <para>
    ///     <b>Every rectangle comes from <see cref="InterfaceLayoutResolver"/></b>, so what is drawn
    ///     here and what the layout tests assert are the same numbers. The canvas contributes the
    ///     paint and the selection, and no geometry of its own.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceCanvas : UserControl {
        /// <summary>The gap between the canvas edge and the interface box, in pixels.</summary>
        private const int CanvasInset = 12;

        private readonly Dictionary<int, InterfaceLayoutNode> resolved = new();
        private readonly List<int> drawOrder = new();

        private InterfaceComponentTree? tree;
        private IDefinitionThumbnailSource? thumbnails;
        private int selectedFileId = -1;
        private bool showNotDrawn;

        /// <summary>Creates an empty canvas.</summary>
        public InterfaceCanvas() {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(0x20, 0x20, 0x24);
            AutoScroll = true;
        }

        /// <summary>Raised when the user picks a component on the canvas.</summary>
        public event EventHandler<int>? ComponentPicked;

        /// <summary>The component the canvas is highlighting, or -1.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int SelectedFileId {
            get => selectedFileId;
            set {
                if (selectedFileId == value)
                    return;

                selectedFileId = value;
                Invalidate();
            }
        }

        /// <summary>
        ///     Whether components the client would never lay out are drawn as well.
        /// </summary>
        /// <remarks>
        ///     Off by default, because they are not part of the interface as the game builds it and
        ///     drawing them at the canvas origin would pile unrelated records on top of each other.
        ///     On, they are outlined in the warning colour so an author can see what the file holds
        ///     that the client ignores.
        /// </remarks>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowNotDrawn {
            get => showNotDrawn;
            set {
                if (showNotDrawn == value)
                    return;

                showNotDrawn = value;
                Invalidate();
            }
        }

        /// <summary>Where sprite tiles come from, or null to draw sprite ids as text.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IDefinitionThumbnailSource? Thumbnails {
            get => thumbnails;
            set {
                if (ReferenceEquals(thumbnails, value))
                    return;

                if (thumbnails != null)
                    thumbnails.TilesReady -= OnTilesReady;

                thumbnails = value;

                if (thumbnails != null)
                    thumbnails.TilesReady += OnTilesReady;

                Invalidate();
            }
        }

        /// <summary>Points the canvas at an interface, or clears it.</summary>
        /// <param name="componentTree">The interface's tree, or null to clear.</param>
        public void Show(InterfaceComponentTree? componentTree) {
            tree = componentTree;
            resolved.Clear();
            drawOrder.Clear();

            if (componentTree != null) {
                foreach (KeyValuePair<int, InterfaceLayoutNode> entry in
                         InterfaceLayoutResolver.ResolveGroup(componentTree, InterfaceRect.FixedModeCanvas)) {
                    resolved[entry.Key] = entry.Value;
                }

                //Paint order, which is a parent before its children in file-id order. Taken from the
                //tree rather than sorted here, so the canvas and the tree cannot disagree.
                foreach (int fileId in componentTree.InDrawOrder())
                    drawOrder.Add(fileId);
            }

            AutoScrollMinSize = componentTree == null
                ? Size.Empty
                : new Size(InterfaceRect.FixedModeCanvas.Width + CanvasInset * 2,
                    InterfaceRect.FixedModeCanvas.Height + CanvasInset * 2);

            Invalidate();
        }

        /// <summary>Frees evicted tiles before the frame is drawn, then paints.</summary>
        /// <param name="e">The paint data.</param>
        protected override void OnPaint(PaintEventArgs e) {
            thumbnails?.DrainRetired();
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.TranslateTransform(AutoScrollPosition.X + CanvasInset, AutoScrollPosition.Y + CanvasInset);

            InterfaceRect canvas = InterfaceRect.FixedModeCanvas;

            /* The client's fixed-mode viewport, drawn whether or not an interface is loaded, so the
               canvas reads as a screen the interface sits on rather than as empty space. */
            using (var screen = new SolidBrush(Color.FromArgb(0x10, 0x10, 0x14)))
                g.FillRectangle(screen, 0, 0, canvas.Width, canvas.Height);

            using (var edge = new Pen(EditorTheme.Separator(EditorSurface.Canvas)))
                g.DrawRectangle(edge, 0, 0, canvas.Width, canvas.Height);

            if (tree == null || drawOrder.Count == 0) {
                DrawCentredNote(g, canvas, "Select an interface to draw it");
                return;
            }

            foreach (int fileId in drawOrder) {
                if (resolved.TryGetValue(fileId, out InterfaceLayoutNode? node))
                    DrawComponent(g, node);
            }

            if (showNotDrawn)
                DrawTheOnesTheClientIgnores(g);

            DrawSelection(g);
        }

        /// <summary>Picks the topmost component under the pointer.</summary>
        /// <param name="e">The mouse data.</param>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Focus();

            int x = e.X - AutoScrollPosition.X - CanvasInset;
            int y = e.Y - AutoScrollPosition.Y - CanvasInset;

            /* Backwards through paint order, because the last thing drawn is the thing on top and
               that is what a click means. A layer is skipped unless nothing above it was hit: a
               layer is a container, and picking one whenever the pointer is inside it would make
               its children unselectable. */
            int layerHit = -1;

            for (int i = drawOrder.Count - 1; i >= 0; i--) {
                if (!resolved.TryGetValue(drawOrder[i], out InterfaceLayoutNode? node))
                    continue;

                InterfaceRect box = node.Absolute;
                if (x < box.X || y < box.Y || x >= box.Right || y >= box.Bottom)
                    continue;

                if (node.Component.ComponentType == 0) {
                    if (layerHit < 0)
                        layerHit = drawOrder[i];
                    continue;
                }

                Select(drawOrder[i]);
                return;
            }

            Select(layerHit);
        }

        private void Select(int fileId) {
            SelectedFileId = fileId;
            if (fileId >= 0)
                ComponentPicked?.Invoke(this, fileId);
        }

        private void DrawComponent(Graphics g, InterfaceLayoutNode node) {
            InterfaceComponentDefinition component = node.Component;
            InterfaceRect box = node.Absolute;

            if (box.Width <= 0 || box.Height <= 0)
                return;

            var rectangle = new Rectangle(box.X, box.Y, box.Width, box.Height);

            switch (component.ComponentType) {
                case 0:
                    //A layer draws nothing of its own. Its outline is the only way to see the
                    //structure the tree describes, so it is drawn faintly rather than not at all.
                    using (var pen = new Pen(Color.FromArgb(60, 0xFF, 0xFF, 0xFF)) {
                        DashStyle = DashStyle.Dot
                    }) {
                        g.DrawRectangle(pen, rectangle.X, rectangle.Y,
                            rectangle.Width - 1, rectangle.Height - 1);
                    }
                    break;

                case 3:
                    DrawRectangleComponent(g, component, rectangle);
                    break;

                case 4:
                    DrawTextComponent(g, component, rectangle);
                    break;

                case 5:
                    DrawSpriteComponent(g, component, rectangle);
                    break;

                case 6:
                    DrawModelPlaceholder(g, component, rectangle);
                    break;

                case 9:
                    DrawLineComponent(g, component, rectangle);
                    break;
            }
        }

        /// <summary>
        ///     A filled or outlined rectangle in the component's colour.
        /// </summary>
        /// <remarks>
        ///     <c>Transparency</c> is inverted in this format - 0 is opaque - because the client
        ///     builds the pixel as <c>((255 - alpha) &lt;&lt; 24) | colour</c>
        ///     (<c>Node_Sub10_Sub24.java:443-449</c>). Reading it as an ordinary alpha would draw
        ///     every opaque rectangle invisible and every invisible one solid, which is the kind of
        ///     inversion that looks like a working renderer until someone checks it against the game.
        /// </remarks>
        private static void DrawRectangleComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            Color colour = ColourOf(component.Colour, component.Transparency);

            if (component.RectangleFilled) {
                using var brush = new SolidBrush(colour);
                g.FillRectangle(brush, rectangle);
            }
            else {
                using var pen = new Pen(colour);
                g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
            }
        }

        /// <summary>
        ///     The component's stored text, in a substitute font.
        /// </summary>
        /// <remarks>
        ///     <b>Not the cache's own glyphs.</b> The font id names an index-13 metric record paired
        ///     with an index-8 glyph sheet, and drawing text properly means laying it out through
        ///     that pair. Until it does, this is a stand-in that gets the string, the colour and the
        ///     alignment right and the letterforms wrong, and the canvas note says so - a preview
        ///     that silently used the wrong typeface would be read as the game's.
        /// </remarks>
        private static void DrawTextComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            string text = component.Message.Text;
            if (string.IsNullOrEmpty(text))
                return;

            using var brush = new SolidBrush(ColourOf(component.Colour, component.Transparency));
            using var format = new StringFormat {
                Alignment = component.HorizontalAlignment switch {
                    1 => StringAlignment.Center,
                    2 => StringAlignment.Far,
                    _ => StringAlignment.Near
                },
                LineAlignment = component.VerticalAlignment switch {
                    1 => StringAlignment.Center,
                    2 => StringAlignment.Far,
                    _ => StringAlignment.Near
                },
                FormatFlags = StringFormatFlags.NoClip
            };

            g.DrawString(text, EditorTheme.UiFont, brush, rectangle, format);
        }

        private void DrawSpriteComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            if (component.SpriteId >= 0) {
                Bitmap? tile = thumbnails?.TryGet(RSConstants.SPRITES_INDEX, component.SpriteId,
                    Math.Max(8, Math.Min(rectangle.Width, rectangle.Height)));

                if (tile != null) {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(tile, rectangle);
                    return;
                }
            }

            //An outline and the id while the tile is being read, or when there is no source. A blank
            //box would read as a sprite that failed to decode.
            using var pen = new Pen(Color.FromArgb(120, 0x78, 0xC8, 0xFF));
            g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
            DrawTinyLabel(g, rectangle, component.SpriteId < 0 ? "no sprite" : component.SpriteId.ToString());
        }

        /// <summary>A hatched box carrying the model id, because a model cannot be drawn here.</summary>
        private static void DrawModelPlaceholder(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal,
                Color.FromArgb(40, 0xFF, 0xB8, 0x26), Color.Transparent)) {
                g.FillRectangle(hatch, rectangle);
            }

            using var pen = new Pen(Color.FromArgb(140, 0xFF, 0xB8, 0x26));
            g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
            DrawTinyLabel(g, rectangle, "model " + component.RawModelId);
        }

        /// <summary>
        ///     A line between two corners of the component's box.
        /// </summary>
        /// <remarks>
        ///     Which two corners is what <c>LineFlipped</c> selects: normally top-left to
        ///     bottom-right, flipped bottom-left to top-right
        ///     (<c>Node_Sub10_Sub24.java:881-897</c>). The endpoint is inclusive, which is also why
        ///     the resolver gives a type-9 component a clip one pixel larger than its rectangle.
        /// </remarks>
        private static void DrawLineComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            using var pen = new Pen(ColourOf(component.Colour, 0), Math.Max(1, component.LineWidth));

            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (component.LineFlipped)
                g.DrawLine(pen, rectangle.Left, rectangle.Bottom, rectangle.Right, rectangle.Top);
            else
                g.DrawLine(pen, rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

            g.SmoothingMode = previous;
        }

        private void DrawTheOnesTheClientIgnores(Graphics g) {
            if (tree == null)
                return;

            using var pen = new Pen(EditorTheme.Accent(EditorSurface.Canvas)) { DashStyle = DashStyle.Dash };

            foreach (KeyValuePair<int, InterfaceLayoutNode> entry in resolved) {
                if (entry.Value.IsDrawn)
                    continue;

                InterfaceRect box = entry.Value.Absolute;
                if (box.Width <= 0 || box.Height <= 0)
                    continue;

                g.DrawRectangle(pen, box.X, box.Y, box.Width - 1, box.Height - 1);
            }
        }

        private void DrawSelection(Graphics g) {
            if (selectedFileId < 0 || !resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? node))
                return;

            InterfaceRect box = node.Absolute;
            var rectangle = new Rectangle(box.X - 1, box.Y - 1,
                Math.Max(1, box.Width) + 1, Math.Max(1, box.Height) + 1);

            //Two pens, light over dark, so the marquee is visible over both a bright sprite and the
            //near-black backdrop. One colour is invisible against something in this cache.
            using (var under = new Pen(Color.FromArgb(190, 0x06, 0x08, 0x10), 3f))
                g.DrawRectangle(under, rectangle);

            using var over = new Pen(EditorTheme.Accent(EditorSurface.Canvas)) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(over, rectangle);
        }

        private static void DrawTinyLabel(Graphics g, Rectangle rectangle, string text) {
            if (rectangle.Width < 24 || rectangle.Height < 10)
                return;

            using var brush = new SolidBrush(Color.FromArgb(190, 0xE6, 0xE6, 0xE6));
            using var format = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            };

            g.DrawString(text, EditorTheme.NoticeFont, brush, rectangle, format);
        }

        private static void DrawCentredNote(Graphics g, InterfaceRect canvas, string text) {
            using var brush = new SolidBrush(EditorTheme.InkMuted(EditorSurface.Canvas));
            using var format = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            g.DrawString(text, EditorTheme.UiFont, brush,
                new Rectangle(0, 0, canvas.Width, canvas.Height), format);
        }

        /// <summary>
        ///     A stored colour and its inverted transparency as a drawable colour.
        /// </summary>
        /// <remarks>
        ///     <b>0 means opaque</b>, per <c>Node_Sub10_Sub24.java:443-449</c>. The alpha is
        ///     <c>255 - transparency</c>, and the stored colour carries no alpha of its own.
        /// </remarks>
        private static Color ColourOf(int packedRgb, int transparency) {
            int alpha = 255 - (transparency & 0xFF);

            return Color.FromArgb(alpha, (packedRgb >> 16) & 0xFF, (packedRgb >> 8) & 0xFF, packedRgb & 0xFF);
        }

        private void OnTilesReady(object? sender, EventArgs e) {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
                BeginInvoke(new Action(Invalidate));
            else
                Invalidate();
        }

        /// <summary>Detaches from the thumbnail source so a closed tab does not keep it alive.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing && thumbnails != null)
                thumbnails.TilesReady -= OnTilesReady;

            base.Dispose(disposing);
        }
    }
}
