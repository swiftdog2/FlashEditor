using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Cache.Util;

namespace FlashEditor.Map {
    /// <summary>
    ///     Renders a map square at one pixel per tile or coarser, straight into the pixel buffer.
    /// </summary>
    /// <remarks>
    ///     A second renderer rather than a fractional <c>TilePixels</c> on
    ///     <see cref="MapRasteriser"/>, for two reasons that do not go away.
    ///
    ///     Sub-pixel GDI+ fills are both slow and wrong: at half a pixel per tile a
    ///     <see cref="RectangleF"/> fill either disappears or bleeds into its neighbour depending on
    ///     where it lands, and widening <c>TilePixels</c> to a float to allow it would touch every
    ///     draw method and break the integer identity the windowed render depends on.
    ///
    ///     And at one pixel per tile a tile <em>is</em> a pixel: 4096 direct writes into
    ///     <see cref="DirectBitmap.Bits"/> beat 4096 <c>FillRectangle</c> calls by an order of
    ///     magnitude, and a whole-world sweep pays that 1684 times.
    ///
    ///     Colour is not reimplemented. The blend, the relief and the per-tile decision all come from
    ///     the <see cref="MapRasteriser"/> handed in, so the overview cannot drift away from the
    ///     detailed view on colour. What it does drop is everything that cannot be drawn in one
    ///     pixel: walls, decorations, object outlines, tile flags, the grid, and the overlay
    ///     triangulation, which reduces to "does this shape cover the tile".
    /// </remarks>
    public sealed class MapOverviewRasteriser {
        private readonly MapRasteriser colours;

        /// <summary>Creates an overview renderer sharing another rasteriser's colour resolution.</summary>
        /// <param name="colours">The rasteriser to take colours, relief and the palette from.</param>
        public MapOverviewRasteriser(MapRasteriser colours) {
            this.colours = colours ?? throw new ArgumentNullException(nameof(colours));
        }

        /// <summary>
        ///     Renders one window of a scene at <c>2^level</c> pixels per tile, for a level at or
        ///     below zero.
        /// </summary>
        /// <param name="scene">The scene to draw from, normally a 3x3 apron.</param>
        /// <param name="sceneTileWindow">The tile window to produce, normally the centre square.</param>
        /// <param name="plane">The plane to draw.</param>
        /// <param name="layers">Which layers are on. Only the terrain ones can apply here.</param>
        /// <param name="level">
        ///     The pyramid level, -4 to 0. Level 0 is one pixel per tile; each step down halves it by
        ///     a box average, which is the only reduction that keeps a coastline readable.
        /// </param>
        /// <param name="reliefStrength">Relief strength, 0 to 1.</param>
        /// <returns>A new bitmap. The caller owns it.</returns>
        public DirectBitmap RenderSquare(MapScene scene, Rectangle sceneTileWindow, int plane,
            MapLayers layers, int level, float reliefStrength) {

            if (level > 0) throw new ArgumentOutOfRangeException(nameof(level), "The overview band is level 0 and below");

            DirectBitmap full = RenderBase(scene, sceneTileWindow, plane, layers, reliefStrength);
            if (level == 0)
                return full;

            using (full)
                return Downsample(full, 1 << -level);
        }

        /// <summary>
        ///     Renders every overview level of a square from one decode.
        /// </summary>
        /// <remarks>
        ///     The decode dominates: a square is one <c>m</c> read plus one <c>l</c> read and
        ///     decrypt, against a 74x74 blend, a 65x65 height grid and 4096 pixel writes. Producing
        ///     the four reductions at the same time therefore costs almost nothing and means the
        ///     whole-world sweep leaves every zoom-out level already drawn, instead of re-decoding
        ///     the cache once per level the user visits.
        ///
        ///     Each reduction is taken from the one-pixel-per-tile base rather than from the level
        ///     above it, so a tile is byte-for-byte what a single
        ///     <see cref="RenderSquare"/> call for that level would have produced.
        /// </remarks>
        /// <param name="scene">The scene to draw from.</param>
        /// <param name="sceneTileWindow">The tile window to produce.</param>
        /// <param name="plane">The plane to draw.</param>
        /// <param name="layers">Which layers are on.</param>
        /// <param name="coarsestLevel">The lowest level to produce, normally -4.</param>
        /// <param name="reliefStrength">Relief strength, 0 to 1.</param>
        /// <returns>One bitmap per level from 0 down to <paramref name="coarsestLevel"/>. The caller owns them all.</returns>
        public IReadOnlyList<(int Level, DirectBitmap Bitmap)> RenderPyramid(MapScene scene, Rectangle sceneTileWindow,
            int plane, MapLayers layers, int coarsestLevel, float reliefStrength) {

            DirectBitmap full = RenderBase(scene, sceneTileWindow, plane, layers, reliefStrength);

            var levels = new List<(int Level, DirectBitmap Bitmap)> { (0, full) };

            for (int level = -1; level >= coarsestLevel; level--) {
                int factor = 1 << -level;
                if (full.Width < factor || full.Height < factor)
                    break;

                levels.Add((level, Downsample(full, factor)));
            }

            return levels;
        }

