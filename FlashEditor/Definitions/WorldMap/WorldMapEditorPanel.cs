using BrightIdeasSoftware;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.UI;
using static FlashEditor.Utils.DebugUtil;

namespace FlashEditor.Definitions.WorldMap {
    /// <summary>
    ///     The World Map Overview tab: index 23, the pre-rendered map the client draws in its
    ///     world-map window.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>This is not the Map tab and the two must not be confused.</b> The Map tab edits index
    ///     5, the terrain the game world is actually built from. This shows index 23, which is a
    ///     separate, pre-rendered picture of that world that the client draws over nothing at all -
    ///     <c>Class278</c> never reads index 5 (<c>InterfaceSettings.java:179</c> hands it index 23
    ///     and nothing else). They are two representations of the same places that can and do
    ///     disagree, so the tab says which one it is showing rather than relying on its name.
    ///     </para>
    ///     <para>
    ///     <b>Three unrelated record families share this index and only one of them is a list.</b>
    ///     The <c>details</c> group holds one record per area, each area's own group holds a
    ///     multi-megabyte tile raster, and a third group per area holds its icons. So the tab is the
    ///     areas as a list, and the selected area's raster and icons beside it - a flat listing of
    ///     all 1043 files would interleave three formats with nothing to say where one ends.
    ///     </para>
    ///     <para>
    ///     <b>Read only, deliberately.</b> All three families re-encode byte for byte, so an encoder
    ///     is not what is missing. What is missing is an edit that means anything: index 23 is
    ///     derived from index 5, so a change made here would be overwritten by any regeneration and
    ///     would not move a single tile in the game. The place to edit a map is the Map tab, and the
    ///     view says so.
    ///     </para>
    /// </remarks>
    public sealed class WorldMapEditorPanel : UserControl {
        /* Consolas 9 on every child. The form puts Consolas 12 on the tab control and everything
           under it inherits, which is half again what these grids are laid out for. */
        private static readonly Font GridFont = new Font("Consolas", 9F);

        /// <summary>
        ///     The descriptor the area list is driven by.
        /// </summary>
        /// <remarks>
        ///     One instance, held rather than built per bind, because <c>DefinitionListPanel.Bind</c>
        ///     treats a different descriptor as a different thing to show and would reload on every
        ///     visit to the tab.
        /// </remarks>
        private static readonly IDefinitionListDescriptor Descriptor = new WorldMapAreaListDescriptor();

        private const string NoCacheText = "No cache loaded";
        private const string NoSelectionText = "Select an area to draw its overview map";

        private readonly Label header = new Label {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = GridFont,
            Text = NoCacheText
        };

        /* Behind an (i), with the clause that stops someone editing the wrong tab kept on screen as
           the summary. The rest is the reasoning, which is worth reading once and not on every
           visit. */
        private readonly InfoAffordance notice = new InfoAffordance {
            Dock = DockStyle.Top,
            Font = GridFont,
            Kind = InfoKind.Limitation,
            Caption = "Read only, and NOT the Map tab",
            Summary = "Read only, and NOT the Map tab",
            Body = "NOT the Map tab. That one edits index 5, the terrain the game world is built from. " +
                   "This is the pre-rendered overview a player opens, which the client draws without " +
                   "reading index 5 at all - so the two can disagree, and editing terrain leaves this " +
                   "picture stale. Read only for that reason: a change made here would move nothing in " +
                   "the game and would be lost the moment index 23 was regenerated."
        };

