using System;
using System.Drawing;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.Map {
    /// <summary>
    ///     The world-to-screen transform for the whole-world map view.
    /// </summary>
    /// <remarks>
    ///     Deliberately not a <see cref="System.Windows.Forms.Control"/> and deliberately free of any
    ///     WinForms dependency. The transform is the part of a slippy map that is worth testing - the
    ///     vertical flip, the clamping, and the zoom-about-a-point arithmetic all have off-by-one and
    ///     sign traps - and a plain object can be exercised without a window handle or a message loop.
    ///
    ///     World coordinates are absolute map tiles: X east, Y north, origin at the south-west corner
    ///     of region 0,0. Screen coordinates are control pixels: X east, Y <em>down</em>. Every
    ///     conversion in here exists to keep that one flip in a single place.
    /// </remarks>
    public sealed class MapCamera {
        /// <summary>
        ///     The coarsest zoom, at which the whole 16,384 tile world is 1024 pixels across.
        /// </summary>
        /// <remarks>
        ///     Matched to the tile pyramid: this is level -4, where a map square's cached bitmap is
        ///     4x4 pixels and all 1684 of them together cost 108 KB.
        /// </remarks>
        public const double MinPixelsPerTile = 0.0625;

        /// <summary>The finest zoom, level 4, where a square's bitmap is 1024x1024.</summary>
        public const double MaxPixelsPerTile = 16.0;

        /// <summary>Coarsest pyramid level, <c>log2</c> of <see cref="MinPixelsPerTile"/>.</summary>
        public const int MinLevel = -4;

        /// <summary>Finest pyramid level, <c>log2</c> of <see cref="MaxPixelsPerTile"/>.</summary>
        public const int MaxLevel = 4;

        /// <summary>Map squares along each axis of the world.</summary>
        public const int WorldSquares = 256;

        /// <summary>The world's extent in tiles along each axis.</summary>
        public const int WorldTiles = WorldSquares * MapRegion.WIDTH;

        private double centreWorldX = WorldTiles / 2.0;
        private double centreWorldY = WorldTiles / 2.0;
        private double pixelsPerTile = 1.0;

        /// <summary>
        ///     World X at the centre of the viewport.
        /// </summary>
        /// <remarks>
        ///     Clamped to the world rather than left free. Unclamped, one flick of an inertial pan at
        ///     16 pixels per tile puts the camera thousands of tiles into empty space with nothing on
        ///     screen to navigate back by.
        /// </remarks>
        public double CentreWorldX {
            get => centreWorldX;
            set => centreWorldX = Math.Clamp(value, 0.0, WorldTiles);
        }

        /// <summary>World Y at the centre of the viewport, clamped to the world.</summary>
        public double CentreWorldY {
            get => centreWorldY;
            set => centreWorldY = Math.Clamp(value, 0.0, WorldTiles);
        }

        /// <summary>Screen pixels per world tile.</summary>
        public double PixelsPerTile => pixelsPerTile;

        /// <summary>Viewport width in pixels.</summary>
        public int ViewportWidth { get; set; }

        /// <summary>Viewport height in pixels.</summary>
        public int ViewportHeight { get; set; }

        /// <summary>
        ///     The pyramid level the current zoom reads from.
        /// </summary>
        /// <remarks>
        ///     <c>ceil</c> of the log rather than <c>floor</c>, which is the whole reason continuous
        ///     zoom looks acceptable here. Rounding up means the cached tile is always at or finer
        ///     than the display, so the draw is always a reduction and the picture softens as you
        ///     zoom out. Rounding down would magnify a coarse tile and the world would go visibly
        ///     blocky between stops.
        /// </remarks>
        public int Level => Math.Clamp((int) Math.Ceiling(Math.Log2(pixelsPerTile)), MinLevel, MaxLevel);

        /// <summary>
        ///     How far the cached tile has to be scaled to fill its place on screen.
        /// </summary>
        /// <remarks>
        ///     Always in <c>(0.5, 1]</c> away from the clamps, and exactly 1 at a power-of-two zoom.
        ///     A reduction of at most two is the one case bilinear resampling handles well without a
        ///     mip chain, and 1 exactly is where the view can switch to nearest neighbour and be
        ///     pixel-for-pixel what the old fixed-zoom viewer drew.
        /// </remarks>
        public double LevelScale => pixelsPerTile / Math.Pow(2.0, Level);

        /// <summary>Where a world point lands on screen.</summary>
        /// <param name="worldX">World tile X.</param>
        /// <param name="worldY">World tile Y.</param>
        /// <returns>The screen point.</returns>
        public PointF WorldToScreen(double worldX, double worldY) => new PointF(
            (float) ((worldX - centreWorldX) * pixelsPerTile + ViewportWidth / 2.0),
            (float) (ViewportHeight / 2.0 - (worldY - centreWorldY) * pixelsPerTile));

        /// <summary>
        ///     The fractional world tile under a screen point.
        /// </summary>
        /// <remarks>
        ///     Fractional on purpose. A hit test floors the result, but zoom-about-the-cursor needs
        ///     the sub-tile part or the view creeps by up to a tile per notch at high zoom.
        /// </remarks>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        /// <returns>The world point.</returns>
        public PointF ScreenToWorld(float screenX, float screenY) => new PointF(
            (float) (centreWorldX + (screenX - ViewportWidth / 2.0) / pixelsPerTile),
            (float) (centreWorldY + (ViewportHeight / 2.0 - screenY) / pixelsPerTile));

        /// <summary>
        ///     The range of map squares the viewport can see.
        /// </summary>
        /// <remarks>
        ///     Half-open like every other rectangle here: iterate <c>Left</c> to <c>Right</c>
        ///     exclusive. Clamped to the world, so a camera looking off the edge yields a smaller
        ///     range rather than negative coordinates.
        /// </remarks>
        /// <returns>Region coordinates, as a half-open rectangle.</returns>
        public Rectangle VisibleRegionBounds() {
            if (ViewportWidth <= 0 || ViewportHeight <= 0 || pixelsPerTile <= 0)
                return Rectangle.Empty;

            double halfW = ViewportWidth / 2.0 / pixelsPerTile;
            double halfH = ViewportHeight / 2.0 / pixelsPerTile;

            int x0 = ClampRegion((int) Math.Floor((centreWorldX - halfW) / MapRegion.WIDTH));
            int x1 = ClampRegion((int) Math.Floor((centreWorldX + halfW) / MapRegion.WIDTH));
            int y0 = ClampRegion((int) Math.Floor((centreWorldY - halfH) / MapRegion.HEIGHT));
            int y1 = ClampRegion((int) Math.Floor((centreWorldY + halfH) / MapRegion.HEIGHT));

            return new Rectangle(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        }

        private static int ClampRegion(int value) => Math.Clamp(value, 0, WorldSquares - 1);

        /// <summary>Where a map square's bitmap belongs on screen.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        /// <returns>The destination rectangle.</returns>
        public RectangleF ScreenRectForRegion(int regionX, int regionY) {
            //Taken from the square's north-west corner, since screen Y grows the other way.
            PointF corner = WorldToScreen(regionX * MapRegion.WIDTH, (regionY + 1) * MapRegion.HEIGHT);
            float side = (float) (MapRegion.WIDTH * pixelsPerTile);
            return new RectangleF(corner.X, corner.Y, side, side);
        }

        /// <summary>
        ///     Zooms, keeping the world point under a screen point where it is.
        /// </summary>
        /// <remarks>
        ///     Anchoring on the cursor rather than on the viewport centre is what makes wheel zoom
        ///     feel like a map instead of like a slideshow: the thing being pointed at is almost
        ///     always the thing being zoomed toward, and centre-pinned zoom slides it off screen.
        /// </remarks>
        /// <param name="value">The requested pixels per tile. Clamped to the supported range.</param>
        /// <param name="anchorScreen">The screen point to hold fixed.</param>
        public void SetPixelsPerTile(double value, PointF anchorScreen) {
            double clamped = Math.Clamp(value, MinPixelsPerTile, MaxPixelsPerTile);
            if (clamped == pixelsPerTile)
                return;

            PointF before = ScreenToWorld(anchorScreen.X, anchorScreen.Y);
            pixelsPerTile = clamped;
            PointF after = ScreenToWorld(anchorScreen.X, anchorScreen.Y);

            CentreWorldX += before.X - after.X;
            CentreWorldY += before.Y - after.Y;
        }

        /// <summary>Zooms about the viewport centre.</summary>
        /// <param name="value">The requested pixels per tile.</param>
        public void SetPixelsPerTile(double value) =>
            SetPixelsPerTile(value, new PointF(ViewportWidth / 2f, ViewportHeight / 2f));

        /// <summary>
        ///     Moves the camera by a screen-space offset.
        /// </summary>
        /// <param name="dxPixels">Pixels to move the camera east.</param>
        /// <param name="dyPixels">Pixels to move the camera down the screen, which is south.</param>
        public void PanByPixels(double dxPixels, double dyPixels) {
            if (pixelsPerTile <= 0)
                return;

            CentreWorldX += dxPixels / pixelsPerTile;
            CentreWorldY -= dyPixels / pixelsPerTile;
        }

        /// <summary>Centres the view on the middle of a map square.</summary>
        /// <param name="regionX">Region X.</param>
        /// <param name="regionY">Region Y.</param>
        public void CentreOnRegion(int regionX, int regionY) {
            CentreWorldX = regionX * MapRegion.WIDTH + MapRegion.WIDTH / 2.0;
            CentreWorldY = regionY * MapRegion.HEIGHT + MapRegion.HEIGHT / 2.0;
        }

        /// <summary>
        ///     Zooms out until the whole world fits, and centres on it.
        /// </summary>
        /// <remarks>
        ///     The result is usually the minimum zoom: fitting 16,384 tiles into a 1200 pixel
        ///     viewport needs 0.073 pixels per tile, and the floor is 0.0625.
        /// </remarks>
        public void FitWorld() {
            int side = Math.Min(ViewportWidth, ViewportHeight);
            if (side > 0)
                pixelsPerTile = Math.Clamp(side / (double) WorldTiles, MinPixelsPerTile, MaxPixelsPerTile);

            CentreWorldX = WorldTiles / 2.0;
            CentreWorldY = WorldTiles / 2.0;
        }
    }
}
