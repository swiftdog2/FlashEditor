using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     The Materials tab: index 26, the roster of texture slots and the render state attached to
    ///     each one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This index was decoded on every cache open and displayed nowhere.</b> It drives the
    ///     renderer - it is what says a texture id exists at all, how the pixels are treated, and what
    ///     flat colour to draw when there are no pixels to generate - and until this tab existed none
    ///     of its nineteen columns could be seen, let alone changed.
    ///     </para>
    ///     <para>
    ///     <b>The slots with no graph are the point of the grid, not noise in it.</b> Index 26 and
    ///     index 9 hold the same number of records in the vanilla b639 capture, which invites the
    ///     conclusion that they are one population; in the repack the table is larger and its tail has
    ///     no procedural content at all. For those ids <c>field1831</c> is the whole of what a player
    ///     ever sees, so a grid that showed only the ids index 9 knows about would hide exactly the
    ///     rows where this table is the only thing there is. Both figures are derived from the loaded
    ///     cache and shown in the header rather than written down here.
    ///     </para>
    ///     <para>
    ///     <b>An edit rewrites the whole index and the tab says so.</b> The file is column-major, so
    ///     one record's 23 bytes are scattered across nineteen places in it and there is no smaller
    ///     unit to stage. What keeps that safe is the encoder rather than the size of the write: a
    ///     column nobody edited is replayed from the bytes it was decoded from, so the rest of the
    ///     file comes back unchanged - including the bytes that decode many-to-one and could not be
    ///     rebuilt from a field.
    ///     </para>
    /// </remarks>
    public sealed class MaterialEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select a texture slot to see what it stores";

        //AutoSize rather than stated heights, so the lines these need are the lines they get.
        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        private readonly Label notice = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = "Two of the nineteen columns have established meanings. field1831 is the texture's " +
                   "representative colour in raw 16-bit RS HSL - the cell holds the stored value and the swatch " +
                   "is what the client resolves it to - and field1824 is the pixel transposition flag the graph " +
                   "evaluator is driven by. The other seventeen carry the client's own field names because " +
                   "nothing settles what they mean, and a name invented here would be read as settled: " +
                   "field1835 was once taken for a tint and multiplied into the generated pixels, which scaled " +
                   "every texture in the editor towards black."
        };

        private readonly Label cost = new Label {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Font = GridFont,
            Text = "Editing any cell stages the whole index-26 file: the format is column-major, so one record's " +
                   "23 bytes sit in nineteen different places in it and there is no smaller unit to write. " +
                   "Columns nobody edited are replayed byte for byte, and a field put back where it started " +
                   "stages nothing at all. Nothing reaches disk until the cache is saved."
        };

        private readonly DefinitionListPanel materials = new DefinitionListPanel {
            //Bound with a null cache before a cache arrives so the grid keeps its headings, and the
            //panel's own default would then claim no cache is loaded.
            EmptyMessage = NoCacheText
        };

        private readonly Label previewNote = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoSelectionText
        };

        private readonly MaterialPreview preview = new MaterialPreview();

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer previewAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private RSCache? cache;
        private DefinitionThumbnailCache? tiles;
        private bool splittersPlaced;

        /// <summary>Creates the panel with its grid headings already in place.</summary>
        public MaterialEditorPanel() {
            Dock = DockStyle.Fill;

            BuildLayout();

            materials.SelectedRowChanged += (_, _) => ShowRecord(materials.SelectedRow as MaterialListing);
            materials.RowsLoaded += (_, _) => DescribeIndex();
            materials.RowCommitted += (_, _) => AfterEdit();
        }

        /// <summary>
        ///     Points the tab at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection and the loaded rows are
        ///     thrown away each time. Identity is the right test because opening a cache builds a new
        ///     <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;

            /* Rebuilt rather than cleared: the tiles it holds were rendered from the cache being
               replaced. Detached from the grid first, because the property unhooks the event the
               producer raises and a disposed cache must not be left wired to a live panel. */
            materials.Thumbnails = null;
            preview.Bind(null, null);
            if (tiles != null)
                tiles.TilesReady -= OnTilesReady;
            tiles?.Dispose();
            tiles = newCache == null ? null : new DefinitionThumbnailCache(newCache);
            if (tiles != null)
                tiles.TilesReady += OnTilesReady;
            materials.Thumbnails = tiles;

            ShowRecord(null);
            header.Text = newCache == null ? NoCacheText : "Reading index 26";

            //A fresh descriptor either way: DefinitionListPanel treats the same cache and descriptor
            //pair as the same thing to show, so reusing one would leave the previous cache's rows up.
            materials.Bind(newCache, new MaterialListDescriptor());
        }

        /// <summary>
        ///     Releases the thumbnail cache, which owns a background thread and a pile of bitmaps.
        /// </summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                materials.Thumbnails = null;
                preview.Bind(null, null);
                if (tiles != null)
                    tiles.TilesReady -= OnTilesReady;
                tiles?.Dispose();
                tiles = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Places the splitters once the layout pass has given the containers a real size.</summary>
        /// <param name="levent">The event data.</param>
        protected override void OnLayout(LayoutEventArgs levent) {
            base.OnLayout(levent);
            PlaceSplitters();
            WrapNotices();
        }

        /// <summary>
        ///     Lets the explanatory labels wrap instead of running off the right edge.
        /// </summary>
        /// <remarks>
        ///     An <c>AutoSize</c> label docked to an edge grows sideways and is clipped by its
        ///     container; it only wraps once its <see cref="Control.MaximumSize"/> states a width, and
        ///     then <c>AutoSize</c> gives it the height the wrapped text needs. These labels carry the
        ///     sentences that say what an edit costs, and half a sentence is worse than none.
        ///     <para>
        ///     Assigning a maximum size lays the panel out again, so each is written only when it
        ///     changes; without that this recurses until the layout engine gives up.
        ///     </para>
        /// </remarks>
        private void WrapNotices() {
            Wrap(header, ClientSize.Width);
            Wrap(notice, ClientSize.Width);
            Wrap(cost, ClientSize.Width);
            Wrap(previewNote, previewAndFields.Panel1.ClientSize.Width);
        }

        private static void Wrap(Label label, int width) {
            if (width > 0 && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>, not
        ///     half, so the distance has to be stated - and stating it in a designer would make it one
        ///     more literal a scaling pass could multiply.
        /// </remarks>
        private void PlaceSplitters() {
            if (splittersPlaced || listAndDetail.Width < 400 || previewAndFields.Height < 200)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splittersPlaced = true;

            try {
                //Two thirds to the grid. It carries twenty-two columns and is the subject of the tab;
                //the preview is one square.
                listAndDetail.SplitterDistance =
                    Math.Max(listAndDetail.Panel1MinSize, listAndDetail.Width * 2 / 3);
                previewAndFields.SplitterDistance =
                    Math.Max(previewAndFields.Panel1MinSize, previewAndFields.Height / 2);
            }
            catch (InvalidOperationException ex) {
                splittersPlaced = false;
                Debug("Materials tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildLayout() {
            previewAndFields.Panel1.Controls.Add(preview);
            previewAndFields.Panel1.Controls.Add(previewNote);
            previewAndFields.Panel2.Controls.Add(fields);

            listAndDetail.Panel1.Controls.Add(materials);
            listAndDetail.Panel2.Controls.Add(previewAndFields);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter and in inside-out order among themselves.
            Controls.Add(listAndDetail);
            Controls.Add(cost);
            Controls.Add(notice);
            Controls.Add(header);

            //Bound before any cache arrives so the grid has its headings from the start.
            materials.Bind(null, new MaterialListDescriptor());
        }

        /// <summary>
        ///     States the shape of the index from what was actually loaded.
        /// </summary>
        /// <remarks>
        ///     Counted rather than quoted. The two supported caches disagree about index 26 and about
        ///     index 9, so any figure written into this file would be true of one of them and quietly
        ///     wrong about the other - and the relationship between the two indexes is the thing this
        ///     tab exists to make visible.
        /// </remarks>
        private void DescribeIndex() {
            if (cache == null) {
                header.Text = NoCacheText;
                return;
            }

            int declared = TextureManager.Materials?.Count ?? materials.Rows.Count;
            int present = materials.Rows.Count;
            int withGraph = 0;

            foreach (object row in materials.Rows)
                if (row is MaterialListing listing && listing.Record.graph != null)
                    withGraph++;

            header.Text = "Index 26 - the table declares " + declared + " texture slots, " + present +
                          " of which carry a material record, and index 9 holds a procedural graph for " +
                          withGraph + " of those. The rest draw field1831 and nothing else.";
        }

        /// <summary>Shows one record's fields and preview, or clears them when there is none.</summary>
        /// <param name="listing">The selected row, or null.</param>
        private void ShowRecord(MaterialListing? listing) {
            fields.ShowFields(listing);
            preview.Bind(tiles, listing);

            if (listing == null) {
                previewNote.Text = cache == null ? NoCacheText : NoSelectionText;
                return;
            }

            previewNote.Text = listing.Summary + Environment.NewLine +
                               "The preview is this editor's own evaluation of the index-9 graph, not the " +
                               "client's raster. Where there is no graph - and while one is still being " +
                               "evaluated - it is the flat colour field1831 resolves to.";
        }

        /// <summary>
        ///     Redraws everything derived from the edited record.
        /// </summary>
        /// <remarks>
        ///     The tiles are keyed by id, so an edited record would otherwise keep the picture it had
        ///     before the edit - and for a slot with no graph that picture <i>is</i> the field that was
        ///     just changed. Dropping the whole cache rather than one entry: it exposes no per-id
        ///     eviction, and re-rendering the visible rows costs far less than an edit that appears not
        ///     to have taken.
        /// </remarks>
        private void AfterEdit() {
            tiles?.Clear();
            fields.ShowFields(materials.SelectedRow as MaterialListing);
            preview.Invalidate();
        }

        /// <summary>Repaints the preview when a queued tile lands.</summary>
        /// <param name="sender">The tile cache.</param>
        /// <param name="e">The event data.</param>
        private void OnTilesReady(object? sender, EventArgs e) {
            if (IsDisposed || !IsHandleCreated)
                return;

            //Raised on the producer thread, so the repaint has to be marshalled.
            if (preview.InvokeRequired)
                preview.BeginInvoke(new Action(preview.Invalidate));
            else
                preview.Invalidate();
        }

        /// <summary>
        ///     One texture slot as a picture: its graph where index 9 holds one, its declared colour
        ///     where it does not.
        /// </summary>
        /// <remarks>
        ///     A control that paints from the tile cache rather than one holding a <see cref="Bitmap"/>
        ///     of its own. The cache owns every tile it hands out and frees evicted ones from the list
        ///     panel's paint, so a picture box keeping a reference to one would be showing memory that
        ///     had already been released.
        /// </remarks>
        private sealed class MaterialPreview : Control {
            /// <summary>
            ///     The side the graph is evaluated at.
            /// </summary>
            /// <remarks>
            ///     Fixed rather than taken from the control's size: evaluation is per pixel per node
            ///     and the cache is keyed by side, so following a resize would render the texture
            ///     again for every intermediate width the user dragged through. 128 is what the
            ///     Textures tab asks for, so a slot the user has already seen there is a cache hit.
            /// </remarks>
            private const int RequestedSide = 128;

            private IDefinitionThumbnailSource? source;
            private int textureId = -1;
            private bool hasGraph;
            private int rgb;

            internal MaterialPreview() {
                Dock = DockStyle.Fill;
                DoubleBuffered = true;

                //A neutral dark grey. The subject here is a colour, and a background that is itself a
                //colour biases the eye judging it.
                BackColor = Color.FromArgb(0xFF, 0x28, 0x28, 0x28);
            }

            /// <summary>Points the preview at a record, or clears it.</summary>
            /// <remarks>
            ///     Not called <c>Show</c>: this derives from a <see cref="Control"/>, and an overload
            ///     of <c>Control.Show</c> that means something else is the kind of name that reads
            ///     correctly and does the wrong thing.
            /// </remarks>
            /// <param name="tiles">Where rendered graphs come from, or null.</param>
            /// <param name="listing">The record to draw, or null.</param>
            internal void Bind(IDefinitionThumbnailSource? tiles, MaterialListing? listing) {
                source = tiles;
                textureId = listing?.TextureId ?? -1;
                hasGraph = listing?.Record.graph != null;
                rgb = listing?.RepresentativeRgb ?? 0;
                Invalidate();
            }

            /// <summary>Draws the texture, square and centred.</summary>
            /// <param name="e">The paint data.</param>
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);

                Graphics graphics = e.Graphics;
                graphics.Clear(BackColor);

                if (textureId < 0)
                    return;

                int side = Math.Min(ClientSize.Width, ClientSize.Height);
                if (side <= 0)
                    return;

                var box = new Rectangle((ClientSize.Width - side) / 2, (ClientSize.Height - side) / 2,
                    side, side);

                if (hasGraph && source != null) {
                    Bitmap? tile = source.TryGet(RSConstants.MATERIALS, textureId, RequestedSide);

                    if (tile != null) {
                        /* Nearest neighbour, because a texture is pixels rather than a photograph and
                           this is drawn larger than it was evaluated. Smoothing it would invent
                           detail the graph did not produce. */
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(tile, box);
                        return;
                    }
                }

                //Either there is no graph, in which case this is the whole of what the client draws
                //for the id, or one is still being evaluated. The note beside the preview says which.
                using var brush = new SolidBrush(Color.FromArgb(0xFF, (rgb >> 16) & 0xFF,
                    (rgb >> 8) & 0xFF, rgb & 0xFF));
                graphics.FillRectangle(brush, box);
            }
        }
    }
}
