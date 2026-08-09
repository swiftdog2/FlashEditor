using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     What a sprite tile is showing, which is not always a picture.
    /// </summary>
    /// <remarks>
    ///     Four states rather than "picture or not". A row whose set has not been decoded yet, a set
    ///     that stores no pixels at all, and a set that would not decode are three different things,
    ///     and a viewer that draws all three as an empty rectangle turns a legitimate record into
    ///     what looks like a defect.
    /// </remarks>
    public enum SpriteTileContent {
        /// <summary>Seeded before the load reached this row.</summary>
        Pending,

        /// <summary>A decoded sprite with at least one pixel.</summary>
        Picture,

        /// <summary>A decoded sprite whose stored plane has no area.</summary>
        Empty,

        /// <summary>A group that would not read or would not decode.</summary>
        Failed
    }

    /// <summary>
    ///     Draws sprites the way this index needs them drawn: letterboxed, hard-edged, and over a
    ///     checkerboard.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The checkerboard is load bearing.</b> Palette entry 0 is the transparent slot and a
    ///     sprite may be entirely transparent, so over a flat colour "nothing was drawn" and "the
    ///     draw failed" are the same rectangle. Over a checkerboard the first is transparent pixels
    ///     and the second is not.
    ///     </para>
    ///     <para>
    ///     <b>The pixels are copied before they are drawn.</b> <see cref="DirectBitmap"/> declares
    ///     its buffer <see cref="PixelFormat.Format32bppPArgb"/> and
    ///     <see cref="RSBufferedImage.SetRGB"/> writes straight, un-premultiplied ARGB into it -
    ///     which is invisible while every pixel is opaque, because the two encodings agree there,
    ///     and wrong for exactly the frames that carry an alpha plane (180 of them in the vanilla
    ///     capture). Compositing such a pixel as premultiplied over the checkerboard washes it out.
    ///     <see cref="ToDisplayBitmap"/> copies the same ints into a bitmap that is labelled
    ///     straight ARGB, so the blend is the one the client's own rule describes. The rasteriser is
    ///     left alone: its buffer is read as straight ARGB by everything else too, so the label is
    ///     what is wrong rather than the pixels.
    ///     </para>
    /// </remarks>
    public static class SpritePainter {
        /// <summary>The lighter of the two checkerboard squares.</summary>
        /// <remarks>
        ///     Two neutral greys rather than the usual white and light grey. Sprites in this index
        ///     are overwhelmingly light UI art on transparency, and a white checker leaves a white
        ///     glyph invisible on half its own background.
        /// </remarks>
        public static readonly Color CheckerLight = Color.FromArgb(0xFF, 0xBE, 0xBE, 0xBE);

        /// <summary>The darker of the two checkerboard squares.</summary>
        public static readonly Color CheckerDark = Color.FromArgb(0xFF, 0x8C, 0x8C, 0x8C);

        /// <summary>The flat fill a row that has not been decoded yet is drawn with.</summary>
        /// <remarks>
        ///     Deliberately not a checkerboard: a seeded row has no sprite behind it, and drawing it
        ///     like a transparent one would claim the file has been read and holds nothing.
        /// </remarks>
        public static readonly Color PendingFill = Color.FromArgb(0xFF, 0x50, 0x50, 0x50);

        /// <summary>Outlines the sprite's own extent, so a transparent picture still has edges.</summary>
        private static readonly Color ExtentEdge = Color.FromArgb(0xFF, 0x40, 0x40, 0x40);

        private static readonly Color MarkerInk = Color.FromArgb(0xFF, 0x30, 0x30, 0x30);
        private static readonly Color FailureInk = Color.FromArgb(0xFF, 0x8B, 0x10, 0x10);

        /// <summary>
        ///     Fills an area with the transparency checkerboard.
        /// </summary>
        /// <param name="graphics">The surface to draw on.</param>
        /// <param name="area">The area to fill, in that surface's coordinates.</param>
        /// <param name="square">The side of one checker square in pixels.</param>
        public static void PaintCheckerboard(Graphics graphics, Rectangle area, int square) {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));
            if (area.Width <= 0 || area.Height <= 0)
                return;

            square = Math.Max(2, square);

            using var light = new SolidBrush(CheckerLight);
            using var dark = new SolidBrush(CheckerDark);

            graphics.FillRectangle(light, area);

            //Indexed from the area's own origin rather than the surface's, so the pattern does not
            //crawl when the same picture is drawn at a different offset.
            for (int y = 0; y < area.Height; y += square) {
                for (int x = (y / square % 2 == 0) ? square : 0; x < area.Width; x += square * 2) {
                    var cell = new Rectangle(area.X + x, area.Y + y,
                        Math.Min(square, area.Width - x), Math.Min(square, area.Height - y));
                    graphics.FillRectangle(dark, cell);
                }
            }
        }

        /// <summary>
        ///     Builds the fixed-size tile the sprite list shows for one row.
        /// </summary>
        /// <remarks>
        ///     Safe to call off the UI thread: nothing here touches a control or an
        ///     <see cref="System.Windows.Forms.ImageList"/>, and the bitmap it returns is unattached
        ///     and owned by the caller until it is handed to the grid.
        /// </remarks>
        /// <param name="picture">The sprite's pixels, or null when there are none to draw.</param>
        /// <param name="size">The tile's side in pixels.</param>
        /// <param name="content">What the tile is showing.</param>
        /// <param name="markerFont">The font the empty and failed markers are written in.</param>
        /// <returns>The tile.</returns>
        public static Bitmap RenderTile(Bitmap? picture, int size, SpriteTileContent content, Font markerFont) {
            size = Math.Max(8, size);
            var tile = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            var area = new Rectangle(0, 0, size, size);

            using Graphics graphics = Graphics.FromImage(tile);

            if (content == SpriteTileContent.Pending) {
                using var pending = new SolidBrush(PendingFill);
                graphics.FillRectangle(pending, area);
                return tile;
            }

            PaintCheckerboard(graphics, area, Math.Max(4, size / 8));

            if (content == SpriteTileContent.Failed) {
                DrawMarker(graphics, area, "failed", markerFont, FailureInk);
                return tile;
            }

            SpriteTileFit fit = picture == null
                ? SpriteTileFit.Empty
                : SpriteTileFit.Fit(picture.Width, picture.Height, size, size);

            if (content == SpriteTileContent.Empty || fit.IsEmpty || picture == null) {
                DrawMarker(graphics, area, "empty", markerFont, MarkerInk);
                return tile;
            }

            DrawSprite(graphics, picture, fit.Bounds, fit.Upscale >= 1);

            //Outlined after the picture, so a fully transparent sprite is a bordered patch of
            //checkerboard rather than indistinguishable from the tile around it.
            using var edge = new Pen(ExtentEdge);
            graphics.DrawRectangle(edge, fit.Bounds.X, fit.Bounds.Y,
                Math.Max(1, fit.Bounds.Width - 1), Math.Max(1, fit.Bounds.Height - 1));

            return tile;
        }

        /// <summary>
        ///     Draws a sprite into a destination rectangle without softening its pixels.
        /// </summary>
        /// <remarks>
        ///     Nearest neighbour whenever the picture is being magnified, because these are pixels
        ///     rather than photographs: the default interpolation turns a 2x2 sprite into four grey
        ///     blobs and there is nothing left to judge an edit against. <c>PixelOffsetMode.Half</c>
        ///     is what makes the magnified blocks land on whole pixels; without it GDI+ samples on
        ///     the half-pixel and the first row and column come out a different width from the rest.
        ///     A shrunk picture is resampled instead, since nearest neighbour at a fifth of size
        ///     simply deletes four pixels in five and thin artwork disappears altogether.
        /// </remarks>
        /// <param name="graphics">The surface to draw on.</param>
        /// <param name="picture">The sprite.</param>
        /// <param name="bounds">Where to draw it.</param>
        /// <param name="magnifying">Whether the destination is at least the source's size.</param>
        public static void DrawSprite(Graphics graphics, Bitmap picture, Rectangle bounds, bool magnifying) {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));
            if (picture == null)
                throw new ArgumentNullException(nameof(picture));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            InterpolationMode interpolation = graphics.InterpolationMode;
            PixelOffsetMode offset = graphics.PixelOffsetMode;
            try {
                graphics.InterpolationMode = magnifying
                    ? InterpolationMode.NearestNeighbor
                    : InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(picture, bounds);
            }
            finally {
                graphics.InterpolationMode = interpolation;
                graphics.PixelOffsetMode = offset;
            }
        }

        /// <summary>
        ///     Copies a rasterised frame into a bitmap whose declared format matches its contents.
        /// </summary>
        /// <remarks>
        ///     See the type remarks: the frame's own bitmap says premultiplied and holds straight
        ///     ARGB, so drawing it directly is wrong for every frame carrying an alpha plane. The
        ///     copy is a block copy per row rather than a per-pixel loop, and <c>LockBits</c> may
        ///     hand back a stride wider than the row, which a single copy would shear.
        /// </remarks>
        /// <param name="frame">The rasterised frame.</param>
        /// <returns>The bitmap, or null when the frame has no pixels.</returns>
        public static Bitmap? ToDisplayBitmap(RSBufferedImage frame) {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            int width = frame.GetWidth();
            int height = frame.GetHeight();
            if (width <= 0 || height <= 0)
                return null;

            int[] pixels = frame.GetPixels();
            if (pixels.Length < width * height)
                return null;

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
            }
            finally {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        /// <summary>
        ///     What a tile for one frame of a set will be showing, decided without rasterising it.
        /// </summary>
        /// <remarks>
        ///     The one place the rule lives, because three callers need it and one of them is a
        ///     test. A frame whose stored plane has no area is <see cref="SpriteTileContent.Empty"/>
        ///     even though its canvas may be a perfectly good size - the canvas is the set's and the
        ///     tile is the frame's, so deciding this from the canvas would draw 2,377 of the vanilla
        ///     capture's frames as a blank picture rather than as no picture.
        /// </remarks>
        /// <param name="set">The decoded set.</param>
        /// <param name="frameId">The frame the tile is for.</param>
        /// <returns>Whether there is a picture to draw.</returns>
        public static SpriteTileContent ContentOf(SpriteDefinition set, int frameId) {
            if (set == null)
                throw new ArgumentNullException(nameof(set));

            if (set.Frames == null || frameId < 0 || frameId >= set.Frames.Count)
                return SpriteTileContent.Empty;

            return set.Frames[frameId].Area > 0 && CanRasterise(set)
                ? SpriteTileContent.Picture
                : SpriteTileContent.Empty;
        }

        /// <summary>
        ///     Whether every frame in a set can be rasterised at all.
        /// </summary>
        /// <remarks>
        ///     <c>SpriteDefinition.GetFrames</c> is all or nothing - it rasterises the whole set on
        ///     first use - and a frame whose canvas works out at zero in either direction cannot be
        ///     given a <see cref="Bitmap"/>, so asking for one set's frames would throw and cost the
        ///     rest of them. The canvas is the stored one grown to fit an overflowing frame, which
        ///     is the same rule <c>SpriteDefinition.Rasterise</c> applies.
        /// </remarks>
        /// <param name="set">The decoded set.</param>
        /// <returns>Whether <c>GetFrames</c> is safe to call.</returns>
        public static bool CanRasterise(SpriteDefinition set) {
            if (set == null)
                throw new ArgumentNullException(nameof(set));
            if (set.Frames == null || set.Frames.Count == 0)
                return false;

            foreach (SpriteFrame frame in set.Frames)
                if (Math.Max(set.width, frame.OffsetX + frame.SubWidth) <= 0 ||
                    Math.Max(set.height, frame.OffsetY + frame.SubHeight) <= 0)
                    return false;

            return true;
        }

        private static void DrawMarker(Graphics graphics, Rectangle area, string text, Font font, Color ink) {
            using var brush = new SolidBrush(ink);
            using var format = new StringFormat {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString(text, font, brush, area, format);
        }
    }
}
