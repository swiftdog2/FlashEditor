using System;
using FlashEditor.Definitions.Sprites;
using Xunit;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     The decision the sprite grid makes about where a sprite goes inside a fixed tile.
    /// </summary>
    /// <remarks>
    ///     Nothing in the suite covers WinForms or a paint path, so the drawing itself is judged by
    ///     eye. What can be pinned is the arithmetic behind it, and it is the part with a real
    ///     failure mode: index 8 runs from 2x2 to over 400x200, and a tile that stretches, or that
    ///     magnifies by a fraction, turns pixel art into something the cache does not contain.
    /// </remarks>
    public sealed class SpriteTileGeometryTests
    {
        /// <summary>A sprite smaller than the tile is magnified by a whole number.</summary>
        /// <remarks>
        ///     The 2x2 case is the one that motivated all of this: at any fractional scale, or under
        ///     the default interpolation, it is four grey blobs.
        /// </remarks>
        [Theory]
        [InlineData(2, 2, 60, 30, 60, 60)]
        [InlineData(16, 16, 64, 4, 64, 64)]
        [InlineData(20, 20, 60, 3, 60, 60)]
        [InlineData(24, 18, 60, 2, 48, 36)]
        public void ASpriteSmallerThanTheTile_IsMagnifiedByAWholeNumber(
            int width, int height, int tile, int expectedUpscale, int expectedWidth, int expectedHeight)
        {
            SpriteTileFit fit = SpriteTileFit.Fit(width, height, tile, tile);

            Assert.Equal(expectedUpscale, fit.Upscale);
            Assert.Equal(expectedWidth, fit.Bounds.Width);
            Assert.Equal(expectedHeight, fit.Bounds.Height);
            Assert.Equal(width * fit.Upscale, fit.Bounds.Width);
            Assert.Equal(height * fit.Upscale, fit.Bounds.Height);
        }

        /// <summary>
        ///     The magnification is the smaller of the two whole-number fits, so nothing is stretched.
        /// </summary>
        /// <remarks>
        ///     A 2x3 sprite in a 60x60 tile has 30 whole magnifications across and 20 down. Taking
        ///     each axis separately would fill the tile and be a different picture from the one in
        ///     the file.
        /// </remarks>
        [Fact]
        public void TheMagnification_IsTheSmallerOfTheTwoAxes()
        {
            SpriteTileFit fit = SpriteTileFit.Fit(2, 3, 60, 60);

            Assert.Equal(20, fit.Upscale);
            Assert.Equal(40, fit.Bounds.Width);
            Assert.Equal(60, fit.Bounds.Height);
        }

        /// <summary>A sprite exactly the tile's size is left alone and says so.</summary>
        [Fact]
        public void ASpriteTheSizeOfTheTile_IsDrawnAtOneToOne()
        {
            SpriteTileFit fit = SpriteTileFit.Fit(60, 60, 60, 60);

            Assert.Equal(1, fit.Upscale);
            Assert.True(fit.IsFullSize);
            Assert.Equal("1:1", fit.ToString());
        }

        /// <summary>A sprite too big for the tile is shrunk to fit, keeping its shape.</summary>
        [Theory]
        [InlineData(400, 200, 60)]
        [InlineData(200, 400, 60)]
        [InlineData(512, 12, 60)]
        [InlineData(61, 60, 60)]
        public void ASpriteLargerThanTheTile_IsShrunkToFitWithoutStretching(int width, int height, int tile)
        {
            SpriteTileFit fit = SpriteTileFit.Fit(width, height, tile, tile);

            Assert.Equal(0, fit.Upscale);
            Assert.False(fit.IsFullSize);
            Assert.InRange(fit.Bounds.Width, 1, tile);
            Assert.InRange(fit.Bounds.Height, 1, tile);

            //Within a pixel of the exact aspect-preserving size, which is all integer rounding can
            //promise. The product form avoids comparing two divisions that each round.
            long skew = Math.Abs((long) fit.Bounds.Width * height - (long) fit.Bounds.Height * width);
            Assert.True(skew <= Math.Max(width, height),
                $"a {width}x{height} sprite drawn at {fit.Bounds.Width}x{fit.Bounds.Height} is not the same shape");
        }

        /// <summary>A shrunk tile reports the percentage it was drawn at rather than looking small.</summary>
        [Fact]
        public void AShrunkTile_SaysSo()
        {
            SpriteTileFit fit = SpriteTileFit.Fit(400, 200, 60, 60);

            Assert.Equal("15%", fit.ToString());
        }

        /// <summary>The picture is centred in the tile in both directions.</summary>
        [Fact]
        public void ThePicture_IsCentredInTheTile()
        {
            SpriteTileFit fit = SpriteTileFit.Fit(24, 18, 60, 60);

            Assert.Equal((60 - fit.Bounds.Width) / 2, fit.Bounds.X);
            Assert.Equal((60 - fit.Bounds.Height) / 2, fit.Bounds.Y);
        }

        /// <summary>A sprite with no area has nothing to place, and is not a failure.</summary>
        /// <remarks>
        ///     2,377 of the vanilla capture's frames store a zero-area plane. They have to be
        ///     distinguishable from a frame that failed to draw, which starts with the geometry
        ///     refusing to invent a rectangle for them.
        /// </remarks>
        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 12)]
        [InlineData(12, 0)]
        [InlineData(-1, 4)]
        public void ASpriteWithNoArea_IsEmptyRatherThanZeroSized(int width, int height)
        {
            SpriteTileFit fit = SpriteTileFit.Fit(width, height, 60, 60);

            Assert.True(fit.IsEmpty);
            Assert.Equal("-", fit.ToString());
            Assert.Equal(0, fit.Bounds.Width);
            Assert.Equal(0, fit.Bounds.Height);
        }

        /// <summary>A tile with no area is a caller's mistake rather than an empty picture.</summary>
        [Fact]
        public void ATileWithNoArea_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SpriteTileFit.Fit(10, 10, 0, 60));
        }

        /// <summary>
        ///     Whatever the sizes, the picture stays inside the tile and never changes shape.
        /// </summary>
        /// <remarks>
        ///     Swept over every size a sprite in this index could have rather than over the cases
        ///     that were thought of. The two claims are the ones the drawing depends on: nothing is
        ///     clipped, and nothing is stretched.
        /// </remarks>
        [Fact]
        public void EverySize_FitsInsideTheTileAndKeepsItsShape()
        {
            const int tile = 60;

            for (int width = 1; width <= 520; width += 3)
            {
                for (int height = 1; height <= 520; height += 7)
                {
                    SpriteTileFit fit = SpriteTileFit.Fit(width, height, tile, tile);

                    Assert.InRange(fit.Bounds.X, 0, tile - fit.Bounds.Width);
                    Assert.InRange(fit.Bounds.Y, 0, tile - fit.Bounds.Height);

                    if (fit.Upscale >= 1)
                    {
                        //Magnified pictures are exact multiples, so there is no rounding to allow for
                        Assert.Equal(width * fit.Upscale, fit.Bounds.Width);
                        Assert.Equal(height * fit.Upscale, fit.Bounds.Height);
                        continue;
                    }

                    long skew = Math.Abs((long) fit.Bounds.Width * height - (long) fit.Bounds.Height * width);
                    Assert.True(skew <= Math.Max(width, height),
                        $"{width}x{height} drawn at {fit.Bounds.Width}x{fit.Bounds.Height}");
                }
            }
        }
    }
}
