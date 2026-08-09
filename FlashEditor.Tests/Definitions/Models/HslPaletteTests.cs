using FlashEditor.Definitions;
using Xunit;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Tests.Definitions.Models
{
    /// <summary>
    ///     Known-answer vectors for the RS colour palette, taken from the client's own output.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Cache colours are a packed 16-bit HSL, and turning one into RGB is two steps that are
    ///     easy to conflate. <c>Class111_Sub2.method2117</c> first redistributes saturation
    ///     against lightness to produce a <em>palette index</em>, and only then does
    ///     <c>Class122.method2199</c>'s 65,536-entry table give the RGB. Indexing the table with
    ///     the raw value skips the first step and yields a visibly different colour, and the
    ///     widely-copied community formula (a plain <c>Color.HSBtoRGB</c> on the unpacked
    ///     fields) skips both that step and the gamma.
    ///     </para>
    ///     <para>
    ///     These expectations were produced by compiling the two decompiled client methods
    ///     verbatim and running them, not by reimplementing them a second time - a second
    ///     reimplementation would only agree with the first one's mistakes. Across all 65,536
    ///     raw values this implementation is byte-identical to the client for 65,024 of them;
    ///     the other 512 are the lightness-zero row of each hue/saturation pair, where
    ///     <c>BuildHslLut</c> deliberately maps pure black to <c>0x000001</c> because the model
    ///     engine treats <c>0x000000</c> as transparent. That row is excluded here.
    ///     </para>
    ///     <para>
    ///     The client's gamma is <c>0.7 + (0.03 * random() - 0.015)</c>, re-rolled per session,
    ///     which is why its colours shift slightly between logins. The vectors below pin the
    ///     midpoint, 0.7, which is what an editor wants.
    ///     </para>
    /// </remarks>
    public class HslPaletteTests
    {
        [Theory]
        [InlineData(0x0001, 0x080808)]
        [InlineData(0x0040, 0x9D9696)]
        [InlineData(0x007E, 0xFDF2F2)]
        [InlineData(0x01A8, 0x8D524F)]
        [InlineData(0x01EE, 0xEEE4E4)]
        [InlineData(0x0391, 0x5E130D)]
        [InlineData(0x03DA, 0xF7908A)]
        [InlineData(0x1C01, 0x080808)]
        [InlineData(0x1C40, 0x9D9B96)]
        [InlineData(0x1C7E, 0xFDF9F2)]
        [InlineData(0x1DA8, 0x8D7C4F)]
        [InlineData(0x1DEE, 0xEEEBE4)]
        [InlineData(0x1F91, 0x5E4B0D)]
        [InlineData(0x1FDA, 0xF7D98A)]
        [InlineData(0x3C01, 0x080808)]
        [InlineData(0x3C40, 0x9A9D96)]
        [InlineData(0x3C7E, 0xF8FDF2)]
        [InlineData(0x3DA8, 0x738D4F)]
        [InlineData(0x3DEE, 0xEAEEE4)]
        [InlineData(0x3F91, 0x405E0D)]
        [InlineData(0x3FDA, 0xC9F78A)]
        [InlineData(0x5C01, 0x080808)]
        [InlineData(0x5C40, 0x969D98)]
        [InlineData(0x5C7E, 0xF2FDF4)]
        [InlineData(0x5DA8, 0x4F8D5D)]
        [InlineData(0x5DEE, 0xE4EEE6)]
        [InlineData(0x5F91, 0x0D5E24)]
        [InlineData(0x5FDA, 0x8AF7A3)]
        [InlineData(0x7C01, 0x080808)]
        [InlineData(0x7C40, 0x969D9D)]
        [InlineData(0x7C7E, 0xF2FDFC)]
        [InlineData(0x7DA8, 0x4F8D8B)]
        [InlineData(0x7DEE, 0xE4EEEE)]
        [InlineData(0x7F91, 0x0D5E5B)]
        [InlineData(0x7FDA, 0x8AF7F2)]
        [InlineData(0x9C01, 0x080808)]
        [InlineData(0x9C40, 0x96989D)]
        [InlineData(0x9C7E, 0xF2F5FD)]
        [InlineData(0x9DA8, 0x4F638D)]
        [InlineData(0x9DEE, 0xE4E7EE)]
        public void RawHslToRgb_MatchesTheClientPalette(int rawHsl, int expectedRgb)
        {
            Assert.Equal(expectedRgb, ModelDefinition.RawHslToRgb(rawHsl) & 0xFFFFFF);
        }

        /// <summary>
        ///     Raising lightness must brighten the colour, across every hue and saturation.
        /// </summary>
        /// <remarks>
        ///     Deliberately an endpoint check rather than a monotonic one. The client's own
        ///     output dips by up to 5/765 of luma partway up the ramp for 272 of the 512
        ///     hue/saturation pairs, because <c>method2117</c> starts <em>reducing</em>
        ///     saturation once lightness passes its midpoint and the chroma sum does not rise
        ///     smoothly across that boundary. Asserting monotonicity would be asserting
        ///     something the client does not do.
        /// </remarks>
        [Fact]
        public void RawHslToRgb_HigherLightnessIsBrighter_ForEveryHueAndSaturation()
        {
            for (int hueSat = 0; hueSat < 512; hueSat++)
            {
                int bits = hueSat << 7;
                Assert.True(Luma(bits | 126) > Luma(bits | 1),
                    $"Hue/saturation pair {hueSat} is not brighter at full lightness.");
            }
        }

        /// <summary>
        ///     The saturation rolloff turns over at lightness 65, not 64.
        /// </summary>
        /// <remarks>
        ///     <c>method2117</c> reads <c>(lightness ^ -1) &lt; -65</c>, which is
        ///     <c>lightness &gt;= 65</c>; the model-rendering notes in the sibling repository
        ///     transcribe it as <c>lightness &lt; 64</c>. One step either way is invisible in a
        ///     spot check, so this pins the boundary by where the resulting luma actually turns
        ///     over on a fully saturated ramp.
        /// </remarks>
        [Fact]
        public void RawHslToRgb_SaturationRolloffTurnsOverAt65()
        {
            const int hueSatBits = 62 << 7;   // hue 15, saturation 6 - a ramp that visibly dips

            int at64 = Luma(hueSatBits | 64);
            int at65 = Luma(hueSatBits | 65);
            Assert.True(at65 < at64,
                $"Expected the turnover at lightness 65, but luma rose from {at64} to {at65}.");
        }

        private static int Luma(int rawHsl)
        {
            int rgb = ModelDefinition.RawHslToRgb(rawHsl);
            return ((rgb >> 16) & 0xFF) + ((rgb >> 8) & 0xFF) + (rgb & 0xFF);
        }
    }
}
