using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.Cache.Util;

//System.Drawing.Region arrives via the WinForms implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     A slippy-map view of the whole world, drawn from a cache of per-square bitmaps.
    /// </summary>
    /// <remarks>
    ///     Replaces the single-scene viewer, which could not scale: rendering the world into one
    ///     bitmap at four pixels per tile would need 65,536 by 65,536 pixels, and 1684 separate
    ///     square bitmaps at that zoom is 430 MB. This draws one cached bitmap per <em>visible</em>
    ///     square at whichever pyramid level matches the zoom, so the pixels held are bounded by the
    ///     viewport rather than by the world.
    ///
    ///     Squares that have not been rendered yet are drawn as a translucent presence rectangle, so
    ///     the world's silhouette is complete from the first frame and fills in with real terrain as
    ///     the background sweep reaches it. That is what makes "see every region at once" true
    ///     immediately rather than after a full decode.
    /// </remarks>
    public sealed class WorldMapViewControl : Control {
        /// <summary>
        ///     Zoom the view opens at: one pixel per tile, about 25 by 16 squares on screen.
        /// </summary>
        /// <remarks>
        ///     "Every region at once" and "open on region 50,50" pull against each other - a zoom
        ///     that fits all 16,384 tiles into a 1200 pixel viewport is unreadable and says no more
        ///     than the world navigator already does. Opening here and offering Fit world, Home and
        ///     a scrub-able navigator gives both without guessing.
        /// </remarks>
        public const double InitialPixelsPerTile = 1.0;

        /// <summary>Region X the view opens centred on.</summary>
        public const int InitialRegionX = 50;

        /// <summary>Region Y the view opens centred on. Lumbridge, which is where the cache starts.</summary>
        public const int InitialRegionY = 50;

        /// <summary>Zoom at or above which a tile is big enough to click accurately.</summary>
        /// <remarks>
        ///     Editing is gated on this so the question of pinning never arises: below two pixels per
        ///     tile a tile is sub-pixel, and pinning every visible square at the coarsest zoom would
        ///     pin all 1684 of them, which is 590 MB of decoded squares.
        /// </remarks>
        public const double MinimumEditingPixelsPerTile = 2.0;

        /// <summary>
        ///     Wheel notches per doubling of the zoom.
        /// </summary>
        /// <remarks>
        ///     About nine percent a notch, 64 notches from one end of the range to the other. The
        ///     old viewer doubled or halved per notch, which is a whole octave a click and made the
        ///     wheel unusable for framing anything.
        /// </remarks>
        private const double ZoomNotchesPerOctave = 8.0;

        /// <summary>
        ///     How many missing tiles one paint may promote to the priority queue.
        /// </summary>
        /// <remarks>
        ///     At "Fit world" every one of the 1684 squares is on screen and missing, and handing
        ///     all of them over turns the priority queue into a second, distance-ordered sweep that
        ///     drains before the row-major one is ever consulted. That is exactly the order
        ///     <c>MapTileRenderService.RequestOverviewSweep</c> exists to avoid: a constant-distance
        ///     ring spans far more squares than the store's 128-square budget, so each apron
        ///     neighbour is decoded, evicted and decoded again. Capping keeps the priority queue
        ///     meaning "what the user is looking at" and leaves the bulk fill to the sweep.
        /// </remarks>
        private const int MaxVisibleRequestsPerPaint = 64;

        private readonly System.Windows.Forms.Timer repaintTimer = new System.Windows.Forms.Timer { Interval = 33 };
        private readonly List<MapTileKey> misses = new List<MapTileKey>();

        /// <summary>
        ///     The cursor highlight and the post-edit flashes.
        /// </summary>
        /// <remarks>
        ///     Drawn in <see cref="OnPaint"/> over the blitted tiles and never through the tile
        ///     cache. A highlight routed through the cache would invalidate a square, which costs a
        ///     decode, a blend and a relief pass - once per mouse move.
        /// </remarks>
        private readonly MapEditOverlay overlay = new MapEditOverlay();

        /// <summary>
        ///     What the overlay covered at the last paint, so the next change can erase it.
        /// </summary>
        /// <remarks>
        ///     Invalidating only the new highlight leaves the old one on screen: nothing else
        ///     repaints the tile the cursor has just left. Every overlay change therefore
        ///     invalidates the union of where it was and where it is going.
        /// </remarks>
        private Rectangle overlayPainted;

        private MapSquareStore store;
        private MapTileRenderService service;

        private int plane;
        private MapLayers layers = MapLayers.Default;
        private float reliefStrength = 0.65f;
        private int generation;

        private bool leftDown;
        private bool dragExceeded;
        private bool dragSelecting;

        //Nullable, unlike the lastHover beside it. That one predates the project's nullable
        //annotations and carries three warnings for it; a new field must not add a fourth.
        private TileHit? lastDragTile;
        private Point pressScreen;
        private double pressCentreX;
        private double pressCentreY;
        private bool middleDragging;

        /// <summary>
        ///     Whether space is currently held, for the space plus left-drag pan.
        /// </summary>
        /// <remarks>
        ///     Tracked rather than read from <c>Control.ModifierKeys</c>, which only ever reports
        ///     Shift, Control and Alt. Testing it for <c>Keys.Space</c> is always false, so the
        ///     gesture fell through to the ordinary left-button path and a short space-drag with a
        ///     paint tool selected edited the tile it was meant to pan away from.
        /// </remarks>
        private bool spaceHeld;

        private int lastReadyCount = -1;
        private TileHit lastHover;

        /// <summary>Raised when the cursor moves onto a different tile.</summary>
        public event EventHandler<TileHit> TileHovered;

        /// <summary>Raised on a left click that was not a drag.</summary>
        public event EventHandler<TileHit> TileClicked;

        /// <summary>Raised on the tile a left-button drag started on, while <see cref="DragSelects"/>.</summary>
        public event EventHandler<TileHit>? DragStarted;

        /// <summary>Raised on each new tile a left-button drag reaches.</summary>
        public event EventHandler<TileHit>? DragMoved;

        /// <summary>Raised when a left-button drag ends, whether or not it moved.</summary>
        public event EventHandler? DragFinished;

        /// <summary>Raised when the camera moves or zooms.</summary>
        public event EventHandler ViewChanged;

        /// <summary>
        ///     Raised when the plane changes from inside the view, for example by Ctrl+wheel.
        /// </summary>
        /// <remarks>
        ///     Kept separate from <see cref="ViewChanged"/> so a plane combo can follow the wheel
        ///     without repainting on every pan.
        /// </remarks>
        public event EventHandler PlaneChanged;

        /// <summary>Creates the control.</summary>
        public WorldMapViewControl() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.FromArgb(20, 20, 24);

            Camera = new MapCamera();
            Camera.SetPixelsPerTile(InitialPixelsPerTile);

            repaintTimer.Tick += (_, _) => {
                if (IsDisposed)
                    return;

                //Flashes fade on a clock of their own: between the edit and the fade finishing
                //nothing else moves, so without this the flash would freeze at full brightness
                //until the next pan. Retired here rather than left to the paint, because on a tab
                //the user has switched away from no paint ever arrives and the list would never
                //empty.
                if (overlay.FlashCount > 0) {
                    RefreshOverlay();
                    overlay.PruneExpired();
                }

                if (service == null)
                    return;

                int ready = service.ReadyCount;
                if (ready == lastReadyCount)
                    return;

                lastReadyCount = ready;
                Invalidate();
            };
        }

        /// <inheritdoc/>
        protected override void OnHandleCreated(EventArgs e) {
            base.OnHandleCreated(e);

            //Started here rather than in the constructor: the panel is built as a designer field
            //initialiser, well before there is a window to invalidate.
            repaintTimer.Start();
        }

        /// <inheritdoc/>
        protected override void OnHandleDestroyed(EventArgs e) {
            repaintTimer.Stop();
            base.OnHandleDestroyed(e);
        }

        /// <summary>The world-to-screen transform. Mutating it directly needs an Invalidate.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapCamera Camera { get; }

        /// <summary>The plane being viewed, 0..3.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Plane {
            get => plane;
            set {
                int clamped = Math.Clamp(value, 0, 3);
                if (clamped == plane)
                    return;

                plane = clamped;

                //A flash belongs to the plane it was raised on and stays hidden on any other. The
                //cursor highlight is re-taken instead: it carries the plane it was hit on, so
                //without this a Ctrl+wheel plane step blanks the highlight until the mouse moves.
                RetakeHover();
                RefreshOverlay();

                BumpSignature();
                PlaneChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Which layers to draw, before the per-level reduction.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapLayers Layers {
            get => layers;
            set {
                if (value == layers)
                    return;

                layers = value;
                BumpSignature();
            }
        }

        /// <summary>Relief shading strength, 0 to 1.</summary>
        /// <remarks>
        ///     Setting this throws away every rendered tile, the whole overview band included, so a
        ///     slider driving it has to be debounced rather than wired to every scroll event.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public float ReliefStrength {
            get => reliefStrength;
            set {
                float clamped = Math.Clamp(value, 0f, 1f);
                if (clamped == reliefStrength)
                    return;

                reliefStrength = clamped;
                BumpSignature();
            }
        }

        /// <summary>Whether the zoom is close enough for the editing tools to be meaningful.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool EditingEnabled => Camera.PixelsPerTile >= MinimumEditingPixelsPerTile;

        /// <summary>
        ///     Whether a left-button drag draws a selection rather than panning the view.
        /// </summary>
        /// <remarks>
        ///     <b>Panning does not go away when this is on.</b> Middle-drag and space plus left-drag
        ///     both still pan, and both already existed - which is the whole reason a selection tool
        ///     can take the plain left drag at all. Taking it without leaving a way to pan would
        ///     make the rectangle and freehand tools unusable, since a selection larger than the
        ///     viewport needs the viewport moved.
        ///     <para>
        ///     Set by the panel while a selection tool is armed. Off, the control behaves exactly as
        ///     it did: a left drag pans and a left click without movement raises
        ///     <see cref="TileClicked"/>.
        ///     </para>
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool DragSelects { get; set; }

        /// <summary>
        ///     Tiles the selection currently holds, for the overlay to outline.
        /// </summary>
        /// <remarks>
        ///     Held as the live object rather than a copy, so a selection that grows during a drag
        ///     shows without the panel pushing it back in on every mouse move.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapSelection? Selection {
            get => overlay.Selection;
            set {
                if (ReferenceEquals(value, overlay.Selection))
                    return;

                overlay.Selection = value;
                RefreshOverlay();
            }
        }

        /// <summary>
        ///     Repaints where the selection is drawn, for a caller that has just changed it.
        /// </summary>
        /// <remarks>
        ///     The view cannot see a mutation of the object it was handed, and subscribing to the
        ///     selection's own event would tie the control's lifetime to it. One call from the place
        ///     that changed it is honest about who knows.
        /// </remarks>
        public void RefreshSelection() {
            //Whole-control rather than the overlay's usual union: a selection that shrinks leaves
            //its old outline behind, and its bounds before the change are no longer available here.
            Invalidate();
        }

        /// <summary>Colour of a square that exists but has not been rendered yet.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color PlaceholderColour { get; set; } = Color.FromArgb(64, 92, 122, 74);

        /// <summary>Colour drawn where the world has nothing at all.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color VoidColour { get; set; } = Color.FromArgb(255, 12, 12, 16);

        /// <summary>
        ///     Whether the hover highlight also shows which height vertex a click would move.
        /// </summary>
        /// <remarks>
        ///     Set by the panel while a height tool is selected. The stored height of a tile is the
        ///     elevation of its south-west corner vertex, shared with three neighbours, so the
        ///     affordance is what makes "raise this tile" legible as a deformation of the surface
        ///     rather than as the tile taking off.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool ShowVertexAffordance {
            get => overlay.ShowVertexAffordance;
            set {
                if (value == overlay.ShowVertexAffordance)
                    return;

                overlay.ShowVertexAffordance = value;
                RefreshOverlay();
            }
        }

        /// <summary>
        ///     Briefly highlights a block of world tiles, to say what an edit just changed.
        /// </summary>
        /// <remarks>
        ///     Purely an overlay: it does not invalidate a tile and so cannot re-rasterise anything.
        ///     A caller that also changed the terrain still has to call
        ///     <see cref="InvalidateSquare"/>.
        /// </remarks>
        /// <param name="worldX">World X of the block's south-west tile.</param>
        /// <param name="worldY">World Y of the block's south-west tile.</param>
        /// <param name="tilesWide">Tiles east.</param>
        /// <param name="tilesHigh">Tiles north.</param>
        /// <param name="editPlane">The plane the change is on.</param>
        /// <param name="kind">What the flash is saying.</param>
        /// <param name="label">A short caption drawn above it, or <c>null</c>.</param>
        public void Flash(int worldX, int worldY, int tilesWide, int tilesHigh, int editPlane,
            MapFlashKind kind, string? label = null) {
            overlay.Add(worldX, worldY, tilesWide, tilesHigh, editPlane, kind, label);
            RefreshOverlay();
        }

        /// <summary>
        ///     Invalidates where the overlay was and where it now is.
        /// </summary>
        /// <remarks>
        ///     A rectangle rather than the whole control. A full invalidate on every mouse move runs
        ///     the visible-square loop and the missing-tile request pass sixty times a second, which
        ///     is exactly the cost this overlay exists to avoid paying.
        /// </remarks>
        private void RefreshOverlay() {
            Camera.ViewportWidth = Width;
            Camera.ViewportHeight = Height;

            Rectangle now = overlay.Bounds(Camera, plane, EditingEnabled);
            Rectangle dirty = overlayPainted.IsEmpty
                ? now
                : now.IsEmpty ? overlayPainted : Rectangle.Union(overlayPainted, now);

            overlayPainted = now;

            if (!dirty.IsEmpty)
                Invalidate(dirty);
        }

        /// <summary>
        ///     Binds the view to a store and a render service, or clears it.
        /// </summary>
        /// <param name="newStore">The square store, or <c>null</c> to unbind.</param>
        /// <param name="newService">The render service, or <c>null</c> to unbind.</param>
        public void Bind(MapSquareStore newStore, MapTileRenderService newService) {
            store = newStore;
            service = newService;
            lastReadyCount = -1;

            //A flash names a world tile in a cache that is about to stop being the one on screen.
            overlay.ClearFlashes();
            overlay.Hover = null;
            overlayPainted = Rectangle.Empty;

            if (service != null)
                BumpSignature();

            Invalidate();
        }

        /// <summary>Centres the view on a map square without changing the zoom.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        public void CentreOnRegion(int regionX, int regionY) {
            Camera.CentreOnRegion(regionX, regionY);
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Zooms out until the whole world fits.</summary>
        public void FitWorld() {
            Camera.FitWorld();
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Drops the rendered tiles around an edited square, so the next paint rebuilds them.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        public void InvalidateSquare(int regionX, int regionY) {
            service?.InvalidateSquare(regionX, regionY);
            Invalidate();
        }

        /// <summary>
        ///     The tile under a control-space point.
        /// </summary>
        /// <param name="screen">A point in control coordinates.</param>
        /// <returns>The tile, or <c>null</c> when the point is off the world.</returns>
        public TileHit HitTest(Point screen) {
            PointF world = Camera.ScreenToWorld(screen.X, screen.Y);

            int worldX = (int) Math.Floor(world.X);
            int worldY = (int) Math.Floor(world.Y);

            if (worldX < 0 || worldY < 0 || worldX >= MapCamera.WorldTiles || worldY >= MapCamera.WorldTiles)
                return null;

            int localX = worldX % MapRegion.WIDTH;
            int localY = worldY % MapRegion.HEIGHT;

            return new TileHit {
                //Scene coordinates for the 3x3 neighbourhood the editor builds around this square,
                //which is the only scene this hit is ever read against.
                SceneX = MapRegion.WIDTH + localX,
                SceneY = MapRegion.HEIGHT + localY,
                WorldX = worldX,
                WorldY = worldY,
                RegionX = worldX / MapRegion.WIDTH,
                RegionY = worldY / MapRegion.HEIGHT,
                LocalX = localX,
                LocalY = localY,
                Plane = plane
            };
        }

        private void BumpSignature() {
            if (service == null)
                return;

            service.SetSignature(new MapRenderSignature(plane, layers, reliefStrength,
                Map.Hillshade.DefaultAzimuthDegrees, Map.Hillshade.DefaultAltitudeDegrees, ++generation));

            lastReadyCount = -1;
            service.RequestOverviewSweep(CentreRegionY());
            Invalidate();
        }

        private int CentreRegionY() =>
            Math.Clamp((int) (Camera.CentreWorldY / MapRegion.HEIGHT), 0, MapSquareStore.WorldSquares - 1);

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            Camera.ViewportWidth = Width;
            Camera.ViewportHeight = Height;

            //Before anything is drawn: this is the point at which nothing from the previous frame
            //can still be on the wire, so it is the only safe place to free evicted bitmaps.
            service?.Tiles.DrainRetired();

            using (var background = new SolidBrush(VoidColour))
                e.Graphics.FillRectangle(background, ClientRectangle);

            if (store == null || service == null) {
                TextRenderer.DrawText(e.Graphics, "No cache loaded", Font, ClientRectangle,
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Rectangle regions = Camera.VisibleRegionBounds();
            if (regions.Width <= 0 || regions.Height <= 0)
                return;

            int level = Camera.Level;

            //Exact at a power-of-two zoom, where the tile is already the right size and nearest
            //neighbour is pixel-for-pixel what the old viewer drew. Otherwise the draw is always a
            //reduction of at most two, which is the one case bilinear handles without a mip chain.
            e.Graphics.InterpolationMode = Camera.LevelScale >= 1.0
                ? InterpolationMode.NearestNeighbor
                : InterpolationMode.Bilinear;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

            misses.Clear();

            using (var placeholder = new SolidBrush(PlaceholderColour)) {
                for (int rx = regions.Left; rx < regions.Right; rx++) {
                    for (int ry = regions.Top; ry < regions.Bottom; ry++) {
                        if (!store.Exists(rx, ry))
                            continue;

                        Rectangle destination = DestinationFor(rx, ry);
                        if (destination.Width <= 0 || destination.Height <= 0)
                            continue;

                        var key = new MapTileKey(rx, ry, level);

                        if (service.TryGetTile(key, out DirectBitmap tile)) {
                            service.Tiles.Touch(key);
                            e.Graphics.DrawImage(tile.Bitmap, destination);
                        }
                        else {
                            e.Graphics.FillRectangle(placeholder, destination);
                            misses.Add(key);
                        }
                    }
                }
            }

            //Last, and over the top of everything: the highlight has to be legible against the
            //terrain, not composited under the next square's bitmap.
            overlayPainted = overlay.Paint(e.Graphics, Camera, plane, EditingEnabled, Font);

            if (misses.Count > 0) {
                //Nearest first, so the square being looked at is drawn before the ring around it.
                PointF centre = new PointF(Width / 2f, Height / 2f);
                misses.Sort((a, b) => DistanceToScreen(a, centre).CompareTo(DistanceToScreen(b, centre)));

                service.RequestVisible(misses.Count > MaxVisibleRequestsPerPaint
                    ? misses.GetRange(0, MaxVisibleRequestsPerPaint)
                    : misses);
            }
        }

        /// <summary>
        ///     The whole-pixel destination for a square.
        /// </summary>
        /// <remarks>
        ///     Both edges are rounded rather than the origin and the size, so the right edge of one
        ///     square is the same integer as the left edge of the next and adjacent tiles cannot
        ///     leave a hairline of background between them.
        /// </remarks>
        private Rectangle DestinationFor(int regionX, int regionY) {
            RectangleF exact = Camera.ScreenRectForRegion(regionX, regionY);

            int left = (int) Math.Round(exact.Left);
            int top = (int) Math.Round(exact.Top);
            int right = (int) Math.Round(exact.Right);
            int bottom = (int) Math.Round(exact.Bottom);

            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private double DistanceToScreen(MapTileKey key, PointF centre) {
            RectangleF rect = Camera.ScreenRectForRegion(key.RegionX, key.RegionY);
            double dx = rect.X + rect.Width / 2.0 - centre.X;
            double dy = rect.Y + rect.Height / 2.0 - centre.Y;
            return dx * dx + dy * dy;
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceHeld)) {
                middleDragging = true;
                pressScreen = e.Location;
                pressCentreX = Camera.CentreWorldX;
                pressCentreY = Camera.CentreWorldY;
                Cursor = Cursors.SizeAll;
                ClearHover();
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            //Deliberately does NOT fire the tool here. Firing on mouse-down is what made drag-to-pan
            //impossible in the old viewer: every pan started by applying the selected tool.
            leftDown = true;
            dragExceeded = false;
            pressScreen = e.Location;
            pressCentreX = Camera.CentreWorldX;
            pressCentreY = Camera.CentreWorldY;

            /* A selection drag is the one gesture that DOES start on mouse-down, because a
               rectangle needs its anchor corner and a lasso needs its first point. It is safe here
               and not for the tools because it writes nothing: a drag abandoned before the button
               comes up leaves the selection exactly as it was. */
            dragSelecting = false;
            lastDragTile = null;

            if (!DragSelects)
                return;

            TileHit start = HitTest(e.Location);
            if (start == null)
                return;

            dragSelecting = true;
            lastDragTile = start;
            DragStarted?.Invoke(this, start);
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            if (middleDragging) {
                PanFromPress(e.Location);
                return;
            }

            if (dragSelecting) {
                TileHit dragged = HitTest(e.Location);
                if (dragged == null)
                    return;

                //Only when the tile changed. A freehand drag adds one tile per notification, and a
                //mouse reports dozens of moves inside one tile at an editing zoom.
                if (lastDragTile != null && lastDragTile.WorldX == dragged.WorldX
                    && lastDragTile.WorldY == dragged.WorldY)
                    return;

                lastDragTile = dragged;
                overlay.Hover = dragged;
                DragMoved?.Invoke(this, dragged);
                return;
            }

            if (leftDown) {
                int dx = e.X - pressScreen.X;
                int dy = e.Y - pressScreen.Y;

                if (!dragExceeded && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)) {
                    dragExceeded = true;
                    Cursor = Cursors.SizeAll;

                    //A pan moves the world under a stationary cursor, so the highlighted tile is no
                    //longer the tile being pointed at. It comes back on the first move after the
                    //drag ends.
                    ClearHover();
                }

                if (dragExceeded)
                    PanFromPress(e.Location);

                return;
            }

            TileHit hit = HitTest(e.Location);
            if (hit == null) {
                ClearHover();
                return;
            }

            //Only when the tile actually changed: the inspector rebuilds a scene per notification.
            if (lastHover != null && lastHover.WorldX == hit.WorldX && lastHover.WorldY == hit.WorldY
                && lastHover.Plane == hit.Plane)
                return;

            lastHover = hit;
            overlay.Hover = hit;
            RefreshOverlay();

            TileHovered?.Invoke(this, hit);
        }

        /// <summary>
        ///     Re-hit-tests under the cursor, for a change that invalidates the current hit.
        /// </summary>
        /// <remarks>
        ///     Reads the live cursor position rather than a stored one, because the last mouse event
        ///     may predate whatever moved the world - a plane step, in the only case that uses this.
        /// </remarks>
        private void RetakeHover() {
            if (!IsHandleCreated || overlay.Hover == null)
                return;

            Point local = PointToClient(MousePosition);
            overlay.Hover = ClientRectangle.Contains(local) ? HitTest(local) : null;
        }

        /// <summary>Drops the cursor highlight and repaints where it was.</summary>
        private void ClearHover() {
            lastHover = null;

            if (overlay.Hover == null)
                return;

            overlay.Hover = null;
            RefreshOverlay();
        }

        /// <inheritdoc/>
        protected override void OnMouseLeave(EventArgs e) {
            base.OnMouseLeave(e);
            ClearHover();
        }

        /// <inheritdoc/>
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);

            if (middleDragging && (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left)) {
                middleDragging = false;
                Cursor = Cursors.Default;
                return;
            }

            if (e.Button != MouseButtons.Left || !leftDown)
                return;

            bool wasDrag = dragExceeded;
            bool wasSelecting = dragSelecting;

            leftDown = false;
            dragExceeded = false;
            dragSelecting = false;
            lastDragTile = null;
            Cursor = Cursors.Default;

            if (wasSelecting) {
                //Raised even for a press and release on one tile, which is a one-tile rectangle and
                //a legitimate thing to draw. TileClicked is deliberately not also raised: the tool
                //that took the drag is the one that owns the gesture.
                DragFinished?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (wasDrag)
                return;

            TileHit hit = HitTest(e.Location);
            if (hit != null)
                TileClicked?.Invoke(this, hit);
        }

        /// <summary>
        ///     How far the mouse has to move before a click becomes a pan.
        /// </summary>
        /// <remarks>
        ///     The system's own definition of a drag, so it matches every other drag affordance on
        ///     the machine and scales with DPI. Three pixels is a floor for the case where the system
        ///     reports something absurdly small.
        /// </remarks>
        private static int DragThreshold => Math.Max(3, SystemInformation.DragSize.Width);

        private void PanFromPress(Point location) {
            //Always re-anchored from the press point rather than accumulated per move: accumulating
            //rounds once per event and the view drifts measurably over a long drag.
            double dx = location.X - pressScreen.X;
            double dy = location.Y - pressScreen.Y;

            Camera.CentreWorldX = pressCentreX - dx / Camera.PixelsPerTile;
            Camera.CentreWorldY = pressCentreY + dy / Camera.PixelsPerTile;

            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);

            //Stops a parent scrollable container from also acting on the wheel.
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;

            if ((ModifierKeys & Keys.Control) != 0) {
                Plane = Math.Clamp(plane + Math.Sign(e.Delta), 0, 3);

                //Must return: Ctrl+wheel steps the plane and never also zooms.
                return;
            }

            //Divided rather than tested for sign, which is the actual answer to "make it granular":
            //a precision touchpad sends deltas well under one notch, and a sign test rounds every
            //one of them up to a full step.
            double notches = e.Delta / (double) SystemInformation.MouseWheelScrollDelta;
            double factor = Math.Pow(2.0, notches / ZoomNotchesPerOctave);

            Camera.ViewportWidth = Width;
            Camera.ViewportHeight = Height;
            Camera.SetPixelsPerTile(Camera.PixelsPerTile * factor, e.Location);

            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        protected override bool IsInputKey(Keys keyData) {
            switch (keyData & Keys.KeyCode) {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.Space:
                    //Claimed here or the containing form treats them as navigation between controls,
                    //and space in particular would otherwise be swallowed as a button activation.
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        /// <inheritdoc/>
        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);

            double stepX = Width / 10.0;
            double stepY = Height / 10.0;

            switch (e.KeyCode) {
                case Keys.Left: Camera.PanByPixels(-stepX, 0); break;
                case Keys.Right: Camera.PanByPixels(stepX, 0); break;
                case Keys.Up: Camera.PanByPixels(0, -stepY); break;
                case Keys.Down: Camera.PanByPixels(0, stepY); break;
                case Keys.Add:
                case Keys.Oemplus:
                    Camera.SetPixelsPerTile(Camera.PixelsPerTile * Math.Pow(2.0, 1.0 / ZoomNotchesPerOctave));
                    break;
                case Keys.Subtract:
                case Keys.OemMinus:
                    Camera.SetPixelsPerTile(Camera.PixelsPerTile / Math.Pow(2.0, 1.0 / ZoomNotchesPerOctave));
                    break;
                case Keys.PageUp: Plane = Math.Clamp(plane + 1, 0, 3); return;
                case Keys.PageDown: Plane = Math.Clamp(plane - 1, 0, 3); return;
                case Keys.Home: FitWorld(); return;
                case Keys.Space: spaceHeld = true; e.Handled = true; return;
                default: return;
            }

            e.Handled = true;
            Invalidate();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        protected override void OnKeyUp(KeyEventArgs e) {
            base.OnKeyUp(e);

            if (e.KeyCode == Keys.Space)
                spaceHeld = false;
        }

        /// <inheritdoc/>
        protected override void OnLostFocus(EventArgs e) {
            base.OnLostFocus(e);

            //Alt+Tab away mid-gesture and the key-up lands on another window, so the flag would
            //stick and the next plain left click would silently pan instead of applying the tool.
            spaceHeld = false;
        }

        /// <inheritdoc/>
        protected override void OnResize(EventArgs e) {
            base.OnResize(e);
            Camera.ViewportWidth = Width;
            Camera.ViewportHeight = Height;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                repaintTimer.Stop();
                repaintTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
