using System;
using System.Drawing;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     Where one sprite is drawn inside a fixed list tile, and at what scale.
    /// </summary>
    /// <remarks>
    ///     A value type with no drawing in it so the decision can be asserted on its own. The list
    ///     column, the status line and the painter all read the same fit, which is what stops the
    ///     grid from claiming a scale the picture was not drawn at.
    /// </remarks>
    public readonly struct SpriteTileFit {
        private SpriteTileFit(Rectangle bounds, int upscale, int percent, bool empty) {
            Bounds = bounds;
            Upscale = upscale;
            Percent = percent;
            IsEmpty = empty;
        }

        /// <summary>Where the picture lands within the tile, in tile coordinates.</summary>
        public Rectangle Bounds { get; }

        /// <summary>
        ///     The whole-number magnification applied, or 0 when the picture had to be shrunk.
        /// </summary>
        /// <remarks>
        ///     Whole numbers only, because a sprite is pixel art: 2x2 sprites exist in this index and
        ///     a fractional magnification resamples four pixels into a smear that says nothing about
        ///     what the file holds. 1 means the picture is at its stored size.
        /// </remarks>
        public int Upscale { get; }

        /// <summary>The drawn size as a percentage of the stored size, for a shrunk picture.</summary>
        /// <remarks>Zero when the picture was not shrunk, so <see cref="IsFullSize"/> is the test to use.</remarks>
        public int Percent { get; }

        /// <summary>Whether the sprite has no pixels at all, so there is nothing to place.</summary>
        /// <remarks>
        ///     2,377 of the vanilla capture's 11,177 frames declare a zero-area plane. They are
        ///     legitimate records and have to read as empty rather than as a tile that failed to
        ///     draw, which is why this is a state of the fit rather than a failure of it.
        /// </remarks>
        public bool IsEmpty { get; }

        /// <summary>Whether every stored pixel is on screen at least once.</summary>
        public bool IsFullSize => IsEmpty || Upscale >= 1;

        /// <summary>The fit for a sprite with no pixels.</summary>
        public static SpriteTileFit Empty => new SpriteTileFit(Rectangle.Empty, 0, 0, true);

        /// <summary>
        ///     How the tile is scaled, in the shortest form that is still unambiguous.
        /// </summary>
        /// <remarks>
        ///     A shrunk tile says so with a percentage rather than being left to look like a small
        ///     sprite. Without that, a 400x200 sprite and a 24x12 one are the same picture in the
        ///     grid and only one of them can be judged from it.
        /// </remarks>
        /// <returns>The label, for example <c>1:1</c>, <c>x4</c> or <c>17%</c>.</returns>
        public override string ToString() {
            if (IsEmpty)
                return "-";
            if (Upscale == 1)
                return "1:1";
            return Upscale > 1 ? "x" + Upscale : Percent + "%";
        }

        /// <summary>
        ///     Letterboxes a sprite into a tile, preserving its aspect ratio.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Never stretched, in either direction. Index 8 runs from 2x2 up to over 400x200 and one
        ///     tile has to serve both, so the two directions are deliberately not symmetrical:
        ///     anything that already fits is magnified by a <b>whole number</b> and drawn with
        ///     nearest-neighbour sampling, and only a picture too big for the tile is resampled down.
        ///     </para>
        ///     <para>
        ///     The magnification is the smaller of the two whole-number fits, which is what keeps the
        ///     aspect exact rather than merely close: a 2x3 sprite in a 60x60 tile becomes 40x60 and
        ///     not 60x60.
        ///     </para>
        /// </remarks>
        /// <param name="sourceWidth">The sprite's stored width in pixels.</param>
        /// <param name="sourceHeight">The sprite's stored height in pixels.</param>
        /// <param name="tileWidth">The tile's width in pixels.</param>
        /// <param name="tileHeight">The tile's height in pixels.</param>
        /// <returns>The fit, which is <see cref="Empty"/> when the sprite has no area.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The tile has no area.</exception>
        public static SpriteTileFit Fit(int sourceWidth, int sourceHeight, int tileWidth, int tileHeight) {
            if (tileWidth <= 0 || tileHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(tileWidth),
                    "A tile of " + tileWidth + "x" + tileHeight + " has nothing to draw into.");

            if (sourceWidth <= 0 || sourceHeight <= 0)
                return Empty;

            if (sourceWidth <= tileWidth && sourceHeight <= tileHeight) {
                int upscale = Math.Max(1, Math.Min(tileWidth / sourceWidth, tileHeight / sourceHeight));
                return new SpriteTileFit(
                    Centre(sourceWidth * upscale, sourceHeight * upscale, tileWidth, tileHeight),
                    upscale, 100, false);
            }

            /* Integer arithmetic throughout. The comparison decides which axis the tile runs out of
               first, and the other axis is then derived from the source's own ratio rather than from
               a second division, so the two can only disagree by the half pixel the rounding adds. */
            int width;
            int height;
            if ((long) sourceWidth * tileHeight >= (long) sourceHeight * tileWidth) {
                width = tileWidth;
                height = (int) Math.Min(tileHeight,
                    Math.Max(1, ((long) sourceHeight * tileWidth + sourceWidth / 2) / sourceWidth));
            } else {
                height = tileHeight;
                width = (int) Math.Min(tileWidth,
                    Math.Max(1, ((long) sourceWidth * tileHeight + sourceHeight / 2) / sourceHeight));
            }

            //Reported off the axis the tile constrained, so a picture shrunk to a single pixel row
            //still reports the percentage it was actually drawn at.
            int percent = (int) Math.Max(1, (long) width * 100 / sourceWidth);
            return new SpriteTileFit(Centre(width, height, tileWidth, tileHeight), 0, percent, false);
        }

        private static Rectangle Centre(int width, int height, int tileWidth, int tileHeight) {
            return new Rectangle((tileWidth - width) / 2, (tileHeight - height) / 2, width, height);
        }
    }
}
