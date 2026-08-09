using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
//System.Threading arrives through the implicit usings, so a bare Timer is ambiguous. Editor.cs:30
//already resolves it this way; do not fully qualify it at each use instead.
using Timer = System.Windows.Forms.Timer;

namespace FlashEditor.UI {
    /// <summary>
    ///     What a note claims, which decides its glyph, its ink and the heading over its paragraph.
    /// </summary>
    /// <remarks>
    ///     <c>CLAUDE.md</c> states two separate obligations - "say what the editor cannot do" and
    ///     "mark what an edit will cost" - and they are different claims. A limitation is about the
    ///     editor and is true whether or not the user touches anything; a cost is about an action
    ///     they are one click from taking. Collapsing both into one (i) would let a user read
    ///     "this rebuilds the baked vertex colours" as background reading. So the kind is part of
    ///     the surface, not a styling hint.
    /// </remarks>
    public enum InfoKind {
        /// <summary>Orientation or usage. Neither obligation; the quietest mark of the three.</summary>
        Help,

        /// <summary>"Say what the editor cannot do" - a deliberate divergence from the client.</summary>
        Limitation,

        /// <summary>"Mark what an edit will cost" - the only kind that is about a pending action.</summary>
        Cost
    }

    /// <summary>
    ///     A small glyph that sits beside a control and reveals a paragraph about it on hover,
    ///     click or keypress.
    /// </summary>
    /// <remarks>
    ///     <b>Why this exists.</b> The editor discharges its two <c>CLAUDE.md</c> obligations today
    ///     with roughly a dozen permanent paragraph labels docked into pages, one of which runs to
    ///     fifteen source lines. The obligations are right and are not being weakened - what changes
    ///     is delivery. A paragraph docked into a resizable pane also drags a wrap-on-resize helper
    ///     behind it, and there are seven separate copies of that helper in the tree, one per page
    ///     that docks one. Retiring the paragraphs retires all seven.
    ///     <para>
    ///     <b>Why a drop-down and not a <see cref="ToolTip"/>.</b> Four reasons, in the order they
    ///     decided it:
    ///     </para>
    ///     <list type="number">
    ///         <item><description>
    ///             <b>A <see cref="ToolTip"/> does not wrap.</b> It sizes itself to the longest line
    ///             in the string, and WinForms exposes no maximum width, so a fifteen-line paragraph
    ///             becomes one strip wider than the monitor unless the caller hard-wraps it first -
    ///             which is the wrap helper this control exists to delete, moved rather than
    ///             removed.
    ///         </description></item>
    ///         <item><description>
    ///             <b>It dismisses itself.</b> <c>AutoPopDelay</c> is capped by the native control
    ///             and the whole point of a paragraph is that it is still there while it is being
    ///             read. A popover stays until it is dismissed.
    ///         </description></item>
    ///         <item><description>
    ///             <b>The application has exactly one <see cref="ToolTip"/> instance</b> -
    ///             <c>Editor.cs:199</c>, attached to the GL control and to the animation combo, with
    ///             <c>AutoPopDelay = 30000</c>. Inheriting that instance would tie a dozen notes to
    ///             timings chosen for two controls; creating a second instance per affordance would
    ///             put a dozen native tooltip windows in the process. Neither is right.
    ///         </description></item>
    ///         <item><description>
    ///             <b>A tooltip is unreachable without a mouse.</b> It cannot be opened from the
    ///             keyboard, focused, or read at the user's own pace.
    ///         </description></item>
    ///     </list>
    ///     <para>
    ///     A <see cref="ToolStripDropDown"/> hosting a wrapping <see cref="Label"/> gives
    ///     click-outside dismissal, Escape and screen-edge flipping for free, which is the whole
    ///     reason not to hand-roll a borderless form. The one thing carried over from
    ///     <c>_modelTooltip</c> is the 300 ms hover delay, so the two mechanisms do not feel like
    ///     different products while both exist.
    ///     </para>
    ///     <para>
    ///     <b>Nothing in the test suite can see this.</b> The suite covers no WinForms at all, and
    ///     <c>tools/Capture-EditorTab.ps1</c> captures the main window handle, so a
    ///     <see cref="ToolStripDropDown"/> is out of frame by construction - a green capture proves
    ///     the glyph rendered and says nothing whatever about the popover. The kind-to-glyph and
    ///     kind-to-ink maps and the wrap measurement are therefore <c>internal static</c> and
    ///     pinned by tests, so that the part a human has to judge is only "does it read well".
    ///     </para>
    ///     <para>
    ///     <b>Content is spliced, not retyped.</b> <see cref="Body"/> is a settable string rather
    ///     than a resource id because at least one existing note concatenates a value out of the
    ///     policy class that enforces it, deliberately, so the stated limit and the enforced limit
    ///     cannot drift. A migration moves the expression, never its current value.
    ///     </para>
    /// </remarks>
    public sealed class InfoAffordance : Control {
        /// <summary>
        ///     How wide the popover's text column is, in characters of its own font.
        /// </summary>
        /// <remarks>
        ///     Stated in characters and measured against the font rather than stated in pixels, per
        ///     the layout rule: the popover font is Consolas, so this is an exact column count, and
        ///     a prose measure is a property of reading rather than of the display. Ninety-two
        ///     columns keeps the longest note in the application - the sprite import essay - inside
        ///     a screen height without turning the short ones into a narrow ribbon.
        /// </remarks>
        private const int BodyColumns = 92;

