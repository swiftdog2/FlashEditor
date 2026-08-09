using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor.Definitions.Sprites;
using Xunit;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     What a sprite tile actually contains, read back pixel by pixel.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     These are the three claims a screenshot cannot settle. A 2x2 sprite magnified thirty
    ///     times either has hard square pixels or it is four grey blobs, and at a glance in a grid
    ///     of 4,593 rows the difference is invisible - so the tile is rendered here and its pixels
    ///     counted. The same for the two states that both look like "nothing was drawn": a sprite
    ///     that is entirely transparent, and one that stores no pixels at all.
    ///     </para>
    ///     <para>
    ///     GDI+ drawing into an unattached bitmap needs no window, so this runs in the test host
    ///     like anything else. It is the only automated check in the suite that touches a paint
    ///     path at all.
    ///     </para>
    /// </remarks>
    public sealed class SpritePainterTests : IDisposable
    {
        private const int Tile = 60;

        private readonly Font _marker = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Pixel);

        /// <summary>Releases the marker font the tiles are drawn with.</summary>
        public void Dispose()
        {
            _marker.Dispose();
        }

        /// <summary>
        ///     A 2x2 sprite magnified thirty times is four flat blocks and nothing in between.
        /// </summary>
        /// <remarks>
        ///     The whole reason the magnification is whole-number and nearest-neighbour. Under the
        ///     default interpolation this tile holds hundreds of intermediate colours and the sprite
        ///     is unjudgeable; the assertion is that it holds exactly the four the file does, each
        ///     one filling its own quarter to the pixel.
        /// </remarks>
        [Fact]
        public void ATinySpriteIsMagnifiedIntoHardSquarePixels()
        {
            Color[,] source =
            {
                { Color.FromArgb(255, 255, 0, 0), Color.FromArgb(255, 0, 255, 0) },
                { Color.FromArgb(255, 0, 0, 255), Color.FromArgb(255, 255, 255, 0) }
            };

            using Bitmap sprite = Draw(source);
            using Bitmap tile = SpritePainter.RenderTile(sprite, Tile, SpriteTileContent.Picture, _marker);

            SpriteTileFit fit = SpriteTileFit.Fit(2, 2, Tile, Tile);
            Assert.Equal(30, fit.Upscale);
            Assert.Equal(new Rectangle(0, 0, 60, 60), fit.Bounds);

            //One pixel in from each edge of each block, so the outline drawn around the sprite's
            //extent is not what is being sampled.
            var seen = new HashSet<int>();
            for (int y = 1; y < Tile - 1; y++)
            {
                for (int x = 1; x < Tile - 1; x++)
                {
                    Color drawn = tile.GetPixel(x, y);
                    seen.Add(drawn.ToArgb());

                    Color expected = source[y / 30, x / 30];
                    Assert.True(drawn.ToArgb() == expected.ToArgb(),
                        $"pixel {x},{y} is {drawn} where the magnified sprite says {expected}");
                }
            }

            Assert.Equal(4, seen.Count);
        }

        /// <summary>
        ///     A sprite that is entirely transparent is checkerboard, not a blank box.
        /// </summary>
        /// <remarks>
        ///     Palette entry 0 is the transparent slot, so a set really can draw nothing at all.
        ///     Over a flat background that is indistinguishable from a draw that failed, which is
        ///     what the checkerboard exists to separate - and the extent outline is what says where
        ///     the invisible sprite is.
        /// </remarks>
        [Fact]
        public void AFullyTransparentSpriteShowsTheCheckerboardThroughIt()
        {
            using Bitmap sprite = Draw(new[,]
            {
                { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0) },
                { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0) }
            });

            using Bitmap tile = SpritePainter.RenderTile(sprite, Tile, SpriteTileContent.Picture, _marker);

            Assert.True(Holds(tile, SpritePainter.CheckerLight), "the light checker squares were painted over");
            Assert.True(Holds(tile, SpritePainter.CheckerDark), "the dark checker squares were painted over");

            //Nothing anywhere is the flat fill a row that has not been read is drawn with, so the
            //two states cannot be confused with each other either.
            Assert.False(Holds(tile, SpritePainter.PendingFill));
        }

        /// <summary>A frame with no stored pixels is marked rather than left blank.</summary>
        /// <remarks>
        ///     2,377 of the vanilla capture's 11,177 frames store a zero-area plane, and 27 of its
        ///     4,593 sets have one as frame 0. A tile that simply drew nothing for them would be the
        ///     picture of a decoder failing.
        /// </remarks>
        [Fact]
        public void AFrameWithNoPixelsIsMarkedEmpty()
        {
            using Bitmap empty = SpritePainter.RenderTile(null, Tile, SpriteTileContent.Empty, _marker);

            using Bitmap transparent = SpritePainter.RenderTile(
                Draw(new[,] { { Color.FromArgb(0, 0, 0, 0) } }), Tile, SpriteTileContent.Picture, _marker);

            /* Measured over the middle of the tile rather than over all of it. A transparent sprite
               does put ink on the tile - the outline around its extent - so counting every non-checker
               pixel compares a marker against a border and says nothing. Inside the middle, a
               transparent sprite is checkerboard and an empty one carries the marker. */
            Assert.True(InkInTheMiddle(empty) > 0, "the empty marker drew nothing where it can be read");
            Assert.Equal(0, InkInTheMiddle(transparent));
        }

        /// <summary>A group that would not decode is marked differently again.</summary>
        [Fact]
        public void AFailedGroupIsNotDrawnLikeAnEmptyOne()
        {
            using Bitmap failed = SpritePainter.RenderTile(null, Tile, SpriteTileContent.Failed, _marker);
            using Bitmap empty = SpritePainter.RenderTile(null, Tile, SpriteTileContent.Empty, _marker);

            Assert.NotEqual(Signature(failed), Signature(empty));
        }

        /// <summary>A row that has not been read yet is neither of the above.</summary>
        /// <remarks>
        ///     Flat rather than a checkerboard on purpose: the checkerboard means "these pixels are
        ///     transparent", and a row nothing has read has no pixels to be transparent.
        /// </remarks>
        [Fact]
        public void APendingRowIsFlatRatherThanCheckerboard()
        {
            using Bitmap pending = SpritePainter.RenderTile(null, Tile, SpriteTileContent.Pending, _marker);

            Assert.False(Holds(pending, SpritePainter.CheckerLight));
            Assert.False(Holds(pending, SpritePainter.CheckerDark));
            Assert.Single(Signature(pending));
        }

        /// <summary>A sprite drawn into a tile never reaches the tile's edge unless it fills it.</summary>
        [Fact]
        public void ALetterboxedSpriteLeavesTheCheckerboardShowing()
        {
            //4x2 in a square tile: fifteen magnifications across, thirty down, so the smaller wins
            //and the picture is 60x30 with checkerboard above and below it.
            using Bitmap sprite = Draw(new[,]
            {
                { Color.Red, Color.Red, Color.Red, Color.Red },
                { Color.Red, Color.Red, Color.Red, Color.Red }
            });

            using Bitmap tile = SpritePainter.RenderTile(sprite, Tile, SpriteTileContent.Picture, _marker);

            SpriteTileFit fit = SpriteTileFit.Fit(4, 2, Tile, Tile);
            Assert.Equal(15, fit.Upscale);
            Assert.Equal(new Rectangle(0, 15, 60, 30), fit.Bounds);

            Assert.True(Holds(tile, SpritePainter.CheckerLight) || Holds(tile, SpritePainter.CheckerDark),
                "the letterbox bands were painted over");
            Assert.Equal(Color.Red.ToArgb(), tile.GetPixel(30, 30).ToArgb());
        }

        /// <summary>Builds a bitmap from a grid of colours, row by row.</summary>
        /// <param name="pixels">The colours, indexed row then column.</param>
        /// <returns>The bitmap, in straight ARGB.</returns>
        private static Bitmap Draw(Color[,] pixels)
        {
            var bitmap = new Bitmap(pixels.GetLength(1), pixels.GetLength(0),
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            for (int y = 0; y < pixels.GetLength(0); y++)
                for (int x = 0; x < pixels.GetLength(1); x++)
                    bitmap.SetPixel(x, y, pixels[y, x]);

            return bitmap;
        }

        private static bool Holds(Bitmap tile, Color colour)
        {
            return Signature(tile).Contains(colour.ToArgb());
        }

        private static HashSet<int> Signature(Bitmap tile)
        {
            var colours = new HashSet<int>();
            for (int y = 0; y < tile.Height; y++)
                for (int x = 0; x < tile.Width; x++)
                    colours.Add(tile.GetPixel(x, y).ToArgb());
            return colours;
        }

        /// <summary>
        ///     How many pixels in the middle of a tile are neither of the checkerboard's colours.
        /// </summary>
        /// <remarks>
        ///     The middle three fifths, so the outline drawn around a sprite's extent - which lands
        ///     on the edge for anything filling the tile - cannot be mistaken for a marker.
        /// </remarks>
        /// <param name="tile">The tile to measure.</param>
        /// <returns>The count.</returns>
        private static int InkInTheMiddle(Bitmap tile)
        {
            int light = SpritePainter.CheckerLight.ToArgb();
            int dark = SpritePainter.CheckerDark.ToArgb();
            int ink = 0;

            for (int y = tile.Height / 5; y < tile.Height * 4 / 5; y++)
                for (int x = tile.Width / 5; x < tile.Width * 4 / 5; x++)
                {
                    int drawn = tile.GetPixel(x, y).ToArgb();
                    if (drawn != light && drawn != dark)
                        ink++;
                }

            return ink;
        }
    }
}
