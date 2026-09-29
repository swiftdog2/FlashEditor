using FlashEditor.Definitions.Config;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the order an overlay's map colour is chosen in, which the rasteriser and the floor
    ///     palette both read through <see cref="FloorColour"/>.
    /// </summary>
    /// <remarks>
    ///     The texture resolver is a lambda so no test touches the process-wide
    ///     <c>TextureManager</c> table, which real-cache tests load in parallel.
    /// </remarks>
    public sealed class FloorColourTests
    {
        private const int Green = 0x00FF00;
        private const int Brown = 0x80501E;
        private const int TextureFive = 5;

        //A packed HSL no RGB conversion in these tests lands on, so a texture win is unmistakable.
        private const int TextureHsl = 0x1234;

        private static int TextureFiveResolves(int textureId) =>
            textureId == TextureFive ? TextureHsl : MapPalette.NoColour;

        private static int NoTextureResolves(int textureId) => MapPalette.NoColour;

        /// <summary>
        ///     A lone underlay's colour is what the blender produces for a field of nothing else.
        /// </summary>
        /// <remarks>
        ///     Red-hue included because its weighted hue is negative, and the blender divides sums
        ///     where the single colour divides one term.
        /// </remarks>
        [Theory]
        [InlineData(0x4C7A2B)]
        [InlineData(0xC02040)]
        [InlineData(0x808080)]
        [InlineData(0x000000)]
        public void Underlay_ColourIsAUniformFieldOfItself(int rgb)
        {
            UnderlayColour colour = UnderlayColour.FromRgb(rgb);
            var field = new int[12, 12];
            for (int x = 0; x < 12; x++)
                for (int y = 0; y < 12; y++)
                    field[x, y] = 1;

            int[,] blended = UnderlayBlender.Blend(field, _ => colour);

            Assert.Equal(blended[6, 6], FloorColour.UnderlayHsl(colour));
        }

        [Fact]
        public void Overlay_SecondaryWinsOverTextureAndPrimary()
        {
            var overlay = new FloorOverlayDefinition
            {
                PrimaryRgb = Green, HasPrimaryRgb = true, SecondaryRgb = Brown, TextureId = TextureFive
            };

            Assert.Equal(MapPalette.FromRgb(Brown), FloorColour.OverlayHsl(overlay, TextureFiveResolves));
        }

        [Fact]
        public void Overlay_TextureWinsOverPrimaryWhenThereIsNoSecondary()
        {
            var overlay = new FloorOverlayDefinition
            {
                PrimaryRgb = Green, HasPrimaryRgb = true, TextureId = TextureFive
            };

            Assert.Equal(TextureHsl, FloorColour.OverlayHsl(overlay, TextureFiveResolves));
        }

        [Fact]
        public void Overlay_PrimaryIsUsedWhenNeitherSecondaryNorTextureResolves()
        {
            var overlay = new FloorOverlayDefinition
            {
                PrimaryRgb = Green, HasPrimaryRgb = true, TextureId = TextureFive
            };

            Assert.Equal(MapPalette.FromRgb(Green), FloorColour.OverlayHsl(overlay, NoTextureResolves));
        }

        [Fact]
        public void Overlay_WithNoStoredPrimaryHasNoColour()
        {
            var overlay = new FloorOverlayDefinition();

            Assert.Equal(MapPalette.NoColour, FloorColour.OverlayHsl(overlay, NoTextureResolves));
            Assert.False(FloorColour.IsHole(overlay, MapPalette.NoColour));
        }

        [Fact]
        public void Overlay_TransparentPrimaryWithNothingElseIsAHole()
        {
            var overlay = new FloorOverlayDefinition
            {
                PrimaryRgb = FloorOverlayDefinition.TransparentRgb, HasPrimaryRgb = true
            };

            int hsl = FloorColour.OverlayHsl(overlay, NoTextureResolves);

            Assert.Equal(MapPalette.NoColour, hsl);
            Assert.True(FloorColour.IsHole(overlay, hsl));
        }

        [Fact]
        public void Overlay_TransparentPrimaryWithASecondaryDrawsTheSecondary()
        {
            var overlay = new FloorOverlayDefinition
            {
                PrimaryRgb = FloorOverlayDefinition.TransparentRgb, HasPrimaryRgb = true, SecondaryRgb = Brown
            };

            int hsl = FloorColour.OverlayHsl(overlay, NoTextureResolves);

            Assert.Equal(MapPalette.FromRgb(Brown), hsl);
            Assert.False(FloorColour.IsHole(overlay, hsl));
        }
    }
}