        /// <summary>
        ///     How long the pointer must rest on the glyph before the popover opens, in
        ///     milliseconds.
        /// </summary>
        /// <remarks>
        ///     300, matching <c>_modelTooltip.InitialDelay</c> (<c>Editor.cs:199</c>). Its
        ///     <c>AutoPopDelay</c> is deliberately <b>not</b> matched: a popover that dismisses
        ///     itself mid-paragraph is the failure this control exists to avoid.
        /// </remarks>
        private const int HoverDelayMs = 300;

        private const TextFormatFlags BodyFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;

        private readonly Timer hoverTimer = new() { Interval = HoverDelayMs };

        private ToolStripDropDown? popover;
        private string body = string.Empty;
        private string caption = string.Empty;
        private string summary = string.Empty;
        private InfoKind kind = InfoKind.Help;
        private Control? describes;
        private bool pointerInside;
        private bool openedByHover;

        /// <summary>Creates a glyph-only help note.</summary>
        public InfoAffordance() {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.UserPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.SupportsTransparentBackColor
                | ControlStyles.Selectable, true);

            /* Transparent rather than a stated colour: twenty-four of the twenty-five tab pages set
               UseVisualStyleBackColor, so the page's real background belongs to the running visual
               style and painting a guess behind the glyph would show as a rectangle a shade off
               from everything around it. */
            BackColor = Color.Transparent;
            Font = EditorTheme.UiFont;
            Cursor = Cursors.Help;
            TabStop = true;
            AutoSize = true;
            AccessibleRole = AccessibleRole.ButtonDropDown;

            hoverTimer.Tick += OnHoverElapsed;
            EditorTheme.Changed += OnThemeChanged;

            UpdateAccessibleText();
        }

        /// <summary>
        ///     The paragraph the glyph reveals.
        /// </summary>
        /// <remarks>
        ///     Plain text. Its own line breaks are honoured and a blank line reads as a paragraph
        ///     break; everything else is wrapped to a measured column, so a caller never hard-wraps
        ///     and no page needs a wrap-on-resize handler.
        /// </remarks>
        [DefaultValue("")]
        public string Body {
            get => body;
            set {
                string next = value ?? string.Empty;
                if (body == next)
                    return;

                body = next;
                DiscardPopover();
                UpdateAccessibleText();
            }
        }

        /// <summary>
        ///     The heading over the paragraph, or empty to use the one the <see cref="Kind"/>
        ///     implies.
        /// </summary>
        /// <remarks>
        ///     Worth setting when the note is about one named control and the kind's own wording
        ///     would be vague - "What importing costs" reads better than "What this edit costs"
        ///     beside a button that is one of three on a strip.
        /// </remarks>
        [DefaultValue("")]
        public string Caption {
            get => caption;
            set {
                string next = value ?? string.Empty;
                if (caption == next)
                    return;

                caption = next;
                DiscardPopover();
                UpdateAccessibleText();
            }
        }

        /// <summary>Which obligation the note discharges, or neither.</summary>
        [DefaultValue(InfoKind.Help)]
        public InfoKind Kind {
            get => kind;
            set {
                if (kind == value)
                    return;

                kind = value;
                DiscardPopover();
                UpdateAccessibleText();
                Invalidate();
            }
        }

