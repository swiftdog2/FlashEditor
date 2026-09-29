using FlashEditor.Definitions.Config;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins what a floor palette swatch shows and what its caption says.
    /// </summary>
    /// <remarks>
    ///     The user-visible failure this guards: a bright green overlay swatch painted a completely
    ///     different colour, because the swatch showed the primary colour and the map draws the
    ///     secondary first.
    /// </remarks>
    public sealed class FloorSwatchTests
    {
        private const int Green = 0x00FF00;
        private const int Brown = 0x80501E;

        private static int NoTextures(int textureId) => MapPalette.NoColour;

        private static string Hex(int rgb) => "#" + rgb.ToString("X6");

        [Fact]
        public void Overlay_SwatchShowsTheSecondaryTheMapDraws_NotThePrimary()
        {
            var overlay = new FloorOverlayDefinition
            {
                Id = 12, PrimaryRgb = Green, HasPrimaryRgb = true, SecondaryRgb = Brown, TextureId = 5
            };

            FloorSwatch swatch = FloorSwatch.Overlay(overlay, NoTextures);
            int drawn = MapPalette.ToRgb(MapPalette.FromRgb(Brown));

            Assert.Equal(FloorSwatchFill.Colour, swatch.Fill);
            Assert.Equal(drawn, swatch.MapRgb);
            Assert.Equal("overlay 12   map " + Hex(drawn) + "   primary #00FF00   secondary #80501E   texture 5",
                swatch.Caption);
        }

        [Fact]
        public void Overlay_SwatchFollowsTheResolverThroughTheTexture()
        {
            var overlay = new FloorOverlayDefinition { Id = 3, PrimaryRgb = Green, HasPrimaryRgb = true, TextureId = 7 };

            FloorSwatch swatch = FloorSwatch.Overlay(overlay, id => id == 7 ? 0x1234 : MapPalette.NoColour);

            Assert.Equal(MapPalette.ToRgb(0x1234), swatch.MapRgb);
        }

        [Fact]
        public void Overlay_TransparentPrimaryIsNamedAsTheUnderlayShowingThrough()
        {
            var overlay = new FloorOverlayDefinition
            {
                Id = 152, PrimaryRgb = FloorOverlayDefinition.TransparentRgb, HasPrimaryRgb = true, SecondaryRgb = Brown
            };

            FloorSwatch swatch = FloorSwatch.Overlay(overlay, NoTextures);

            Assert.Equal(FloorSwatchFill.Colour, swatch.Fill);
            Assert.Contains("   primary transparent (underlay shows)   secondary #80501E", swatch.Caption);
        }

        [Fact]
        public void Overlay_AHoleIsItsOwnFill_NotBlack()
        {
            var overlay = new FloorOverlayDefinition
            {
                Id = 40, PrimaryRgb = FloorOverlayDefinition.TransparentRgb, HasPrimaryRgb = true
            };

            FloorSwatch swatch = FloorSwatch.Overlay(overlay, NoTextures);

            Assert.Equal(FloorSwatchFill.Hole, swatch.Fill);
            Assert.Equal("overlay 40   map hole (nothing drawn)   primary transparent (underlay shows)", swatch.Caption);
        }

        [Fact]
        public void Overlay_WithNoStoredColourLetsTheUnderlayShow()
        {
            var overlay = new FloorOverlayDefinition { Id = 0 };

            FloorSwatch swatch = FloorSwatch.Overlay(overlay, NoTextures);

            Assert.Equal(FloorSwatchFill.UnderlayShows, swatch.Fill);
            Assert.Equal("overlay 0   map none (underlay shows)   primary none", swatch.Caption);
        }

        [Fact]
        public void Underlay_SwatchShowsTheBlendedColour_NotTheRawRgb()
        {
            var underlay = new FloorUnderlayDefinition { Id = 9, Rgb = 0x4C7A2B, TextureId = 3 };

            FloorSwatch swatch = FloorSwatch.Underlay(underlay);
            int drawn = MapPalette.ToRgb(FloorColour.UnderlayHsl(UnderlayColour.FromRgb(0x4C7A2B)));

            Assert.Equal(FloorSwatchFill.Colour, swatch.Fill);
            Assert.Equal(drawn, swatch.MapRgb);
            Assert.Equal("underlay 9   map " + Hex(drawn) + "   colour #4C7A2B   texture 3", swatch.Caption);
        }
    }
}
