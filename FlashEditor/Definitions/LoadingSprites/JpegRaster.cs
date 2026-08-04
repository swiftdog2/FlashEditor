using System;
using System.IO;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     A decoded JPEG: one full-resolution sample plane per component, before any colour model
    ///     is applied.
    /// </summary>
    /// <remarks>
    ///     Planes and pixels are kept apart because the colour model is the one thing about these
    ///     files that is not stated anywhere. The planes are what the entropy coder, the
    ///     dequantiser and the inverse DCT produce and are not in dispute; <see cref="ToArgb"/> is
    ///     where an interpretation is applied, and it refuses rather than guesses when the file is
    ///     a shape the evidence does not cover.
    /// </remarks>
    public sealed class JpegRaster {
        private readonly byte[][] _planes;

        /// <summary>Image width in pixels.</summary>
        public int Width { get; }

        /// <summary>Image height in pixels.</summary>
        public int Height { get; }

        /// <summary>How many component planes the image carries.</summary>
        public int ComponentCount => _planes.Length;

        /// <summary>
        ///     How many of the scan's bytes the entropy decode actually read.
        /// </summary>
        /// <remarks>
        ///     The one sharp check available on the JPEG half. Nothing in a JPEG states how long the
        ///     entropy-coded data is - it runs until a marker - so a decoder using the wrong Huffman
        ///     table, the wrong MCU count or the wrong sampling factors desynchronises and stops
        ///     somewhere else. Landing on the last byte of the scan is what says every block was
        ///     read the way the encoder wrote it, and it is the JPEG equivalent of the
        ///     exact-consumption sweep the opcode formats get.
        /// </remarks>
        public int ScanBytesConsumed { get; }

        /// <summary>How many bytes the scan holds, for <see cref="ScanBytesConsumed"/> to be held against.</summary>
        public int ScanBytesAvailable { get; }

        /// <summary>Binds the decoded planes to the image geometry.</summary>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <param name="planes">One <c>width * height</c> plane per component.</param>
        /// <param name="scanBytesConsumed">How many scan bytes the entropy decode read.</param>
        /// <param name="scanBytesAvailable">How many scan bytes there were.</param>
        public JpegRaster(int width, int height, byte[][] planes, int scanBytesConsumed, int scanBytesAvailable) {
            Width = width;
            Height = height;
            _planes = planes ?? throw new ArgumentNullException(nameof(planes));
            ScanBytesConsumed = scanBytesConsumed;
            ScanBytesAvailable = scanBytesAvailable;
        }

        /// <summary>One component's samples, row-major, at full resolution.</summary>
        /// <param name="component">The component index, in frame-header order.</param>
        /// <returns>The plane.</returns>
        public byte[] Plane(int component) => _planes[component];

        /// <summary>
        ///     Whether a component's plane holds one value everywhere.
        /// </summary>
        /// <remarks>
        ///     This is what licenses discarding the fourth component of an index-32 image. It holds
        ///     for every one of the twenty-one in both caches, and for the client's own probe blob,
        ///     so the plane carries no picture in any of them - but it is measured per file rather
        ///     than assumed, because a plane that did vary would be information and dropping it
        ///     would be a silent edit.
        /// </remarks>
        /// <param name="component">The component index.</param>
        /// <returns>Whether every sample is the same.</returns>
        public bool IsConstant(int component) {
            byte[] plane = _planes[component];
            for (int i = 1; i < plane.Length; i++)
                if (plane[i] != plane[0])
                    return false;
            return true;
        }

        /// <summary>
        ///     Converts the planes to opaque ARGB pixels.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     <b>Three planes are read as Y, Cb and Cr, and a fourth is discarded.</b> Nothing in
        ///     the 637 client says so - it hands the bytes to
        ///     <c>Toolkit.getDefaultToolkit().createImage</c> and takes whatever the JVM returns
        ///     (<c>Class271.java:29-65</c>) - so the reading is settled by the file's own tables
        ///     instead, and every part of it is checkable in seconds:
        ///     </para>
        ///     <list type="bullet">
        ///     <item>The two quantisation tables are the ITU T.81 Annex K <b>luminance</b> and
        ///     <b>chrominance</b> tables at IJG quality 75. The luminance one is assigned to
        ///     components 1 and 4, the chrominance one to components 2 and 3 alone.</item>
        ///     <item>Components 2 and 3 alone are subsampled 2x2 against the others, which is 4:2:0
        ///     chroma subsampling. A genuine CMYK image has no reason to halve two of its four inks
        ///     and not the rest.</item>
        ///     <item>The fourth plane is constant in every file, so it carries nothing.</item>
        ///     <item>In the flat interface images the two chroma planes sit at exactly 128, the
        ///     level-shift midpoint, and the image comes out neutral grey. Under a CMYK reading the
        ///     same file would be a tinted picture.</item>
        ///     </list>
        ///     <para>
        ///     The transform itself is libjpeg's <c>ycc_rgb_convert</c>, table for table, because
        ///     libjpeg is what the client's JVM runs underneath <c>createImage</c>. It is worth
        ///     naming what goes wrong without this: these files carry no <c>JFIF APP0</c> and no
        ///     <c>Adobe APP14</c>, so a four-component file makes every standard decoder fall back
        ///     to CMYK. The result is a recognisable, plausible, wrong image - which is the failure
        ///     that gets accepted because it looks like an image.
        ///     </para>
        /// </remarks>
        /// <returns>Opaque ARGB pixels, row-major.</returns>
        /// <exception cref="InvalidDataException">
        ///     The component layout is one no evidence covers - a fourth plane that varies, or a
        ///     component count other than one, three or four.
        /// </exception>
        public int[] ToArgb() {
            int[] pixels = new int[Width * Height];

            if (ComponentCount == 1) {
                byte[] grey = _planes[0];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = unchecked((int) 0xFF000000) | (grey[i] << 16) | (grey[i] << 8) | grey[i];
                return pixels;
            }

            if (ComponentCount != 3 && ComponentCount != 4)
                throw new InvalidDataException(
                    $"A {ComponentCount}-component JPEG has no established colour model in this cache.");

            if (ComponentCount == 4 && !IsConstant(3))
                throw new InvalidDataException(
                    "The fourth component varies, so it carries picture information. Discarding it is only " +
                    "justified while it is a constant filler plane, which it is in every index-32 image; " +
                    "establish what a varying one means before rendering it.");

            byte[] luma = _planes[0];
            byte[] blueChroma = _planes[1];
            byte[] redChroma = _planes[2];

            for (int i = 0; i < pixels.Length; i++) {
                int y = luma[i];
                int cb = blueChroma[i];
                int cr = redChroma[i];

                int red = Clamp(y + YCbCr.RedFromCr[cr]);
                int green = Clamp(y + ((YCbCr.GreenFromCb[cb] + YCbCr.GreenFromCr[cr]) >> YCbCr.ScaleBits));
                int blue = Clamp(y + YCbCr.BlueFromCb[cb]);

                pixels[i] = unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue;
            }

            return pixels;
        }

        private static int Clamp(int value) {
            return value < 0 ? 0 : (value > 255 ? 255 : value);
        }

        /// <summary>
        ///     libjpeg's fixed-point YCbCr to RGB tables, built the way <c>jdcolor.c</c> builds them.
        /// </summary>
        /// <remarks>
        ///     Reproduced rather than replaced with floating-point arithmetic so the output matches
        ///     what the client's JVM produces: <c>Toolkit.createImage</c> decodes through libjpeg,
        ///     and its rounding is part of the answer.
        /// </remarks>
        private static class YCbCr {
            /// <summary>Fractional bits in the fixed-point constants - libjpeg's <c>SCALEBITS</c>.</summary>
            public const int ScaleBits = 16;

            /// <summary>Red offset per Cr value.</summary>
            public static readonly int[] RedFromCr = new int[256];

            /// <summary>Blue offset per Cb value.</summary>
            public static readonly int[] BlueFromCb = new int[256];

            /// <summary>Unscaled green contribution per Cr value, summed before shifting.</summary>
            public static readonly int[] GreenFromCr = new int[256];

            /// <summary>Unscaled green contribution per Cb value, carrying the rounding term.</summary>
            public static readonly int[] GreenFromCb = new int[256];

            static YCbCr() {
                const int one = 1 << ScaleBits;
                const int half = 1 << (ScaleBits - 1);

                for (int i = 0; i < 256; i++) {
                    int x = i - 128;
                    RedFromCr[i] = (Fix(1.40200, one) * x + half) >> ScaleBits;
                    BlueFromCb[i] = (Fix(1.77200, one) * x + half) >> ScaleBits;
                    GreenFromCr[i] = -Fix(0.71414, one) * x;
                    GreenFromCb[i] = -Fix(0.34414, one) * x + half;
                }
            }

            private static int Fix(double value, int one) {
                return (int) (value * one + 0.5);
            }
        }
    }
}