        /// <summary>
        ///     One short line kept permanently on screen beside the glyph, or empty for glyph only.
        /// </summary>
        /// <remarks>
        ///     The escape hatch for a note whose first clause has to be visible without a hover -
        ///     "Read only", "Index 8". A note whose whole body is important is not a candidate for
        ///     this control at all and should stay a docked label.
        /// </remarks>
        [DefaultValue("")]
        public string Summary {
            get => summary;
            set {
                string next = value ?? string.Empty;
                if (summary == next)
                    return;

                summary = next;
                UpdateAccessibleText();

                if (AutoSize)
                    PerformLayout();
                Invalidate();
            }
        }

        /// <summary>
        ///     The control the note is about.
        /// </summary>
        /// <remarks>
        ///     Used only to name the note for a screen reader - a bare "what this edit costs" is
        ///     useless read out of context. It deliberately does <b>not</b> reparent, position or
        ///     size anything: where the glyph sits is the caller's layout decision, and this control
        ///     silently moving itself would fight whichever table or flow it was added to.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Control? Describes {
            get => describes;
            set {
                if (ReferenceEquals(describes, value))
                    return;

                describes = value;
                UpdateAccessibleText();
            }
        }

        /// <summary>Whether the popover is currently showing.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool IsOpen => popover is { Visible: true };

        /// <summary>
        ///     Attaches a note to a control.
        /// </summary>
        /// <remarks>
        ///     Returns the affordance rather than adding it anywhere: it has to go into the same
        ///     cell, flow or strip the described control sits in, and only the caller knows which.
        /// </remarks>
        /// <param name="describes">The control the note is about.</param>
        /// <param name="kind">Which obligation the note discharges.</param>
        /// <param name="body">The paragraph.</param>
        /// <returns>The affordance, not yet parented.</returns>
        public static InfoAffordance For(Control describes, InfoKind kind, string body) {
            return new InfoAffordance {
                Describes = describes,
                Kind = kind,
                Body = body
            };
        }

        /// <summary>
        ///     Shows the popover.
        /// </summary>
        /// <remarks>
        ///     Named <c>Open</c> rather than <c>Show</c> because <see cref="Control.Show"/> already
        ///     means "make this control visible", and a method that made the glyph appear and a
        ///     method that made the paragraph appear could not share a name without one of them
        ///     being read as the other.
        ///     <para>
        ///     Does nothing when <see cref="Body"/> is empty. An empty popover is worse than none:
        ///     it reads as a note that failed to load.
        ///     </para>
        /// </remarks>
        public void Open() {
            if (IsOpen || body.Length == 0)
                return;

            popover ??= BuildPopover();
            popover.Show(this, new Point(0, Height), ToolStripDropDownDirection.BelowRight);
            Invalidate();
        }

        /// <summary>Hides the popover.</summary>
        public void Close() {
            hoverTimer.Stop();
            popover?.Close(ToolStripDropDownCloseReason.CloseCalled);
        }

        /// <summary>
        ///     The glyph box, plus the summary line when there is one.
        /// </summary>
        /// <remarks>
        ///     Measured, never stated. The glyph box is the larger of the font's line height and
        ///     <see cref="EditorTheme.IconSide"/> so that the glyph aligns with a line of text
        ///     beside it and still gets its icon at 1:1 - see <see cref="GlyphBox"/> for why the
        ///     1:1 matters.
        /// </remarks>
        /// <param name="proposedSize">The size the layout engine is offering.</param>
        /// <returns>The size the control wants.</returns>
        public override Size GetPreferredSize(Size proposedSize) {
            int side = GlyphSide;
            if (summary.Length == 0)
                return new Size(side, side);

            Size text = TextRenderer.MeasureText(summary, Font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            return new Size(side + Gap + text.Width, Math.Max(side, text.Height));
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            EditorSurface surface = EditorTheme.SurfaceOf(this);
            Color ink = InkFor(kind, surface);
            Rectangle glyph = GlyphBox;

            if (pointerInside || IsOpen)
                using (var fill = new SolidBrush(EditorTheme.HoverFill(surface)))
                    e.Graphics.FillRectangle(fill, glyph);

            /* Drawn unscaled at EditorTheme.IconSide rather than stretched to the glyph box.
               EditorIcons puts its axis-aligned strokes on exact pixel rows on a 16x16 grid, and a
               fractional scale undoes precisely that work - the process is DPI-unaware, so there is
               never a reason to ask for any other side. */
            Image image = EditorIcons.Render(GlyphFor(kind), ink, EditorTheme.IconSide);
            e.Graphics.DrawImageUnscaled(image,
                glyph.X + (glyph.Width - EditorTheme.IconSide) / 2,
                glyph.Y + (glyph.Height - EditorTheme.IconSide) / 2);

            if (summary.Length > 0) {
                var textArea = new Rectangle(glyph.Right + Gap, 0, Width - glyph.Right - Gap, Height);
                TextRenderer.DrawText(e.Graphics, summary, Font, textArea,
                    EditorTheme.InkMuted(surface),
                    TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter);
            }

            if (Focused)
                ControlPaint.DrawFocusRectangle(e.Graphics, glyph);
        }

        /// <inheritdoc/>
        protected override void OnMouseEnter(EventArgs e) {
            base.OnMouseEnter(e);
            pointerInside = true;
            Invalidate();

            if (!IsOpen)
                hoverTimer.Start();
        }

        /// <inheritdoc/>
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            pointerInside = false;
            hoverTimer.Stop();
            Invalidate();

            /* A popover the user opened by hovering follows the pointer away; one they clicked
               stays, because they asked for it and are about to read it. The drop-down's own
               click-outside dismissal takes care of the second case. */
            if (openedByHover)
                Close();
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            hoverTimer.Stop();
            Focus();

            if (IsOpen)
                Close();
            else
                OpenPinned();
        }

        /// <inheritdoc/>
        protected override void OnGotFocus(EventArgs e) {
            base.OnGotFocus(e);
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void OnLostFocus(EventArgs e) {
            base.OnLostFocus(e);
            Invalidate();
        }

        /// <summary>
        ///     Claims Space and Enter, which a container would otherwise route to its default
        ///     button.
        /// </summary>
        /// <param name="keyData">The key.</param>
        /// <returns>Whether this control wants it.</returns>
        protected override bool IsInputKey(Keys keyData) {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Space || key == Keys.Enter || base.IsInputKey(keyData);
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            Keys key = e.KeyCode;
            if (key != Keys.Space && key != Keys.Enter)
                return;

            if (IsOpen)
                Close();
            else
                OpenPinned();

            e.Handled = true;
        }

        /// <summary>
        ///     Closes on Escape.
        /// </summary>
        /// <remarks>
        ///     The drop-down handles Escape itself once it holds focus, but it does not always take
        ///     focus, so the glyph has to answer for it too or a keyboard user can open a popover
        ///     they cannot dismiss.
        /// </remarks>
        /// <param name="keyData">The key.</param>
        /// <returns>Whether it was consumed.</returns>
        protected override bool ProcessDialogKey(Keys keyData) {
            if ((keyData & Keys.KeyCode) == Keys.Escape && IsOpen) {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        /// <inheritdoc/>
        protected override void OnFontChanged(EventArgs e) {
            base.OnFontChanged(e);
            DiscardPopover();

            if (AutoSize)
                PerformLayout();
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                EditorTheme.Changed -= OnThemeChanged;
                hoverTimer.Tick -= OnHoverElapsed;
                hoverTimer.Dispose();
                DiscardPopover();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     Which glyph a kind is drawn with.
        /// </summary>
        /// <remarks>
        ///     Two glyphs across three kinds, on purpose. A cost is the one claim about an action
        ///     the user is about to take, so it is the only one that gets a mark that reads as a
        ///     warning; help and a limitation are both statements about what is on screen and share
        ///     the (i), separated by ink rather than by shape. Three shapes at sixteen pixels would
        ///     be three things to learn for a distinction two of them do not carry.
        /// </remarks>
        /// <param name="kind">The kind.</param>
        /// <returns>The icon to draw.</returns>
        internal static EditorIcon GlyphFor(InfoKind kind) {
            return kind == InfoKind.Cost ? EditorIcon.Warning : EditorIcon.Info;
        }

        /// <summary>
        ///     The ink a kind is drawn in on a surface.
        /// </summary>
        /// <remarks>
        ///     Never a literal colour: the glyph has to clear a near-white page and a set of dark
        ///     canvases, and only <see cref="EditorTheme"/> knows which it is on. Help is muted
        ///     because orientation text should not compete with the control it annotates; a
        ///     limitation is full ink because it is a claim about correctness; a cost takes the
        ///     accent, which is the theme's existing "look here" mark on both surfaces.
        /// </remarks>
        /// <param name="kind">The kind.</param>
        /// <param name="surface">The surface the glyph sits on.</param>
        /// <returns>The ink.</returns>
        internal static Color InkFor(InfoKind kind, EditorSurface surface) {
            return kind switch {
                InfoKind.Cost => EditorTheme.Accent(surface),
                InfoKind.Limitation => EditorTheme.Ink(surface),
                _ => EditorTheme.InkMuted(surface)
            };
        }

        /// <summary>The heading a kind carries when the caller states none.</summary>
        /// <param name="kind">The kind.</param>
        /// <returns>The default caption.</returns>
        internal static string DefaultCaptionFor(InfoKind kind) {
            return kind switch {
                InfoKind.Cost => "What this edit costs",
                InfoKind.Limitation => "What this cannot do",
                _ => "About this"
            };
        }

        /// <summary>
        ///     How wide the popover's text column should be.
        /// </summary>
        /// <remarks>
        ///     The requested column count measured in the popover's own font, then clamped to three
        ///     quarters of the available width so the popover cannot run off the monitor on a small
        ///     display or a narrow secondary one. Exposed so the wrap rule is checkable without a
        ///     window: the failure this control exists to avoid is a note that truncates, and a
        ///     truncating note and a correct one look identical in code.
        /// </remarks>
        /// <param name="font">The font the body will be drawn in.</param>
        /// <param name="columns">The target width in characters.</param>
        /// <param name="available">The width the popover may occupy.</param>
        /// <returns>The wrap width in pixels.</returns>
        internal static int MeasureColumn(Font font, int columns, int available) {
            int wanted = TextRenderer.MeasureText(new string('0', columns), font,
                new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding).Width;

            return Math.Min(wanted, Math.Max(1, available * 3 / 4));
        }

        /// <summary>
        ///     Line breaks normalised so what is measured is what is drawn.
        /// </summary>
        /// <remarks>
        ///     The existing notes are written with three different break spellings - a literal
        ///     <c>\n</c>, a literal <c>\r\n</c> and <c>Environment.NewLine</c> - because each was
        ///     written for a <see cref="Label"/> that tolerates all of them.
        ///     <see cref="TextRenderer.MeasureText(string, Font, Size, TextFormatFlags)"/> does not
        ///     tolerate a bare <c>\r</c> the same way, so a body that mixed them would measure to a
        ///     different height than it draws and the popover would clip its last line.
        /// </remarks>
        /// <param name="text">The body as written.</param>
        /// <returns>The body with one break spelling.</returns>
        internal static string NormaliseBreaks(string text) {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
        }

        /// <summary>The side of the square the glyph is centred in.</summary>
        private int GlyphSide => Math.Max(Font.Height, EditorTheme.IconSide);

        /// <summary>The gap between the glyph and a summary line beside it.</summary>
        private int Gap => Math.Max(2, Font.Height / 3);

        private Rectangle GlyphBox {
            get {
                int side = GlyphSide;
                return new Rectangle(0, Math.Max(0, (Height - side) / 2), side, side);
            }
        }

        /// <summary>Opens the popover and marks it as one the pointer leaving must not close.</summary>
        private void OpenPinned() {
            openedByHover = false;
            Open();
        }

        private void OnHoverElapsed(object? sender, EventArgs e) {
            hoverTimer.Stop();

            if (!pointerInside || IsOpen)
                return;

            openedByHover = true;
            Open();
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            /* The popover's own colours came out of the theme when it was built, so it is no longer
               correct. Dropping it is cheaper than walking into it and repainting it, and it is
               rebuilt on the next open. */
            DiscardPopover();
            Invalidate();
        }

        /// <summary>
        ///     Builds the popover window.
        /// </summary>
        /// <remarks>
        ///     Built on first open rather than in the constructor. A dozen of these exist across the
        ///     application and most are never opened in a session; building each one eagerly would
        ///     put a dozen native drop-down windows and two dozen labels in the process to show
        ///     nothing.
        /// </remarks>
        /// <returns>The drop-down.</returns>
        private ToolStripDropDown BuildPopover() {
            EditorSurface surface = EditorTheme.SurfaceOf(this);
            Font bodyFont = EditorTheme.UiFont;
            Font captionFont = EditorTheme.UiFontBold;

            Rectangle work = WorkingArea;
            int column = MeasureColumn(bodyFont, BodyColumns, work.Width);
            int pad = Math.Max(4, bodyFont.Height / 2);

            string headingText = caption.Length > 0 ? caption : DefaultCaptionFor(kind);
            string bodyText = NormaliseBreaks(body);

            var heading = new Label {
                AutoSize = true,
                MaximumSize = new Size(column, 0),
                Font = captionFont,
                ForeColor = InkFor(kind, surface),
                Margin = new Padding(0, 0, 0, pad / 2),
                Text = headingText,
                UseMnemonic = false
            };

            var paragraph = new Label {
                AutoSize = true,
                MaximumSize = new Size(column, 0),
                Font = bodyFont,
                ForeColor = EditorTheme.Ink(surface),
                Margin = Padding.Empty,
                Text = bodyText,
                UseMnemonic = false
            };

            var stack = new FlowLayoutPanel {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            stack.Controls.Add(heading);
            stack.Controls.Add(paragraph);

            /* The paragraph can legitimately be longer than the screen - the sprite import note is
               fifteen source lines - so the stack goes inside a scroller capped at two thirds of the
               working height rather than being allowed to size a window off the bottom of it. The
               cap is measured from the monitor, not stated. */
            Size wanted = stack.PreferredSize;
            int ceiling = work.Height * 2 / 3;
            bool scrolls = wanted.Height > ceiling;

            var frame = new Panel {
                AutoScroll = scrolls,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = new Size(
                    wanted.Width + (scrolls ? SystemInformation.VerticalScrollBarWidth : 0),
                    Math.Min(wanted.Height, ceiling))
            };
            frame.Controls.Add(stack);

            var host = new ToolStripControlHost(frame) {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = frame.Size
            };

            var dropDown = new ToolStripDropDown {
                AutoClose = true,
                AutoSize = true,
                DropShadowEnabled = true,
                Padding = new Padding(pad),
                /* Background(Page) is deliberately Color.Transparent, which a floating window
                   cannot use. SystemColors.Window is the visual style's own popup surface and is
                   therefore right under High Contrast too - the same reasoning that makes
                   EditorTheme classify the page by luminance rather than by a constant. */
                BackColor = surface == EditorSurface.Canvas
                    ? EditorTheme.Background(EditorSurface.Canvas)
                    : SystemColors.Window
            };
            dropDown.Items.Add(host);
            dropDown.Closed += (_, _) => {
                openedByHover = false;
                Invalidate();
            };

            return dropDown;
        }

        /// <summary>The monitor's usable area, falling back to the primary before a handle exists.</summary>
        private Rectangle WorkingArea {
            get {
                Screen? screen = IsHandleCreated ? Screen.FromControl(this) : Screen.PrimaryScreen;
                return screen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
            }
        }

        private void DiscardPopover() {
            if (popover == null)
                return;

            //Guarded on Visible because this also runs from Dispose, and by then the form teardown
            //may already have destroyed the window a Close would try to hide.
            if (popover.Visible)
                popover.Close(ToolStripDropDownCloseReason.CloseCalled);

            popover.Dispose();
            popover = null;
        }

        /// <summary>
        ///     Names the note for a screen reader.
        /// </summary>
        /// <remarks>
        ///     A bare "what this edit costs" read out of context says nothing, so the described
        ///     control's own caption is put in front of it where there is one. The body becomes the
        ///     accessible description, which is what a reader announces on focus - so the paragraph
        ///     is reachable without the popover ever being opened.
        /// </remarks>
        private void UpdateAccessibleText() {
            string subject = describes?.AccessibleName ?? string.Empty;
            if (subject.Length == 0)
                subject = describes?.Text ?? string.Empty;

            string heading = caption.Length > 0 ? caption : DefaultCaptionFor(kind);

            AccessibleName = subject.Length > 0 ? subject + ": " + heading : heading;
            AccessibleDescription = body;
        }
    }
}
