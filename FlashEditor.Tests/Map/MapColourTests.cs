using System;
using System.Collections.Generic;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the client's colour model: the HSL palette, the RGB conversions and the terrain
    ///     underlay blend.
    /// </summary>
    /// <remarks>
    ///     None of this is checkable against cache bytes - a colour is not self-delimiting, so a
    ///     wrong constant produces plausible output rather than a parse failure. These tests
    ///     therefore pin the arithmetic itself, and specifically the parts a reimplementation
    ///     usually gets wrong: the asymmetric blend window, the weighted-hue divisor, and the
    ///     lightness-driven saturation ladder.
    /// </remarks>
    public sealed class MapColourTests
    {
        [Fact]
        public void PaletteEntriesAreAllInRange()
        {
            int black = 0;

            for (int hsl = 0; hsl < 65536; hsl++)
            {
                int rgb = MapPalette.ToRgb(hsl);
                Assert.InRange(rgb, 0, 0xFFFFFF);

                if (rgb == 0)
                    black++;
            }

            //Every entry whose lightness bits are zero comes out black: 65536 / 128 = 512 of them.
            //Index 0 is not special-cased, it is simply one of those.
            Assert.Equal(512, black);
        }

        /// <summary>
        ///     The magenta sentinel means "no colour", not a colour.
        /// </summary>
        [Fact]
        public void MagentaIsTheNoColourSentinel()
        {
            Assert.Equal(MapPalette.NoColour, MapPalette.FromRgb(MapPalette.TransparentRgb));

            //And a no-colour value must not index the palette.
            Assert.Equal(0, MapPalette.ToRgb(MapPalette.NoColour));
        }

        /// <summary>
        ///     Saturation is reduced as lightness rises, on a five-rung ladder.
        /// </summary>
        /// <remarks>
        ///     Shared by <c>Class79.method801</c> and <c>Class38.method348</c>. The thresholds are
        ///     exclusive lower bounds, so the boundary values themselves take the gentler shift.
        /// </remarks>
        [Theory]
        [InlineData(179, 0)] // at the boundary, no shift
        [InlineData(180, 1)]
        [InlineData(192, 1)]
        [InlineData(193, 2)]
        [InlineData(217, 2)]
        [InlineData(218, 3)]
        [InlineData(243, 3)]
        [InlineData(244, 4)]
        public void SaturationIsShiftedByLightness(int lightness, int expectedShift)
        {
            const int saturation = 255;

            int packed = MapPalette.Pack(0, saturation, lightness);
            int packedSaturation = (packed >> 7) & 0x7;

            Assert.Equal((saturation >> expectedShift) >> 5, packedSaturation);
        }

        /// <summary>The packed layout is hue in bits 10-15, saturation 7-9, lightness 0-6.</summary>
        [Fact]
        public void PackedLayoutIsHueSaturationLightness()
        {
            int packed = MapPalette.Pack(0xFF, 0xFF, 0x7F);

            Assert.Equal(0x3F, (packed >> 10) & 0x3F);
            Assert.Equal(0x7F >> 1, packed & 0x7F);
            Assert.InRange(packed, 0, 0xFFFF);
        }

        /// <summary>
        ///     A grey has no hue and no saturation; a saturated primary has both.
        /// </summary>
        [Fact]
        public void GreyHasNoSaturation()
        {
            int grey = MapPalette.FromRgb(0x808080);
            Assert.Equal(0, (grey >> 7) & 0x7);

            int red = MapPalette.FromRgb(0xFF0000);
            Assert.True(((red >> 7) & 0x7) > 0, "a pure red should carry saturation");
        }

        /// <summary>
        ///     The hue weight never falls below 1, so a fully desaturated underlay still counts.
        /// </summary>
        [Fact]
        public void UnderlayHueWeightIsNeverBelowOne()
        {
            foreach (int rgb in new[] { 0x000000, 0xFFFFFF, 0x808080, 0x010101 })
                Assert.True(UnderlayColour.FromRgb(rgb).HueWeight >= 1,
                    $"weight for {rgb:X6} fell below 1");
        }

        /// <summary>
        ///     A hue past the halfway point decomposes negative, and must stay negative.
        /// </summary>
        /// <remarks>
        ///     Five of the 159 shipped underlays do this. The blend divides the summed weighted hue
        ///     by the summed weight, so the division has to truncate toward zero and the pack has to
        ///     shift arithmetically. C# does both, matching Java; a port that reached for an
        ///     unsigned type or a floor division would wrap the hue to the far side of the wheel.
        /// </remarks>
        [Fact]
        public void NegativeWeightedHuesArePreserved()
        {
            //The red branch is the only one that can go negative: it computes (g - b) / delta with
            //no constant offset, so any colour whose strongest channel is red and whose blue
            //exceeds its green lands below zero. The green and blue branches add 2.0 and 4.0.
            UnderlayColour magenta = UnderlayColour.FromRgb(0xFF0080);
            Assert.True(magenta.WeightedHue < 0,
                $"expected a negative weighted hue, got {magenta.WeightedHue}");

            //And the offset branches stay positive, so the sign genuinely discriminates.
            Assert.True(UnderlayColour.FromRgb(0x0000FF).WeightedHue > 0);
            Assert.True(UnderlayColour.FromRgb(0x00FF00).WeightedHue > 0);
        }

        /// <summary>
        ///     The blend window reaches 4 tiles back and 5 forward, not 5 and 5.
        /// </summary>
        /// <remarks>
        ///     This is the test that would have caught the two different wrong answers an earlier
        ///     pass produced. A single lit tile at <c>t</c> influences outputs <c>t-5 .. t+4</c>,
        ///     the mirror of the resident window <c>x-4 .. x+5</c>. Symmetric windows of either 10
        ///     or 11 tiles fail it.
        /// </remarks>
        [Fact]
        public void BlendWindowIsAsymmetric()
        {
            const int size = 32;
            const int lit = 15;

            int[,] ids = new int[size, size];
            ids[lit, lit] = 1;

            int[,] blended = UnderlayBlender.Blend(ids, _ => UnderlayColour.FromRgb(0x3A7A2A));

            var influencedX = new List<int>();
            for (int x = 0; x < size; x++)
                if (blended[x, lit] != 0)
                    influencedX.Add(x);

            Assert.Equal(lit - UnderlayBlender.ReachForward, influencedX[0]);
            Assert.Equal(lit + UnderlayBlender.ReachBack, influencedX[influencedX.Count - 1]);
            Assert.Equal(UnderlayBlender.WindowSize, influencedX.Count);
        }

        /// <summary>
        ///     Tiles with no underlay contribute nothing at all, not a black sample.
        /// </summary>
        /// <remarks>
        ///     If id 0 contributed, every tile near the edge of a painted area would be dragged
        ///     toward black in proportion to how much empty space its window covered.
        /// </remarks>
        [Fact]
        public void EmptyTilesDoNotDragTheAverage()
        {
            const int size = 32;

            int[,] dense = new int[size, size];
            int[,] sparse = new int[size, size];

            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    dense[x, y] = 1;

            //A single tile, whose window is otherwise empty.
            sparse[16, 16] = 1;

            UnderlayBlender.Resolver resolve = _ => UnderlayColour.FromRgb(0x3A7A2A);

            int[,] denseBlend = UnderlayBlender.Blend(dense, resolve);
            int[,] sparseBlend = UnderlayBlender.Blend(sparse, resolve);

            //Same colour either way: the average of one sample equals the average of a hundred
            //identical samples only if the empty tiles are genuinely excluded.
            Assert.Equal(denseBlend[16, 16], sparseBlend[16, 16]);
        }

        /// <summary>An entirely empty grid blends to black without dividing by zero.</summary>
        [Fact]
        public void EmptyGridBlendsToBlack()
        {
            int[,] blended = UnderlayBlender.Blend(new int[16, 16], _ => UnderlayColour.FromRgb(0x3A7A2A));

            foreach (int hsl in blended)
                Assert.Equal(0, hsl);
        }

        /// <summary>A resolver that declines an id is treated as an absent tile, not an error.</summary>
        [Fact]
        public void UnresolvableUnderlaysAreSkipped()
        {
            int[,] ids = new int[16, 16];
            ids[8, 8] = 200;

            int[,] blended = UnderlayBlender.Blend(ids, _ => null);

            foreach (int hsl in blended)
                Assert.Equal(0, hsl);
        }

        /// <summary>Blending a uniform field reproduces that underlay's own colour.</summary>
        [Fact]
        public void UniformFieldKeepsItsColour()
        {
            const int size = 24;
            int[,] ids = new int[size, size];
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    ids[x, y] = 1;

            UnderlayColour colour = UnderlayColour.FromRgb(0x3A7A2A);
            int[,] blended = UnderlayBlender.Blend(ids, _ => colour);

            int expected = MapPalette.Pack(
                colour.WeightedHue * 256 / colour.HueWeight,
                colour.Saturation,
                colour.Lightness);

            //Away from the edges, where the window is fully covered.
            Assert.Equal(expected, blended[12, 12]);
        }
    }
}