        private readonly DefinitionListPanel areas = new DefinitionListPanel {
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

        private readonly RasterView preview = new RasterView { Dock = DockStyle.Fill };

        private readonly FastObjectListView iconGrid = new FastObjectListView {
            Dock = DockStyle.Fill,
            Font = GridFont,
            FullRowSelect = true,
            GridLines = true,
            ShowGroups = false,
            UseFiltering = true,
            View = View.Details
        };

        private readonly DetailFieldGrid fields = new DetailFieldGrid();

        /* No splitter states a minimum size. Setting one re-checks the current distance against it,
           and a container is still at its 150x100 default when a field initialiser runs. */
        private readonly SplitContainer listAndPreview = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly SplitContainer previewAndDetail = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        private readonly SplitContainer iconsAndFields = new SplitContainer {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal
        };

        private readonly Button exportPicture = new Button {
            AutoSize = true,
            Enabled = false,
            Font = GridFont,
            Text = "Export this map as PNG..."
        };

        /* A switch rather than a fixed choice: the surface area places 556 icons on a picture whose
           features are one pixel across, and marking all of them buries the terrain underneath. The
           selected icon stays ringed either way. */
        private readonly CheckBox markIcons = new CheckBox {
            AutoSize = true,
            Checked = true,
            Font = GridFont,
            Text = "Mark every icon"
        };

        private readonly Label status = new Label {
            AutoSize = true,
            Font = GridFont,
            Text = string.Empty
        };

        private readonly FlowLayoutPanel actions = new FlowLayoutPanel {
            AutoSize = true,
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };

        private RSCache? cache;
        private WorldMapFloorPalette? palette;
        private WorldMapAreaPicture? shownPicture;
        private Bitmap? shownBitmap;
        private BackgroundWorker? renderer;
        private bool splitterPlaced;

        /// <summary>Creates the panel with its grids already headed.</summary>
        public WorldMapEditorPanel() {
            Dock = DockStyle.Fill;

            BuildIconColumns();
            BuildLayout();

            areas.SelectedRowChanged += (_, _) => ShowArea(areas.SelectedRow as WorldMapAreaListing);
            iconGrid.SelectedIndexChanged += (_, _) =>
                preview.Highlight(iconGrid.SelectedObject as WorldMapIconPlacement);
            exportPicture.Click += (_, _) => ExportPicture();
            markIcons.CheckedChanged += (_, _) => preview.ShowMarks(markIcons.Checked);
        }

        /// <summary>
        ///     Points the tab at a cache, or clears it when given none.
        /// </summary>
        /// <remarks>
        ///     <c>Editor.LoadEditorTab</c> calls this on every visit to the tab, so a rebind of the
        ///     cache already on display has to be a no-op or the selection and the rendered picture
        ///     are thrown away each time. Identity is the right test because opening a cache builds a
        ///     new <see cref="RSCache"/>.
        /// </remarks>
        /// <param name="newCache">The open cache, or null to unbind.</param>
        public void Bind(RSCache? newCache) {
            if (ReferenceEquals(newCache, cache))
                return;

            cache = newCache;
            //Rebuilt with the cache rather than cleared, because everything it memoises is that
            //cache's floor table and map elements.
            palette = newCache == null ? null : new WorldMapFloorPalette(newCache);

            ShowArea(null);
            header.Text = newCache == null
                ? NoCacheText
                : "Index 23 - " + newCache.EnumerateGroups(RSConstants.WORLD_MAP).Count() +
                  " groups holding three unrelated families: one 'details' record per area, one tile " +
                  "raster per area, and one icon group per area. Every one of them is addressed by " +
                  "hashed name, at both the group and the file level.";

            areas.Bind(newCache, Descriptor);
        }

        /// <summary>Releases the bitmap the preview is holding.</summary>
        /// <param name="disposing">Whether managed state should be released.</param>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                renderer?.CancelAsync();
                renderer = null;
                preview.ShowRaster(null, null);
                shownBitmap?.Dispose();
                shownBitmap = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Places the splitters and wraps the notices once layout has given them a size.</summary>
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
        ///     container; it wraps only once <see cref="Control.MaximumSize"/> states a width, and
        ///     then <c>AutoSize</c> gives it the height the wrapped text needs. Measured rather than
        ///     stated, because these labels carry the sentences saying what the tab is not, and a
        ///     sentence cut off half way through is worse than one never written.
        ///     <para>
        ///     Assigning a maximum size lays the panel out again, so each is written only when it
        ///     changes; without that this recurses until the layout engine gives up.
        ///     </para>
        /// </remarks>
        private void WrapNotices() {
            Wrap(header, ClientSize.Width);
            Wrap(previewNote, previewAndDetail.Panel1.ClientSize.Width);
        }

        private static void Wrap(Label label, int width) {
            if (width > 0 && label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
        }

        /// <summary>
        ///     Divides the panel proportionally, once, when it first has a size worth dividing.
        /// </summary>
        /// <remarks>
        ///     A <see cref="SplitContainer"/> defaults to a splitter distance of 50 <i>pixels</i>,
        ///     not half, so the distance has to be stated - and stating it in a designer would make
        ///     it one more literal the form multiplies by its DPI factor.
        /// </remarks>
        private void PlaceSplitters() {
            if (splitterPlaced || listAndPreview.Height < 300 || previewAndDetail.Width < 400)
                return;

            //Set before the assignments, not after: changing a splitter distance lays the panel out
            //again, and this is called from that layout.
            splitterPlaced = true;

            try {
                //A quarter to the area list. There are only 39 rows and the picture is the point of
                //the tab, so the picture gets the rest.
                listAndPreview.SplitterDistance =
                    Math.Max(listAndPreview.Panel1MinSize, listAndPreview.Height / 4);
                previewAndDetail.SplitterDistance =
                    Math.Max(previewAndDetail.Panel1MinSize, previewAndDetail.Width * 3 / 5);
                iconsAndFields.SplitterDistance =
                    Math.Max(iconsAndFields.Panel1MinSize, iconsAndFields.Height / 2);
            }
            catch (InvalidOperationException ex) {
                splitterPlaced = false;
                Debug("World map tab splitters not placed yet: " + ex.Message, LOG_DETAIL.ADVANCED);
            }
        }

        private void BuildIconColumns() {
            //Delegates rather than aspect names: a name looked up by reflection blanks the column
            //when a property is renamed, where a delegate stops compiling.
            /* Narrow, because this grid shares the pane with the field list. The two ids come first
               because they are what a user carries to another tab: the file id addresses the
               placement in this index and the element id addresses the record in config group 36. */
            AddIconColumn("File", 50, icon => icon.Id);
            //Wide enough for the heading itself: a list view column does not grow to its header, it
            //ellipsises it, so "Element" at 60 reads as "Ele...".
            AddIconColumn("Element", 75, icon => icon.MapElementId);
            AddIconColumn("Label", 165, icon => icon.Label);
            AddIconColumn("Sprite", 62, icon => icon.SpriteId < 0 ? string.Empty : icon.SpriteId);
            AddIconColumn("World", 105,
                icon => icon.Element.X + "," + icon.Element.Y + " p" + icon.Element.Plane);
            AddIconColumn("On map", 90,
                icon => icon.IsPlaced ? icon.CanvasX + "," + icon.CanvasY : "off canvas");
            AddIconColumn("Cat", 40, icon => icon.CategoryId < 0 ? string.Empty : icon.CategoryId);
            AddIconColumn("P2P", 40, icon => icon.Element.HiddenOnFreeWorlds ? "yes" : string.Empty);
        }

        private void AddIconColumn(string heading, int width, Func<WorldMapIconPlacement, object?> read) {
            var column = new OLVColumn(heading, null) {
                Width = width,
                Groupable = false,
                IsEditable = false,
                /* ObjectListView evaluates aspects for rows being recycled during a scroll and for
                   cells measured before a model is attached, so a null row is a legitimate state and
                   renders empty. A row of the wrong type still throws, because that could only mean
                   this grid was pointed at something other than an icon placement. */
                AspectGetter = row => row == null
                    ? null
                    : read((WorldMapIconPlacement) row)
            };

            iconGrid.AllColumns.Add(column);
            iconGrid.Columns.Add(column);
        }

        private void BuildLayout() {
            actions.Controls.Add(exportPicture);
            actions.Controls.Add(markIcons);
            actions.Controls.Add(status);

            iconsAndFields.Panel1.Controls.Add(iconGrid);
            iconsAndFields.Panel2.Controls.Add(fields);

            previewAndDetail.Panel1.Controls.Add(preview);
            previewAndDetail.Panel1.Controls.Add(previewNote);
            previewAndDetail.Panel2.Controls.Add(iconsAndFields);

            listAndPreview.Panel1.Controls.Add(areas);
            listAndPreview.Panel2.Controls.Add(previewAndDetail);

            //Docking resolves from the end of the Controls collection backwards, so the strips have
            //to be added after the filled splitter and in inside-out order among themselves.
            Controls.Add(listAndPreview);
            Controls.Add(actions);
            Controls.Add(notice);
            Controls.Add(header);

            //Named for a screen reader only. InfoAffordance does not reparent or position itself
            //from this, so it has to be docked as well, not instead.
            notice.Describes = areas;

            //Bound before any cache arrives so the grid has headings from the start.
            areas.Bind(null, Descriptor);
        }

        /// <summary>
        ///     Draws the selected area, decoding and rendering its raster on a worker.
        /// </summary>
        /// <remarks>
        ///     Not done on the list panel's worker, because that would put every area's raster in
        ///     the list: index 23's rasters total just over 6 MB and the surface alone is 4.7 MB, so
        ///     they are read one at a time, when asked for.
        /// </remarks>
        /// <param name="listing">The selected area, or null.</param>
        private void ShowArea(WorldMapAreaListing? listing) {
            //Cancelled rather than left running. The completion handler also refuses to publish a
            //superseded result, because cancellation is cooperative.
            renderer?.CancelAsync();
            renderer = null;

            fields.ShowFields(listing);
            iconGrid.ClearObjects();
            preview.ShowRaster(null, null);
            shownBitmap?.Dispose();
            shownBitmap = null;
            shownPicture = null;
            exportPicture.Enabled = false;

            if (listing == null || cache == null || palette == null) {
                previewNote.Text = cache == null ? NoCacheText : NoSelectionText;
                status.Text = string.Empty;
                return;
            }

            previewNote.Text = listing.Summary + Environment.NewLine + "Drawing...";
            status.Text = string.Empty;

            RSCache open = cache;
            WorldMapFloorPalette floors = palette;

            var worker = new BackgroundWorker { WorkerSupportsCancellation = true };
            renderer = worker;

            worker.DoWork += (_, e) => {
                var reader = new WorldMapReader(open);
                WorldMapAreaRaster? raster = reader.ReadRaster(listing.InternalName);
                if (raster == null)
                    throw new FileNotFoundException(
                        "Area '" + listing.InternalName + "' has no raster group or no file named '" +
                        WorldMapNaming.RasterFile + "' inside it.");

                IReadOnlyList<WorldMapElement> elements = reader.ReadStaticElements(listing.InternalName);
                e.Result = WorldMapAreaRenderer.Render(listing.Area, raster, elements, floors);
            };

            worker.RunWorkerCompleted += (_, e) => {
                if (!ReferenceEquals(renderer, worker))
                    return;

                renderer = null;

                if (e.Cancelled)
                    return;

                if (e.Error != null) {
                    previewNote.Text = listing.Summary + Environment.NewLine +
                                       "Could not draw this area: " + e.Error.Message;
                    Debug("World map render failed: " + e.Error);
                    return;
                }

                //DoWork assigns Result on every path that is not cancelled or faulted
                Publish(listing, (WorldMapAreaPicture) e.Result!);
            };

            worker.RunWorkerAsync();
        }

        /// <summary>Puts a finished render on screen. UI thread only.</summary>
        /// <param name="listing">The area it was rendered from.</param>
        /// <param name="picture">The finished picture.</param>
        private void Publish(WorldMapAreaListing listing, WorldMapAreaPicture picture) {
            shownPicture = picture;
            previewNote.Text = listing.Summary + Environment.NewLine + picture.Note;

            iconGrid.SetObjects(new List<WorldMapIconPlacement>(picture.Icons));

            WorldMapPictureCounts counts = picture.Counts;
            /* "8x8 block" rather than "zone", though the format calls it one: a zone is also the name
               of the world rectangles in the details record, and those are listed six inches away in
               the field grid. Two different things sharing a word on one screen is worse than a
               slightly long label. */
            status.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} blocks ({1} whole map squares, {2} single 8x8 blocks) holding {3:N0} tiles: " +
                "{4:N0} terrain, {5:N0} decorated carrying {6:N0} object references, {7:N0} blank.",
                counts.Blocks, counts.MapSquareBlocks, counts.Blocks - counts.MapSquareBlocks,
                counts.Tiles, counts.Terrain, counts.Decorated, counts.TileElements, counts.Blank);

            if (!picture.HasImage)
                return;

            shownBitmap = ToBitmap(picture);
            preview.ShowRaster(shownBitmap, picture);
            exportPicture.Enabled = true;
        }

