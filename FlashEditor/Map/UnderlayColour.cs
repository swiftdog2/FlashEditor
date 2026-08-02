using System;

namespace FlashEditor.Map {
    /// <summary>
    ///     A floor underlay's colour, decomposed into the four accumulators the terrain blender
    ///     averages.
    /// </summary>
    /// <remarks>
    ///     An underlay does not carry a packed HSL. The client splits its RGB into these four
    ///     components at decode time (FloorUnderlay.method718, FloorUnderlay.java:71-134) precisely
    ///     so a neighbourhood of tiles can be averaged before packing.
    ///
    ///     The hue is stored pre-multiplied by a chroma-derived weight, and the blender divides the
    ///     summed weighted hue by the summed weight rather than by the tile count. That is what
    ///     stops grey and near-grey tiles from dragging the averaged hue toward zero, and it is the
    ///     single detail most likely to be lost in a reimplementation.
    /// </remarks>
    public readonly struct UnderlayColour {
        /// <summary>Hue, pre-multiplied by <see cref="HueWeight"/>.</summary>
        public int WeightedHue { get; }

        /// <summary>The chroma-derived weight the hue is multiplied by. Never below 1.</summary>
        public int HueWeight { get; }

        /// <summary>Saturation, 0..255.</summary>
        public int Saturation { get; }

        /// <summary>Lightness, 0..255.</summary>
        public int Lightness { get; }

        private UnderlayColour(int weightedHue, int hueWeight, int saturation, int lightness) {
            WeightedHue = weightedHue;
            HueWeight = hueWeight;
            Saturation = saturation;
            Lightness = lightness;
        }

        /// <summary>
        ///     Decomposes a 24-bit RGB into blend components.
        /// </summary>
        /// <param name="rgb">The underlay's colour.</param>
        /// <returns>The four accumulator components.</returns>
        public static UnderlayColour FromRgb(int rgb) {
            //Normalised by 256, not 255, consistently with the rest of the client's colour model.
            double r = ((rgb >> 16) & 0xFF) / 256.0;
            double g = ((rgb >> 8) & 0xFF) / 256.0;
            double b = (rgb & 0xFF) / 256.0;

            double min = Math.Min(r, Math.Min(g, b));
            double max = Math.Max(r, Math.Max(g, b));

            double hue = 0.0;
            double saturation = 0.0;
            double lightness = (max + min) / 2.0;

            if (max != min) {
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

            int light = Clamp255((int) (lightness * 256.0));
            int sat = Clamp255((int) (saturation * 256.0));

            //The weight peaks at mid lightness and falls away toward black and white, so washed-out
            //tiles contribute little hue to the average.
            int weight = lightness > 0.5
                ? (int) (saturation * (1.0 - lightness) * 512.0)
                : (int) (lightness * saturation * 512.0);

            //Floored at 1 so a fully desaturated underlay still contributes to the weight sum and
            //cannot make the blender divide by zero on its own.
            if (weight < 1)
                weight = 1;

            return new UnderlayColour((int) (hue * weight), weight, sat, light);
        }

        private static int Clamp255(int value) {
            if (value < 0) return 0;
            return value > 255 ? 255 : value;
        }
    }
}
