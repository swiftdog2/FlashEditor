using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     The pictures the sprite import tests convert, stated once for every suite that needs one.
    /// </summary>
    /// <remarks>
    ///     Shared rather than copied per file because the convention these builders encode is the
    ///     thing under test on the other side: every one of them writes straight, un-premultiplied
    ///     ARGB through <c>LockBits</c>, which is the same convention
    ///     <c>SpriteImageImporter.ReadStraightArgb</c> reads with. Two copies of that could drift into
    ///     two conventions, and a premultiplied source would quietly darken every partly transparent
    ///     pixel in a test that looked like it was measuring the palette.
    /// </remarks>
    internal static class SpritePictures
    {
        /// <summary>
        ///     Builds a straight-ARGB bitmap from the pixels given, row by row.
        /// </summary>
        /// <param name="width">The picture's width.</param>
        /// <param name="height">The picture's height.</param>
        /// <param name="pixels">One ARGB value per pixel, row-major.</param>
        /// <returns>The bitmap, owned by the caller.</returns>
        public static Bitmap Picture(int width, int height, params int[] pixels)
        {
            Assert.Equal(width * height, pixels.Length);

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        /// <summary>Builds a picture of one flat colour.</summary>
        /// <param name="width">The picture's width.</param>
        /// <param name="height">The picture's height.</param>
        /// <param name="argb">The colour every pixel holds.</param>
        /// <returns>The bitmap, owned by the caller.</returns>
        public static Bitmap Flat(int width, int height, int argb)
        {
            var pixels = new int[width * height];
            Array.Fill(pixels, argb);
            return Picture(width, height, pixels);
        }

        /// <summary>
        ///     Opaque colours no two of which are equal, and none of which is black.
        /// </summary>
        /// <remarks>
        ///     Spread across all three channels rather than along one, so a quantiser that only ever
        ///     splits on red still has to work. Black is excluded because it is the one colour whose
        ///     stored spelling changes, and it has its own cases.
        /// </remarks>
        /// <param name="count">How many, up to the 336 the lattice below holds.</param>
        /// <returns>The colours, as opaque ARGB.</returns>
        public static int[] DistinctColours(int count)
        {
            //A 6 x 7 x 8 lattice, which is a bijection from i and therefore cannot collide - the
            //first version of this multiplied i by three primes modulo 251 and silently repeated
            //itself every 251 colours, so a test asking for 255 got 251 and failed on the palette
            //size rather than on anything the importer did.
            Assert.InRange(count, 0, 6 * 7 * 8);

            var colours = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int red = 1 + i % 6 * 40;
                int green = 1 + i / 6 % 7 * 35;
                int blue = 1 + i / 42 * 25;
                colours.Add(unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue);
            }

            Assert.Equal(count, colours.Distinct().Count());
            return colours.ToArray();
        }

        /// <summary>
        ///     A smooth ramp of far more colours than a palette holds, which is what forces a cut.
        /// </summary>
        /// <param name="count">How many pixels.</param>
        /// <returns>The colours, as opaque ARGB.</returns>
        public static int[] Gradient(int count)
        {
            var colours = new int[count];
            for (int i = 0; i < count; i++)
            {
                int red = 1 + i * 255 / Math.Max(1, count - 1);
                int green = 1 + (count - 1 - i) * 200 / Math.Max(1, count - 1);
                int blue = 1 + i % 199;
                colours[i] = unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue;
            }

            return colours;
        }
    }
}
