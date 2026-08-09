using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BrightIdeasSoftware;

namespace FlashEditor.UI {
    /// <summary>
    ///     Which of the application's two chrome backgrounds a control is drawn against.
    /// </summary>
    /// <remarks>
    ///     This is about <i>where a control sits</i>, not about a user preference. The editor has
    ///     no dark mode and this does not introduce one: it has a near-white page surface and a set
    ///     of dark canvas surfaces, and a monochrome icon has to be legible on whichever one it
    ///     lands on. Conflating the two ideas is how a theme object turns into a rewrite.
    /// </remarks>
    public enum EditorSurface {
        /// <summary>The near-white page background: the form, the menu strip, and every tab page.</summary>
        Page,

        /// <summary>
        ///     The dark canvas backgrounds - the map views, the world navigator, the sprite and
        ///     glyph canvases, the GL clear.
        /// </summary>
        Canvas
    }

    /// <summary>
    ///     The one place the application's chrome colours, fonts and metrics are stated.
    /// </summary>
    /// <remarks>
    ///     <b>Why this exists.</b> Before it, the editor had no theme object of any kind, and its
    ///     colours were around sixty literals spread across the designer, the map controls, the
    ///     sprite painter and the renderer. That was survivable while nothing needed to draw
    ///     against them. An icon set does: a monochrome glyph has to be tinted for the surface it
    ///     sits on, and there is no way to pick that tint without one statement of what the
    ///     surfaces are.
    ///     <para>
    ///     <b>Static rather than injected.</b> The application has no composition root -
    ///     <c>FlashEditorForm.Main</c> constructs one <c>Editor</c> and every panel news up its own
    ///     children in field initialisers. Threading a service through twenty-five panels would buy
    ///     nothing over a static table.
    ///     </para>
    ///     <para>
    ///     <b>Two inks, not a palette.</b> <see cref="Ink"/> on the page has to clear white;
    ///     on a canvas it has to clear <c>#333333</c>, the lightest of the dark surfaces (the GL
    ///     clear at <c>Editor.cs:501</c>). Everything else here is derived from those two
    ///     constraints. The accent differs per surface for the same reason: the existing
    ///     <c>Color.DarkRed</c> progress accent is invisible on a canvas, so the canvas reuses the
    ///     amber the map overlay and the index labels already use rather than introducing a third.
    ///     </para>
    ///     <para>
    ///     <b>Nothing here scales.</b> The process is pinned DPI-unaware at
    ///     <c>FlashEditorForm.cs:46</c> - a crash fix, because OpenTK's <c>GLControl</c> otherwise
    ///     changes the awareness context mid-session and <c>SetParent</c> then fails so no further
    ///     tab opens. A DPI-unaware process is always told 96 dpi, so the form's
    ///     <c>AutoScaleMode.Dpi</c> against <c>AutoScaleDimensions(96, 96)</c> computes a factor of
    ///     exactly 1.0 on every machine. <see cref="IconSide"/> being 16 therefore means 16
    ///     physical pixels, always, and it is stated once here rather than assumed at each call.
    ///     </para>
    /// </remarks>
    public static class EditorTheme {
        /* The page inks are measured against Color.White, which the form, the menu strip and the
           Meta page all state outright; the twenty-four pages that defer to the visual style paint
           a tab body that is near-white under every non-High-Contrast style. */
        private static readonly Color PageInk = Color.FromArgb(0x1E, 0x1E, 0x1E);
        private static readonly Color PageInkMuted = Color.FromArgb(0x5A, 0x5A, 0x5A);
        private static readonly Color PageInkDisabled = Color.FromArgb(0xA8, 0xA8, 0xA8);
        private static readonly Color PageSeparator = Color.FromArgb(0xD0, 0xD0, 0xD0);
        private static readonly Color PageHover = Color.FromArgb(0xE6, 0xE6, 0xE6);
        private static readonly Color PagePressed = Color.FromArgb(0xCF, 0xCF, 0xCF);
        private static readonly Color PageChecked = Color.FromArgb(0xF0, 0xE2, 0xE2);

        /* #202024 sits between the darkest canvas (#0C0C10, the map void) and the lightest
           (#333333, the GL clear), so a strip carrying it reads as chrome against either. */
        private static readonly Color CanvasBack = Color.FromArgb(0x20, 0x20, 0x24);
        private static readonly Color CanvasInk = Color.FromArgb(0xE6, 0xE6, 0xE6);
        private static readonly Color CanvasInkMuted = Color.FromArgb(0x9A, 0x9A, 0xA0);
        private static readonly Color CanvasInkDisabled = Color.FromArgb(0x5C, 0x5C, 0x64);
        private static readonly Color CanvasSeparator = Color.FromArgb(0x3A, 0x3A, 0x40);
        private static readonly Color CanvasHover = Color.FromArgb(0x30, 0x30, 0x38);
        private static readonly Color CanvasPressed = Color.FromArgb(0x3E, 0x3E, 0x48);
        private static readonly Color CanvasChecked = Color.FromArgb(0x3A, 0x36, 0x2A);

