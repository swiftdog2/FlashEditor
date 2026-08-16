using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.UI;

namespace FlashEditor.Definitions.Audio {
    /// <summary>Which key the user picked.</summary>
    public sealed class MidiKeyEventArgs : EventArgs {
        /// <summary>Names the key.</summary>
        /// <param name="key">The key, 0..127.</param>
        public MidiKeyEventArgs(int key) {
            Key = key;
        }

        /// <summary>The key, 0..127.</summary>
        public int Key { get; }
    }

    /// <summary>
    ///     A patch's 128 keys drawn as a piano keyboard, coloured by what each key actually plays.
    /// </summary>
    /// <remarks>
    ///     <b>A keyboard rather than a grid, because the index is a keyboard.</b> A patch is 128 keys
    ///     and the questions a user has about it are positional: where does this drum kit put its
    ///     hi-hats, which register of this instrument is sampled, which keys choke each other. A grid
    ///     of 128 numbered rows answers none of those at a glance.
    ///     <para>
    ///     <b>What the drawing states, and it says so in its own legend.</b> A key with no sample is
    ///     drawn dead; a sounding key carries a band along its foot; a key whose sample lives in
    ///     index 4 carries that band hatched, because this editor has no index-4 renderer and the key
    ///     is therefore silent in the player rather than wrong. Selecting a key that belongs to a
    ///     mute group outlines every other key in the same group, which is the only way that field is
    ///     legible: a mute group is how a closed hi-hat chokes an open one, and as an integer in a
    ///     column it says nothing.
    ///     </para>
    ///     <para>
    ///     This is deliberately not the client's own instrument display, because the client has none.
    ///     Nothing here is a rendering of anything the game draws.
    ///     </para>
    /// </remarks>
    public sealed class MidiKeyboardControl : Control {
        /// <summary>How thick the band along a sounding key's foot is, as a fraction of the keyboard.</summary>
        private const int BandDenominator = 8;

        /// <summary>Padding around the legend's swatches.</summary>
        private const int LegendPadding = 4;

        private MidiPatchListing? listing;
        private int selectedKey = -1;
        private int hoverKey = -1;

        /// <summary>Creates an unbound keyboard.</summary>
        public MidiKeyboardControl() {
            /* Double buffered and fully self-painted: every repaint redraws 128 keys, their bands and
               the legend, and the default painting path would flicker through all of it on a resize. */
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            //A drawn surface rather than chrome, so it takes the canvas colours. EditorTheme.SurfaceOf
            //classifies by luminance, so setting the background is what makes every other colour here
            //resolve to the canvas set.
            BackColor = EditorTheme.Background(EditorSurface.Canvas);
            Font = EditorTheme.NoticeFont;
            TabStop = true;
        }

        /// <summary>Raised when the user picks a key, whether by clicking it or with the arrow keys.</summary>
        public event EventHandler<MidiKeyEventArgs>? KeyActivated;

        /// <summary>Raised when the selection moves, including when a rebind clears it.</summary>
        public event EventHandler? SelectedKeyChanged;

        /// <summary>The selected key, or -1 for none.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedKey {
            get => selectedKey;
            set {
                int clamped = value < 0 || value >= MidiPatchDefinition.Keys ? -1 : value;
                if (clamped == selectedKey)
                    return;

                selectedKey = clamped;
                Invalidate();
                SelectedKeyChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>The patch on display, or null.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MidiPatchListing? Patch => listing;

        /// <summary>
        ///     Puts a patch on the keyboard, or clears it.
        /// </summary>
        /// <remarks>
        ///     The selection is moved to the patch's lowest sounding key rather than cleared, so that
        ///     stepping down the patch list lands on something playable each time instead of on an
        ///     empty detail pane the user has to click into.
        /// </remarks>
        /// <param name="patch">The patch, or null to show an empty keyboard.</param>
        public void Bind(MidiPatchListing? patch) {
            listing = patch;
            hoverKey = -1;

            int first = -1;
            if (patch != null)
                foreach (MidiKeySnapshot key in patch.Keys)
                    if (key.Sounds) {
                        first = key.Key;
                        break;
                    }

            //Assigned through the property so the change is announced even when the key number is
            //the same on two patches running, which is the common case down a list of drum kits.
            selectedKey = -1;
            SelectedKey = first;
            Invalidate();
        }

        /// <summary>The selected key's values, or null when nothing is selected.</summary>
        /// <returns>The snapshot, or null.</returns>
        public MidiKeySnapshot? SelectedSnapshot() {
            if (listing == null || selectedKey < 0)
                return null;

            return listing.Keys[selectedKey];
        }

        /// <summary>Picks the key under the pointer and plays it.</summary>
        /// <param name="e">The mouse event.</param>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Focus();

            int key = LayoutFor().KeyAt(e.X, e.Y);
            if (key < 0)
                return;

            SelectedKey = key;
            KeyActivated?.Invoke(this, new MidiKeyEventArgs(key));
        }

