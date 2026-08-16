using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.UI {
    /// <summary>
    ///     A kind of thing a field can name.
    /// </summary>
    /// <remarks>
    ///     Named per what the id <i>means</i> rather than per index, because two kinds can share an
    ///     index and one kind can be reached from several fields. The index each resolves through is
    ///     stated once, in <see cref="AssetPickerDialog"/>.
    /// </remarks>
    public enum AssetKind {
        /// <summary>A sprite set, index 8.</summary>
        Sprite,

        /// <summary>A model, index 7.</summary>
        Model,

        /// <summary>A procedural texture, index 9.</summary>
        Texture,

        /// <summary>A font, index 13.</summary>
        Font,

        /// <summary>An animation, index 20.</summary>
        Animation,

        /// <summary>
        ///     An object definition, index 16, addressed by <c>group &lt;&lt; 8 | file</c>.
        /// </summary>
        /// <remarks>
        ///     The one kind whose ids are not group ids. Index 16 holds its definitions as files
        ///     inside its groups and a map location names a <em>file</em>, so this kind enumerates
        ///     files and packs the pair exactly as <c>RSCache.GetObjectDefinition</c> unpacks it.
        ///     Listing the group ids instead would have produced a picker whose every entry was the
        ///     wrong number, which is worse than no picker at all.
        /// </remarks>
        Object
    }

    /// <summary>
    ///     Picks an asset by looking at it, rather than by typing a number and hoping.
    /// </summary>
    /// <remarks>
    ///     <b>The problem this exists for.</b> Every field in this editor that names a sprite, a
    ///     model, a texture, a font or an animation is a bare integer in a box. There is no way to
    ///     find out what 4,271 is except to go to that index's tab, sort to it, look, and come back
    ///     - so in practice nobody changes those fields, and the ones that do get changed are
    ///     changed by copying a number from a record that already worked.
    ///     <para>
    ///     <b>It shows every id the index declares, including the ones that cannot be drawn.</b> A
    ///     picker that listed only the ids with a picture would hide exactly the records a user
    ///     most needs to find - the empty sprite, the texture slot with no graph - and would also
    ///     silently become a model picker that lists nothing, since models cannot be rasterised
    ///     here at all. An id with no picture gets a labelled placeholder and stays selectable.
    ///     </para>
    ///     <para>
    ///     <b>Drawn rather than built from controls.</b> Index 7 declares tens of thousands of
    ///     groups; one control per id is not viable, and a <c>ListView</c> in an icon mode needs an
    ///     <c>ImageList</c> holding every image at once, which is the exact thing the thumbnail
    ///     cache exists to avoid. This paints only the rows on screen and asks the cache for only
    ///     those tiles, so the working set is a screenful whatever the index holds.
    ///     </para>
    /// </remarks>
    public sealed class AssetPickerDialog : Form {
        private const int TileSide = 48;
        private const int LabelHeight = 14;
        private const int CellPadding = 6;

        private readonly List<int> allIds = new();
        private readonly List<int> shown = new();
        private readonly AssetKind kind;
        private readonly DefinitionThumbnailCache tiles;

        private readonly TextBox search = new TextBox { Dock = DockStyle.Fill };
        private readonly AssetGrid grid;
        private readonly Label status = new Label { AutoSize = true, Dock = DockStyle.Left };
        private readonly Button accept = new Button { Text = "Choose", DialogResult = DialogResult.OK };
        private readonly Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        private int selectedId = -1;

        /// <summary>Opens a picker over one kind of asset.</summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="assetKind">What kind of thing is being picked.</param>
        /// <param name="currentId">The id the field holds now, selected on open, or -1.</param>
        public AssetPickerDialog(RSCache cache, AssetKind assetKind, int currentId) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            kind = assetKind;
            selectedId = currentId;

            //Its own cache, disposed with the dialog. Borrowing a tab's would leave this dialog's
            //48px tiles filling a budget sized for that tab's 14px ones.
            tiles = new DefinitionThumbnailCache(cache);
            grid = new AssetGrid(this);

            Text = "Choose a " + assetKind.ToString().ToLowerInvariant();
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            Font = EditorTheme.UiFont;
            ClientSize = new Size(680, 520);

            LoadIds(cache, assetKind);
            BuildLayout(assetKind);

            tiles.TilesReady += OnTilesReady;
            search.TextChanged += (_, _) => ApplyFilter();
            grid.Chosen += (_, _) => { DialogResult = DialogResult.OK; Close(); };
            grid.SelectionChanged += (_, id) => {
                selectedId = id;
                UpdateStatus();
            };

            ApplyFilter();
        }

        /// <summary>The id the user chose, or -1.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SelectedId => selectedId;

        /// <summary>
        ///     The cache index a kind of asset is addressed in.
        /// </summary>
        /// <remarks>
        ///     Stated here once rather than at each call site, so a caller says what a field
        ///     <i>means</i> and never which index it happens to live in.
        /// </remarks>
        /// <param name="assetKind">The kind.</param>
        /// <returns>The index id.</returns>
        public static int IndexOf(AssetKind assetKind) {
            return assetKind switch {
                AssetKind.Sprite => RSConstants.SPRITES_INDEX,
                AssetKind.Model => RSConstants.MODELS_INDEX,
                AssetKind.Texture => RSConstants.TEXTURES,
                AssetKind.Font => RSConstants.FONTS_INDEX,
                AssetKind.Animation => RSConstants.ANIMATIONS_INDEX,
                AssetKind.Object => RSConstants.OBJECTS_DEFINITIONS_INDEX,
                _ => throw new ArgumentOutOfRangeException(nameof(assetKind), assetKind, "No index for this kind.")
            };
        }

        /// <summary>
        ///     Opens the picker and returns what was chosen, or null when it was cancelled.
        /// </summary>
        /// <param name="owner">The window to centre on.</param>
        /// <param name="cache">The open cache.</param>
        /// <param name="assetKind">What is being picked.</param>
        /// <param name="currentId">The id the field holds now, or -1.</param>
        /// <returns>The chosen id, or null.</returns>
        public static int? Pick(IWin32Window? owner, RSCache cache, AssetKind assetKind, int currentId) {
            using var dialog = new AssetPickerDialog(cache, assetKind, currentId);

            return dialog.ShowDialog(owner) == DialogResult.OK && dialog.SelectedId >= 0
                ? dialog.SelectedId
                : null;
        }

        /// <summary>Releases the dialog's own thumbnail cache and its background producer.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                tiles.TilesReady -= OnTilesReady;
                tiles.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        ///     Fills the id list for a kind.
        /// </summary>
        /// <remarks>
        ///     Every kind bar one is addressed by group id, and the odd one out is stated here
        ///     rather than left to the caller: an object definition is a <em>file</em> inside an
        ///     index-16 group, and its id is <c>group &lt;&lt; 8 | file</c>, which is the packing
        ///     <c>RSCache.GetObjectDefinition</c> and the map's location records both use.
        /// </remarks>
        private void LoadIds(RSCache cache, AssetKind assetKind) {
            int index = IndexOf(assetKind);

            try {
                if (assetKind == AssetKind.Object) {
                    foreach ((int group, int file) in cache.EnumerateFiles(index))
                        allIds.Add((group << 8) | file);
                }
                else {
                    foreach (int groupId in cache.EnumerateGroups(index))
                        allIds.Add(groupId);
                }
            }
            catch (Exception) {
                //An index whose table will not read leaves an empty picker with an honest status
                //line, rather than taking the dialog down on top of whatever opened it.
            }

            allIds.Sort();
        }

        private void BuildLayout(AssetKind assetKind) {
            var root = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new TableLayoutPanel {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                Padding = new Padding(4)
            };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            top.Controls.Add(new Label { Text = "Find", AutoSize = true, Dock = DockStyle.Left,
                TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            top.Controls.Add(search, 1, 0);
            top.Controls.Add(NoteFor(assetKind), 2, 0);

            var bottom = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(4)
            };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(accept);
            bottom.Controls.Add(status);

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(grid, 0, 1);
            root.Controls.Add(bottom, 0, 2);

            Controls.Add(root);
            AcceptButton = accept;
            CancelButton = cancel;
        }

        /// <summary>
        ///     The (i) beside the search box, saying what this picker can and cannot show.
        /// </summary>
        /// <remarks>
        ///     Three of the five kinds cannot be drawn at all in this application, and a grid of
        ///     identical placeholders with no explanation reads as a broken picker rather than as a
        ///     stated limit.
        /// </remarks>
        private static Control NoteFor(AssetKind assetKind) {
            return assetKind switch {
                AssetKind.Model => Note(InfoKind.Limitation,
                    "Models cannot be drawn here. The only route to model pixels in this editor is " +
                    "OpenGL on the one UI-thread context, and there is no offscreen path, so every " +
                    "model shows as a placeholder carrying its id.\n\n" +
                    "The Models page under Entities does render them, one at a time."),

                AssetKind.Animation => Note(InfoKind.Limitation,
                    "Animations cannot be drawn here. An animation is a sequence of poses applied " +
                    "to a skeleton and then to a model, so drawing one needs the model renderer, " +
                    "which is OpenGL-only in this editor.\n\n" +
                    "Ids are listed so the right one can be chosen by number where it is already " +
                    "known."),

                AssetKind.Font => Note(InfoKind.Limitation,
                    "Fonts show as placeholders. A font is an index-13 metric record paired with an " +
                    "index-8 glyph sheet by id, and rendering a sample needs both laid out " +
                    "together, which is not built yet.\n\n" +
                    "The Fonts page draws the glyph grid for a selected font."),

                AssetKind.Object => Note(InfoKind.Limitation,
                    "Objects cannot be drawn here. An object definition names models, and the only " +
                    "route to model pixels in this editor is OpenGL on the one UI-thread context, " +
                    "so every object shows as a placeholder carrying its id.\n\n" +
                    "The id listed is the one a map location stores: group << 8 | file, which is " +
                    "the same number the Entities page shows against an object.\n\n" +
                    "The Entities page names them. Find the object there and bring the number back."),

                AssetKind.Texture => Note(InfoKind.Help,
                    "A texture is a procedural graph, not a stored picture, so each tile here is " +
                    "evaluated on demand and appears as it lands.\n\n" +
                    "A slot with no graph falls back to its index-26 representative colour, which " +
                    "is exactly what the game draws for it."),

                _ => Note(InfoKind.Help,
                    "Tiles are read in the background and appear as they land. A sprite set shows " +
                    "its first frame.")
            };
        }

        /// <summary>
        ///     An affordance describing the picker rather than one control inside it.
        /// </summary>
        /// <remarks>
        ///     <see cref="InfoAffordance.For"/> takes the control being described, and here there is
        ///     no single one - the note is about the whole grid and its contents. Constructed
        ///     directly so <c>Describes</c> stays unset rather than pointed at something arbitrary,
        ///     which would put a misleading name in the accessibility tree.
        /// </remarks>
        /// <param name="noteKind">Whether it is help or a stated limit.</param>
        /// <param name="body">The paragraph.</param>
        /// <returns>The affordance.</returns>
        private static Control Note(InfoKind noteKind, string body) {
            return new InfoAffordance { Kind = noteKind, Body = body };
        }

        private void ApplyFilter() {
            string text = search.Text.Trim();
            shown.Clear();

            if (text.Length == 0) {
                shown.AddRange(allIds);
            }
            else if (int.TryParse(text, out int exact)) {
                /* A number filters by prefix rather than to the single exact id. Someone typing 42
                   is usually narrowing towards 4271, and jumping straight to a single tile hides
                   the neighbours that make the choice. */
                string prefix = exact.ToString();
                foreach (int id in allIds) {
                    if (id.ToString().StartsWith(prefix, StringComparison.Ordinal))
                        shown.Add(id);
                }
            }

            grid.SetIds(shown, selectedId);
            UpdateStatus();
        }

        private void UpdateStatus() {
            string scope = shown.Count == allIds.Count
                ? shown.Count.ToString("N0") + " " + kind.ToString().ToLowerInvariant() + "s"
                : shown.Count.ToString("N0") + " of " + allIds.Count.ToString("N0");

            status.Text = selectedId >= 0 ? scope + "   -   chosen: " + selectedId : scope;
            accept.Enabled = selectedId >= 0;
        }

        private void OnTilesReady(object? sender, EventArgs e) {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (InvokeRequired)
                BeginInvoke(new Action(grid.Invalidate));
            else
                grid.Invalidate();
        }

        /// <summary>
        ///     The scrolling grid of tiles, painted rather than built from controls.
        /// </summary>
        /// <remarks>
        ///     Nested because it is meaningless outside the dialog and needs the dialog's cache and
        ///     kind. It paints only the rows the clip rectangle touches and asks the thumbnail
        ///     source only for those, so scrolling index 7 costs a screenful of decodes rather than
        ///     sixty thousand.
        /// </remarks>
        private sealed class AssetGrid : UserControl {
            private readonly AssetPickerDialog owner;
            private readonly List<int> ids = new();

            private int selected = -1;
            private int columns = 1;

            internal AssetGrid(AssetPickerDialog owner) {
                this.owner = owner;

                Dock = DockStyle.Fill;
                DoubleBuffered = true;
                AutoScroll = true;
                BackColor = Color.FromArgb(0x20, 0x20, 0x24);
                TabStop = true;
            }

            internal event EventHandler<int>? SelectionChanged;

            internal event EventHandler? Chosen;

            internal void SetIds(IReadOnlyList<int> newIds, int selectedId) {
                ids.Clear();
                ids.AddRange(newIds);
                selected = selectedId;

                Relayout();
                Invalidate();
            }

            protected override void OnResize(EventArgs e) {
                base.OnResize(e);
                Relayout();
            }

            protected override void OnPaint(PaintEventArgs e) {
                owner.tiles.DrainRetired();
                base.OnPaint(e);

                Graphics g = e.Graphics;
                int cellWidth = TileSide + CellPadding * 2;
                int cellHeight = TileSide + LabelHeight + CellPadding * 2;

                int originX = AutoScrollPosition.X;
                int originY = AutoScrollPosition.Y;

                //Only the rows the clip rectangle touches. This is what makes a 63,607-entry index
                //cost a screenful rather than the whole index.
                int firstRow = Math.Max(0, (e.ClipRectangle.Top - originY) / cellHeight);
                int lastRow = (e.ClipRectangle.Bottom - originY) / cellHeight;

                using var idBrush = new SolidBrush(EditorTheme.InkMuted(EditorSurface.Canvas));
                using var chosenBrush = new SolidBrush(EditorTheme.Ink(EditorSurface.Canvas));
                using var placeholderPen = new Pen(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
                using var selectionPen = new Pen(EditorTheme.Accent(EditorSurface.Canvas), 2f);
                using var format = new StringFormat {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
                };

                for (int row = firstRow; row <= lastRow; row++) {
                    for (int column = 0; column < columns; column++) {
                        int i = row * columns + column;
                        if (i >= ids.Count)
                            return;

                        int id = ids[i];
                        int x = originX + column * cellWidth + CellPadding;
                        int y = originY + row * cellHeight + CellPadding;

                        var tileBox = new Rectangle(x, y, TileSide, TileSide);

                        Bitmap? tile = owner.tiles.TryGet(IndexOf(owner.kind), id, TileSide);
                        if (tile != null)
                            g.DrawImage(tile, tileBox);
                        else
                            g.DrawRectangle(placeholderPen, tileBox);

                        if (id == selected)
                            g.DrawRectangle(selectionPen, Rectangle.Inflate(tileBox, 2, 2));

                        g.DrawString(id.ToString(), EditorTheme.NoticeFont,
                            id == selected ? chosenBrush : idBrush,
                            new Rectangle(x, y + TileSide, TileSide, LabelHeight), format);
                    }
                }
            }

            protected override void OnMouseDown(MouseEventArgs e) {
                base.OnMouseDown(e);
                Focus();

                int hit = HitTest(e.X, e.Y);
                if (hit < 0)
                    return;

                selected = hit;
                SelectionChanged?.Invoke(this, hit);
                Invalidate();

                if (e.Clicks >= 2)
                    Chosen?.Invoke(this, EventArgs.Empty);
            }

            /// <summary>Lets the arrow keys and Enter drive the grid, not just the mouse.</summary>
            /// <param name="e">The key data.</param>
            protected override void OnKeyDown(KeyEventArgs e) {
                base.OnKeyDown(e);

                int at = ids.IndexOf(selected);
                if (at < 0)
                    at = 0;

                int moved = e.KeyCode switch {
                    Keys.Left => at - 1,
                    Keys.Right => at + 1,
                    Keys.Up => at - columns,
                    Keys.Down => at + columns,
                    Keys.Home => 0,
                    Keys.End => ids.Count - 1,
                    _ => at
                };

                if (e.KeyCode == Keys.Enter && selected >= 0) {
                    Chosen?.Invoke(this, EventArgs.Empty);
                    return;
                }

                if (moved == at || moved < 0 || moved >= ids.Count)
                    return;

                selected = ids[moved];
                SelectionChanged?.Invoke(this, selected);
                EnsureVisible(moved);
                Invalidate();
                e.Handled = true;
            }

            /// <summary>Arrow keys reach this control rather than moving to the next one.</summary>
            /// <param name="keyData">The key.</param>
            /// <returns>Whether the key is an input key.</returns>
            protected override bool IsInputKey(Keys keyData) {
                return keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down
                    or Keys.Home or Keys.End || base.IsInputKey(keyData);
            }

            private int HitTest(int mouseX, int mouseY) {
                int cellWidth = TileSide + CellPadding * 2;
                int cellHeight = TileSide + LabelHeight + CellPadding * 2;

                int column = (mouseX - AutoScrollPosition.X) / cellWidth;
                int row = (mouseY - AutoScrollPosition.Y) / cellHeight;

                if (column < 0 || column >= columns || row < 0)
                    return -1;

                int i = row * columns + column;
                return i >= 0 && i < ids.Count ? ids[i] : -1;
            }

            private void EnsureVisible(int position) {
                int cellHeight = TileSide + LabelHeight + CellPadding * 2;
                int row = position / Math.Max(1, columns);

                int top = row * cellHeight;
                int bottom = top + cellHeight;

                int viewTop = -AutoScrollPosition.Y;
                int viewBottom = viewTop + ClientSize.Height;

                if (top < viewTop)
                    AutoScrollPosition = new Point(0, top);
                else if (bottom > viewBottom)
                    AutoScrollPosition = new Point(0, bottom - ClientSize.Height);
            }

            private void Relayout() {
                int cellWidth = TileSide + CellPadding * 2;
                int cellHeight = TileSide + LabelHeight + CellPadding * 2;

                columns = Math.Max(1, (ClientSize.Width - SystemInformation.VerticalScrollBarWidth) / cellWidth);
                int rows = (ids.Count + columns - 1) / columns;

                AutoScrollMinSize = new Size(0, rows * cellHeight);
            }
        }
    }
}