        private DirectBitmap RenderBase(MapScene scene, Rectangle sceneTileWindow, int plane,
            MapLayers layers, float reliefStrength) {

            if (scene == null) throw new ArgumentNullException(nameof(scene));

            int[,] blended = (layers & MapLayers.Underlay) != 0
                ? colours.BlendWindow(scene, plane, sceneTileWindow)
                : null;

            float[,] relief = (layers & MapLayers.Hillshade) != 0
                ? colours.ReliefWindow(scene, plane, sceneTileWindow, reliefStrength)
                : null;

            //The blended array is handed back in window coordinates, so its origin is the window's.
            Rectangle blendWindow = sceneTileWindow;

            var full = new DirectBitmap(sceneTileWindow.Width, sceneTileWindow.Height);
            int voidArgb = colours.VoidColour.ToArgb();

            for (int x = sceneTileWindow.Left; x < sceneTileWindow.Right; x++) {
                for (int y = sceneTileWindow.Top; y < sceneTileWindow.Bottom; y++) {
                    int lx = x - sceneTileWindow.Left;
                    int ly = y - sceneTileWindow.Top;

                    float shade = relief == null ? 1f : relief[lx, ly];
                    int rgb = colours.TileColourOrNone(scene, plane, x, y, blended, blendWindow, shade, layers, out _);

                    //Scene Y runs north and the bitmap's rows run down, the same flip the tile
                    //rasteriser applies.
                    int row = sceneTileWindow.Height - 1 - ly;
                    full.Bits[row * full.Width + lx] = rgb == MapPalette.NoColour
                        ? voidArgb
                        : unchecked((int) 0xFF000000) | (rgb & 0xFFFFFF);
                }
            }

            return full;
        }

        /// <summary>
        ///     Box-averages a bitmap down by a whole factor.
        /// </summary>
        /// <remarks>
        ///     A box average rather than point sampling. At one sixteenth scale a point sample keeps
        ///     one tile in 256 and the world dissolves into noise, whereas the average keeps the
        ///     silhouette of a coastline even when a whole square is four pixels.
        /// </remarks>
        /// <param name="source">The full-resolution bitmap.</param>
        /// <param name="factor">The reduction factor, a power of two.</param>
        /// <returns>A new bitmap. The caller owns it.</returns>
        private static DirectBitmap Downsample(DirectBitmap source, int factor) {
            int width = Math.Max(1, source.Width / factor);
            int height = Math.Max(1, source.Height / factor);

            var target = new DirectBitmap(width, height);
            int samples = factor * factor;

            for (int x = 0; x < width; x++) {
                for (int y = 0; y < height; y++) {
                    int red = 0, green = 0, blue = 0;

                    for (int sx = 0; sx < factor; sx++) {
                        for (int sy = 0; sy < factor; sy++) {
                            int pixel = source.Bits[(y * factor + sy) * source.Width + x * factor + sx];
                            red += (pixel >> 16) & 0xFF;
                            green += (pixel >> 8) & 0xFF;
                            blue += pixel & 0xFF;
                        }
                    }

                    target.Bits[y * width + x] = unchecked((int) 0xFF000000)
                        | ((red / samples) << 16)
                        | ((green / samples) << 8)
                        | (blue / samples);
                }
            }

            return target;
        }
    }
}