        /* DarkRed is what the two progress bars already use. #FFB826 is IndexLabelPainter's face
           colour, which is the established "look here" mark on a dark surface. Reusing both keeps
           the accent count at the two the application already had. */
        private static readonly Color PageAccent = Color.DarkRed;
        private static readonly Color CanvasAccent = Color.FromArgb(0xFF, 0xB8, 0x26);

        private static bool alternatingRows;
        private static Color alternatingRowColour = Color.FromArgb(0xF2, 0xF2, 0xF2);

        /// <summary>
        ///     Raised when a themed value changes, so painted chrome can drop its caches and
        ///     invalidate.
        /// </summary>
        public static event EventHandler? Changed;

        /// <summary>
        ///     Which surface a control is drawn against.
        /// </summary>
        /// <remarks>
        ///     Decided by the luminance of the control's effective background rather than by
        ///     matching a table of known colours. <b>The page background cannot be read from a
        ///     constant.</b> Twenty-four of the twenty-five tab pages set
        ///     <c>UseVisualStyleBackColor</c>, so what they paint is a property of the running
        ///     Windows visual style and not of this repository, and a toolbar that hardcoded
        ///     <c>#FFFFFF</c> would be visibly wrong under a High Contrast theme. Luminance is
        ///     correct under any style, and it is also correct for a canvas that picks a shade this
        ///     class has never heard of.
        ///     <para>
        ///     <c>BackColor</c> is an ambient property, so the getter already walks to the nearest
        ///     ancestor that states one; no manual parent walk is needed. A null control is treated
        ///     as the page, because that is the surface an unparented control is about to be added
        ///     to in every case in this application.
        ///     </para>
        /// </remarks>
        /// <param name="control">The control to classify, or null.</param>
        /// <returns>The surface it sits on.</returns>
        public static EditorSurface SurfaceOf(Control? control) {
            if (control == null)
                return EditorSurface.Page;

            return IsLight(control.BackColor) ? EditorSurface.Page : EditorSurface.Canvas;
        }

        /// <summary>Which surface a background colour represents.</summary>
        /// <param name="background">The background colour.</param>
        /// <returns>The surface.</returns>
        public static EditorSurface SurfaceOf(Color background) {
            return IsLight(background) ? EditorSurface.Page : EditorSurface.Canvas;
        }

        /// <summary>
        ///     The background a piece of chrome paints when it owns its own strip.
        /// </summary>
        /// <remarks>
        ///     On the page this is deliberately <see cref="Color.Transparent"/>: the page's real
        ///     colour comes from the visual style and repainting it with a guess would show as a
        ///     rectangle a shade off from everything around it.
        /// </remarks>
        /// <param name="surface">The surface.</param>
        /// <returns>The background colour.</returns>
        public static Color Background(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasBack : Color.Transparent;
        }

        /// <summary>The colour a line icon and its label are drawn in.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The ink.</returns>
        public static Color Ink(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasInk : PageInk;
        }

        /// <summary>The ink for secondary text: a numeric id beside a swatch, a unit, a count.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The muted ink.</returns>
        public static Color InkMuted(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasInkMuted : PageInkMuted;
        }

        /// <summary>The ink for a control that cannot currently be used.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The disabled ink.</returns>
        public static Color InkDisabled(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasInkDisabled : PageInkDisabled;
        }

        /// <summary>The colour that marks the active or important thing on a surface.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The accent.</returns>
        public static Color Accent(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasAccent : PageAccent;
        }

        /// <summary>A hairline between groups of controls.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The separator colour.</returns>
        public static Color Separator(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasSeparator : PageSeparator;
        }

        /// <summary>The fill behind a tool the pointer is over.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The hover fill.</returns>
        public static Color HoverFill(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasHover : PageHover;
        }

        /// <summary>The fill behind a tool being pressed.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The pressed fill.</returns>
        public static Color PressedFill(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasPressed : PagePressed;
        }

        /// <summary>The fill behind a tool that is currently selected.</summary>
        /// <param name="surface">The surface.</param>
        /// <returns>The checked fill.</returns>
        public static Color CheckedFill(EditorSurface surface) {
            return surface == EditorSurface.Canvas ? CanvasChecked : PageChecked;
        }

        /// <summary>
        ///     The edge drawn around a selected tool.
        /// </summary>
        /// <remarks>
        ///     The accent, so that which tool is armed survives a user who cannot distinguish the
        ///     checked fill from the hover fill. A fill alone is not enough of a signal for a
        ///     palette where exactly one of twelve tools is live.
        /// </remarks>
        /// <param name="surface">The surface.</param>
        /// <returns>The checked edge colour.</returns>
        public static Color CheckedEdge(EditorSurface surface) {
            return Accent(surface);
        }

