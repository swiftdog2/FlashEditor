using System.Collections.Generic;

namespace FlashEditor.Definitions.Models {
    /// <summary>
    ///     Turns an RGB colour into the colour word a model face stores.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The client has no such conversion: model colours are authored as words. What it has is
    ///     the forward path a face is drawn through - <c>Class111_Sub2.method2117</c>
    ///     (Class111_Sub2.java:8, applied to a face at Renderable_Sub1.java:2300) and then the
    ///     <c>Class122.method2199</c> palette (Class122.java:9) - which <see cref="ModelDefinition.RawHslToRgb"/>
    ///     reproduces. So the inverse is a search of that path: the word the client draws as the
    ///     given colour, or as the nearest colour it can draw.
    ///     </para>
    ///     <para>
    ///     Not <c>MapPalette.FromRgb</c>. That is the floor conversion (<c>Class38.method348</c>),
    ///     which feeds the <c>Class93_Sub1</c> HSL palette rather than the HSV one faces use, and
    ///     its words draw as a different colour on a model.
    ///     </para>
    ///     <para>
    ///     Unlit colours: the client multiplies a face's lightness by the scene's lighting before
    ///     drawing (Renderable_Sub1.java:2299), so a face in game is this colour lit, as it is for
    ///     every shipped model.
    ///     </para>
    /// </remarks>
    public static class ModelColour {
        /// <summary>Every drawable colour, mapped to the lowest word that draws it.</summary>
        private static readonly Dictionary<int, ushort> ByRgb = BuildIndex();

        /// <summary>
        ///     The colour word the client draws as <paramref name="rgb"/>, or, when no word draws
        ///     exactly that, the one whose colour is nearest by squared RGB distance.
        /// </summary>
        /// <remarks>
        ///     Many words draw the same colour, because <c>method2117</c> folds saturation into
        ///     lightness; ties go to the lowest word, so the answer is stable.
        /// </remarks>
        /// <param name="rgb">A 24-bit RGB colour; the top byte is ignored.</param>
        /// <returns>The packed HSL word to store for a face.</returns>
        public static ushort FromRgb(int rgb) {
            rgb &= 0xFFFFFF;
            if (ByRgb.TryGetValue(rgb, out ushort exact))
                return exact;

            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            int best = int.MaxValue;
            ushort bestWord = 0;
            foreach (KeyValuePair<int, ushort> entry in ByRgb) {
                int dr = ((entry.Key >> 16) & 0xFF) - r;
                int dg = ((entry.Key >> 8) & 0xFF) - g;
                int db = (entry.Key & 0xFF) - b;
                int distance = dr * dr + dg * dg + db * db;
                if (distance < best || (distance == best && entry.Value < bestWord)) {
                    best = distance;
                    bestWord = entry.Value;
                }
            }
            return bestWord;
        }

        private static Dictionary<int, ushort> BuildIndex() {
            var index = new Dictionary<int, ushort>();
            for (int word = 0; word <= 0xFFFF; word++) {
                int rgb = ModelDefinition.RawHslToRgb(word) & 0xFFFFFF;
                index.TryAdd(rgb, (ushort) word);
            }
            return index;
        }
    }
}
