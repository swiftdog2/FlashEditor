using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FlashEditor.Cache.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     A whole-world thumbnail, one pixel per map square, for jumping around the map.
    /// </summary>
    /// <remarks>
    ///     Built from the index-5 reference table alone: it asks only which group names resolve, so
    ///     it costs one name hash per square and decodes no map data at all. That is what makes it
    ///     cheap enough to build on cache open.
    /// </remarks>
    public sealed class WorldNavigatorControl : Control {
        /// <summary>Map squares along each axis of the world.</summary>
        public const int WorldSquares = 256;

        private bool[,] present = new bool[WorldSquares, WorldSquares];
        private Bitmap thumbnail;

        /// <summary>
        ///     The region-coordinate box the thumbnail covers.
        /// </summary>
        /// <remarks>
        ///     The occupied world is a small part of the 256x256 grid, so drawing the whole grid
        ///     spends almost all of the control on empty space and leaves the land too small to aim
        ///     at. Everything is therefore expressed against this box rather than against
        ///     <see cref="WorldSquares"/>.
        ///
        ///     <see cref="Paint"/>, <see cref="ThumbnailArea"/>, <see cref="OnPaint"/> and
        ///     <see cref="ToRegion"/> all have to agree on it: if the draw and the pick disagree,
        ///     clicking the navigator silently jumps to the wrong square, which reads as the map
        ///     being broken rather than the navigator.
        /// </remarks>
        private Rectangle occupied = new Rectangle(0, 0, WorldSquares, WorldSquares);

        private int currentRegionX = -1;
        private int currentRegionY = -1;

        /// <summary>
        ///     The main view's visible square range, in region coordinates, or empty.
        /// </summary>
        /// <remarks>
        ///     Held as a field rather than a property so it does not become another designer-visible
        ///     surface on a control that has no design-time story.
        /// </remarks>
        private RectangleF viewport;

        /// <summary>Raised when a square is clicked.</summary>
        public event EventHandler<Point> RegionPicked;

        /// <summary>Creates the control.</summary>
        public WorldNavigatorControl() {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.FromArgb(16, 16, 20);
            Cursor = Cursors.Hand;
        }

        /// <summary>Colour of a square that exists in the cache.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color LandColour { get; set; } = Color.FromArgb(255, 92, 122, 74);

        /// <summary>Colour of the marker on the square currently open.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color MarkerColour { get; set; } = Color.FromArgb(255, 255, 96, 96);

        /// <summary>Colour of the outline showing what the main view can see.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color ViewportColour { get; set; } = Color.FromArgb(255, 255, 220, 120);

        /// <summary>Number of squares the cache holds.</summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int SquareCount { get; private set; }

        /// <summary>
        ///     Scans the cache for which squares exist and builds the thumbnail.
        /// </summary>
        /// <param name="loader">The loader to resolve names through, or <c>null</c> to clear.</param>
        public void Build(MapSquareLoader loader) {
            var scanned = new bool[WorldSquares, WorldSquares];
            int count = 0;

            if (loader != null) {
                for (int rx = 0; rx < WorldSquares; rx++) {
                    for (int ry = 0; ry < WorldSquares; ry++) {
                        if (!loader.Exists(rx, ry))
                            continue;
                        scanned[rx, ry] = true;
                        count++;
                    }
                }
            }

            Build(scanned, count);
        }

        /// <summary>
        ///     Builds the thumbnail from a presence map somebody else has already scanned.
        /// </summary>
        /// <remarks>
        ///     The scan is 65,536 string concatenations and name hashes against the index-5 reference
        ///     table. <see cref="MapSquareStore"/> has to do it anyway to know which squares exist,
        ///     so taking its answer here is what stops it happening twice per cache open.
        /// </remarks>
        /// <param name="presence">Which squares exist, indexed <c>[regionX, regionY]</c>.</param>
        /// <param name="squareCount">How many of them there are.</param>
        public void Build(bool[,] presence, int squareCount) {
            present = presence ?? new bool[WorldSquares, WorldSquares];
            SquareCount = squareCount;
            occupied = OccupiedBounds(present);

            thumbnail?.Dispose();
            thumbnail = Paint(present);
            Invalidate();
        }

        /// <summary>
        ///     The smallest region box containing every square that exists, padded by one.
        /// </summary>
        /// <remarks>
        ///     The padding keeps a coastal square off the very edge of the thumbnail, so the
        ///     viewport outline around it stays visible instead of being clipped by the border.
        ///     Falls back to the whole world when nothing is present, so an empty cache still has a
        ///     well-formed box to divide by.
        /// </remarks>
        /// <param name="squares">Which squares exist, indexed <c>[regionX, regionY]</c>.</param>
        /// <returns>The box in region coordinates.</returns>
        private static Rectangle OccupiedBounds(bool[,] squares) {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;

            for (int rx = 0; rx < WorldSquares; rx++) {
                for (int ry = 0; ry < WorldSquares; ry++) {
                    if (!squares[rx, ry])
                        continue;
                    if (rx < minX) minX = rx;
                    if (rx > maxX) maxX = rx;
                    if (ry < minY) minY = ry;
                    if (ry > maxY) maxY = ry;
                }
            }

            if (maxX < minX)
                return new Rectangle(0, 0, WorldSquares, WorldSquares);

            minX = Math.Max(0, minX - 1);
            minY = Math.Max(0, minY - 1);
            maxX = Math.Min(WorldSquares - 1, maxX + 1);
            maxY = Math.Min(WorldSquares - 1, maxY + 1);

            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        /// <summary>
        ///     Shows which squares the main view is currently looking at.
        /// </summary>
        /// <remarks>
        ///     At world zoom a single-square marker says nothing - it is one pixel of 65,536 and the
        ///     view is showing hundreds of squares around it. The outlined rectangle is what actually
        ///     answers "where am I".
        /// </remarks>
        /// <param name="regionRect">The visible range in region coordinates, or empty to hide it.</param>
        public void SetViewport(RectangleF regionRect) {
            if (regionRect == viewport)
                return;

            viewport = regionRect;
            Invalidate();
        }

        /// <summary>Moves the marker to a square.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        public void SetCurrent(int regionX, int regionY) {
            if (regionX == currentRegionX && regionY == currentRegionY)
                return;
            currentRegionX = regionX;
            currentRegionY = regionY;
            Invalidate();
        }

        /// <summary>Whether a square exists, for callers that already built the map.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns><c>true</c> when the cache has terrain for it.</returns>
        public bool Exists(int regionX, int regionY) =>
            regionX >= 0 && regionY >= 0 && regionX < WorldSquares && regionY < WorldSquares
            && present[regionX, regionY];

        private Bitmap Paint(bool[,] squares) {
            var bitmap = new Bitmap(occupied.Width, occupied.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            for (int rx = occupied.Left; rx < occupied.Right; rx++) {
                for (int ry = occupied.Top; ry < occupied.Bottom; ry++) {
                    if (!squares[rx, ry])
                        continue;

                    //Region Y runs north and screen Y runs down, same flip as the map view.
                    bitmap.SetPixel(rx - occupied.Left, occupied.Bottom - 1 - ry, LandColour);
                }
            }

            return bitmap;
        }

        /// <inheritdoc/>
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);

            if (thumbnail == null) {
                TextRenderer.DrawText(e.Graphics, "No cache", Font, ClientRectangle,
                    Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            Rectangle area = ThumbnailArea();

            //Nearest neighbour: a square is one pixel, and smoothing turns a coastline into mush.
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(thumbnail, area);

            float scale = area.Width / (float) occupied.Width;

            if (viewport.Width > 0 && viewport.Height > 0) {
                //Region Y runs north, so the rectangle's top edge is its highest region row.
                float top = area.Top + (occupied.Bottom - viewport.Bottom) * scale;
                using (var pen = new Pen(ViewportColour, 1f))
                    e.Graphics.DrawRectangle(pen,
                        area.Left + (viewport.Left - occupied.Left) * scale, top,
                        Math.Max(1f, viewport.Width * scale), Math.Max(1f, viewport.Height * scale));
            }

            if (currentRegionX < 0)
                return;

            float markerX = area.Left + (currentRegionX - occupied.Left) * scale;
            float markerY = area.Top + (occupied.Bottom - 1 - currentRegionY) * scale;
            float size = Math.Max(3f, scale);

            using (var pen = new Pen(MarkerColour, 1f))
                e.Graphics.DrawRectangle(pen, markerX - size / 2, markerY - size / 2, size, size);
        }

        /// <inheritdoc/>
        protected override void OnMouseDown(MouseEventArgs e) {
            base.OnMouseDown(e);
            Pick(e.Location);
        }

        /// <inheritdoc/>
        /// <remarks>
        ///     Held-and-dragged picks as well as clicked, so the navigator can be scrubbed and the
        ///     main view follows live. That is only worth having because panning the main view is now
        ///     free - it repaints from cached tiles rather than decoding a scene per square.
        /// </remarks>
        protected override void OnMouseMove(MouseEventArgs e) {
            base.OnMouseMove(e);

            if ((e.Button & MouseButtons.Left) != 0)
                Pick(e.Location);
        }

        private void Pick(Point location) {
            Point? region = ToRegion(location);
            if (region != null)
                RegionPicked?.Invoke(this, region.Value);
        }

        /// <summary>The square under a control-space point, or <c>null</c> when outside.</summary>
        /// <param name="location">A point in control coordinates.</param>
        /// <returns>The region coordinates.</returns>
        public Point? ToRegion(Point location) {
            Rectangle area = ThumbnailArea();
            if (area.Width <= 0 || !area.Contains(location))
                return null;

            float scale = area.Width / (float) occupied.Width;
            int rx = occupied.Left + (int) ((location.X - area.Left) / scale);
            int ry = occupied.Bottom - 1 - (int) ((location.Y - area.Top) / scale);

            if (rx < 0 || ry < 0 || rx >= WorldSquares || ry >= WorldSquares)
                return null;

            return new Point(rx, ry);
        }

        /// <summary>
        ///     The thumbnail rectangle, centred and aspect-preserved against the occupied box.
        /// </summary>
        /// <remarks>
        ///     One scale factor for both axes, so a square map square stays square. Taking the
        ///     smaller of the two ratios is what keeps the thumbnail inside the control rather than
        ///     letting the longer axis overflow.
        /// </remarks>
        private Rectangle ThumbnailArea() {
            if (occupied.Width <= 0 || occupied.Height <= 0)
                return Rectangle.Empty;

            float scale = Math.Min(Width / (float) occupied.Width, Height / (float) occupied.Height);
            int w = Math.Max(1, (int) (occupied.Width * scale));
            int h = Math.Max(1, (int) (occupied.Height * scale));

            return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing)
                thumbnail?.Dispose();
            base.Dispose(disposing);
        }
    }
}
