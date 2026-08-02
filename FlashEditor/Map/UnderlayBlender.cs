using System;

namespace FlashEditor.Map {
    /// <summary>
    ///     Averages floor underlay colour over a neighbourhood to produce the per-tile ground colour.
    /// </summary>
    /// <remarks>
    ///     Ported from Class305.method3568 (Class305.java:222-318). This is what makes terrain read
    ///     as RuneScape rather than as a grid of flat swatches, and it is the reason a map square
    ///     cannot be coloured in isolation.
    ///
    ///     The window is <b>10 tiles wide, spanning -4 to +5</b> on each axis. It is deliberately
    ///     asymmetric: the loop starts at -5 and adds column <c>x + 5</c> while subtracting column
    ///     <c>x - 5</c>, so by the time it writes output column <c>x</c> the live set is
    ///     <c>x-4 .. x+5</c>. Earlier readings of this routine recorded it as 10x10 symmetric and as
    ///     11x11; it is neither.
    ///
    ///     See <c>reference/hydra-637-maps/05-colour-and-rendering.md</c>.
    /// </remarks>
    public static class UnderlayBlender {
        /// <summary>
        ///     The offset the sliding loop runs at. Not the same as the window's reach.
        /// </summary>
        /// <remarks>
        ///     Each step adds column <c>x + 5</c> and removes column <c>x - 5</c>, so the resident
        ///     window is <c>x-4 .. x+5</c>: <see cref="ReachBack"/> back and <see cref="ReachForward"/>
        ///     forward, ten columns in total.
        /// </remarks>
        private const int WindowOffset = 5;

        /// <summary>Tiles the resident window reaches back along each axis.</summary>
        public const int ReachBack = 4;

        /// <summary>Tiles the resident window reaches forward along each axis.</summary>
        public const int ReachForward = 5;

        /// <summary>Width of the resident window along each axis.</summary>
        public const int WindowSize = ReachBack + 1 + ReachForward;

        /// <summary>
        ///     Resolves an underlay definition to its blend components.
        /// </summary>
        /// <param name="definitionId">
        ///     The zero-based definition id, already decremented from the one-based id the terrain
        ///     file stores.
        /// </param>
        /// <returns>The components, or <c>null</c> to treat the tile as contributing nothing.</returns>
        public delegate UnderlayColour? Resolver(int definitionId);

        /// <summary>
        ///     Blends a grid of underlay ids into packed HSL per tile.
        /// </summary>
        /// <remarks>
        ///     Tiles whose id is 0 contribute nothing, and tiles the blend cannot resolve keep a
        ///     packed HSL of 0, which is black rather than transparent. The client behaves the same
        ///     way: its output array is freshly zeroed and the guarded write simply does not happen.
        /// </remarks>
        /// <param name="underlayIds">Underlay ids indexed <c>[x, y]</c>. 0 means no underlay.</param>
        /// <param name="resolve">Resolves an id to its components.</param>
        /// <returns>Packed HSL indexed <c>[x, y]</c>.</returns>
        public static int[,] Blend(int[,] underlayIds, Resolver resolve) {
            if (underlayIds == null) throw new ArgumentNullException(nameof(underlayIds));
            if (resolve == null) throw new ArgumentNullException(nameof(resolve));

            int width = underlayIds.GetLength(0);
            int height = underlayIds.GetLength(1);

            int[,] blended = new int[width, height];

            //Per-column running sums, carried as the X window slides.
            int[] hueSums = new int[height];
            int[] weightSums = new int[height];
            int[] saturationSums = new int[height];
            int[] lightnessSums = new int[height];
            int[] counts = new int[height];

            for (int x = -WindowOffset; x < width; x++) {
                for (int y = 0; y < height; y++) {
                    int entering = x + WindowOffset;
                    if (entering < width)
                        Accumulate(underlayIds, resolve, entering, y, +1,
                            hueSums, weightSums, saturationSums, lightnessSums, counts);

                    int leaving = x - WindowOffset;
                    if (leaving >= 0)
                        Accumulate(underlayIds, resolve, leaving, y, -1,
                            hueSums, weightSums, saturationSums, lightnessSums, counts);
                }

                if (x < 0)
                    continue;

                //Second running sum, this time along Y over the per-column totals.
                int hue = 0, weight = 0, saturation = 0, lightness = 0, count = 0;

                for (int y = -WindowOffset; y < height; y++) {
                    int entering = y + WindowOffset;
                    if (entering < height) {
                        hue += hueSums[entering];
                        weight += weightSums[entering];
                        saturation += saturationSums[entering];
                        lightness += lightnessSums[entering];
                        count += counts[entering];
                    }

                    int leaving = y - WindowOffset;
                    if (leaving >= 0) {
                        hue -= hueSums[leaving];
                        weight -= weightSums[leaving];
                        saturation -= saturationSums[leaving];
                        lightness -= lightnessSums[leaving];
                        count -= counts[leaving];
                    }

                    if (y < 0 || weight <= 0 || count <= 0)
                        continue;

                    //Hue divides by the summed chroma weight, the others by the tile count. Using
                    //the count for hue as well washes every blend toward red.
                    blended[x, y] = MapPalette.Pack(
                        hue * 256 / weight,
                        saturation / count,
                        lightness / count);
                }
            }

            return blended;
        }

        private static void Accumulate(
            int[,] underlayIds, Resolver resolve, int x, int y, int sign,
            int[] hueSums, int[] weightSums, int[] saturationSums, int[] lightnessSums, int[] counts) {

            //Only a non-zero id contributes. Ids are 1-based in the terrain file.
            int id = underlayIds[x, y] & 0xFF;
            if (id <= 0)
                return;

            UnderlayColour? colour = resolve(id - 1);
            if (colour == null)
                return;

            UnderlayColour c = colour.Value;
            hueSums[y] += sign * c.WeightedHue;
            weightSums[y] += sign * c.HueWeight;
            saturationSums[y] += sign * c.Saturation;
            lightnessSums[y] += sign * c.Lightness;
            counts[y] += sign;
        }
    }
}
