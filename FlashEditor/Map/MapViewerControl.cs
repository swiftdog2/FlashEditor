using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using FlashEditor.Cache.Region;
using FlashEditor.Cache.Util;

//System.Drawing.Region arrives via the WinForms implicit usings and collides with the map type.
using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     A pannable, zoomable top-down view of a single <see cref="MapScene"/>.
    /// </summary>
    /// <remarks>
    ///     Superseded by <see cref="WorldMapViewControl"/>, which virtualises the whole world rather
    ///     than blitting one pre-rendered scene, and is what the Map tab now uses. This is kept
    ///     because it is the only view that renders a scene in one piece, which is what the windowed
    ///     rasteriser has to agree with; deleting it would delete the reference implementation the
    ///     seam tests compare against.
    ///
    ///     Renders once into a <see cref="DirectBitmap"/> and blits it, rather than redrawing on
    ///     every paint. A scene is static until an edit or a layer change invalidates it, so the
    ///     expensive part - the underlay blend - should not run on a scroll.
    /// </remarks>
    public sealed class MapViewerControl : Control {
        private MapScene scene;
        private MapRasteriser rasteriser;
        private DirectBitmap rendered;

        private int plane;
        private MapLayers layers = MapLayers.Default;
        private int tilePixels = 4;

        private Point viewOffset;
        private Point dragAnchor;
        private Point dragOrigin;
        private bool dragging;

        /// <summary>Raised when the cursor moves over a different tile.</summary>
        public event EventHandler<TileHit> TileHovered;

        /// <summary>Raised when a tile is clicked.</summary>
        public event EventHandler<TileHit> TileClicked;

        /// <summary>Creates the control.</summary>
        public MapViewerControl() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.FromArgb(20, 20, 24);
        }

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
                Rerender();
            }
        }

        /// <summary>Which layers to draw.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapLayers Layers {
            get => layers;
            set {
                if (value == layers)
                    return;
                layers = value;
                Rerender();
            }
        }

        /// <summary>Screen pixels per tile.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int TilePixels {
            get => tilePixels;
            set {
                int clamped = Math.Clamp(value, 1, 32);
                if (clamped == tilePixels)
                    return;

                //Keep the centre of the viewport pinned across a zoom, otherwise zooming walks the
                //view toward the origin and the thing being looked at slides off screen.
                PointF centre = ScreenToSceneF(new Point(Width / 2, Height / 2));
                tilePixels = clamped;
                Rerender();
                CentreOn(centre);
            }
        }

        /// <summary>The scene currently displayed, or <c>null</c>.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public MapScene Scene => scene;

        /// <summary>
        ///     Where the scene bitmap's top-left corner sits in control coordinates.
        /// </summary>
        /// <remarks>
        ///     Exposed so a caller can save and restore the view, and so hit-testing can be
        ///     exercised at a known pan position.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Point ViewOffset {
            get => viewOffset;
            set {
                if (value == viewOffset)
                    return;
                viewOffset = value;
                Invalidate();
            }
        }

        /// <summary>Shows a scene.</summary>
        /// <param name="newScene">The scene, or <c>null</c> to clear.</param>
        /// <param name="newRasteriser">The rasteriser to draw it with.</param>
        public void Show(MapScene newScene, MapRasteriser newRasteriser) {
            scene = newScene;
            rasteriser = newRasteriser;
            Rerender();
            CentreOnSquare(1, 1);
        }

        /// <summary>Redraws the scene bitmap, for example after an edit.</summary>
        public void Rerender() {
            rendered?.Dispose();
            rendered = null;

            if (scene != null && rasteriser != null) {
                rasteriser.TilePixels = tilePixels;
                rendered = rasteriser.Render(scene, plane, layers);
            }

            Invalidate();
        }

        /// <summary>Centres the view on a square of the scene grid.</summary>
        /// <param name="dx">Squares east of the scene origin.</param>
        /// <param name="dy">Squares north of the scene origin.</param>
        public void CentreOnSquare(int dx, int dy) {
            if (scene == null)
                return;
            CentreOn(new PointF(
                dx * MapRegion.WIDTH + MapRegion.WIDTH / 2f,
                dy * MapRegion.HEIGHT + MapRegion.HEIGHT / 2f));
        }

        private void CentreOn(PointF sceneTile) {
            if (scene == null)
                return;

            int px = (int) (sceneTile.X * tilePixels);
            int py = (int) ((scene.HeightTiles - sceneTile.Y) * tilePixels);

            viewOffset = new Point(Width / 2 - px, Height / 2 - py);
            Invalidate();
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            if (rendered == null) {
                TextRenderer.DrawText(e.Graphics, "No map loaded", Font, ClientRectangle,
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            e.Graphics.DrawImageUnscaled(rendered.Bitmap, viewOffset);
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Focus();

            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && ModifierKeys == Keys.Space)) {
                dragging = true;
                dragAnchor = e.Location;
                dragOrigin = viewOffset;
                Cursor = Cursors.SizeAll;
                return;
            }

            if (e.Button == MouseButtons.Left) {
                TileHit hit = HitTest(e.Location);
                if (hit != null)
                    TileClicked?.Invoke(this, hit);
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            if (dragging) {
                viewOffset = new Point(
                    dragOrigin.X + (e.X - dragAnchor.X),
                    dragOrigin.Y + (e.Y - dragAnchor.Y));
                Invalidate();
                return;
            }

            TileHit hit = HitTest(e.Location);
            if (hit != null)
                TileHovered?.Invoke(this, hit);
        }

        /// <inheritdoc/>
        protected override void OnMouseUp(MouseEventArgs e) {
            base.OnMouseUp(e);
            if (!dragging)
                return;
            dragging = false;
            Cursor = Cursors.Default;
        }

        /// <inheritdoc/>
        protected override void OnMouseWheel(MouseEventArgs e) {
            base.OnMouseWheel(e);
            TilePixels = e.Delta > 0 ? tilePixels * 2 : tilePixels / 2;
        }

        /// <summary>
        ///     Converts a screen point to a scene tile.
        /// </summary>
        /// <param name="screen">A point in control coordinates.</param>
        /// <returns>The tile, or <c>null</c> when the point is off the scene.</returns>
        public TileHit HitTest(Point screen) {
            if (scene == null)
                return null;

            //Computed in integers rather than by flooring ScreenToSceneF. The rasteriser puts
            //scene row y at screen row (H - 1 - y), and the float form lands a row high exactly on
            //a tile's top edge, so inverting it directly is both simpler and correct at the seams.
            int px = screen.X - viewOffset.X;
            int py = screen.Y - viewOffset.Y;

            if (px < 0 || py < 0)
                return null;

            int sx = px / tilePixels;
            int sy = scene.HeightTiles - 1 - py / tilePixels;

            if (sx < 0 || sy < 0 || sx >= scene.WidthTiles || sy >= scene.HeightTiles)
                return null;

            return new TileHit {
                SceneX = sx,
                SceneY = sy,
                WorldX = scene.BaseX + sx,
                WorldY = scene.BaseY + sy,
                RegionX = (scene.BaseX + sx) / MapRegion.WIDTH,
                RegionY = (scene.BaseY + sy) / MapRegion.HEIGHT,
                LocalX = sx % MapRegion.WIDTH,
                LocalY = sy % MapRegion.HEIGHT,
                Plane = plane
            };
        }

        /// <summary>Converts a screen point to fractional scene tile coordinates.</summary>
        private PointF ScreenToSceneF(Point screen) {
            if (scene == null)
                return PointF.Empty;

            float px = screen.X - viewOffset.X;
            float py = screen.Y - viewOffset.Y;

            //Screen Y grows downward, scene Y grows north.
            return new PointF(px / tilePixels, scene.HeightTiles - py / tilePixels);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing)
                rendered?.Dispose();
            base.Dispose(disposing);
        }
    }
}
