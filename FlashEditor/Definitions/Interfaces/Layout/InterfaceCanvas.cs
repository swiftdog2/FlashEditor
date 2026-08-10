using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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

        /* The side asked of the thumbnail cache for a sprite. The canvas's sprite renderer is the
           uncomposited one, which returns the frame at its own size and ignores this entirely, so
           the value only has to be constant: the cache keys on (index, id, side), and asking at
           each component's size would store one copy of the same picture per distinct component
           size that happens to use it.
           It must still be positive. DefinitionThumbnailCache.TryGet answers null for a side of
           zero or less before it looks anything up, so a zero here is not "natural size" - it is
           every sprite and every model on the canvas silently falling back to its placeholder. */
        private const int SpriteNaturalSide = 1;

        /* Model tiles are quantised to a multiple of this and then drawn to fit. A model is
           rasterised on the CPU and is by far the most expensive thing the producer thread is
           asked for, so keying one on each component's exact pixel size would rasterise the same
           model again for every interface that shows it a few pixels larger. */
        private const int ModelSideStep = 32;
        private const int ModelSideMax = 256;

        private readonly Dictionary<int, InterfaceLayoutNode> resolved = new();
        private readonly List<int> drawOrder = new();

        private InterfaceComponentTree? tree;
        private IDefinitionThumbnailSource? thumbnails;
        private InterfaceTextPainter? textPainter;
        private int selectedFileId = -1;
        private bool showNotDrawn;

        private DragKind dragging = DragKind.None;
        private Point dragFrom;
        private Point dragOrigin;

        /// <summary>What a drag in progress is doing.</summary>
        private enum DragKind {
            /// <summary>Nothing is being dragged.</summary>
            None,

            /// <summary>The selected component is being moved.</summary>
            Move,

            /// <summary>The selected component is being resized from its corner grip.</summary>
            Resize
        }

        /// <summary>Creates an empty canvas.</summary>
        public InterfaceCanvas() {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(0x20, 0x20, 0x24);
            AutoScroll = true;
        }

        /// <summary>The side of the resize grip drawn on a selected component's bottom-right corner.</summary>
        private const int GripSide = 8;

        /// <summary>Raised when the user picks a component on the canvas.</summary>
        public event EventHandler<int>? ComponentPicked;

        /// <summary>
        ///     Raised when a drag or a nudge has changed a component's stored geometry.
        /// </summary>
        /// <remarks>
        ///     The canvas mutates the component and then asks to be saved; it does not save. The
        ///     write path belongs to the panel that owns the descriptor, and routing through it is
        ///     what keeps the "an edit that changes nothing writes nothing" rule in one place.
        /// </remarks>
        public event EventHandler<int>? ComponentGeometryChanged;

        /// <summary>Says what a drag or a nudge could not do, for the status line.</summary>
        public event EventHandler<string>? Refused;

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

        /// <summary>
        ///     Draws text in the cache's own font, or null to fall back to the editor's.
        /// </summary>
        /// <remarks>
        ///     Set by the tab from the open cache. Without it the canvas still draws text, in
        ///     Consolas, which is wider than any font the cache holds and so wraps captions that fit
        ///     in the game - the fallback is honest about being a fallback rather than absent.
        /// </remarks>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public InterfaceTextPainter? TextPainter {
            get => textPainter;
            set {
                if (ReferenceEquals(textPainter, value))
                    return;

                textPainter = value;
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

        /// <summary>
        ///     Moves the selected component by whole pixels, or resizes it with Shift held.
        /// </summary>
        /// <remarks>
        ///     A nudge is the only way to place a component exactly when its positioning mode stores
        ///     a fraction of its parent, because a drag on a narrow parent cannot reach every pixel.
        /// </remarks>
        /// <param name="e">The key data.</param>
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            if (selectedFileId < 0 || !resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? node))
                return;

            int step = e.Control ? 10 : 1;
            int dx = e.KeyCode switch { Keys.Left => -step, Keys.Right => step, _ => 0 };
            int dy = e.KeyCode switch { Keys.Up => -step, Keys.Down => step, _ => 0 };

            if (dx == 0 && dy == 0)
                return;

            e.Handled = true;

            if (e.Shift)
                ApplyResize(node, node.Relative.Width + dx, node.Relative.Height + dy);
            else
                ApplyMove(node, node.Relative.X + dx, node.Relative.Y + dy);
        }

        /// <summary>The arrow keys reach the canvas rather than moving focus off it.</summary>
        /// <param name="keyData">The key.</param>
        /// <returns>Whether the key is an input key.</returns>
        protected override bool IsInputKey(Keys keyData) {
            Keys code = keyData & Keys.KeyCode;
            return code is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
        }

        /// <summary>Picks the topmost component under the pointer.</summary>
        /// <param name="e">The mouse data.</param>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Focus();

            int x = e.X - AutoScrollPosition.X - CanvasInset;
            int y = e.Y - AutoScrollPosition.Y - CanvasInset;

            //A press inside the selected component's grip starts a resize rather than reselecting
            //whatever is underneath, so the grip stays usable when it overlaps a sibling.
            if (selectedFileId >= 0 && resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? current)
                && GripOf(current.Absolute).Contains(x, y)) {
                dragging = DragKind.Resize;
                dragFrom = new Point(x, y);
                dragOrigin = new Point(current.Relative.Width, current.Relative.Height);
                return;
            }

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
                BeginMove(x, y);
                return;
            }

            Select(layerHit);

            if (layerHit >= 0)
                BeginMove(x, y);
        }

        /// <summary>Tracks a drag in progress, or finishes one.</summary>
        /// <param name="e">The mouse data.</param>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            int x = e.X - AutoScrollPosition.X - CanvasInset;
            int y = e.Y - AutoScrollPosition.Y - CanvasInset;

            if (dragging == DragKind.None) {
                if (selectedFileId >= 0 && resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? hovered))
                    Cursor = GripOf(hovered.Absolute).Contains(x, y) ? Cursors.SizeNWSE : Cursors.Default;
                else
                    Cursor = Cursors.Default;

                return;
            }

            if (!resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? node))
                return;

            int dx = x - dragFrom.X;
            int dy = y - dragFrom.Y;

            //A drag is applied from where it STARTED, not accumulated frame by frame. Accumulating
            //would compound the rounding a fractional positioning mode does on every mouse move,
            //so a slow drag would land somewhere a fast one did not.
            if (dragging == DragKind.Move)
                ApplyMove(node, dragOrigin.X + dx, dragOrigin.Y + dy);
            else
                ApplyResize(node, dragOrigin.X + dx, dragOrigin.Y + dy);
        }

        /// <summary>Ends a drag.</summary>
        /// <param name="e">The mouse data.</param>
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            dragging = DragKind.None;
        }

        private void BeginMove(int x, int y) {
            if (selectedFileId < 0 || !resolved.TryGetValue(selectedFileId, out InterfaceLayoutNode? node))
                return;

            dragging = DragKind.Move;
            dragFrom = new Point(x, y);
            dragOrigin = new Point(node.Relative.X, node.Relative.Y);
        }

        /// <summary>
        ///     Puts a component's top-left corner at a wanted position and asks for it to be saved.
        /// </summary>
        /// <remarks>
        ///     <b>The wanted pixel is turned into a stored base through the mode's inverse, never
        ///     added to it.</b> Only mode 0 stores a pixel: mode 2 measures from the far edge so the
        ///     stored number moves the other way, and the shift modes store a fraction of the parent
        ///     where one pixel is about 21 units. Adding a delta to the base would move a mode-2
        ///     component backwards and barely move a mode-3 one.
        /// </remarks>
        private void ApplyMove(InterfaceLayoutNode node, int wantedX, int wantedY) {
            if (tree == null)
                return;

            (int parentWidth, int parentHeight) = InterfaceLayoutResolver.ParentExtentsFor(
                tree, resolved, node.Component.FileId, InterfaceRect.FixedModeCanvas);
            InterfaceComponentDefinition component = node.Component;

            component.BasePositionX = InterfaceLayoutResolver.BaseForPosition(
                component.XMode, wantedX, parentWidth, node.Relative.Width);
            component.BasePositionY = InterfaceLayoutResolver.BaseForPosition(
                component.YMode, wantedY, parentHeight, node.Relative.Height);

            Reresolve();
            ComponentGeometryChanged?.Invoke(this, component.FileId);
        }

        /// <summary>
        ///     Gives a component a wanted extent, where its sizing mode has an inverse at all.
        /// </summary>
        /// <remarks>
        ///     Sizing modes 3 and 4 never read their stored base - 3 keeps the previous extent and 4
        ///     derives it from the aspect pair - so writing one would produce a file the client
        ///     ignores and an editor that looked like it had saved nothing. Refused out loud
        ///     instead. Neither mode occurs in either supported cache, so this is a guard rather
        ///     than a live path.
        /// </remarks>
        private void ApplyResize(InterfaceLayoutNode node, int wantedWidth, int wantedHeight) {
            if (tree == null)
                return;

            InterfaceComponentDefinition component = node.Component;

            if (!InterfaceLayoutResolver.SizeModeUsesItsBase(component.WidthMode)
                || !InterfaceLayoutResolver.SizeModeUsesItsBase(component.HeightMode)) {
                Refused?.Invoke(this, "Component " + component.FileId + " sizes itself from mode "
                    + component.WidthMode + "/" + component.HeightMode + ", which ignores the stored"
                    + " size, so it cannot be resized by dragging.");
                return;
            }

            (int parentWidth, int parentHeight) = InterfaceLayoutResolver.ParentExtentsFor(
                tree, resolved, node.Component.FileId, InterfaceRect.FixedModeCanvas);

            //Clamped at 0 rather than allowed negative. The format permits a negative extent and the
            //resolver reproduces one, but nothing should be able to CREATE one by dragging past the
            //corner - that is a mis-drag, not an intent.
            component.BaseWidth = InterfaceLayoutResolver.BaseForSize(
                component.WidthMode, Math.Max(0, wantedWidth), parentWidth, component.BaseWidth);
            component.BaseHeight = InterfaceLayoutResolver.BaseForSize(
                component.HeightMode, Math.Max(0, wantedHeight), parentHeight, component.BaseHeight);

            Reresolve();
            ComponentGeometryChanged?.Invoke(this, component.FileId);
        }

        /// <summary>
        ///     Recomputes every rectangle after an edit, rather than moving the one that changed.
        /// </summary>
        /// <remarks>
        ///     A component's box is not independent: resizing a layer changes the content extents
        ///     its children resolve against, so every proportionally-positioned descendant moves.
        ///     Nudging only the edited rectangle would show the user a layout the client would never
        ///     produce, and it is the resolver's job to know that rather than the canvas's.
        /// </remarks>
        private void Reresolve() {
            if (tree == null)
                return;

            resolved.Clear();
            foreach (KeyValuePair<int, InterfaceLayoutNode> entry in
                     InterfaceLayoutResolver.ResolveGroup(tree, InterfaceRect.FixedModeCanvas)) {
                resolved[entry.Key] = entry.Value;
            }

            Invalidate();
        }

        private static Rectangle GripOf(InterfaceRect box) {
            return new Rectangle(box.Right - GripSide / 2, box.Bottom - GripSide / 2, GripSide, GripSide);
        }

        private void Select(int fileId) {
            SelectedFileId = fileId;
            if (fileId >= 0)
                ComponentPicked?.Invoke(this, fileId);
        }

        /// <summary>
        ///     Draws one component, clipped exactly as the client clips it.
        /// </summary>
        /// <remarks>
        ///     <b>A component is clipped by its parent, not by its own box.</b> This canvas used to
        ///     clip every component to <see cref="InterfaceLayoutNode.Clip"/>, and an earlier
        ///     version of this comment asserted that was the client's rule. It is not. The client
        ///     sets its scissor once per component list, to the clip the list inherited
        ///     (<c>Node_Sub10_Sub24.java:85</c>); the narrower own-box intersection it computes at
        ///     <c>:190-203</c> is passed to the recursive call for a layer's children
        ///     (<c>:414</c>) and used for nothing else.
        ///     <para>
        ///     Three arms narrow it themselves and restore it afterwards, and those are the only
        ///     three: a tiled sprite (<c>:601</c>, <c>:634</c>), a line (<c>:837</c>, <c>:868</c>),
        ///     and text - but text only while the <c>clipcomponents</c> dev toggle is on, which is
        ///     <c>false</c> in the shipped client and reachable only from a debug command. So a
        ///     stretched sprite, a model, a rectangle and text all overflow their own boxes when
        ///     they are bigger than them, and the game shows the overflow.
        ///     </para>
        ///     <para>
        ///     Clipping to the box instead was visibly wrong rather than merely conservative:
        ///     interface 35 stores three paragraphs of four lines in components 34 pixels tall,
        ///     four lines of font 494 need 42, and every paragraph lost the bottom half of its last
        ///     line. They fit the 59-pixel gap between the paragraphs, which is what the layout was
        ///     evidently designed around.
        ///     </para>
        /// </remarks>
        private void DrawComponent(Graphics g, InterfaceLayoutNode node) {
            InterfaceComponentDefinition component = node.Component;
            InterfaceRect box = node.Absolute;

            if (box.Width <= 0 || box.Height <= 0)
                return;

            /* A line is the one leaf whose own rectangle is the clip in the client too, and its
               clip is a pixel wider and taller than its box because the endpoint is inclusive -
               which is exactly what ClipFor already builds. A tiled sprite also self-clips, but it
               does that inside its own draw so that the clip is in force only while it repeats. */
            InterfaceRect clip = component.ComponentType == 9 ? node.Clip : node.InheritedClip;
            if (clip.IsEmpty)
                return;

            var rectangle = new Rectangle(box.X, box.Y, box.Width, box.Height);

            /* Intersected, never assigned. Assigning Clip REPLACES what is there, including the
               clip WinForms set to the invalidated region, so a partial repaint would let a
               component paint over parts of the control that were not being redrawn. */
            GraphicsState state = g.Save();
            try {
                g.SetClip(new Rectangle(clip.X, clip.Y, clip.Width, clip.Height), CombineMode.Intersect);
                DrawComponentBody(g, component, rectangle);
            }
            finally {
                g.Restore(state);
            }
        }

        private void DrawComponentBody(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
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
                    DrawModelComponent(g, component, rectangle);
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
        private void DrawTextComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            string text = component.Message.Text;
            if (string.IsNullOrEmpty(text))
                return;

            Color ink = ColourOf(component.Colour, component.Transparency);

            //The cache's own glyphs first. Only when the font will not load does this fall back to
            //a substitute, which is wider and therefore wraps captions the game fits on one line.
            if (textPainter != null && textPainter.Draw(g, text, component.FontId, rectangle, ink,
                    component.HorizontalAlignment, component.VerticalAlignment,
                    component.LineHeight)) {
                return;
            }

            using var brush = new SolidBrush(ink);
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

        /// <summary>
        ///     The component's sprite, or a mark saying which of two different things is missing.
        /// </summary>
        /// <remarks>
        ///     <b>A sprite id of -1 is a real and common state, not a failure, and the two must not
        ///     look alike.</b> The seven drop columns of the RuneLink board in interface 36 store no
        ///     sprite at all - CS2 sets one at runtime depending on whose turn it is - so an id of
        ///     -1 means "the file leaves this to a script", while a missing tile for a real id means
        ///     "not read yet". The first draft drew both as an outline captioned with text, and in a
        ///     32-pixel column "no sprite" clipped to the four middle characters and read as
        ///     corruption.
        ///     <para>
        ///     So: a stored-but-unresolved sprite keeps its outline and its id, and a component the
        ///     file deliberately leaves empty gets a faint dashed outline and no caption at all
        ///     unless there is room for one.
        ///     </para>
        /// </remarks>
        private void DrawSpriteComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            if (component.SpriteId < 0) {
                using var empty = new Pen(Color.FromArgb(70, 0x78, 0xC8, 0xFF)) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(empty, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
                DrawTinyLabel(g, rectangle, "set by script");
                return;
            }

            /* One cache key per sprite rather than one per size. The canvas renderer is the
               uncomposited one, which hands back the frame at its own size and ignores the side it
               is given, so asking at the component's size would store the same picture under a key
               per component that happens to use it. */
            Bitmap? tile = thumbnails?.TryGet(RSConstants.SPRITES_INDEX, component.SpriteId,
                SpriteNaturalSide);

            if (tile != null) {
                DrawSprite(g, tile, component, rectangle);
                return;
            }

            //An outline and the id while the tile is being read, or when there is no source. A blank
            //box would read as a sprite that failed to decode.
            using var pen = new Pen(Color.FromArgb(120, 0x78, 0xC8, 0xFF));
            g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
            DrawTinyLabel(g, rectangle, component.SpriteId.ToString());
        }

        /// <summary>
        ///     Puts one sprite into its component's rectangle the way the client would.
        /// </summary>
        /// <remarks>
        ///     <b>The branch structure is the client's</b>, from the type-5 arm at
        ///     <c>Node_Sub10_Sub24.java:565-667</c>, because the choice between stretching and
        ///     repeating changes what an interface looks like more than anything else on this canvas:
        ///     a window frame is a handful of edge sprites the file expects to be repeated along each
        ///     side, and stretching them instead smears one into a gradient and leaves every corner
        ///     in the wrong place. That is the "the frame of the bank is offset" report, and the
        ///     frame was never offset - the resolver had it right and the paint was wrong.
        ///     <para>
        ///     <b>Stretching is the default and repeating is the exception</b>, which is the opposite
        ///     of the guess worth making. <c>:642-649</c> stretches to the component whenever the
        ///     sizes differ and only blits 1:1 when they already match, so the 1:1 path is an
        ///     optimisation rather than a fallback. Bit 0 of the flags byte
        ///     (<see cref="InterfaceComponentDefinition.SpriteTiles"/>) is the one thing that selects
        ///     repetition, at <c>:600</c>.
        ///     </para>
        ///     <para>
        ///     The flips are applied before anything else because the client applies them when it
        ///     resolves the sprite rather than when it draws it (<c>RSInterface.java:479</c> and
        ///     <c>:483</c>), so they compose with rotation in that order and not the other one.
        ///     </para>
        ///     <para>
        ///     <b>Not drawn:</b> the outline and shadow that <c>RSInterface.java:487-509</c> bakes
        ///     into the resolved sprite, and the client's exact tint blend. The canvas note says the
        ///     view diverges here; both are edits to the sprite's own pixels rather than to its
        ///     placement, so they change how a component looks and never where it sits.
        ///     </para>
        /// </remarks>
        private static void DrawSprite(Graphics g, Bitmap sprite,
            InterfaceComponentDefinition component, Rectangle rectangle) {
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
                return;

            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            //255 is fully transparent and 0 is opaque, so a component that stores nothing is drawn
            //as it is. The client reads the same byte the same way at :598.
            int alpha = 255 - (component.Transparency & 0xFF);
            if (alpha <= 0)
                return;

            using ImageAttributes? attributes = AttributesFor(alpha, component.Colour);

            using var flipped = Flipped(sprite, component);
            Bitmap source = flipped ?? sprite;

            int spriteWidth = Math.Max(1, source.Width);
            int spriteHeight = Math.Max(1, source.Height);

            if (component.SpriteTiles) {
                /* Clipped to the component first, then repeated over it, so the last row and column
                   are cut rather than overhanging. The client intersects its scissor rect at :601
                   and restores it at :634; SetClip with Intersect is the same thing, and assigning
                   g.Clip instead would discard the invalidated region and repaint the whole canvas
                   through this one component. */
                GraphicsState state = g.Save();
                g.SetClip(rectangle, CombineMode.Intersect);

                for (int y = rectangle.Y; y < rectangle.Bottom; y += spriteHeight)
                    for (int x = rectangle.X; x < rectangle.Right; x += spriteWidth)
                        DrawTile(g, source, new Rectangle(x, y, spriteWidth, spriteHeight),
                            component.SpriteTransform, attributes);

                g.Restore(state);
                return;
            }

            DrawTile(g, source, rectangle, component.SpriteTransform, attributes);
        }

        /// <summary>
        ///     One copy of a sprite in one rectangle, rotated if the component asks for it.
        /// </summary>
        /// <remarks>
        ///     <b>A rotated sprite is scaled by its width alone and is not made to fit.</b> The
        ///     client's zoom is <c>componentWidth * 4096 / spriteWidth</c> (<c>:640</c>) about the
        ///     component's centre, with the height taking no part, so a rotated sprite in a tall box
        ///     genuinely does not fill it. Fitting it here would look tidier and would not be what
        ///     the game draws.
        ///     <para>
        ///     A tiled rotation is different again: <c>:605-625</c> lays out a grid at the sprite's
        ///     own size and rotates each cell about its own centre at 4096, so there is no scaling at
        ///     all. Passing this the tile rectangle gets that for free, because the tile rectangle is
        ///     the sprite's size and the ratio is 1.
        ///     </para>
        /// </remarks>
        private static void DrawTile(Graphics g, Bitmap sprite, Rectangle into, int angle,
            ImageAttributes? attributes) {
            var destination = new Rectangle(0, 0, sprite.Width, sprite.Height);

            if (angle == 0) {
                if (attributes == null)
                    g.DrawImage(sprite, into);
                else
                    g.DrawImage(sprite, into, 0, 0, sprite.Width, sprite.Height,
                        GraphicsUnit.Pixel, attributes);
                return;
            }

            //The client's angle is a whole turn in 65536 steps, and it turns the sprite rather than
            //the axes, so the sign is the one that makes a positive angle read as clockwise.
            float degrees = angle * 360f / 65536f;
            float zoom = sprite.Width == 0 ? 1f : into.Width / (float) sprite.Width;

            GraphicsState state = g.Save();
            try {
                g.TranslateTransform(into.X + into.Width / 2f, into.Y + into.Height / 2f);
                g.RotateTransform(degrees);
                g.ScaleTransform(zoom, zoom);
                g.TranslateTransform(-sprite.Width / 2f, -sprite.Height / 2f);

                if (attributes == null)
                    g.DrawImage(sprite, destination);
                else
                    g.DrawImage(sprite, destination, 0, 0, sprite.Width, sprite.Height,
                        GraphicsUnit.Pixel, attributes);
            }
            finally {
                g.Restore(state);
            }
        }

        /// <summary>
        ///     A mirrored copy of the sprite, or null when the component asks for no mirroring.
        /// </summary>
        /// <remarks>
        ///     Null rather than a copy so the common case allocates nothing: 5 of the vanilla
        ///     capture's type-5 components set either flag. The caller disposes what it gets and
        ///     falls back to the cached sprite, which it must not dispose - it belongs to the
        ///     thumbnail cache.
        /// </remarks>
        private static Bitmap? Flipped(Bitmap sprite, InterfaceComponentDefinition component) {
            RotateFlipType flip = (component.SpriteFlipHorizontal, component.SpriteFlipVertical) switch {
                (true, true) => RotateFlipType.RotateNoneFlipXY,
                (true, false) => RotateFlipType.RotateNoneFlipX,
                (false, true) => RotateFlipType.RotateNoneFlipY,
                _ => RotateFlipType.RotateNoneFlipNone
            };

            if (flip == RotateFlipType.RotateNoneFlipNone)
                return null;

            var copy = (Bitmap) sprite.Clone();
            copy.RotateFlip(flip);
            return copy;
        }

        /// <summary>
        ///     Transparency and tint as a draw-time attribute, or null when neither applies.
        /// </summary>
        /// <remarks>
        ///     The tint multiplies, which is what the client's <c>:596-598</c> composes and what
        ///     leaves an untinted sprite untouched when the stored colour is white. A stored 0 means
        ///     "no tint" rather than "black", so it is read as white here for the same reason the
        ///     client substitutes <c>0xffffff</c> for it.
        /// </remarks>
        private static ImageAttributes? AttributesFor(int alpha, int tint) {
            bool tinted = tint != 0 && (tint & 0xFFFFFF) != 0xFFFFFF;
            if (alpha >= 255 && !tinted)
                return null;

            float r = tinted ? ((tint >> 16) & 0xFF) / 255f : 1f;
            float gr = tinted ? ((tint >> 8) & 0xFF) / 255f : 1f;
            float b = tinted ? (tint & 0xFF) / 255f : 1f;

            var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix(new[] {
                new[] { r,  0f, 0f, 0f, 0f },
                new[] { 0f, gr, 0f, 0f, 0f },
                new[] { 0f, 0f, b,  0f, 0f },
                new[] { 0f, 0f, 0f, alpha / 255f, 0f },
                new[] { 0f, 0f, 0f, 0f, 1f }
            }));

            return attributes;
        }

        /// <summary>
        ///     The component's model, or a mark saying which of two different things is missing.
        /// </summary>
        /// <remarks>
        ///     Models are rasterised on the CPU now rather than skipped. It is not the client's
        ///     renderer - flat shading, no textures, no lighting - and the canvas note says so, but
        ///     a recognisable model beats a box captioned with a number.
        ///     <para>
        ///     <b>Read <see cref="InterfaceComponentDefinition.ModelId"/> and never
        ///     <c>RawModelId</c>.</b> The stored field is an unsigned short whose 65535 means "no
        ///     model", which the client maps to -1 at <c>RSInterface.java:1102-1104</c> and this
        ///     project maps in the <c>ModelId</c> property. The raw value exists so the record
        ///     re-encodes to the bytes it was read from and is not a model id. Using it here asked
        ///     the cache for model 65535 on every scripted component, got nothing back, and drew a
        ///     box captioned "model 65535" - which reads as a decode defect and is the opposite: it
        ///     is a component that deliberately stores no model. All seven boxes on interface 25,
        ///     the Barrows puzzle, are that case.
        ///     </para>
        /// </remarks>
        private void DrawModelComponent(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            if (component.ModelId >= 0 && rectangle.Width > 0 && rectangle.Height > 0) {
                int wanted = Math.Max(rectangle.Width, rectangle.Height);
                int side = Math.Min(ModelSideMax,
                    Math.Max(ModelSideStep,
                        (wanted + ModelSideStep - 1) / ModelSideStep * ModelSideStep));

                Bitmap? drawn = thumbnails?.TryGet(RSConstants.MODELS_INDEX, component.ModelId, side);

                if (drawn != null) {
                    /* Centred and aspect-preserved rather than stretched to the component. The tile
                       is square because the cache keys on a single side, so stretching it into a
                       tall or wide component would squash the model - which reads as a decode
                       defect rather than as a cache shape. */
                    float scale = Math.Min(rectangle.Width / (float) drawn.Width,
                        rectangle.Height / (float) drawn.Height);
                    int width = Math.Max(1, (int) (drawn.Width * scale));
                    int height = Math.Max(1, (int) (drawn.Height * scale));

                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(drawn, new Rectangle(
                        rectangle.X + (rectangle.Width - width) / 2,
                        rectangle.Y + (rectangle.Height - height) / 2,
                        width, height));
                    return;
                }
            }

            DrawModelPlaceholder(g, component, rectangle);
        }

        /// <summary>
        ///     A marked box for a model that has no id, or one whose picture is not ready.
        /// </summary>
        /// <remarks>
        ///     <b>Storing no model is a normal state, not a failure, and the two must not look
        ///     alike</b> - the same distinction <see cref="DrawSpriteComponent"/> draws for sprites,
        ///     and for the same reason. Interface 25's Barrows puzzle stores no model in any of its
        ///     seven boxes because CS2 sets one per shape as the puzzle is dealt, so "model 65535"
        ///     on all seven read as seven broken records rather than as an empty board.
        /// </remarks>
        private static void DrawModelPlaceholder(Graphics g, InterfaceComponentDefinition component,
            Rectangle rectangle) {
            bool scripted = component.ModelId < 0;

            /* Faint. A model box can be most of the interface - model 4608 in the RuneLink board
               is 299x252 of a 512x334 window - and at the first draft's opacity the hatching read
               as the interface's background rather than as one component that cannot be drawn. */
            if (!scripted) {
                using var hatch = new HatchBrush(HatchStyle.BackwardDiagonal,
                    Color.FromArgb(18, 0xFF, 0xB8, 0x26), Color.Transparent);
                g.FillRectangle(hatch, rectangle);
            }

            using var pen = new Pen(Color.FromArgb(scripted ? 70 : 90, 0xFF, 0xB8, 0x26)) {
                DashStyle = scripted ? DashStyle.Dot : DashStyle.Solid
            };

            g.DrawRectangle(pen, rectangle.X, rectangle.Y, rectangle.Width - 1, rectangle.Height - 1);
            DrawTinyLabel(g, rectangle, scripted ? "set by script" : "model " + component.ModelId);
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

            //One grip, on the bottom-right. Eight would be the usual thing and most of them would
            //be unusable here: components run down to a few pixels, and eight grips on a 6x6 sprite
            //would leave nothing of the component to grab for a move.
            Rectangle grip = GripOf(box);
            using var gripFill = new SolidBrush(EditorTheme.Accent(EditorSurface.Canvas));
            g.FillRectangle(gripFill, grip);
        }

        /// <summary>
        ///     A caption inside a component's box, drawn only when the whole of it fits.
        /// </summary>
        /// <remarks>
        ///     <b>Measured against the box rather than trimmed to it.</b> A centred string that does
        ///     not fit loses characters from both ends, so "no sprite" in a 32-pixel column rendered
        ///     as "spri" - which reads as corrupt data rather than as a caption that did not fit.
        ///     Nothing at all is the honest output there: the outline already says what the box is,
        ///     and the grid beside the canvas has the id.
        /// </remarks>
        private static void DrawTinyLabel(Graphics g, Rectangle rectangle, string text) {
            if (rectangle.Width < 24 || rectangle.Height < 10)
                return;

            SizeF needed = g.MeasureString(text, EditorTheme.NoticeFont);
            if (needed.Width > rectangle.Width - 2 || needed.Height > rectangle.Height)
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
