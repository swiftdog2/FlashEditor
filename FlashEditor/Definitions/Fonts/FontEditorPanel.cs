using BrightIdeasSoftware;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Definitions.Editing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using static FlashEditor.Utils.DebugUtil;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     The Fonts tab: index 13's metrics beside the index-8 glyph sheet that draws them.
    /// </summary>
    /// <remarks>
    ///     <b>Two indexes, one asset.</b> Index 13 holds no pixels at all, so a font list on its own
    ///     can show nine numbers and nothing a user could recognise as a font. The glyphs are a
    ///     256-frame sprite set at the same id in index 8, joined here by
    ///     <see cref="FontGlyphSheet"/> and checked per font by <see cref="FontGlyphSheet.Verify"/>
    ///     rather than trusted because the ids line up.
    ///     <para>
    ///     <b>The master list stays a <see cref="DefinitionListPanel"/> descriptor.</b> That panel is
    ///     flat and cannot express master/detail, but it raises
    ///     <see cref="DefinitionListPanel.SelectedRowChanged"/> for exactly this, so the font list is
    ///     still driven by <see cref="FontListDescriptor"/> and only the three detail views belong to
    ///     this control - the same division the Animation tab uses.
    ///     </para>
    ///     <para>
    ///     <b>The detail views load synchronously.</b> One font is one index-13 file of 263 bytes and
    ///     one index-8 group, and the 256 glyph bitmaps are at most 54 by 61 pixels each. The sweep
    ///     behind the master list is the expensive half and is already on a worker; putting a single
    ///     group decode on a second one would buy nothing and add a race between a selection change
    ///     and the pane it fills.
    ///     </para>
    /// </remarks>
    public sealed class FontEditorPanel : UserControl {
        /// <summary>
        ///     The descriptor the font list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind: <see cref="DefinitionListPanel.Bind"/>
        ///     treats a different descriptor as a different thing to show and would re-sweep the
        ///     whole index on every visit to the tab.
        /// </remarks>
        private static readonly IDefinitionListDescriptor Fonts = new FontListDescriptor();

        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        /// <summary>The colour the one-bit glyph masks are tinted with.</summary>
        /// <remarks>
        ///     Chosen here rather than taken from the sheet's palette, which is 0xFFFFFF, 0xFEFEFE or
        ///     0xFDFDFD on all 25 fonts and is a placeholder the client recolours per draw. Drawing
        ///     the stored colour would put white glyphs on a white grid.
        /// </remarks>
        private static readonly Color GlyphInk = Color.FromArgb(0xFF, 0xF0, 0xF0, 0xF0);

        /// <summary>The backdrop a glyph is drawn on, dark enough for a near-white mask to read.</summary>
        private static readonly Color GlyphBackdrop = Color.FromArgb(0xFF, 0x2A, 0x2A, 0x2A);

        /// <summary>The canvas outside the advance box, so the width being edited is a visible region.</summary>
        private static readonly Color OutsideAdvance = Color.FromArgb(0xFF, 0x14, 0x14, 0x14);

        /// <summary>The baseline rule in a glyph tile, which is what heights are compared against.</summary>
        private static readonly Color BaselineRule = Color.FromArgb(0xFF, 0x50, 0x78, 0xA0);

        private readonly DefinitionListPanel fontList = new DefinitionListPanel();

        private readonly FastObjectListView glyphs = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly FastObjectListView kerningGrid = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            View = View.Details,
            Visible = false
        };

        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly TabControl detail = new TabControl { Dock = DockStyle.Fill, Font = GridFont };
        private readonly TabPage glyphPage = new TabPage("Glyphs") { Font = GridFont };
        private readonly TabPage previewPage = new TabPage("Preview") { Font = GridFont };
        private readonly TabPage kerningPage = new TabPage("Kerning") { Font = GridFont };

        private readonly Label header = Caption(NoCacheText);

        /* Every caption is a wrapping label whose height is measured against the width it ends up
           with, and each half of that was a defect first. AutoSize does not wrap, so the longest of
           these ran off the right edge of the pane and the clipped half read as a sentence nobody
           had written; a Bottom-docked AutoSize label drew nothing at all, because the filled grid
           above it claimed the page; and a height stated in whole lines still clipped, because how
           many lines the text wraps to is a property of the pane's width rather than of the text. */
        private readonly Label glyphNote = Caption(
            "Advance width is editable - double click the cell. It restages the whole record. " +
            "The glyph pixels live in index 8 and are not editable here. " +
            "Importing a TTF or OTF is NOT AVAILABLE: it would mean rasterising into the index-8 " +
            "sprite format as well as writing these metrics.");

        private readonly Label previewNote = Caption(
            "A metrics preview, not the client's text renderer. It applies advance widths and " +
            "kerning and nothing else: no <br>, <img=n> or colour markup " +
            "(Class197.method2675:236-268), and no mapping into the cache's own character " +
            "encoding, so a byte above 127 is not Latin-1 here.");

        private readonly Label kerningNote = Caption(string.Empty);

        private readonly TextBox previewText = new TextBox {
            Dock = DockStyle.Top, Font = GridFont, Multiline = true, ScrollBars = ScrollBars.Vertical,
            Text = "Sherlock Holmes 0123456789\r\nThe quick brown fox jumps over the lazy dog."
        };

        private readonly Panel previewSurface = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        private readonly PictureBox previewImage = new PictureBox { SizeMode = PictureBoxSizeMode.AutoSize };

        private readonly FlowLayoutPanel previewControls = new FlowLayoutPanel {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true
        };

        private readonly Label zoomCaption = new Label {
            AutoSize = true, Font = GridFont, Text = "Zoom", TextAlign = ContentAlignment.MiddleLeft
        };

        private readonly NumericUpDown zoom = new NumericUpDown {
            Font = GridFont, Minimum = 1, Maximum = 8, Value = 2, Width = 60
        };

        private readonly CheckBox showBaselines = new CheckBox {
            AutoSize = true, Font = GridFont, Text = "Rule the baselines"
        };

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a font to see its glyphs";

        /// <summary>The rendered glyph tile per character code, rebuilt whenever the font changes.</summary>
        /// <remarks>
        ///     Held rather than drawn in the aspect getter: ObjectListView asks for an image for
        ///     every visible row on every scroll, and rasterising 256 masks per scroll would be
        ///     visible as lag on a grid this small.
        /// </remarks>
        private readonly Dictionary<int, Bitmap> glyphTiles = new Dictionary<int, Bitmap>();

        private RSCache? cache;
        private FontListing? selected;
        private FontGlyphSheet? sheet;
        private Bitmap? previewBitmap;
        private bool splitterPlaced;
        private int glyphTileHeight;

        /// <summary>
        ///     A caption that wraps to the pane it is in and is measured, never stated.
        /// </summary>
        /// <remarks>
        ///     <see cref="Label.AutoSize"/> is deliberately off, because a Label only word-wraps when
        ///     it is not auto-sizing and every one of these is longer than the detail pane is wide.
        ///     The height that follows from that is set by <see cref="FitCaptions"/> once the pane
        ///     has a width to wrap against.
        /// </remarks>
        /// <param name="text">The caption.</param>
        /// <returns>The label.</returns>
        private static Label Caption(string text) {
            return new Label {
                AutoSize = false,
                Dock = DockStyle.Top,
                Font = GridFont,
                Height = GridFont.Height,
                Text = text
            };
        }

        /// <summary>
        ///     Gives every caption the height its own text needs at the width it has.
        /// </summary>
        /// <remarks>
        ///     Measured rather than stated, which is the only thing that holds at every pane width
        ///     and DPI: how many lines a caption wraps to depends on how wide the pane is, so a
        ///     reserved line count is right at one size and clips at every narrower one. The kerning
        ///     note lost its last sentence exactly that way, and the sentence it lost was the one
        ///     saying no shipped font exercises that pane.
        ///     <para>
        ///     Only assigns when the height actually changes. Setting it lays the panel out again,
        ///     and this runs from that layout.
        ///     </para>
        /// </remarks>
        private void FitCaptions() {
            foreach (Label caption in new[] { header, glyphNote, previewNote, kerningNote }) {
                if (caption.Width <= 0 || caption.Dock != DockStyle.Top)
                    continue;

                Size needed = TextRenderer.MeasureText(caption.Text, caption.Font,
                    new Size(caption.Width, 0), TextFormatFlags.WordBreak);
                int height = Math.Max(GridFont.Height, needed.Height + 2);

                if (caption.Height != height)
                    caption.Height = height;
            }
        }

        /// <summary>Creates the panel.</summary>
        public FontEditorPanel() {
            Dock = DockStyle.Fill;
            BuildLayout();

            fontList.SelectedRowChanged += (_, _) => ShowFont(fontList.SelectedRow as FontListing);
            glyphs.CellEditFinished += (_, e) => CommitAdvance(e.RowObject);
            previewText.TextChanged += (_, _) => RedrawPreview();
            zoom.ValueChanged += (_, _) => RedrawPreview();
            showBaselines.CheckedChanged += (_, _) => RedrawPreview();
        }

        /// <summary>
        ///     Points the panel at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already shown has to be a no-op - the sweep behind the list reads every group in
        ///     index 13 and doing it again would also throw away the selection.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            ShowFont(null);
            header.Text = newCache == null ? NoCacheText : NoSelectionText;

            //The descriptor is passed either way: DefinitionListPanel only requires one alongside a
            //non-null cache, and keeping it constant means the columns survive an unbind.
            fontList.Bind(newCache, Fonts);
        }

        /// <summary>Places the splitter once the layout pass has given the container a real width.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitter();
            FitCaptions();
        }

        /// <summary>Releases the glyph tiles and the preview bitmap.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                ReleaseGlyphTiles();
                previewBitmap?.Dispose();
                previewBitmap = null;
                sheet?.Dispose();
                sheet = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a width worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, and
        ///     stating one in the designer would be one more literal the form multiplies by its scale
        ///     factor. A fraction of the measured width is the same division at any DPI. Deferred to
        ///     layout because assigning a distance the control is not yet wide enough for throws.
        /// </remarks>
        private void PlaceSplitter() {
            if (splitterPlaced || listAndDetail.Width < 200)
                return;

            //Set before the assignment, not after: changing the distance lays the panel out again,
            //and this is called from that layout.
            splitterPlaced = true;

            try {
                listAndDetail.SplitterDistance =
                    Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 5);
            } catch (InvalidOperationException ex) {
                //Left for the next layout rather than clamped: a clamped distance sticks, and the
                //user would see a collapsed pane on a window that later has room for both.
                splitterPlaced = false;
                Debug("Fonts tab splitter not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            BuildGlyphColumns();

            //Docking resolves from the end of the Controls collection backwards, so a filled control
            //is added first and the strips that frame it after, or the filled one takes the lot.
            glyphPage.Controls.Add(glyphs);
            glyphPage.Controls.Add(glyphNote);

            previewImage.Location = Point.Empty;
            previewSurface.Controls.Add(previewImage);
            previewControls.Controls.Add(zoomCaption);
            previewControls.Controls.Add(zoom);
            previewControls.Controls.Add(showBaselines);

            //Derived from the font rather than stated, so the box holds the lines it was sized for
            //whatever the form scales to. A literal here is multiplied by the scale factor and clips.
            previewText.Height = previewText.Font.Height * 3;
            previewPage.Controls.Add(previewSurface);
            previewPage.Controls.Add(previewControls);
            previewPage.Controls.Add(previewText);
            previewPage.Controls.Add(previewNote);

            kerningPage.Controls.Add(kerningGrid);
            kerningPage.Controls.Add(kerningNote);

            detail.TabPages.Add(glyphPage);
            detail.TabPages.Add(previewPage);
            detail.TabPages.Add(kerningPage);

            listAndDetail.Panel1.Controls.Add(fontList);
            listAndDetail.Panel2.Controls.Add(detail);
            listAndDetail.Panel2.Controls.Add(header);

            Controls.Add(listAndDetail);
        }

        private void BuildGlyphColumns() {
            //Five rows of text. The per-font magnification below is measured against this, so it is
            //derived from the panel's own font rather than stated in pixels.
            glyphTileHeight = Math.Clamp(GridFont.Height * 5, 48, 96);
            glyphs.RowHeight = glyphTileHeight + 2;

            var image = new OLVColumn("Glyph", null) {
                Width = 160,
                Groupable = false,
                IsEditable = false,
                ImageGetter = row => row is GlyphRow glyph ? TileFor(glyph.Character) : null,
                AspectGetter = _ => string.Empty
            };
            glyphs.AllColumns.Add(image);
            glyphs.Columns.Add(image);

            AddColumn(glyphs, "Code", 60, row => Glyph(row)?.Character);
            AddColumn(glyphs, "Char", 60, row => Glyph(row)?.Label);

            //The one editable column on this grid. Everything else here is index 8's, which this tab
            //does not write.
            var advance = new OLVColumn("Advance", null) {
                Width = 80,
                Groupable = false,
                IsEditable = true,
                AspectGetter = row => Glyph(row)?.Advance
            };
            advance.AspectPutter = (row, value) => {
                if (row is GlyphRow glyph)
                    glyph.Advance = ToByte(value);
            };
            glyphs.AllColumns.Add(advance);
            glyphs.Columns.Add(advance);

            AddColumn(glyphs, "Ink w", 55, row => Glyph(row)?.InkWidth);
            AddColumn(glyphs, "Ink h", 55, row => Glyph(row)?.InkHeight);
            AddColumn(glyphs, "Left", 50, row => Glyph(row)?.OffsetX);
            AddColumn(glyphs, "Top", 50, row => Glyph(row)?.OffsetY);
            AddColumn(glyphs, "Right", 55, row => Glyph(row)?.RightBearing);
            AddColumn(glyphs, "Rows", 55, row => Glyph(row)?.StoredRows);
            AddColumn(glyphs, "Prof top", 75, row => Glyph(row)?.StoredTop);

            glyphs.CellEditActivation = ObjectListView.CellEditActivateMode.DoubleClick;
        }

        /// <summary>
        ///     Adds one read-only column, reading its value through a delegate rather than a name.
        /// </summary>
        /// <remarks>
        ///     Same reasoning as <see cref="DefinitionColumn"/>: a name resolved by reflection blanks
        ///     the column when the property is renamed, where a delegate stops compiling.
        /// </remarks>
        /// <param name="list">The grid to add to.</param>
        /// <param name="heading">The column heading.</param>
        /// <param name="width">The width, in the grid's own pinned font.</param>
        /// <param name="read">Reads the displayed value off a row.</param>
        private static void AddColumn(FastObjectListView list, string heading, int width,
            Func<object, object?> read) {
            //Delegated so the null-row guard has one implementation. Ten copies of this method
            //existed and not one of them had it, which is how closing a cache crashed the
            //interfaces list.
            DetailGrid.AddColumn(list, heading, width, read);
        }

        /// <summary>
        ///     The row as a glyph row, or null when there is no row.
        /// </summary>
        /// <remarks>
        ///     ObjectListView evaluates aspects for rows being recycled during a scroll and for cells
        ///     measured before a model is attached, so a null row is a state rather than a defect. A
        ///     row of the wrong type still throws, because that can only mean this grid was bound to
        ///     something it does not produce.
        /// </remarks>
        /// <param name="row">The row object.</param>
        /// <returns>The typed row, or null.</returns>
        private static GlyphRow? Glyph(object? row) {
            if (row == null)
                return null;
            return row as GlyphRow ?? throw new ArgumentException(
                "The glyph grid reads a GlyphRow but was handed a " + row.GetType().Name + ".", nameof(row));
        }

        private static byte ToByte(object? value) {
            if (value == null)
                return 0;
            return (byte) Math.Clamp(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture),
                byte.MinValue, byte.MaxValue);
        }

        /// <summary>
        ///     Loads the selected font's glyph sheet and fills all three detail views.
        /// </summary>
        /// <param name="row">The selected font, or null.</param>
        private void ShowFont(FontListing? row) {
            selected = row;
            glyphs.ClearObjects();
            kerningGrid.ClearObjects();
            ReleaseGlyphTiles();
            sheet?.Dispose();
            sheet = null;

            if (cache == null || row == null) {
                header.Text = cache == null ? NoCacheText : NoSelectionText;
                kerningNote.Text = string.Empty;
                kerningGrid.Visible = false;
                RedrawPreview();
                return;
            }

            string? joinFailure = null;
            try {
                sheet = FontGlyphSheet.TryLoadSheet(cache, row.Record);
                joinFailure = sheet == null
                    ? "index 8 declares no group " + row.FontId + ", so this font has no glyphs in this cache"
                    : sheet.JoinFailure();
            } catch (Exception ex) {
                //One font's sheet costs its own detail pane, not the tab. A sprite set that will not
                //decode is worth seeing the metrics beside.
                joinFailure = "the index-8 sheet could not be read: " + ex.Message;
                Debug("Font " + row.FontId + " glyph sheet failed: " + ex);
            }

            header.Text = Describe(row, joinFailure);
            BuildGlyphRows(row);
            BuildKerningView(row);
            RedrawPreview();

            //The captions just changed, and a re-measure is what keeps the longest of them from
            //losing its last line. A Text assignment alone does not re-run this control's layout.
            FitCaptions();
        }

        /// <summary>The header line: which font, which layout, and whether its glyph sheet joined.</summary>
        /// <remarks>
        ///     The layout is stated rather than left to be inferred from an empty kerning tab. A
        ///     kerned record and an unkerned one are different lengths and hold different fields, and
        ///     a user who cannot tell which they are looking at reads a legitimately absent kerning
        ///     matrix as a broken pane.
        ///     <para>
        ///     <b>A failure and an oddity are worded so they cannot be confused.</b>
        ///     <see cref="FontGlyphSheet.JoinFailure"/> means the glyphs below are not this font's
        ///     and the views are not to be trusted; <see cref="FontGlyphSheet.Irregularity"/> means
        ///     the pairing is unusual for this build and the views are fine. Only the first is a
        ///     defect, and only the first says so.
        ///     </para>
        /// </remarks>
        /// <param name="row">The selected font.</param>
        /// <param name="joinFailure">Why the glyph sheet is not this font's, or null.</param>
        /// <returns>The header text.</returns>
        private string Describe(FontListing row, string? joinFailure) {
            string name = string.IsNullOrEmpty(row.Name) ? "unnamed" : row.Name!;
            string layout = row.Record.IsKerned
                ? "kerned layout: 3 tables of 256 and 2 edge-profile blocks, and NO stored line height"
                : "unkerned layout: " + FontDefinition.UnkernedLength + " bytes, and no kerning tables";

            if (joinFailure != null || sheet == null) {
                return "Font " + row.FontId + " (" + name + ") - " + layout + "\r\n" +
                       "GLYPH SHEET NOT JOINED, so the views below are not this font's glyphs: " +
                       joinFailure;
            }

            string glyphState = "glyph sheet: index 8 group " + row.FontId + ", " + sheet.FrameCount +
                                " frames, canvas " + sheet.CanvasWidth + "x" + sheet.CanvasHeight +
                                ", baseline at row " + sheet.Baseline;

            string? irregularity = sheet.Irregularity();
            if (irregularity != null) {
                glyphState += "\r\nUNUSUAL FOR THIS BUILD, not a join failure - the glyphs below are " +
                              "still this font's: " + irregularity;
            }

            return "Font " + row.FontId + " (" + name + ") - " + layout + "\r\n" + glyphState;
        }

        /// <summary>Fills the glyph grid with one row per character code.</summary>
        /// <remarks>
        ///     All 256, including the ones with no ink. An empty frame is still an advance the client
        ///     steps by, and space is the clearest case: every font in both caches gives it a
        ///     positive advance and a zero-by-zero frame.
        /// </remarks>
        /// <param name="row">The selected font.</param>
        private void BuildGlyphRows(FontListing row) {
            var rows = new List<GlyphRow>(FontDefinition.CharacterCount);
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                rows.Add(new GlyphRow(row.Record, sheet, character));

            //Sized to the tile this font actually produces rather than to a constant, so a 12 row
            //sheet magnified five times and a 61 row one at 1x both sit in a row that fits them.
            glyphs.RowHeight = sheet == null
                ? glyphTileHeight + 2
                : GlyphBoxRows * GlyphScale + 4;

            glyphs.SetObjects(rows);

            //Scrolled to the first character that draws anything. Codes 0 to 31 are control codes
            //with no ink in every font of both caches, so a grid parked at the top opens on 32
            //blank rows and reads as a tab that failed to load. The rows are still there and still
            //in code order - only the viewport moves.
            for (int character = 0; character < FontDefinition.CharacterCount; character++) {
                if (sheet == null || !sheet.HasInk(character))
                    continue;
                glyphs.EnsureVisible(Math.Min(character + 12, FontDefinition.CharacterCount - 1));
                glyphs.EnsureVisible(character);
                break;
            }
        }

        /// <summary>
        ///     Shows the kerning matrix, or says why there is not one.
        /// </summary>
        /// <remarks>
        ///     <b>The pane no shipped font reaches.</b> No group in either supported cache sets the
        ///     kerning flag, which <c>RealCacheFontTests.NoFontInThisCache_SetsTheKerningFlag</c>
        ///     asserts rather than merely observes. So the grid below is exercised only by a
        ///     hand-built record, and the note says so on screen instead of leaving an empty grid to
        ///     be read as a defect.
        ///     <para>
        ///     Columns are the right-hand characters 32 to 126 and rows the left-hand ones, which is
        ///     the subscript order the client uses -
        ///     <c>aByteArrayArray1516[previous][current]</c> at <c>Class197.method2675:250</c>. The
        ///     matrix is 256 by 256 and the printable range is what a reader can act on; the range is
        ///     stated in the note so nobody reads the grid as the whole matrix.
        ///     </para>
        /// </remarks>
        /// <param name="row">The selected font.</param>
        private void BuildKerningView(FontListing row) {
            sbyte[,]? matrix = row.Record.KerningMatrix();

            if (matrix == null) {
                kerningGrid.Visible = false;
                kerningNote.Text =
                    "Font " + row.FontId + " is unkerned, so it has no kerning matrix. That is the " +
                    "record's own shape and not a missing feature - the client keeps the matrix null " +
                    "for these fonts and every reader checks it (Class197.java:151,249). " +
                    "NO FONT IN EITHER SUPPORTED CACHE SETS THE KERNING FLAG, so this grid is " +
                    "reached only by a hand-built record and is defended by FontGlyphSheetTests and " +
                    "FontDefinitionCodecTests rather than by any sweep over the cache.";
                return;
            }

            BuildKerningColumns();

            var rows = new List<KerningRow>(FontDefinition.CharacterCount);
            for (int left = 0; left < FontDefinition.CharacterCount; left++)
                rows.Add(new KerningRow(left, matrix));

            kerningGrid.SetObjects(rows);
            kerningGrid.Visible = true;
            kerningNote.Text =
                "Font " + row.FontId + " is kerned. Rows are the LEFT character and columns the " +
                "RIGHT one, which is the client's own subscript order (Class197.method2675:250). " +
                "Columns cover the printable range " + FirstKerningColumn + " to " + LastKerningColumn +
                "; the stored matrix is 256 by 256, and a blank cell kerns by zero. A negative entry " +
                "pulls the pair together. The matrix is derived from the edge profiles and the " +
                "advance widths, so editing an advance moves it.";
        }

        /// <summary>The first right-hand character the kerning grid gives a column to.</summary>
        private const int FirstKerningColumn = 32;

        /// <summary>The last right-hand character the kerning grid gives a column to.</summary>
        private const int LastKerningColumn = 126;

        private bool kerningColumnsBuilt;

        private void BuildKerningColumns() {
            if (kerningColumnsBuilt)
                return;
            kerningColumnsBuilt = true;

            AddColumn(kerningGrid, "Left", 70, row => (row as KerningRow)?.Label);

            for (int right = FirstKerningColumn; right <= LastKerningColumn; right++) {
                int column = right;
                AddColumn(kerningGrid, CharacterLabel(column), 34,
                    row => (row as KerningRow)?.At(column));
            }
        }

        /// <summary>
        ///     Redraws the preview from the current text, zoom and font.
        /// </summary>
        /// <remarks>
        ///     The whole point of the tab. An advance-width edit moves every glyph after it by a
        ///     pixel, which is invisible in a grid of numbers and obvious here.
        /// </remarks>
        private void RedrawPreview() {
            Bitmap? previous = previewBitmap;
            previewBitmap = null;
            previewImage.Image = null;
            previous?.Dispose();

            if (sheet == null || !sheet.IsGlyphSheet)
                return;

            try {
                previewBitmap = FontTextLayout.Render(sheet, previewText.Text, GlyphInk, GlyphBackdrop,
                    (int) zoom.Value, showBaselines.Checked);
                previewImage.Image = previewBitmap;
            } catch (Exception ex) {
                //Reported rather than thrown: this runs from a TextChanged handler, and an exception
                //out of one takes the form down.
                Debug("Font preview failed: " + ex);
            }
        }

        /// <summary>
        ///     Writes an advance-width edit back, unless it changes nothing.
        /// </summary>
        /// <remarks>
        ///     The comparison is against what the cache holds now rather than a snapshot from when
        ///     the edit began, so a cell put back to its original value writes nothing. Re-encoding
        ///     rewrites the stored bytes and so the archive CRC, which drags in the reference-table
        ///     entry of every archive packed alongside it.
        ///     <para>
        ///     The whole record goes back, because an advance width is one byte of a 263 byte file
        ///     that has no independently addressable parts. That is what makes the byte-identity
        ///     sweep over index 13 the thing defending this: every other byte has to survive.
        ///     </para>
        /// </remarks>
        /// <param name="row">The edited glyph row.</param>
        private void CommitAdvance(object? row) {
            if (row is not GlyphRow glyph || cache == null || selected == null)
                return;

            try {
                DefinitionAddress address = selected.Address;
                byte[] encoded = selected.Record.Encode().ToArray();
                byte[] stored = cache.ReadFileBytes(RSConstants.FONTS_INDEX, address.GroupId, address.FileId);

                if (encoded.AsSpan().SequenceEqual(stored)) {
                    header.Text = "No change to font " + selected.FontId + " character " + glyph.Character;
                    return;
                }

                cache.WriteFile(RSConstants.FONTS_INDEX, address.GroupId, address.FileId, new JagStream(encoded));

                //The tile shows the advance box, and the kerning matrix is capped by the advance
                //(Class378.method4003:55-57), so both follow the edit.
                DropTile(glyph.Character);
                glyphs.RefreshObject(row);
                if (kerningGrid.Visible)
                    kerningGrid.BuildList(true);
                RedrawPreview();

                header.Text = "Staged font " + selected.FontId + ": character " + glyph.Character +
                              " now advances " + glyph.Advance;
            } catch (Exception ex) {
                //Reported rather than thrown: this runs from a cell editor, and an exception out of
                //an ObjectListView event handler takes the form down.
                header.Text = "Edit failed: " + ex.Message;
                Debug("Font advance edit failed: " + ex);
            }
        }

        /// <summary>
        ///     The tile drawn for one character: its advance box, and its ink placed inside it.
        /// </summary>
        /// <remarks>
        ///     Drawn against the canvas rather than against the ink, so a glyph's height above the
        ///     baseline and its bearings are visible and comparable down the column. The advance box
        ///     is outlined because that is the quantity being edited and an empty glyph would
        ///     otherwise be an empty cell.
        /// </remarks>
        /// <param name="character">The character code.</param>
        /// <returns>The tile, or null when there is no sheet to draw from.</returns>
        private Bitmap? TileFor(int character) {
            if (sheet == null || !sheet.IsGlyphSheet)
                return null;

            if (glyphTiles.TryGetValue(character, out Bitmap? cached))
                return cached;

            int boxWidth = Math.Max(1, sheet.CanvasWidth);
            int boxRows = GlyphBoxRows;
            int boxTop = GlyphBoxTop;
            int scale = GlyphScale;

            var tile = new Bitmap(boxWidth * scale + 2, boxRows * scale + 2, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(tile)) {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.SmoothingMode = SmoothingMode.None;

                //The canvas is filled darker than the advance box, so the width being edited reads as
                //a lit region rather than as a one-pixel outline lost against a mask of the same
                //colour. A zero-advance character is then visibly a sliver rather than an empty cell.
                int advance = sheet.Metrics.AdvanceOf(character);
                graphics.Clear(OutsideAdvance);

                using (var inside = new SolidBrush(GlyphBackdrop))
                    graphics.FillRectangle(inside, 1, 1, Math.Max(1, advance * scale), boxRows * scale);

                //And the baseline, because a glyph's height above it is the thing the eye compares
                //down the column and nothing else in the tile states where it is.
                int baseline = 1 + (sheet.Baseline - boxTop) * scale;
                using (var rule = new Pen(BaselineRule))
                    graphics.DrawLine(rule, 0, baseline, tile.Width, baseline);

                using Bitmap? ink = sheet.RenderInk(character, GlyphInk);
                if (ink != null) {
                    SpriteFrame frame = sheet.FrameFor(character)!;
                    graphics.DrawImage(ink,
                        new Rectangle(1 + frame.OffsetX * scale, 1 + (frame.OffsetY - boxTop) * scale,
                            ink.Width * scale, ink.Height * scale),
                        new Rectangle(0, 0, ink.Width, ink.Height), GraphicsUnit.Pixel);
                }
            }

            glyphTiles[character] = tile;
            return tile;
        }

        /// <summary>
        ///     The rows a glyph tile shows: the metrics' own box, not the whole sprite canvas.
        /// </summary>
        /// <remarks>
        ///     <c>ascent + descent</c>, which is the box the client reserves for a line of this font
        ///     (<c>RSFont.java:942</c>). The sprite canvas is taller - <c>lineHeight + descent</c> -
        ///     and the difference is empty padding above the ink: verdana 11pt's canvas is 38 rows
        ///     for a 15 row box. Drawing the canvas made every glyph a sixth of the tile and
        ///     illegible; drawing the box is what lets the tile magnify. Nothing is lost by it,
        ///     because "no ink reaches above the ascent" is one of the relations the join is checked
        ///     with, on all 25 fonts of both caches.
        /// </remarks>
        private int GlyphBoxRows =>
            sheet == null ? 1 : Math.Max(1, sheet.Metrics.Ascent + sheet.Metrics.Descent);

        /// <summary>The canvas row the glyph tile starts at.</summary>
        private int GlyphBoxTop => sheet == null ? 0 : sheet.CanvasHeight - GlyphBoxRows;

        /// <summary>
        ///     How far the glyph tiles are magnified for the font on display.
        /// </summary>
        /// <remarks>
        ///     Per font, because the boxes in this cache run from 11 rows to 60 and a fixed
        ///     magnification serves neither end: at 1x the small fonts are eight pixels of glyph in a
        ///     row five times that, and at 5x the large ones do not fit in the row at all. Integer
        ///     only, and the drawing never smooths - these are one-bit masks, and interpolating one
        ///     turns the one-pixel bearing an advance edit moves into a smear.
        /// </remarks>
        private int GlyphScale => Math.Clamp(glyphTileHeight / GlyphBoxRows, 1, 6);

        private void DropTile(int character) {
            if (glyphTiles.Remove(character, out Bitmap? tile))
                tile.Dispose();
        }

        private void ReleaseGlyphTiles() {
            foreach (Bitmap tile in glyphTiles.Values)
                tile.Dispose();
            glyphTiles.Clear();
        }

        /// <summary>
        ///     A character code as something readable, without pretending byte 200 is a letter.
        /// </summary>
        /// <remarks>
        ///     Only the printable ASCII range is shown as itself. The cache's own encoding above 127
        ///     is not Latin-1 - the client maps through <c>ScriptRuntime.method3843</c> - so
        ///     rendering byte 233 as an accented e here would be a claim this project has not
        ///     established.
        /// </remarks>
        /// <param name="character">The character code.</param>
        /// <returns>The label.</returns>
        private static string CharacterLabel(int character) {
            if (character == 32)
                return "sp";
            return character > 32 && character < 127 ? ((char) character).ToString() : character.ToString();
        }

        /// <summary>One character of the selected font: its metrics beside its glyph's geometry.</summary>
        /// <remarks>
        ///     The row exists to put the two indexes' halves next to each other. The advance on the
        ///     left is index 13's and everything to its right is index 8's, and the relationship
        ///     between them - the ink fits inside the advance - is what
        ///     <see cref="FontGlyphSheet.Verify"/> proves the pairing with.
        /// </remarks>
        private sealed class GlyphRow {
            private readonly FontDefinition metrics;
            private readonly FontGlyphSheet? sheet;

            internal GlyphRow(FontDefinition metrics, FontGlyphSheet? sheet, int character) {
                this.metrics = metrics;
                this.sheet = sheet;
                Character = character;
            }

            /// <summary>The character code, 0..255.</summary>
            internal int Character { get; }

            /// <summary>The code as something readable.</summary>
            internal string Label => CharacterLabel(Character);

            /// <summary>The stored advance width, the one editable field on this grid.</summary>
            internal int Advance {
                get => metrics.AdvanceOf(Character);
                set => metrics.SetAdvance(Character, value);
            }

            private SpriteFrame? Frame => sheet?.FrameFor(Character);

            /// <summary>Width of the stored ink, or blank with no sheet.</summary>
            internal object? InkWidth => Frame?.SubWidth;

            /// <summary>Height of the stored ink.</summary>
            internal object? InkHeight => Frame?.SubHeight;

            /// <summary>The ink's left bearing within the advance box.</summary>
            internal object? OffsetX => Frame?.OffsetX;

            /// <summary>The ink's top within the canvas, which is what puts it on the baseline.</summary>
            internal object? OffsetY => Frame?.OffsetY;

            /// <summary>
            ///     Pixels of advance left over to the right of the ink.
            /// </summary>
            /// <remarks>
            ///     <c>advance - (offsetX + subWidth)</c>, which is never negative in either supported
            ///     cache - that is one of the four relations the join is proved with. A negative one
            ///     here would mean the sheet beside these metrics is not this font's.
            /// </remarks>
            internal object? RightBearing {
                get {
                    SpriteFrame? frame = Frame;
                    return frame == null ? null : Advance - (frame.OffsetX + frame.SubWidth);
                }
            }

            /// <summary>
            ///     The record's own row count for this character, meaningful only on a kerned font.
            /// </summary>
            /// <remarks>
            ///     Blank on an unkerned record rather than shown as 0, because the record does not
            ///     store the field at all there and a 0 would read as a measurement.
            /// </remarks>
            internal object? StoredRows => metrics.IsKerned ? metrics.GlyphRows[Character] : null;

            /// <summary>The record's own profile origin for this character. See <see cref="StoredRows"/>.</summary>
            internal object? StoredTop => metrics.IsKerned ? metrics.GlyphTops[Character] : null;
        }

        /// <summary>One left-hand character's row of the kerning matrix.</summary>
        private sealed class KerningRow {
            private readonly sbyte[,] matrix;

            internal KerningRow(int left, sbyte[,] matrix) {
                Left = left;
                this.matrix = matrix;
            }

            /// <summary>The left-hand character code.</summary>
            internal int Left { get; }

            /// <summary>The code as something readable.</summary>
            internal string Label => Left + " " + CharacterLabel(Left);

            /// <summary>
            ///     The kern for this pair, or blank when it is zero.
            /// </summary>
            /// <remarks>
            ///     Blank rather than 0 so the pairs that actually kern stand out of a 95 column grid.
            ///     Zero is overwhelmingly the common entry and printing it fills the grid with noise.
            /// </remarks>
            /// <param name="right">The right-hand character code.</param>
            /// <returns>The kern, or null.</returns>
            internal object? At(int right) {
                sbyte kern = matrix[Left, right];
                return kern == 0 ? null : kern;
            }
        }
    }
}
