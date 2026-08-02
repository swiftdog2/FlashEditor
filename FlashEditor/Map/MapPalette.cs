using System;

namespace FlashEditor.Map {
    /// <summary>
    ///     The client's colour model: packed 16-bit HSL, the 65536-entry HSL to RGB palette, and the
    ///     RGB to HSL conversion floor overlays use.
    /// </summary>
    /// <remarks>
    ///     None of this is textbook HSL. It normalises by 256 rather than 255, truncates at every
    ///     step, and desaturates on a lightness ladder before packing. Substituting a standard
    ///     library conversion produces colours that are close but visibly wrong, so the arithmetic
    ///     here is transcribed from the client rather than reimplemented.
    ///
    ///     Sources: Class93_Sub1.method904 (palette), Class79.method801 (pack),
    ///     Class38.method348 with the Class64_Sub24.method652 wrapper (RGB to HSL).
    ///     See <c>reference/hydra-637-maps/05-colour-and-rendering.md</c>.
    /// </remarks>
    public static class MapPalette {
        /// <summary>Packed HSL meaning "no colour", used by overlays to show the underlay through.</summary>
        public const int NoColour = -1;

        /// <summary>The RGB value an overlay uses to mean <see cref="NoColour"/>.</summary>
        public const int TransparentRgb = 0xFF00FF;

        /// <summary>
        ///     The gamma the palette is built with.
        /// </summary>
        /// <remarks>
        ///     The client randomises this per session as <c>0.7 + (random() * 0.03 - 0.015)</c>
        ///     (Class93_Sub1.java:140). An editor wants the same colours every run, so the centre
        ///     value is pinned and the jitter dropped.
        /// </remarks>
        public const double Gamma = 0.7;

        private static readonly int[] Rgb = BuildPalette(Gamma);

        /// <summary>
        ///     Converts a packed HSL to 24-bit RGB.
        /// </summary>
        /// <param name="hsl16">A packed HSL, or <see cref="NoColour"/>.</param>
        /// <returns>The RGB value, or 0 for <see cref="NoColour"/>.</returns>
        public static int ToRgb(int hsl16) {
            if (hsl16 < 0)
                return 0;
            return Rgb[hsl16 & 0xFFFF];
        }

        /// <summary>
        ///     Packs hue, saturation and lightness into the client's 16-bit HSL.
        /// </summary>
        /// <remarks>
        ///     Class79.method801. The saturation is reduced as lightness rises, because a very light
        ///     colour cannot carry much saturation in the 3 bits available without banding badly.
        /// </remarks>
        /// <param name="hue">Hue, 0..255.</param>
        /// <param name="saturation">Saturation, 0..255.</param>
        /// <param name="lightness">Lightness, 0..255.</param>
        /// <returns>The packed value: hue in bits 10-15, saturation 7-9, lightness 0-6.</returns>
        public static int Pack(int hue, int saturation, int lightness) {
            saturation = Desaturate(saturation, lightness);
            return ((hue >> 2 & 0x3F) << 10) + ((saturation >> 5) << 7) + (lightness >> 1);
        }

        /// <summary>The lightness-driven saturation reduction shared by both pack paths.</summary>
        private static int Desaturate(int saturation, int lightness) {
            if (lightness > 243) return saturation >> 4;
            if (lightness > 217) return saturation >> 3;
            if (lightness > 192) return saturation >> 2;
            if (lightness > 179) return saturation >> 1;
            return saturation;
        }

        /// <summary>
        ///     Converts a 24-bit RGB to packed HSL, as floor overlays do.
        /// </summary>
        /// <remarks>
        ///     Class38.method348, wrapped by Class64_Sub24.method652 which maps the magenta sentinel
        ///     to <see cref="NoColour"/>.
        /// </remarks>
        /// <param name="rgb">A 24-bit RGB value.</param>
        /// <returns>The packed HSL, or <see cref="NoColour"/> for <see cref="TransparentRgb"/>.</returns>
        public static int FromRgb(int rgb) {
            if (rgb == TransparentRgb)
                return NoColour;

            //Normalised by 256, not 255. The client is consistent about this and it shifts every
            //resulting channel slightly; matching it matters more than being correct.
            double r = ((rgb >> 16) & 0xFF) / 256.0;
            double g = ((rgb >> 8) & 0xFF) / 256.0;
            double b = (rgb & 0xFF) / 256.0;

            double min = Math.Min(r, Math.Min(g, b));
            double max = Math.Max(r, Math.Max(g, b));

            double hue = 0.0;
            double saturation = 0.0;
            double lightness = (min + max) / 2.0;

            if (min != max) {
                double delta = max - min;
                saturation = lightness < 0.5
                    ? delta / (max + min)
                    : delta / (2.0 - max - min);

                if (r == max)
                    hue = (g - b) / delta;
                else if (g == max)
                    hue = 2.0 + (b - r) / delta;
                else if (b == max)
                    hue = 4.0 + (r - g) / delta;
            }

            hue /= 6.0;

            int h = (int) (hue * 256.0);
            int s = Clamp255((int) (saturation * 256.0));
            int l = Clamp255((int) (lightness * 256.0));

            s = Desaturate(s, l);

            //h is masked to a byte before the shift, so a hue that lands exactly on 256 wraps to 0.
            return ((s >> 5) << 7) + (((h & 0xFF) >> 2) << 10) + (l >> 1);
        }

        private static int Clamp255(int value) {
            if (value < 0) return 0;
            return value > 255 ? 255 : value;
        }

        /// <summary>
        ///     Builds the 65536-entry HSL to RGB lookup.
        /// </summary>
        /// <remarks>
        ///     Class93_Sub1.method904. Note the saturation term can never be zero - it is
        ///     <c>bits / 8.0 + 0.0625</c> - so the greyscale short-circuit in the client is
        ///     unreachable and is not reproduced here.
        /// </remarks>
        private static int[] BuildPalette(double gamma) {
            int[] palette = new int[65536];

            for (int i = 0; i < palette.Length; i++) {
                double hue = ((i >> 10) & 0x3F) / 64.0 + 0.0078125;

                //The client masks with 0x3a8 here. Bits 3 and 5 of that mask are discarded by the
                //shift, so it is 0x380 with two junk bits set - one of the redundant-mask tricks
                //the obfuscator uses throughout.
                double saturation = ((i & 0x380) >> 7) / 8.0 + 0.0625;
                double lightness = (i & 0x7F) / 128.0;

                double q = lightness < 0.5
                    ? lightness * (1.0 + saturation)
                    : lightness + saturation - lightness * saturation;
                double p = 2.0 * lightness - q;

                double r = HueToChannel(p, q, Wrap(hue + 1.0 / 3.0));
                double g = HueToChannel(p, q, hue);
                double b = HueToChannel(p, q, Wrap(hue - 1.0 / 3.0));

                int red = (int) (Math.Pow(r, gamma) * 256.0);
                int green = (int) (Math.Pow(g, gamma) * 256.0);
                int blue = (int) (Math.Pow(b, gamma) * 256.0);

                palette[i] = (red << 16) + (green << 8) + blue;
            }

            return palette;
        }

        private static double Wrap(double t) {
            if (t > 1.0) return t - 1.0;
            if (t < 0.0) return t + 1.0;
            return t;
        }

        private static double HueToChannel(double p, double q, double t) {
            if (t * 6.0 < 1.0) return p + (q - p) * 6.0 * t;
            if (t * 2.0 < 1.0) return q;
            if (t * 3.0 < 2.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }
    }
}