        /// <summary>
        ///     Copies rendered pixels into a bitmap.
        /// </summary>
        /// <remarks>
        ///     A block copy rather than <c>SetPixel</c>: the surface area is 4.7 megapixels and
        ///     <c>SetPixel</c> locks and unlocks the bitmap once each. The layouts line up exactly -
        ///     a .NET <c>int</c> is little-endian on every platform this builds for, so 0xAARRGGBB
        ///     in memory is the B, G, R, A byte order <c>Format32bppArgb</c> wants.
        /// </remarks>
        /// <param name="picture">The rendered picture.</param>
        /// <returns>The bitmap.</returns>
        private static Bitmap ToBitmap(WorldMapAreaPicture picture) {
            var bitmap = new Bitmap(picture.Width, picture.Height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, picture.Width, picture.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try {
                //Row by row rather than one copy: LockBits may hand back a stride wider than the
                //row, and a single copy would then shear the image.
                for (int y = 0; y < picture.Height; y++)
                    Marshal.Copy(picture.Pixels, y * picture.Width, data.Scan0 + y * data.Stride, picture.Width);
            }
            finally {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        private void ExportPicture() {
            if (shownBitmap == null || shownPicture == null ||
                areas.SelectedRow is not WorldMapAreaListing listing)
                return;

            using var dialog = new SaveFileDialog {
                Filter = "PNG image (*.png)|*.png",
                FileName = "worldmap_" + listing.InternalName + ".png"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try {
                shownBitmap.Save(dialog.FileName, ImageFormat.Png);
                status.Text = "Wrote " + shownPicture.Width + "x" + shownPicture.Height + " to " +
                              Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex) {
                //Reported rather than thrown: an exception out of a button handler takes the form down.
                status.Text = "Export failed: " + ex.Message;
                Debug("World map PNG export failed: " + ex);
            }
        }

        /// <summary>
        ///     Draws an area's raster to fit, with its icon positions marked.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Its own control rather than a <see cref="PictureBox"/> in zoom mode, because the icon
        ///     marks have to land on the same transform the picture is drawn through. A picture box
        ///     computes that rectangle privately, so marking on top of one means recomputing the fit
        ///     and hoping the two agree - and a mark half a tile out is invisible as a bug and
        ///     obvious as a wrong answer.
        ///     </para>
        ///     <para>
        ///     Marks rather than sprites. The client blits each icon's index-8 sprite, which is
        ///     around 15 pixels across on a map where a tile is one, so at any zoom that fits an
        ///     area on screen the sprites would cover more of the map than they identify. The list
        ///     beside the picture is where an icon is identified; this says where it is.
        ///     </para>
        /// </remarks>
        private sealed class RasterView : Control {
            /// <summary>Screen pixels a mark reaches at most, however far the picture is magnified.</summary>
            private const float MaxMarkRadius = 4f;

            private Bitmap? image;
            private WorldMapAreaPicture? picture;
            private WorldMapIconPlacement? highlighted;
            private bool marksVisible = true;

            /// <summary>Creates an empty view.</summary>
            internal RasterView() {
                //Every repaint redraws a multi-megapixel scale, so an unbuffered one flickers badly.
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                BackColor = Color.FromArgb(0xFF, 0x1E, 0x1E, 0x1E);
            }

            /// <summary>Shows a rendered area, or clears the view.</summary>
            /// <param name="bitmap">The rendered picture, or null.</param>
            /// <param name="rendered">The picture the bitmap came from, for its icon positions.</param>
            internal void ShowRaster(Bitmap? bitmap, WorldMapAreaPicture? rendered) {
                image = bitmap;
                picture = rendered;
                highlighted = null;
                Invalidate();
            }

            /// <summary>Rings one icon, or clears the ring.</summary>
            /// <param name="icon">The icon to ring, or null.</param>
            internal void Highlight(WorldMapIconPlacement? icon) {
                highlighted = icon;
                Invalidate();
            }

            /// <summary>
            ///     Shows or hides the icon marks, leaving the selected one visible either way.
            /// </summary>
            /// <remarks>
            ///     Worth a switch because the surface area places 556 icons on a picture whose
            ///     features are one pixel across, so marking them all buries the terrain the marks
            ///     are meant to sit on. The selected icon keeps its ring regardless, which is what
            ///     makes the list beside the picture usable as a way of finding one place.
            /// </remarks>
            /// <param name="visible">Whether every icon is marked.</param>
            internal void ShowMarks(bool visible) {
                marksVisible = visible;
                Invalidate();
            }

            /// <summary>Draws the picture to fit, then the icon marks over it.</summary>
            /// <param name="e">The event data.</param>
            protected override void OnPaint(PaintEventArgs e) {
                base.OnPaint(e);

                if (image == null || picture == null) {
                    TextRenderer.DrawText(e.Graphics, "No area drawn", Font, ClientRectangle,
                        Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                RectangleF target = Fit(image.Width, image.Height, ClientSize);
                if (target.Width <= 0 || target.Height <= 0)
                    return;

                /* Nearest neighbour, because every pixel of this image is one stored record and
                   smoothing it invents tiles that are not in the file. Half-pixel offset so a tile
                   lands on the pixel it addresses rather than straddling two when magnified. */
                e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.DrawImage(image, target);

                float scaleX = target.Width / image.Width;
                float scaleY = target.Height / image.Height;

                /* Sized from the zoom rather than fixed. A mark two pixels across is a dot on a
                   surface map shrunk to a quarter scale and a barely visible speck on an eight
                   square dungeon blown up to fill the pane, so a constant radius is wrong at both
                   ends - and at the shrunk end 556 constant marks cover the terrain entirely. */
                float radius = Math.Clamp(Math.Min(scaleX, scaleY) * 1.5f, 1f, MaxMarkRadius);

                using var pen = new Pen(Color.FromArgb(0xFF, 0xFF, 0xD0, 0x40));
                using var ring = new Pen(Color.FromArgb(0xFF, 0xFF, 0x40, 0x40), 2f);

                foreach (WorldMapIconPlacement icon in picture.Icons) {
                    if (!icon.IsPlaced)
                        continue;

                    bool selected = ReferenceEquals(icon, highlighted);
                    if (!marksVisible && !selected)
                        continue;

                    //The canvas counts y northward and the bitmap counts rows downward, the same
                    //flip the renderer applied to the pixels.
                    float cx = target.Left + (icon.CanvasX + 0.5f) * scaleX;
                    float cy = target.Top + (picture.Height - 1 - icon.CanvasY + 0.5f) * scaleY;

                    //The selected one is always findable, whatever the zoom and whatever the switch
                    //says, because picking a row in the list is how a place is located here.
                    float drawn = selected ? Math.Max(radius * 2f, 6f) : radius;
                    e.Graphics.DrawEllipse(selected ? ring : pen,
                        cx - drawn, cy - drawn, drawn * 2, drawn * 2);
                }
            }

            /// <summary>
            ///     The largest rectangle of the control's aspect-preserving fit for an image.
            /// </summary>
            /// <remarks>
            ///     Only ever shrinks below one screen pixel per tile when it has to; it will magnify
            ///     a small area to fill the pane, which is what makes the eight-square areas legible
            ///     at all next to a surface map 2048 tiles across.
            /// </remarks>
            /// <param name="width">The image width.</param>
            /// <param name="height">The image height.</param>
            /// <param name="into">The space available.</param>
            /// <returns>The centred destination rectangle.</returns>
            private static RectangleF Fit(int width, int height, Size into) {
                if (width <= 0 || height <= 0 || into.Width <= 0 || into.Height <= 0)
                    return RectangleF.Empty;

                float scale = Math.Min((float) into.Width / width, (float) into.Height / height);
                float drawnWidth = width * scale;
                float drawnHeight = height * scale;

                return new RectangleF((into.Width - drawnWidth) / 2f, (into.Height - drawnHeight) / 2f,
                    drawnWidth, drawnHeight);
            }
        }
    }
}