        /// <summary>
        ///     The halo an icon needs when it is drawn over cache content rather than over chrome.
        /// </summary>
        /// <remarks>
        ///     Cache content is neither surface - a sprite sits on a mid-grey checkerboard and a map
        ///     tile can be any colour at all - so an icon over it needs its own backing. This is the
        ///     shade <c>MapEditOverlay</c> and <c>IndexLabelPainter</c> already use for exactly that,
        ///     reused rather than a third one invented beside them.
        /// </remarks>
        public static Color ContentHalo { get; } = Color.FromArgb(200, 0x06, 0x08, 0x10);

        /// <summary>
        ///     Consolas 9pt, the panel standard.
        /// </summary>
        /// <remarks>
        ///     Sixteen panels each declare this same font privately, every one with the same comment
        ///     explaining that the tab deck is Consolas 12 and children would otherwise inherit it.
        ///     New chrome reads it from here instead of adding a seventeenth copy.
        /// </remarks>
        public static Font UiFont { get; } = new Font("Consolas", 9F);

        /// <summary>Consolas 9pt bold, for a heading inside a panel.</summary>
        public static Font UiFontBold { get; } = new Font("Consolas", 9F, FontStyle.Bold);

        /// <summary>Consolas 8pt, for the explanatory notes docked into pages.</summary>
        public static Font NoticeFont { get; } = new Font("Consolas", 8F);

        /// <summary>
        ///     The side of a line icon, in pixels.
        /// </summary>
        /// <remarks>
        ///     16, and it never changes: the process is DPI-unaware, so a logical pixel is a
        ///     physical pixel on every machine and no caller is ever handed a scaled request.
        /// </remarks>
        public static int IconSide { get; } = 16;

        /// <summary>The side of a square tool button, icon plus padding.</summary>
        public static int ToolButtonSide { get; } = 24;

        /// <summary>The padding inside a tool strip, around its buttons.</summary>
        public static int ToolStripPadding { get; } = 2;

        /// <summary>
        ///     Whether grids shade alternate rows.
        /// </summary>
        /// <remarks>
        ///     Owned here rather than by the View menu handler, which reached four grids by name -
        ///     the reference table, container and sprite lists and the entity page - and missed
        ///     every other <c>DefinitionListPanel</c> page in the application even though the panel
        ///     has exposed <c>SetAlternatingRows</c> the whole time.
        /// </remarks>
        public static bool AlternatingRows {
            get => alternatingRows;
            set {
                if (alternatingRows == value)
                    return;
                alternatingRows = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>The shade alternate rows are drawn in when <see cref="AlternatingRows"/> is set.</summary>
        public static Color AlternatingRowColour {
            get => alternatingRowColour;
            set {
                if (alternatingRowColour == value)
                    return;
                alternatingRowColour = value;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        }

        /// <summary>
        ///     Pushes the current alternating-row choice into every grid under a control.
        /// </summary>
        /// <remarks>
        ///     Walks the tree rather than taking a list, so a page that grows a second grid is
        ///     covered without anyone remembering to add it - which is the failure the hand-written
        ///     four-grid handler had.
        ///     <para>
        ///     Deliberately does <b>not</b> touch fonts. Several panels choose a font other than
        ///     <see cref="UiFont"/> on purpose, and a recursive font assignment would silently undo
        ///     those choices - the column widths a descriptor states are measured against the font
        ///     its panel pins, so overriding it would also mis-size every grid.
        ///     </para>
        /// </remarks>
        /// <param name="root">The control to walk, or null.</param>
        public static void ApplyAlternatingRows(Control? root) {
            if (root == null)
                return;

            /* The grids are reached directly rather than through the panels that own them.
               DefinitionListPanel keeps its ObjectListView private and exposes SetAlternatingRows,
               but the walk finds that same grid as a descendant anyway, so going through the
               wrapper would shade some grids twice and the bespoke ones not at all. */
            foreach (Control control in Descendants(root)) {
                if (control is not ObjectListView grid)
                    continue;

                grid.UseAlternatingBackColors = alternatingRows;
                grid.AlternateRowBackColor = alternatingRowColour;
                grid.Refresh();
            }
        }

        /// <summary>
        ///     Whether a colour is light enough that dark ink reads against it.
        /// </summary>
        /// <remarks>
        ///     Rec. 601 luma, which is the weighting GDI itself uses for greyscale and is close
        ///     enough for a two-way classification. A fully transparent colour counts as light: an
        ///     unpainted control shows whatever is behind it, and in this application that is a
        ///     page.
        /// </remarks>
        /// <param name="colour">The colour.</param>
        /// <returns>Whether dark ink is the right choice against it.</returns>
        private static bool IsLight(Color colour) {
            if (colour.A == 0)
                return true;

            return (0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B) >= 128.0;
        }

        private static IEnumerable<Control> Descendants(Control root) {
            foreach (Control child in root.Controls) {
                yield return child;

                foreach (Control descendant in Descendants(child))
                    yield return descendant;
            }
        }
    }
}
