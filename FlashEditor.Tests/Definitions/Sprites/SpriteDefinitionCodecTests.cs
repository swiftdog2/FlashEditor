using System;
using System.Linq;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Cache.Util;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.Sprites
{
    /// <summary>
    ///     Pins the index-8 sprite codec against bytes it did not produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Round-tripping this encoder against this decoder proves nothing, so the two sources here
    ///     are the cache and the client. The five captured sets are groups the vanilla b639 capture
    ///     and the repack agree on byte for byte, and <c>RealCacheSpriteTests</c> asserts they still
    ///     are; the synthetic pairs are laid out by hand to the read order in
    ///     <c>Class324.method3690</c> (<c>Class324.java:43-133</c>).
    ///     </para>
    ///     <para>
    ///     The synthetic pairs exist for a reason the sweep cannot cover. Each pair is two different
    ///     files that decode to the <em>same picture</em>, which is what makes the stored form
    ///     unrecoverable from pixels. Two of the three are live in the shipped data and the sweep
    ///     would catch a regression in them; the third - a column-major flag on a frame one pixel
    ///     wide - occurs nowhere in either cache, so nothing but a hand-built file defends it.
    ///     </para>
    /// </remarks>
    public class SpriteDefinitionCodecTests
    {
        /// <summary>
        ///     Index 8 group 2287: a 25x25 canvas holding one frame of no pixels at all.
        /// </summary>
        /// <remarks>
        ///     Sixteen bytes, and every one of the format's degenerate cases at once - a zero-area
        ///     frame, a palette of one entry (the transparent index, which is never stored), and a
        ///     canvas that no frame reaches. A decoder that sized anything from the pixels rather
        ///     than from the metadata cannot reproduce it.
        /// </remarks>
        private static readonly byte[] CapturedEmptyFrame =
        {
            0x00,                                      //frame 0 flags: row-major, no alpha
            0x00, 0x19, 0x00, 0x19,                    //canvas 25 x 25
            0x00,                                      //paletteSize - 1
            0x00, 0x00, 0x00, 0x00,                    //offsetX, offsetY
            0x00, 0x00, 0x00, 0x00,                    //subWidth, subHeight - a 0 x 0 plane
            0x00, 0x01                                 //one frame
        };

        /// <summary>
        ///     Index 8 group 1848: a single pixel whose only palette colour is stored as black.
        /// </summary>
        /// <remarks>
        ///     Twenty bytes carrying two aliases at once. The stored 0x000000 is promoted to
        ///     0x000001 on read because entry 0 means transparent, and a 1x1 plane reads the same
        ///     row-major as column-major.
        /// </remarks>
        private static readonly byte[] CapturedBlackPalette =
        {
            0x00, 0x01,                                //frame 0: flags, one palette index
            0x00, 0x00, 0x00,                          //palette entry 1, stored as black
            0x00, 0x01, 0x00, 0x01,                    //canvas 1 x 1
            0x01,                                      //paletteSize - 1
            0x00, 0x00, 0x00, 0x00,                    //offsetX, offsetY
            0x00, 0x01, 0x00, 0x01,                    //subWidth, subHeight
            0x00, 0x01                                 //one frame
        };

        /// <summary>
        ///     Index 8 group 1657: a 4x2 plane stored column-major.
        /// </summary>
        /// <remarks>
        ///     The pixel bytes are not symmetric under transposition, so this is what settles the
        ///     traversal: read row-major the plane comes out <c>1 1 2 2 1 1 2 2</c>, and read the
        ///     way the client does it comes out <c>1 2 1 2 1 2 1 2</c>.
        /// </remarks>
        private static readonly byte[] CapturedColumnMajor =
        {
            0x01,                                      //frame 0 flags: column-major, no alpha
            0x01, 0x01, 0x02, 0x02, 0x01, 0x01, 0x02, 0x02,
            0x54, 0x3B, 0x18,                          //palette entry 1
            0x80, 0x80, 0x80,                          //palette entry 2
            0x00, 0x08, 0x00, 0x08,                    //canvas 8 x 8
            0x02,                                      //paletteSize - 1
            0x00, 0x00,                                //offsetX
            0x00, 0x01,                                //offsetY
            0x00, 0x04,                                //subWidth
            0x00, 0x02,                                //subHeight
            0x00, 0x01                                 //one frame
        };

        /// <summary>
        ///     Index 8 group 4499: column-major and alpha together, with a palette entry stored as
        ///     black.
        /// </summary>
        /// <remarks>
        ///     The only one of the four flag combinations that needs both planes transposed, and
        ///     the alpha plane here is genuinely varying rather than a flat 0xFF.
        /// </remarks>
        private static readonly byte[] CapturedVerticalAlpha =
        {
            0x03,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x01, 0x01, 0x01, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02, 0x02,
            0x02, 0x02, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x02, 0x02, 0x02, 0x02, 0x05, 0x06, 0x05, 0x07, 0x07, 0x06,
            0x05, 0x03, 0x02, 0x02, 0x02, 0x02, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x01, 0x01, 0x02, 0x03, 0x03, 0x04, 0x05, 0x05, 0x05, 0x05, 0x08, 0x07,
            0x05, 0x05, 0x05, 0x05, 0x05, 0x04, 0x02, 0x01, 0x01, 0x01, 0x01, 0x01,
            0x04, 0x0B, 0x17, 0x25, 0x37, 0x4E, 0x5D, 0x63, 0x63, 0x63, 0x63, 0x63,
            0x63, 0x5D, 0x4E, 0x37, 0x28, 0x1E, 0x17, 0x0B, 0x04, 0x00, 0x00, 0x00,
            0x0B, 0x21, 0x43, 0x63, 0x86, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0x86, 0x6F, 0x5A, 0x43, 0x21, 0x0B, 0x00, 0x00, 0x00,
            0x17, 0x43, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x43, 0x17, 0x00, 0x00, 0x00,
            0x1E, 0x5A, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
            0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x5A, 0x1E, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x30, 0x06, 0x06, 0x63, 0x0E, 0x0E, 0x5A, 0x0C, 0x0C,
            0x52, 0x0A, 0x0A, 0x2C, 0x02, 0x02, 0x4D, 0x08, 0x08, 0x4D, 0x07, 0x07,
            0x00, 0x04, 0x00, 0x18,                    //canvas 4 x 24
            0x08,                                      //paletteSize - 1
            0x00, 0x00, 0x00, 0x00,                    //offsetX, offsetY
            0x00, 0x04, 0x00, 0x18,                    //subWidth, subHeight
            0x00, 0x01                                 //one frame
        };

        /// <summary>
        ///     Index 8 group 300: six frames sharing one 32-entry palette.
        /// </summary>
        /// <remarks>
        ///     The multi-frame case, and the one that makes the shared palette visible - each frame
        ///     carries its own flags byte and geometry but there is exactly one palette block in the
        ///     file. Palette entry 17 is stored as black and is referenced by pixels in every frame,
        ///     so it also pins the promotion on the drawing side rather than only in the bytes.
        /// </remarks>
        private static readonly byte[] CapturedSixFrames =
        {
            0x00, 0x00, 0x01, 0x02, 0x00, 0x01, 0x09, 0x0A, 0x0B, 0x02, 0x0A, 0x0B,
            0x12, 0x11, 0x19, 0x1A, 0x11, 0x00, 0x11, 0x11, 0x00, 0x00, 0x00, 0x03,
            0x03, 0x00, 0x03, 0x0C, 0x0D, 0x0E, 0x0C, 0x0C, 0x13, 0x14, 0x11, 0x0E,
            0x14, 0x11, 0x00, 0x11, 0x11, 0x00, 0x00, 0x00, 0x04, 0x04, 0x00, 0x04,
            0x04, 0x04, 0x0F, 0x04, 0x04, 0x0F, 0x15, 0x11, 0x1B, 0x1C, 0x11, 0x00,
            0x11, 0x11, 0x00, 0x00, 0x00, 0x05, 0x05, 0x00, 0x05, 0x05, 0x05, 0x10,
            0x05, 0x05, 0x10, 0x16, 0x11, 0x16, 0x1D, 0x11, 0x00, 0x11, 0x11, 0x00,
            0x00, 0x00, 0x06, 0x06, 0x00, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06,
            0x17, 0x11, 0x1E, 0x17, 0x11, 0x00, 0x11, 0x11, 0x00, 0x00, 0x00, 0x07,
            0x07, 0x00, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x07, 0x18, 0x11, 0x1F,
            0x18, 0x11, 0x00, 0x11, 0x11, 0x00, 0xFF, 0x36, 0x36, 0xFE, 0x0C, 0x0C,
            0xFE, 0xFE, 0x11, 0xFE, 0xFE, 0xFE, 0x00, 0xFD, 0x00, 0x5E, 0x8B, 0xD0,
            0x84, 0x0B, 0xFF, 0xFF, 0xC0, 0xC0, 0xFE, 0x20, 0x20, 0xFC, 0x06, 0x06,
            0xED, 0x00, 0x00, 0xFC, 0xFC, 0x02, 0xF5, 0xF5, 0x00, 0xDD, 0xDD, 0x00,
            0xEC, 0xEC, 0xEC, 0x00, 0xEB, 0x00, 0x00, 0x00, 0x00, 0xCE, 0x00, 0x00,
            0xED, 0xED, 0x00, 0xCC, 0xCC, 0x00, 0xD2, 0xD2, 0xD2, 0x00, 0xC4, 0x00,
            0x2C, 0x56, 0x97, 0x55, 0x03, 0xBB, 0xD9, 0x00, 0x00, 0xBC, 0x00, 0x00,
            0xE2, 0xE2, 0xE2, 0xC5, 0xC5, 0xC5, 0x00, 0xAF, 0x00, 0x52, 0x7A, 0xB8,
            0x5F, 0x00, 0xEE,
            0x00, 0x04, 0x00, 0x05,                    //canvas 4 x 5
            0x1F,                                      //paletteSize - 1, so 32 entries
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x04, 0x00, 0x04, 0x00, 0x04, 0x00, 0x04, 0x00, 0x04, 0x00, 0x04,
            0x00, 0x05, 0x00, 0x05, 0x00, 0x05, 0x00, 0x05, 0x00, 0x05, 0x00, 0x05,
            0x00, 0x06                                 //six frames
        };

        /// <summary>The groups the captured sets were read from, in fixture order.</summary>
        public static readonly int[] CapturedGroupIds = { 2287, 1848, 1657, 4499, 300 };

        /// <summary>
        ///     The captured sets, so the cache-backed test can compare without a second copy.
        /// </summary>
        /// <returns>Fresh copies, in the same order as <see cref="CapturedGroupIds"/>.</returns>
        public static byte[][] CapturedGroupBytes()
        {
            return new[]
            {
                (byte[]) CapturedEmptyFrame.Clone(),
                (byte[]) CapturedBlackPalette.Clone(),
                (byte[]) CapturedColumnMajor.Clone(),
                (byte[]) CapturedVerticalAlpha.Clone(),
                (byte[]) CapturedSixFrames.Clone()
            };
        }

        // ===================================================================
        //  The captured sets
        // ===================================================================

        /// <summary>
        ///     A frame with no pixels still carries a canvas, a palette size and a flags byte.
        /// </summary>
        /// <remarks>
        ///     The canvas is the assertion that matters. It is 25x25 while the only frame is 0x0, so
        ///     a decoder that recomputed the canvas from the frames would produce 0x0 and write a
        ///     different file.
        /// </remarks>
        [Fact]
        public void AnEmptyFrame_KeepsTheCanvasItCouldNotHaveDerived()
        {
            SpriteDefinition sprite = Decode(CapturedEmptyFrame);

            Assert.Equal(25, sprite.width);
            Assert.Equal(25, sprite.height);
            Assert.Equal(1, sprite.GetFrameCount());
            Assert.Single(sprite.PaletteStored);

            SpriteFrame frame = sprite.Frames[0];
            Assert.Equal(0, frame.Flags);
            Assert.Equal(0, frame.SubWidth);
            Assert.Equal(0, frame.SubHeight);
            Assert.Empty(frame.PaletteIndices);
            Assert.Null(frame.Alpha);

            //Nothing sits between the single flags byte and the metadata block.
            Assert.Equal(1L, sprite.PixelPlaneEnd);
            Assert.Equal(1L, sprite.PaletteOffset);
            Assert.Empty(sprite.PixelPlaneTrailer);

            Assert.Equal(CapturedEmptyFrame, sprite.Encode().ToArray());
        }

        /// <summary>
        ///     A colour stored as black keeps both spellings: the stored one and the promoted one.
        /// </summary>
        /// <remarks>
        ///     The pixel is drawn as 0x000001 because entry 0 is the transparent index, so the drawn
        ///     colour cannot say whether the file held 0x000000 or 0x000001. Both occur in both
        ///     caches, which is why the stored value is kept verbatim.
        /// </remarks>
        [Fact]
        public void APaletteEntryStoredAsBlack_KeepsTheStoredValueAndDrawsThePromotedOne()
        {
            SpriteDefinition sprite = Decode(CapturedBlackPalette);

            Assert.Equal(0x000000, sprite.PaletteStored[1]);
            Assert.Equal(0x000001, sprite.RenderPalette[1]);

            using (var frame = sprite.GetFrame(0))
                Assert.Equal(unchecked((int) 0xFF000001), frame.GetPixels()[0]);

            Assert.Equal(CapturedBlackPalette, sprite.Encode().ToArray());
        }

        /// <summary>
        ///     A column-major plane is transposed into the client's canonical layout on read and
        ///     back again on write.
        /// </summary>
        /// <remarks>
        ///     This is the one captured set whose pixels are not symmetric under transposition, so
        ///     it fails loudly if the traversal is wrong in either direction - which a re-encode
        ///     alone would not, since transposing twice is the identity.
        /// </remarks>
        [Fact]
        public void AColumnMajorPlane_IsReadIntoTheCanonicalRowMajorLayout()
        {
            SpriteDefinition sprite = Decode(CapturedColumnMajor);
            SpriteFrame frame = sprite.Frames[0];

            Assert.True(frame.IsColumnMajor);
            Assert.False(frame.HasAlphaPlane);
            Assert.Equal(4, frame.SubWidth);
            Assert.Equal(2, frame.SubHeight);

            //x + y * subWidth, so two identical rows of 1 2 1 2 - not the file's 1 1 2 2 1 1 2 2.
            Assert.Equal(new byte[] { 1, 2, 1, 2, 1, 2, 1, 2 }, frame.PaletteIndices);

            Assert.Equal(CapturedColumnMajor, sprite.Encode().ToArray());
        }

        /// <summary>Column-major and alpha together transpose both planes independently.</summary>
        [Fact]
        public void AColumnMajorAlphaFrame_TransposesBothPlanes()
        {
            SpriteDefinition sprite = Decode(CapturedVerticalAlpha);
            SpriteFrame frame = sprite.Frames[0];

            Assert.True(frame.IsColumnMajor);
            Assert.True(frame.HasAlphaPlane);
            Assert.Equal(96, frame.PaletteIndices.Length);
            Assert.Equal(96, frame.Alpha.Length);
            Assert.False(frame.AlphaPlaneIsRedundant);

            //Entry 1 is stored black here too, and this frame's pixels reference it.
            Assert.Equal(0x000000, sprite.PaletteStored[1]);
            Assert.Contains((byte) 1, frame.PaletteIndices);

            Assert.Equal(CapturedVerticalAlpha, sprite.Encode().ToArray());
        }

        /// <summary>
        ///     A six-frame set shares one palette and one canvas across every frame.
        /// </summary>
        /// <remarks>
        ///     Also the drawing side of the black promotion: entry 17 is stored as black and frame 0
        ///     references it, so the pixel has to come out opaque rather than transparent.
        /// </remarks>
        [Fact]
        public void AMultiFrameSet_SharesOnePaletteAndReEncodes()
        {
            SpriteDefinition sprite = Decode(CapturedSixFrames);

            Assert.Equal(6, sprite.GetFrameCount());
            Assert.Equal(32, sprite.PaletteStored.Length);
            Assert.Equal(4, sprite.width);
            Assert.Equal(5, sprite.height);
            Assert.All(sprite.Frames, frame =>
            {
                Assert.Equal(0, frame.Flags);
                Assert.Equal(20, frame.PaletteIndices.Length);
            });

            Assert.Equal(0x000000, sprite.PaletteStored[17]);
            Assert.Equal(0x000001, sprite.RenderPalette[17]);

            //Frame 0, pixel (0, 3), which the file stores as palette index 17.
            Assert.Equal(17, sprite.Frames[0].PaletteIndices[0 + 3 * 4]);
            using (var frame = sprite.GetFrame(0))
                Assert.Equal(unchecked((int) 0xFF000001), frame.GetPixels()[0 + 3 * 4]);

            Assert.Equal(CapturedSixFrames, sprite.Encode().ToArray());
        }

        // ===================================================================
        //  The aliasing pairs: two files, one picture
        // ===================================================================

        /// <summary>
        ///     A one-pixel-wide frame stores the same bytes either way round, so the traversal flag
        ///     is a free choice the file records and the pixels cannot.
        /// </summary>
        /// <remarks>
        ///     Latent in the shipped data and defended by nothing else. Both caches hold thousands
        ///     of frames whose order is unrecoverable and every one of them stores the bit clear, so
        ///     an encoder that assumed row-major would sweep both caches clean and still corrupt the
        ///     first frame anyone packs the other way.
        /// </remarks>
        [Fact]
        public void TheTraversalFlagSurvivesAFrameWhoseOrderTheBytesCannotState()
        {
            byte[] rowMajor = OnePixelWideColumn(0x00);
            byte[] columnMajor = OnePixelWideColumn(SpriteFrame.FlagVertical);

            //Identical but for the flags byte, which is the whole point.
            Assert.Equal(rowMajor.Skip(1), columnMajor.Skip(1));

            SpriteDefinition asRows = Decode(rowMajor);
            SpriteDefinition asColumns = Decode(columnMajor);

            Assert.True(asRows.Frames[0].OrderIsUnrecoverable);
            Assert.True(asColumns.Frames[0].OrderIsUnrecoverable);
            Assert.Equal(asRows.Frames[0].PaletteIndices, asColumns.Frames[0].PaletteIndices);

            Assert.False(asRows.Frames[0].IsColumnMajor);
            Assert.True(asColumns.Frames[0].IsColumnMajor);

            Assert.Equal(rowMajor, asRows.Encode().ToArray());
            Assert.Equal(columnMajor, asColumns.Encode().ToArray());
        }

        /// <summary>
        ///     An alpha plane of nothing but 0xFF draws like no plane at all and is still kept.
        /// </summary>
        /// <remarks>
        ///     The client drops such a plane on load (<c>Class324.java:127-129</c>) because it only
        ///     wants to know whether to blend. Copying that here would shorten the file by one plane
        ///     on the sets that carry one, and both caches have some.
        /// </remarks>
        [Fact]
        public void ARedundantAlphaPlaneIsKeptRatherThanInferredFromThePixels()
        {
            byte[] withPlane = TwoPixelRow(SpriteFrame.FlagAlpha);
            byte[] withoutPlane = TwoPixelRow(0x00);

            SpriteDefinition carried = Decode(withPlane);
            SpriteDefinition bare = Decode(withoutPlane);

            Assert.NotNull(carried.Frames[0].Alpha);
            Assert.True(carried.Frames[0].AlphaPlaneIsRedundant);
            Assert.Null(bare.Frames[0].Alpha);

            //Same picture from two different files.
            using (var carriedFrame = carried.GetFrame(0))
            using (var bareFrame = bare.GetFrame(0))
                Assert.Equal(carriedFrame.GetPixels(), bareFrame.GetPixels());

            Assert.Equal(withPlane, carried.Encode().ToArray());
            Assert.Equal(withoutPlane, bare.Encode().ToArray());
            Assert.NotEqual(withPlane.Length, withoutPlane.Length);
        }

        /// <summary>
        ///     A palette entry stored as black and one stored as 0x000001 draw identically.
        /// </summary>
        /// <remarks>
        ///     The captured set covers the first spelling; this covers the pair, which is what says
        ///     the drawn colour cannot choose between them. Both spellings occur in both caches.
        /// </remarks>
        [Fact]
        public void BlackAndThePromotedBlackDrawTheSameAndStoreDifferently()
        {
            byte[] storedBlack = OnePixel(0x000000);
            byte[] storedOne = OnePixel(0x000001);

            SpriteDefinition black = Decode(storedBlack);
            SpriteDefinition one = Decode(storedOne);

            Assert.Equal(0x000000, black.PaletteStored[1]);
            Assert.Equal(0x000001, one.PaletteStored[1]);
            Assert.Equal(black.RenderPalette[1], one.RenderPalette[1]);

            using (var blackFrame = black.GetFrame(0))
            using (var oneFrame = one.GetFrame(0))
                Assert.Equal(blackFrame.GetPixels(), oneFrame.GetPixels());

            Assert.Equal(storedBlack, black.Encode().ToArray());
            Assert.Equal(storedOne, one.Encode().ToArray());
        }

        // ===================================================================
        //  Malformed input
        // ===================================================================

        /// <summary>
        ///     A frame whose declared plane would run into the palette is rejected rather than
        ///     silently overlapping it.
        /// </summary>
        /// <remarks>
        ///     Nothing in the file states where the planes stop - they run forwards from 0 while the
        ///     palette is found by seeking back from the end - so this overlap is the only signal a
        ///     plane was sized wrongly, and without the check the decode would appear to succeed.
        /// </remarks>
        [Fact]
        public void APlaneThatRunsIntoThePaletteIsRejected()
        {
            byte[] overlapping = new byte[40];
            overlapping[25] = 0x00; overlapping[26] = 0x08;   //canvas 8 x ...
            overlapping[27] = 0x00; overlapping[28] = 0x08;   //... 8
            overlapping[29] = 0x08;                           //nine palette entries, 24 bytes
            overlapping[35] = 0x02;                           //subWidth 2
            overlapping[37] = 0x02;                           //subHeight 2
            overlapping[39] = 0x01;                           //one frame

            //The palette starts at byte 1, so a four pixel plane from byte 1 cannot fit.
            Assert.Throws<InvalidOperationException>(() => Decode(overlapping));
        }

        /// <summary>A file too short to hold its own metadata block is rejected.</summary>
        [Fact]
        public void AFileTooShortForItsFrameCountIsRejected()
        {
            //Two frames need 7 + 16 bytes of metadata alone.
            byte[] truncated = { 0x00, 0x00, 0x00, 0x02 };

            Assert.Throws<InvalidOperationException>(() => Decode(truncated));
        }

        // ===================================================================
        //  Builders
        // ===================================================================

        /// <summary>Decodes a set from raw bytes.</summary>
        /// <param name="bytes">The stored bytes.</param>
        /// <returns>The decoded set.</returns>
        private static SpriteDefinition Decode(byte[] bytes)
        {
            var sprite = new SpriteDefinition();
            sprite.Decode(new JagStream(bytes));
            return sprite;
        }

        /// <summary>
        ///     A 1x4 frame on a 1x4 canvas, whose plane bytes are the same either traversal.
        /// </summary>
        /// <param name="flags">The flags byte to store.</param>
        /// <returns>The file.</returns>
        private static byte[] OnePixelWideColumn(int flags)
        {
            return new byte[]
            {
                (byte) flags,
                0x01, 0x00, 0x01, 0x00,                //four palette indices, one per row
                0x11, 0x22, 0x33,                      //palette entry 1
                0x00, 0x01, 0x00, 0x04,                //canvas 1 x 4
                0x01,                                  //paletteSize - 1
                0x00, 0x00, 0x00, 0x00,                //offsetX, offsetY
                0x00, 0x01, 0x00, 0x04,                //subWidth 1, subHeight 4
                0x00, 0x01                             //one frame
            };
        }

        /// <summary>
        ///     A 2x1 opaque frame, optionally carrying an alpha plane of nothing but 0xFF.
        /// </summary>
        /// <param name="flags">The flags byte to store.</param>
        /// <returns>The file.</returns>
        private static byte[] TwoPixelRow(int flags)
        {
            bool alpha = (flags & SpriteFrame.FlagAlpha) != 0;
            var file = new System.Collections.Generic.List<byte> { (byte) flags, 0x01, 0x01 };
            if (alpha)
            {
                file.Add(0xFF);
                file.Add(0xFF);
            }
            file.AddRange(new byte[]
            {
                0x11, 0x22, 0x33,                      //palette entry 1
                0x00, 0x02, 0x00, 0x01,                //canvas 2 x 1
                0x01,                                  //paletteSize - 1
                0x00, 0x00, 0x00, 0x00,                //offsetX, offsetY
                0x00, 0x02, 0x00, 0x01,                //subWidth 2, subHeight 1
                0x00, 0x01                             //one frame
            });
            return file.ToArray();
        }

        /// <summary>A 1x1 frame drawn in one palette colour.</summary>
        /// <param name="colour">The 24-bit colour to store in palette entry 1.</param>
        /// <returns>The file.</returns>
        private static byte[] OnePixel(int colour)
        {
            return new byte[]
            {
                0x00, 0x01,                            //flags, one palette index
                (byte) (colour >> 16), (byte) (colour >> 8), (byte) colour,
                0x00, 0x01, 0x00, 0x01,                //canvas 1 x 1
                0x01,                                  //paletteSize - 1
                0x00, 0x00, 0x00, 0x00,                //offsetX, offsetY
                0x00, 0x01, 0x00, 0x01,                //subWidth, subHeight
                0x00, 0x01                             //one frame
            };
        }
    }
}
