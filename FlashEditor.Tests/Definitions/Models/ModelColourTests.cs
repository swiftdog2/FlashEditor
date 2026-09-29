using FlashEditor.Definitions.Models;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Definitions.Models {
    /// <summary>
    ///     RGB to a model's colour word: the inverse of the path the client draws model faces with.
    /// </summary>
    /// <remarks>
    ///     The expected RGB values are the client's own output, the same known answers
    ///     <see cref="HslPaletteTests"/> pins the forward path with, so the inverse is held to the
    ///     client rather than to a second reading of it.
    /// </remarks>
    public sealed class ModelColourTests {
        [Theory]
        [InlineData(0x9D9696)]
        [InlineData(0x8D524F)]
        [InlineData(0xF7908A)]
        [InlineData(0x5E4B0D)]
        [InlineData(0x405E0D)]
        [InlineData(0x0D5E5B)]
        [InlineData(0xE4E7EE)]
        public void FromRgb_OfAColourTheClientDraws_IsAWordTheClientDrawsAsThatColour(int clientRgb) {
            ushort word = ModelColour.FromRgb(clientRgb);

            Assert.Equal(clientRgb, ModelDefinition.RawHslToRgb(word) & 0xFFFFFF);
        }

        [Fact]
        public void FromRgb_OfEveryColourInThePalette_DrawsBackAsThatColour() {
            for (int raw = 0; raw <= 0xFFFF; raw++) {
                int rgb = ModelDefinition.RawHslToRgb(raw) & 0xFFFFFF;
                Assert.Equal(rgb, ModelDefinition.RawHslToRgb(ModelColour.FromRgb(rgb)) & 0xFFFFFF);
            }
        }

        /// <summary>
        ///     A colour the palette cannot hit lands on a near one rather than an arbitrary one.
        /// </summary>
        /// <remarks>
        ///     Kd 0.80/0.82/0.86 is the Saradomin axe's silver. The bound is loose on purpose: it
        ///     separates "nearest" from "wrong conversion", which is off by far more.
        /// </remarks>
        [Fact]
        public void FromRgb_OfAColourOffThePalette_DrawsWithinAFewStepsOfIt() {
            const int silver = (204 << 16) | (209 << 8) | 219;

            int drawn = ModelDefinition.RawHslToRgb(ModelColour.FromRgb(silver));

            Assert.InRange(((drawn >> 16) & 0xFF) - 204, -8, 8);
            Assert.InRange(((drawn >> 8) & 0xFF) - 209, -8, 8);
            Assert.InRange((drawn & 0xFF) - 219, -8, 8);
        }

        /// <summary>
        ///     The floor conversion is not the model one, so reusing it would miscolour a model.
        /// </summary>
        /// <remarks>
        ///     Floors go through <c>Class38.method348</c> into the <c>Class93_Sub1</c> HSL palette;
        ///     model faces go through <c>Class111_Sub2.method2117</c> into the <c>Class122</c> HSV one.
        ///     Pinned so that <see cref="MapPalette.FromRgb"/> is not taken for a shortcut.
        /// </remarks>
        [Fact]
        public void TheFloorConversion_DoesNotDrawAModelColourBackAsItself() {
            const int clientRgb = 0x8D524F;

            int viaFloor = ModelDefinition.RawHslToRgb(MapPalette.FromRgb(clientRgb)) & 0xFFFFFF;

            Assert.NotEqual(clientRgb, viaFloor);
        }
    }
}
