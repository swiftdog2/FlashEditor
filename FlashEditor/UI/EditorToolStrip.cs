using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace FlashEditor.UI {
    /// <summary>
    ///     One tool: an icon, a tooltip, a shortcut and a checked state, and nothing else.
    /// </summary>
    /// <remarks>
    ///     <b>It paints itself rather than going through a renderer.</b> A
    ///     <see cref="ToolStripRenderer"/> can be made to do this, but its hooks for the image, the
    ///     background and the border are separate calls with the framework's own state handling
    ///     between them, and the corners where the professional renderer leaks through - the
    ///     overflow button, the grip, the separator - are exactly where a half-themed strip looks
    ///     broken. Painting the whole button here is fewer moving parts and reads as one method.
    ///     <para>
    ///     <b>The icon is resolved at paint time, not stored.</b> The right ink depends on which
    ///     surface the strip ended up on, and that is not known when the button is constructed - a
    ///     palette built in a field initialiser has no parent yet. Resolving late also means a
    ///     strip moved from a page to a canvas re-tints instead of staying dark on dark.
    ///     </para>
    /// </remarks>
    public sealed class EditorToolButton : ToolStripButton {
        private readonly List<EditorToolButton> radioGroup;
        private EditorIcon icon;

        /// <summary>Creates a tool.</summary>
        /// <param name="icon">The icon to draw.</param>
        /// <param name="tooltip">What the tool does, in a few words.</param>
        /// <param name="shortcut">The key that arms it, or <see cref="Keys.None"/>.</param>
        /// <param name="radioGroup">
        ///     The group this tool belongs to when exactly one of a set may be armed, or null for a
        ///     plain button or an independent toggle.
        /// </param>
        internal EditorToolButton(EditorIcon icon, string tooltip, Keys shortcut,
            List<EditorToolButton>? radioGroup) {
            this.icon = icon;
            Shortcut = shortcut;
            this.radioGroup = radioGroup ?? new List<EditorToolButton>(0);

            AutoSize = false;
            Size = new Size(EditorTheme.ToolButtonSide, EditorTheme.ToolButtonSide);
            DisplayStyle = ToolStripItemDisplayStyle.None;
            Margin = new Padding(1, 1, 1, 1);

            Describe(tooltip);
        }

        /// <summary>
        ///     Which icon this tool draws.
        /// </summary>
        /// <remarks>
        ///     Settable, because a transport's play control is one button with two meanings rather
        ///     than two buttons of which one is hidden. Two buttons is the obvious alternative and
        ///     it is worse: the strip's width changes as they swap unless both are always present,
        ///     the tab order gains a control the user can never reach, and every caller that wants
        ///     to enable or disable "the play button" has to remember there are two of them.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public EditorIcon Icon {
            get => icon;
            set {
                if (icon == value)
                    return;

                icon = value;
                Invalidate();
            }
        }

        /// <summary>
        ///     Restates what the tool does, keeping its shortcut on the end.
        /// </summary>
        /// <remarks>
        ///     The shortcut belongs in the tooltip because there is nowhere else for it to appear:
        ///     <see cref="ToolStripButton"/> has no <c>ShortcutKeys</c> property and no shortcut
        ///     display - those live on <see cref="ToolStripMenuItem"/> - so a shortcut nobody writes
        ///     down is a shortcut nobody finds. A caller that changes the caption through
        ///     <see cref="ToolStripItem.ToolTipText"/> instead drops it, which is why this is here
        ///     rather than left to the constructor.
        /// </remarks>
        /// <param name="tooltip">What the tool does, in a few words.</param>
        public void Describe(string tooltip) {
            ToolTipText = Shortcut == Keys.None
                ? tooltip
                : tooltip + "  (" + DescribeShortcut(Shortcut) + ")";
        }

        /// <summary>The key that arms this tool, or <see cref="Keys.None"/>.</summary>
        public Keys Shortcut { get; }

        /// <summary>
        ///     Arms this tool and disarms every other tool in its group.
        /// </summary>
        /// <remarks>
        ///     Public because a palette's state is often changed by something other than a click -
        ///     an eyedropper that reverts to the previous tool after one use, a shortcut, an undo.
        /// </remarks>
        public void Arm() {
            foreach (EditorToolButton sibling in radioGroup)
                sibling.Checked = ReferenceEquals(sibling, this);

            if (radioGroup.Count == 0)
                Checked = true;
        }

        /// <summary>
        ///     Draws the button: its state fill, its selected edge, and its icon.
        /// </summary>
        /// <param name="e">The paint data.</param>
        protected override void OnPaint(PaintEventArgs e) {
            EditorSurface surface = EditorTheme.SurfaceOf(Parent);
            var bounds = new Rectangle(Point.Empty, Size);

            Color? fill = null;
            if (!Enabled)
                fill = null;
            else if (Pressed)
                fill = EditorTheme.PressedFill(surface);
            else if (Checked)
                fill = EditorTheme.CheckedFill(surface);
            else if (Selected)
                fill = EditorTheme.HoverFill(surface);

            if (fill.HasValue) {
                using var brush = new SolidBrush(fill.Value);
                e.Graphics.FillRectangle(brush, bounds);
            }

            /* An accent edge as well as a fill, because a fill alone is not enough signal for a
               palette where one of twelve tools is live and the checked and hover fills are a few
               levels apart. */
            if (Checked && Enabled) {
                using var pen = new Pen(EditorTheme.CheckedEdge(surface));
                e.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
            }

            Color ink = Enabled ? EditorTheme.Ink(surface) : EditorTheme.InkDisabled(surface);
            int side = EditorTheme.IconSide;
            var iconBox = new Rectangle(
                (bounds.Width - side) / 2, (bounds.Height - side) / 2, side, side);

            e.Graphics.DrawImageUnscaled(EditorIcons.Render(Icon, ink, side), iconBox.Location);
        }

        /// <summary>
        ///     A shortcut in the form a user reads on a tooltip.
        /// </summary>
        /// <remarks>
        ///     Hand-written rather than through <see cref="KeysConverter"/>, which renders a bare
        ///     letter key as "E" but a modified one as "Ctrl+E" with a localised modifier name, so
        ///     the two disagree in style within one palette.
        /// </remarks>
        /// <param name="shortcut">The key combination.</param>
        /// <returns>The text.</returns>
        private static string DescribeShortcut(Keys shortcut) {
            var text = string.Empty;

            if ((shortcut & Keys.Control) == Keys.Control)
                text += "Ctrl+";
            if ((shortcut & Keys.Alt) == Keys.Alt)
                text += "Alt+";
            if ((shortcut & Keys.Shift) == Keys.Shift)
                text += "Shift+";

            return text + (shortcut & Keys.KeyCode);
        }
    }

    /// <summary>
    ///     A strip of tools, themed, with tooltips and keyboard shortcuts.
    /// </summary>
    /// <remarks>
    ///     <b>Why derive from <see cref="ToolStrip"/> rather than hand-roll a control.</b> A
    ///     <c>MenuStrip</c> is already in the process, and <c>MenuStrip</c> derives from
    ///     <c>ToolStrip</c> - so the item model, hit testing, keyboard navigation, overflow when the
    ///     strip is narrower than its tools, and the accessibility tree are all already loaded and
    ///     already work. None of that is worth rewriting to avoid a base class.
    ///     <para>
    ///     <b>Shortcuts are routed by the host, deliberately.</b> A <c>ToolStripButton</c> has no
    ///     <c>ShortcutKeys</c>, and <c>ProcessCmdKey</c> only reaches a control's own ancestors -
    ///     a palette is a sibling of the canvas the user is actually typing into, never its parent,
    ///     so a strip that tried to catch its own keys would work only while the strip itself had
    ///     focus, which is never. The owning panel calls <see cref="HandleShortcut"/> from its
    ///     <c>ProcessCmdKey</c>. One line at the host, and it is honest about where the key
    ///     actually arrives.
    ///     </para>
    /// </remarks>
    public sealed class EditorToolStrip : ToolStrip {
        private readonly List<EditorToolButton> tools = new();
        private readonly Dictionary<object, List<EditorToolButton>> groups = new();

        /// <summary>Creates an empty strip.</summary>
        public EditorToolStrip() {
            GripStyle = ToolStripGripStyle.Hidden;
            Renderer = new EditorToolStripRenderer();
            ShowItemToolTips = true;
            Padding = new Padding(EditorTheme.ToolStripPadding);
            AutoSize = true;

            /* Stated rather than left to the framework default. MenuStrip in this application sets
               24 (Editor.Designer.cs:183) and the ToolStrip default is 16, so a strip that said
               nothing would silently disagree with the menu above it. */
            ImageScalingSize = new Size(EditorTheme.IconSide, EditorTheme.IconSide);

            EditorTheme.Changed += OnThemeChanged;
        }

        /// <summary>Raised when a tool is armed, whether by click or by shortcut.</summary>
        public event EventHandler<EditorToolButton>? ToolArmed;

        /// <summary>Adds a tool that does something when clicked and holds no state.</summary>
        /// <param name="icon">The icon.</param>
        /// <param name="tooltip">What it does.</param>
        /// <param name="shortcut">Its shortcut, or <see cref="Keys.None"/>.</param>
        /// <param name="onClick">What to run.</param>
        /// <returns>The tool, for a caller that wants to enable or disable it later.</returns>
        public EditorToolButton AddAction(EditorIcon icon, string tooltip, Keys shortcut,
            EventHandler onClick) {
            if (onClick == null)
                throw new ArgumentNullException(nameof(onClick));

            EditorToolButton button = Build(icon, tooltip, shortcut, null);
            button.Click += onClick;
            return button;
        }

        /// <summary>Adds a tool that turns something on and off independently of the others.</summary>
        /// <param name="icon">The icon.</param>
        /// <param name="tooltip">What it toggles.</param>
        /// <param name="shortcut">Its shortcut, or <see cref="Keys.None"/>.</param>
        /// <param name="onToggled">What to run when it changes.</param>
        /// <returns>The tool.</returns>
        public EditorToolButton AddToggle(EditorIcon icon, string tooltip, Keys shortcut,
            EventHandler onToggled) {
            EditorToolButton button = Build(icon, tooltip, shortcut, null);
            button.CheckOnClick = true;
            if (onToggled != null)
                button.CheckedChanged += onToggled;
            return button;
        }

        /// <summary>
        ///     Adds a tool of which exactly one in its group may be armed at a time.
        /// </summary>
        /// <remarks>
        ///     The group is keyed by an arbitrary object so a panel with two independent palettes -
        ///     a paint tool and a selection mode, say - keeps them from disarming each other.
        /// </remarks>
        /// <param name="group">Anything, used only for identity.</param>
        /// <param name="icon">The icon.</param>
        /// <param name="tooltip">What the tool does.</param>
        /// <param name="shortcut">Its shortcut, or <see cref="Keys.None"/>.</param>
        /// <returns>The tool.</returns>
        public EditorToolButton AddTool(object group, EditorIcon icon, string tooltip, Keys shortcut) {
            if (group == null)
                throw new ArgumentNullException(nameof(group));

            if (!groups.TryGetValue(group, out List<EditorToolButton>? members)) {
                members = new List<EditorToolButton>();
                groups[group] = members;
            }

            EditorToolButton button = Build(icon, tooltip, shortcut, members);
            members.Add(button);

            button.Click += (_, _) => {
                button.Arm();
                ToolArmed?.Invoke(this, button);
            };

            return button;
        }

        /// <summary>Adds a hairline between two groups of tools.</summary>
        public void AddSeparator() {
            Items.Add(new ToolStripSeparator());
        }

        /// <summary>
        ///     Arms whichever tool owns a key, and says whether one did.
        /// </summary>
        /// <remarks>
        ///     Called by the hosting panel from its own <c>ProcessCmdKey</c>. Returns false for a
        ///     key no tool claims so the host can go on to handle it - a palette must not swallow
        ///     the arrow keys a canvas uses for nudging.
        /// </remarks>
        /// <param name="keyData">The key combination, as <c>ProcessCmdKey</c> received it.</param>
        /// <returns>Whether a tool took it.</returns>
        public bool HandleShortcut(Keys keyData) {
            foreach (EditorToolButton tool in tools) {
                if (tool.Shortcut == Keys.None || tool.Shortcut != keyData || !tool.Enabled)
                    continue;

                if (tool.CheckOnClick)
                    tool.Checked = !tool.Checked;
                else
                    tool.PerformClick();

                return true;
            }

            return false;
        }

        /// <summary>Every tool on the strip, in the order they were added.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<EditorToolButton> Tools => tools;

        /// <summary>Detaches from the theme so a closed panel's strip is not kept alive by it.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing)
                EditorTheme.Changed -= OnThemeChanged;

            base.Dispose(disposing);
        }

        private EditorToolButton Build(EditorIcon icon, string tooltip, Keys shortcut,
            List<EditorToolButton>? group) {
            var button = new EditorToolButton(icon, tooltip, shortcut, group);
            tools.Add(button);
            Items.Add(button);
            return button;
        }

        private void OnThemeChanged(object? sender, EventArgs e) {
            if (IsDisposed)
                return;

            if (InvokeRequired)
                BeginInvoke(new Action(Invalidate));
            else
                Invalidate(true);
        }

        /// <summary>
        ///     Paints the strip itself. The buttons paint themselves.
        /// </summary>
        /// <remarks>
        ///     Derived from <see cref="ToolStripRenderer"/> directly rather than from
        ///     <c>ToolStripSystemRenderer</c> or <c>ToolStripProfessionalRenderer</c>. Those two
        ///     paint a great deal by default and every unoverridden hook is a place the system
        ///     theme shows through a strip that is otherwise ours. The abstract base paints
        ///     nothing, so what appears is exactly what is written here.
        /// </remarks>
        private sealed class EditorToolStripRenderer : ToolStripRenderer {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
                Color background = EditorTheme.Background(EditorTheme.SurfaceOf(e.ToolStrip?.Parent));

                //Transparent on a page: the real colour comes from the visual style, and painting
                //a guess over it shows as a rectangle a shade off from everything around it.
                if (background.A == 0)
                    return;

                using var brush = new SolidBrush(background);
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
                EditorSurface surface = EditorTheme.SurfaceOf(e.ToolStrip?.Parent);
                using var pen = new Pen(EditorTheme.Separator(surface));

                Rectangle bounds = e.Item.Bounds;
                if (e.Vertical) {
                    int x = bounds.Width / 2;
                    e.Graphics.DrawLine(pen, x, 3, x, bounds.Height - 4);
                }
                else {
                    int y = bounds.Height / 2;
                    e.Graphics.DrawLine(pen, 3, y, bounds.Width - 4, y);
                }
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
                //No border. A strip docked against the control it drives reads as part of it, and a
                //line there would say they are separate things.
            }
        }
    }
}
