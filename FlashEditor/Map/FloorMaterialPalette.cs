using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.UI;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Map {
    /// <summary>Which floor table a swatch came from.</summary>
    public enum FloorKind {
        /// <summary>Config group 1: the base colour and texture of a ground tile.</summary>
        Underlay,

        /// <summary>Config group 4: the shape drawn over an underlay.</summary>
        Overlay
    }

    /// <summary>
    ///     Every floor material the cache holds, as swatches you can pick from.
    /// </summary>
    /// <remarks>
    ///     <b>The piece that makes the map tab learnable.</b> Painting terrain meant knowing which
    ///     integer you wanted: the tool box offered "Paint underlay" and an unlabelled number, and
    ///     there was no way anywhere in the application to see what underlay 47 looked like. So the
    ///     workflow was to paint a tile, look, undo, and try the next number. This shows all of them
    ///     at once, in their own colours, and clicking one loads the brush.
    ///     <para>
    ///     <b>The counts are read from the reference table, not written down.</b> Both floor tables
    ///     hold the same number of records in the two supported caches - so they are properties of
    ///     build 639 rather than of one capture - but reading them is still right: it is the
    ///     difference between a palette that shows what is there and one that shows what someone
    ///     expected to be there.
    ///     </para>
    ///     <para>
    ///     <b>A swatch is the colour the top-down map draws</b>, built by <see cref="FloorSwatch"/>
    ///     through the same <see cref="FloorColour"/> the rasteriser draws with. For an overlay that
    ///     is the secondary colour, then the texture's representative colour, then the primary; for
    ///     an underlay it is the blended colour of a field of that underlay alone, not its raw RGB.
    ///     It is not the in-game 3D ground colour, which comes from an overlay's primary, so the
    ///     caption lists the primary separately. A record that names a texture carries a corner
    ///     mark either way, so two floors that differ only by texture do not look identical.
    ///     </para>
    /// </remarks>
    public sealed class FloorMaterialPalette : UserControl {
        /* 18, not the 26 the first draft used. The palette shares a crowded column with five other
           groups, and at 26 it either pushed the layer list off the bottom or left room for two
           rows of swatches - and a palette showing 12 of 159 underlays is not a palette. */
        private const int SwatchSide = 18;
        private const int Gap = 2;

        private readonly List<FloorSwatch> entries = new();
        private readonly EditorToolStrip tools = new EditorToolStrip { Dock = DockStyle.Top };
        private readonly Label caption = new Label { Dock = DockStyle.Bottom, AutoSize = true };

        private FloorKind showing = FloorKind.Underlay;
        private int columns = 1;
        private int hovered = -1;

        /// <summary>Creates an empty palette.</summary>
        public FloorMaterialPalette() {
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Color.FromArgb(0x20, 0x20, 0x24);

            caption.ForeColor = EditorTheme.InkMuted(EditorSurface.Canvas);
            caption.Font = EditorTheme.NoticeFont;
            caption.Text = "No cache loaded";

            BuildTools();

            Controls.Add(caption);
            Controls.Add(tools);
        }

        /// <summary>Raised when the user picks a material to paint with.</summary>
        public event EventHandler<FloorPick>? Picked;

        /// <summary>Which table is on show.</summary>
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(
            System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FloorKind Showing => showing;

        /// <summary>
        ///     How much of the top the toolbar takes, which is nothing once it is hidden.
        /// </summary>
        /// <remarks>
        ///     Read rather than assumed, because <see cref="ShowOnly"/> hides the strip and a docked
        ///     control that is not visible keeps its <c>Height</c> while taking none of the layout -
        ///     so every swatch would sit one toolbar below where the hit test looked for it.
        /// </remarks>
        private int ToolsHeight => tools.Visible ? tools.Height : 0;

        /// <summary>Reads both floor tables out of a cache, or clears the palette.</summary>
        /// <param name="cache">The open cache, or null.</param>
        public void Bind(RSCache? cache) {
            entries.Clear();
            hovered = -1;

            if (cache == null) {
                caption.Text = "No cache loaded";
                Relayout();
                Invalidate();
                return;
            }

            LoadTable(cache, FloorKind.Underlay, RSConstants.FLOOR_UNDERLAY_GROUP);
            LoadTable(cache, FloorKind.Overlay, RSConstants.FLOOR_OVERLAY_GROUP);

            Relayout();
            Invalidate();
            UpdateCaption();
        }

        /// <inheritdoc/>
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            Relayout();
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            int cell = SwatchSide + Gap;
            int index = 0;

            using var edge = new Pen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
            using var hover = new Pen(EditorTheme.Accent(EditorSurface.Canvas), 2f);
            using var idBrush = new SolidBrush(EditorTheme.InkMuted(EditorSurface.Canvas));

            foreach (FloorSwatch entry in entries) {
                if (entry.Kind != showing) {
                    index++;
                    continue;
                }

                int position = VisiblePositionOf(index);
                int x = AutoScrollPosition.X + Gap + position % columns * cell;
                int y = AutoScrollPosition.Y + ToolsHeight + Gap + position / columns * cell;

                var box = new Rectangle(x, y, SwatchSide, SwatchSide);

                PaintFill(g, entry, box);

                /* A corner mark rather than a different swatch colour. Two floors can share a
                   colour and differ only by texture, and the renderer draws the texture - so
                   without this they are indistinguishable in the one place a user picks between
                   them. */
                if (entry.TextureId >= 0) {
                    using var mark = new SolidBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x14));
                    g.FillRectangle(mark, box.Right - 7, box.Bottom - 7, 6, 6);
                }

                g.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);

                if (index == hovered)
                    g.DrawRectangle(hover, box.X - 1, box.Y - 1, box.Width + 1, box.Height + 1);

                index++;
            }

            if (entries.Count == 0)
                g.DrawString("No floors loaded", EditorTheme.UiFont, idBrush, 8, ToolsHeight + 8);
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            int hit = HitTest(e.X, e.Y);
            if (hit == hovered)
                return;

            hovered = hit;
            UpdateCaption();
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            hovered = -1;
            UpdateCaption();
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);

            int hit = HitTest(e.X, e.Y);
            if (hit < 0)
                return;

            FloorSwatch entry = entries[hit];
            Picked?.Invoke(this, new FloorPick(entry.Kind, entry.Id));
        }

        private void BuildTools() {
            var group = new object();

            EditorToolButton underlays = tools.AddTool(group, EditorIcon.Grid, "Show floor underlays", Keys.None);
            EditorToolButton overlays = tools.AddTool(group, EditorIcon.Texture, "Show floor overlays", Keys.None);

            underlays.Click += (_, _) => Show(FloorKind.Underlay);
            overlays.Click += (_, _) => Show(FloorKind.Overlay);
            underlays.Arm();

            tools.Items.Add(new ToolStripControlHost(InfoAffordance.For(this, InfoKind.Help,
                "Every floor material in the cache, in the colour the top-down map draws for it.\n\n" +
                "This is not the in-game 3D ground colour: the game draws an overlay's primary " +
                "colour, the map draws its secondary first. Hover a swatch to see both.\n\n" +
                "A crossed swatch is a hole: the map draws nothing there, underlay included. A " +
                "hatched one has no colour of its own and lets the underlay show.\n\n" +
                "A small dark mark in the corner means the record names a texture. The renderer " +
                "draws that texture and treats the colour as the flat fallback, so two floors can " +
                "share a colour and look completely different in game.\n\n" +
                "Clicking a swatch loads it into the map brush.")) {
                Alignment = ToolStripItemAlignment.Right
            });
        }

        /// <summary>
        ///     Fixes the palette to one table and hides the switch between them.
        /// </summary>
        /// <remarks>
        ///     For a host that already knows which table it is showing - the Config tab lists one
        ///     index-2 group at a time, so a toolbar offering to flip the palette to the other family
        ///     would put overlays beside a grid of underlays and read as the two disagreeing. The map
        ///     tab keeps the switch, because there the palette is a brush and both tables are in
        ///     scope at once.
        /// </remarks>
        /// <param name="kind">The table to show.</param>
        public void ShowOnly(FloorKind kind) {
            tools.Visible = false;
            Show(kind);
        }

        private void Show(FloorKind kind) {
            if (showing == kind)
                return;

            showing = kind;
            hovered = -1;
            Relayout();
            Invalidate();
            UpdateCaption();
        }

        /// <summary>
        ///     Reads one floor table.
        /// </summary>
        /// <remarks>
        ///     Enumerated from the reference table rather than counted up from zero, because a
        ///     missing record would otherwise stop the palette at the hole rather than skipping it.
        ///     A record that will not decode costs its own swatch and nothing else.
        /// </remarks>
        private void LoadTable(RSCache cache, FloorKind kind, int groupId) {
            int[] ids;
            try {
                ids = cache.GetFileIds(RSConstants.CONFIG, groupId);
            }
            catch (Exception ex) {
                Debug("Floor palette could not list config group " + groupId + ": " + ex.Message);
                return;
            }

            foreach (int id in ids) {
                try {
                    entries.Add(kind == FloorKind.Underlay
                        ? FloorSwatch.Underlay(cache.GetFloorUnderlay(id))
                        : FloorSwatch.Overlay(cache.GetFloorOverlay(id), FloorColour.TextureHsl));
                }
                catch (Exception ex) {
                    Debug("Floor palette could not decode " + kind + " " + id + ": " + ex.Message);
                }
            }
        }

        private void UpdateCaption() {
            int shown = 0;
            foreach (FloorSwatch entry in entries) {
                if (entry.Kind == showing)
                    shown++;
            }

            if (hovered >= 0 && hovered < entries.Count) {
                caption.Text = entries[hovered].Caption;
                return;
            }

            caption.Text = shown + " " + showing.ToString().ToLowerInvariant() + "s";
        }

        private int HitTest(int mouseX, int mouseY) {
            int cell = SwatchSide + Gap;

            int column = (mouseX - AutoScrollPosition.X - Gap) / cell;
            int row = (mouseY - AutoScrollPosition.Y - ToolsHeight - Gap) / cell;

            if (column < 0 || column >= columns || row < 0)
                return -1;

            int position = row * columns + column;
            int index = 0;
            int seen = 0;

            foreach (FloorSwatch entry in entries) {
                if (entry.Kind == showing) {
                    if (seen == position)
                        return index;
                    seen++;
                }

                index++;
            }

            return -1;
        }

        /// <summary>Where a swatch sits among the ones currently shown.</summary>
        private int VisiblePositionOf(int index) {
            int position = 0;
            for (int i = 0; i < index; i++) {
                if (entries[i].Kind == showing)
                    position++;
            }

            return position;
        }

        private void Relayout() {
            int cell = SwatchSide + Gap;
            columns = Math.Max(1, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Gap) / cell);

            int shown = 0;
            foreach (FloorSwatch entry in entries) {
                if (entry.Kind == showing)
                    shown++;
            }

            int rows = (shown + columns - 1) / columns;
            AutoScrollMinSize = new Size(0, ToolsHeight + rows * cell + Gap + caption.Height);
        }

        /// <summary>Fills one swatch box with what the map draws for its record.</summary>
        /// <remarks>
        ///     A hole and an overlay with no colour of its own both draw no overlay colour, and a
        ///     black fill would make each indistinguishable from a real black floor and from the
        ///     other. A hole gets a cross over the canvas; an overlay that lets the underlay show
        ///     gets a hatch.
        /// </remarks>
        private void PaintFill(Graphics g, FloorSwatch entry, Rectangle box) {
            Color ink = EditorTheme.InkMuted(EditorSurface.Canvas);

            switch (entry.Fill) {
                case FloorSwatchFill.Hole:
                    using (var pen = new Pen(ink, 1.5f)) {
                        g.DrawLine(pen, box.Left + 3, box.Top + 3, box.Right - 4, box.Bottom - 4);
                        g.DrawLine(pen, box.Left + 3, box.Bottom - 4, box.Right - 4, box.Top + 3);
                    }
                    break;

                case FloorSwatchFill.UnderlayShows:
                    using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, ink, BackColor))
                        g.FillRectangle(hatch, box);
                    break;

                default:
                    using (var fill = new SolidBrush(Color.FromArgb(0xFF, Color.FromArgb(entry.MapRgb))))
                        g.FillRectangle(fill, box);
                    break;
            }
        }
    }

    /// <summary>Which floor the user picked.</summary>
    /// <param name="Kind">Which table it came from.</param>
    /// <param name="Id">Its definition id.</param>
    public readonly record struct FloorPick(FloorKind Kind, int Id);
}