        /// <summary>Tracks which key the pointer is over.</summary>
        /// <param name="e">The mouse event.</param>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            int key = LayoutFor().KeyAt(e.X, e.Y);
            if (key == hoverKey)
                return;

            hoverKey = key;
            Invalidate();
        }

        /// <summary>Drops the hover mark when the pointer leaves.</summary>
        /// <param name="e">The event data.</param>
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);

            if (hoverKey < 0)
                return;

            hoverKey = -1;
            Invalidate();
        }

        /// <summary>Lets the arrow keys walk the keyboard.</summary>
        /// <remarks>
        ///     <c>IsInputKey</c> has to claim them or the containing form treats them as navigation
        ///     and the control never sees a key press at all.
        /// </remarks>
        /// <param name="keyData">The key.</param>
        /// <returns>Whether this control handles it.</returns>
        protected override bool IsInputKey(Keys keyData) {
            switch (keyData) {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Enter:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        /// <summary>Moves the selection, and plays what it lands on when asked to.</summary>
        /// <param name="e">The key event.</param>
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            if (listing == null)
                return;

            int step;
            switch (e.KeyCode) {
                case Keys.Left:
                    step = -1;
                    break;
                case Keys.Right:
                    step = 1;
                    break;
                //An octave at a time, which is what up and down mean on a keyboard.
                case Keys.Up:
                    step = 12;
                    break;
                case Keys.Down:
                    step = -12;
                    break;
                case Keys.Enter:
                    if (selectedKey >= 0)
                        KeyActivated?.Invoke(this, new MidiKeyEventArgs(selectedKey));
                    e.Handled = true;
                    return;
                default:
                    return;
            }

            int moved = Math.Clamp((selectedKey < 0 ? 0 : selectedKey) + step, 0,
                MidiPatchDefinition.Keys - 1);
            SelectedKey = moved;
            e.Handled = true;
        }

        /// <summary>Draws the keyboard and its legend.</summary>
        /// <param name="e">The paint event.</param>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            EditorSurface surface = EditorTheme.SurfaceOf(this);
            e.Graphics.Clear(BackColor);

            int legend = LegendHeight;
            var layout = LayoutFor();

            if (layout.IsDrawable) {
                DrawWhiteKeys(e.Graphics, surface, layout);
                DrawBlackKeys(e.Graphics, surface, layout);
                DrawSelection(e.Graphics, surface, layout);
            } else {
                TextRenderer.DrawText(e.Graphics, "Too narrow to draw a keyboard", Font,
                    new Point(LegendPadding, LegendPadding), EditorTheme.InkMuted(surface));
            }

            DrawLegend(e.Graphics, surface, new Rectangle(0, Height - legend, Width, legend));
        }

        /// <summary>The keyboard's geometry at the control's current size, legend excluded.</summary>
        /// <returns>The layout.</returns>
        private MidiKeyboardLayout LayoutFor() {
            return new MidiKeyboardLayout(Width, Math.Max(0, Height - LegendHeight));
        }

        /// <summary>How much room the legend takes at the foot of the control.</summary>
        private int LegendHeight => Font.Height + (LegendPadding * 2);

        private void DrawWhiteKeys(Graphics graphics, EditorSurface surface, MidiKeyboardLayout layout) {
            using var edge = new Pen(EditorTheme.Separator(surface));
            using var live = new SolidBrush(EditorTheme.Ink(surface));
            using var dead = new SolidBrush(EditorTheme.InkDisabled(surface));
            using var hovered = new SolidBrush(EditorTheme.HoverFill(surface));

            for (int key = 0; key < MidiPatchDefinition.Keys; key++) {
                if (!MidiKeyboardLayout.IsWhite(key))
                    continue;

                Rectangle bounds = layout.KeyBounds(key);
                graphics.FillRectangle(Sounds(key) ? live : dead, bounds);

                if (key == hoverKey)
                    graphics.FillRectangle(hovered, bounds);

                graphics.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                DrawBand(graphics, surface, key, bounds);
                DrawOctaveLabel(graphics, surface, key, bounds);
            }
        }

        private void DrawBlackKeys(Graphics graphics, EditorSurface surface, MidiKeyboardLayout layout) {
            /* The same disabled ink the white keys use for a silent key, so the legend's one "no
               sample" swatch is accurate for both halves of the keyboard rather than for one. */
            using var live = new SolidBrush(EditorTheme.Background(EditorSurface.Canvas));
            using var dead = new SolidBrush(EditorTheme.InkDisabled(surface));
            using var hovered = new SolidBrush(EditorTheme.PressedFill(surface));
            using var edge = new Pen(EditorTheme.Ink(surface));

            for (int key = 0; key < MidiPatchDefinition.Keys; key++) {
                if (MidiKeyboardLayout.IsWhite(key))
                    continue;

                Rectangle bounds = layout.KeyBounds(key);
                graphics.FillRectangle(Sounds(key) ? live : dead, bounds);

                if (key == hoverKey)
                    graphics.FillRectangle(hovered, bounds);

                graphics.DrawRectangle(edge, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
                DrawBand(graphics, surface, key, bounds);
            }
        }

        /// <summary>
        ///     Draws the band that says a key plays something, hatched when nothing here can play it.
        /// </summary>
        /// <remarks>
        ///     Hatched rather than a second colour. The theme states one accent per surface, and a
        ///     tab that invented a warning colour beside it would be the first colour literal in a
        ///     control that this project has spent an entire convention getting rid of. A texture
        ///     carries the same distinction and survives being read in greyscale.
        /// </remarks>
        private void DrawBand(Graphics graphics, EditorSurface surface, int key, Rectangle bounds) {
            if (listing == null)
                return;

            MidiKeySnapshot snapshot = listing.Keys[key];
            if (!snapshot.Sounds)
                return;

            int thickness = Math.Max(2, bounds.Height / BandDenominator);
            var band = new Rectangle(bounds.X + 1, bounds.Bottom - thickness - 1,
                Math.Max(1, bounds.Width - 2), thickness);

            if (!snapshot.SilentHere) {
                using var solid = new SolidBrush(EditorTheme.Accent(surface));
                graphics.FillRectangle(solid, band);
                return;
            }

            using var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, EditorTheme.Accent(surface),
                EditorTheme.Background(EditorSurface.Canvas));
            graphics.FillRectangle(hatch, band);
        }

        /// <summary>Writes the note name inside every C, where the key is wide enough to hold it.</summary>
        private void DrawOctaveLabel(Graphics graphics, EditorSurface surface, int key, Rectangle bounds) {
            if (key % 12 != 0)
                return;

            string text = GeneralMidi.NoteName(key);
            Size size = TextRenderer.MeasureText(text, Font);
            if (size.Width > bounds.Width - 2)
                return;

            //Below the band, so the two never overlap on a narrow keyboard.
            int thickness = Math.Max(2, bounds.Height / BandDenominator);
            TextRenderer.DrawText(graphics, text, Font,
                new Point(bounds.X + 1, bounds.Bottom - thickness - size.Height - 2),
                EditorTheme.InkMuted(surface));
        }

        /// <summary>
        ///     Outlines the selected key, and every other key its mute group would cut.
        /// </summary>
        /// <remarks>
        ///     The companions are what make the mute group readable. Drawn last so the outlines sit
        ///     over the black keys, which are painted over the white ones they straddle.
        /// </remarks>
        private void DrawSelection(Graphics graphics, EditorSurface surface, MidiKeyboardLayout layout) {
            if (listing == null || selectedKey < 0)
                return;

            MidiKeySnapshot selected = listing.Keys[selectedKey];

            if (selected.MuteGroup >= 0) {
                using var companion = new Pen(EditorTheme.Accent(surface)) { DashStyle = DashStyle.Dot };
                foreach (MidiKeySnapshot other in listing.Keys) {
                    if (other.Key == selectedKey || other.MuteGroup != selected.MuteGroup)
                        continue;

                    Rectangle bounds = layout.KeyBounds(other.Key);
                    graphics.DrawRectangle(companion, bounds.X, bounds.Y, bounds.Width - 1,
                        bounds.Height - 1);
                }
            }

            using var mark = new Pen(EditorTheme.CheckedEdge(surface), 2f);
            Rectangle chosen = layout.KeyBounds(selectedKey);
            graphics.DrawRectangle(mark, chosen.X + 1, chosen.Y + 1, chosen.Width - 3, chosen.Height - 3);
        }

        /// <summary>
        ///     Says what the drawing means, on the drawing.
        /// </summary>
        /// <remarks>
        ///     Stated here rather than in a note above the control, because a legend that is not
        ///     beside the marks it explains gets read as decoration. The index-4 entry is the one
        ///     that has to be here: it is the difference between "this editor cannot play that" and
        ///     "the cache is broken", and nothing else on screen distinguishes them.
        /// </remarks>
        private void DrawLegend(Graphics graphics, EditorSurface surface, Rectangle area) {
            if (area.Height <= 0 || area.Width <= 0)
                return;

            using var separator = new Pen(EditorTheme.Separator(surface));
            graphics.DrawLine(separator, area.Left, area.Top, area.Right, area.Top);

            int swatch = Math.Max(6, Font.Height - 2);
            int x = LegendPadding;
            int y = area.Top + LegendPadding;

            using (var live = new SolidBrush(EditorTheme.Accent(surface)))
                x = Entry(graphics, surface, live, "plays index 14", x, y, swatch, area.Right);

            using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, EditorTheme.Accent(surface),
                       EditorTheme.Background(EditorSurface.Canvas)))
                x = Entry(graphics, surface, hatch, "index 4, silent here", x, y, swatch, area.Right);

            using (var dead = new SolidBrush(EditorTheme.InkDisabled(surface)))
                x = Entry(graphics, surface, dead, "no sample", x, y, swatch, area.Right);

            using var group = new SolidBrush(EditorTheme.CheckedFill(surface));
            Entry(graphics, surface, group, "dotted outline = same mute group", x, y, swatch, area.Right);
        }

        /// <summary>Draws one legend swatch and its caption, and reports where the next one starts.</summary>
        private int Entry(Graphics graphics, EditorSurface surface, Brush fill, string caption, int x, int y,
            int swatch, int right) {
            if (x + swatch >= right)
                return x;

            graphics.FillRectangle(fill, x, y, swatch, swatch);

            using (var edge = new Pen(EditorTheme.Separator(surface)))
                graphics.DrawRectangle(edge, x, y, swatch, swatch);

            int textLeft = x + swatch + LegendPadding;
            Size size = TextRenderer.MeasureText(caption, Font);
            TextRenderer.DrawText(graphics, caption, Font, new Point(textLeft, y),
                EditorTheme.InkMuted(surface));

            return textLeft + size.Width + (LegendPadding * 3);
        }

        /// <summary>Whether a key names a sample at all.</summary>
        private bool Sounds(int key) {
            return listing != null && listing.Keys[key].Sounds;
        }
    }
}
