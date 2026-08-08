using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;
using FlashEditor.Definitions.Sprites;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the PNG, JPEG and BMP import path, which no sweep over the cache can defend.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     An import writes bytes that are not in either cache and never were, so the byte-identity
    ///     sweeps over index 8 say nothing at all about it - they compare shipped files against a
    ///     re-encode of themselves and this path produces neither. Every case here is therefore a
    ///     hand-made picture whose expected bytes are worked out from the format rather than from
    ///     what this code happens to emit.
    ///     </para>
    ///     <para>
    ///     Three of the conversion's decisions have no visible result and are each pinned twice,
    ///     once on the stored bytes and once on the picture drawn back out of them: pure black is
    ///     stored as 0x000001, a picture that needs no alpha plane is written without one, and an
    ///     opaque pixel never addresses palette entry 0. The traversal flag is the fourth, and is
    ///     asserted on a non-square picture whose rows and columns cannot be confused.
    ///     </para>
    /// </remarks>
    public class SpriteImageImportTests
    {
        // ===================================================================
        //  The black trap
        // ===================================================================

        /// <summary>
        ///     A pure black pixel is stored as 0x000001 and draws as the black the client draws.
        /// </summary>
        /// <remarks>
        ///     Both spellings occur in the shipped palettes of both caches, so either is legal. This
        ///     one is chosen because it equals what the client promotes a stored zero to
        ///     (<c>Class324.java:76-79</c>), which is what makes exporting a set and importing it
        ///     back a fixed point rather than a colour that creeps by one unit a round.
        /// </remarks>
        [Fact]
        public void PureBlack_IsStoredAsThePromotedSpellingAndKeepsItsIndex()
        {
            SpriteImageImport imported = Convert(Picture(1, 1, unchecked((int) 0xFF000000)));

            Assert.Equal(1, imported.BlackPixels);
            Assert.Equal(1, imported.PaletteColours);

            SpriteDefinition set = imported.Set;
            Assert.Equal(0, set.PaletteStored[0]);
            Assert.Equal(0x000001, set.PaletteStored[1]);

            //Entry 0 is the transparent slot, so an opaque pixel must never point at it.
            Assert.Equal(new byte[] { 1 }, set.Frames[0].PaletteIndices);

            using (RSBufferedImage frame = set.GetFrame(0))
                Assert.Equal(unchecked((int) 0xFF000001), frame.GetPixels()[0]);

            //And the stored spelling survives a trip through the file the import would write.
            Assert.Equal(0x000001, RoundTrip(set).PaletteStored[1]);
        }

        /// <summary>
        ///     Black and the 0x000001 the client promotes it to are one palette colour, not two.
        /// </summary>
        /// <remarks>
        ///     They draw identically, so keeping them apart would spend two of the 255 entries on
        ///     one colour and could tip a picture of exactly 255 colours into being quantised for
        ///     nothing.
        /// </remarks>
        [Fact]
        public void BlackAndItsPromotedSpelling_AreOneColour()
        {
            SpriteImageImport imported = Convert(Picture(2, 1,
                unchecked((int) 0xFF000000), unchecked((int) 0xFF000001)));

            Assert.Equal(1, imported.SourceColours);
            Assert.Equal(1, imported.PaletteColours);
            Assert.False(imported.Quantised);
            Assert.Equal(2, imported.BlackPixels);
            Assert.Equal(new byte[] { 1, 1 }, imported.Set.Frames[0].PaletteIndices);
        }

        // ===================================================================
        //  Transparency and the alpha plane
        // ===================================================================

        /// <summary>A picture of nothing but transparent pixels stores no colour at all.</summary>
        /// <remarks>
        ///     The degenerate palette - one entry, the reserved one, and therefore nothing written
        ///     between the planes and the metadata. Index 8 really holds files of this shape; group
        ///     2287 of both caches is one.
        /// </remarks>
        [Fact]
        public void AFullyTransparentPicture_StoresAnEmptyPaletteAndNoPlane()
        {
            SpriteImageImport imported = Convert(Picture(2, 2, 0, 0, 0, 0));

            Assert.Equal(0, imported.SourceColours);
            Assert.Equal(0, imported.PaletteColours);
            Assert.Equal(4, imported.TransparentPixels);
            Assert.False(imported.CarriesAnAlphaPlane);

            SpriteDefinition set = imported.Set;
            Assert.Single(set.PaletteStored);
            Assert.Equal(new byte[] { 0, 0, 0, 0 }, set.Frames[0].PaletteIndices);
            Assert.Null(set.Frames[0].Alpha);

            SpriteDefinition read = RoundTrip(set);
            Assert.Single(read.PaletteStored);
            Assert.Equal(0, read.Frames[0].Flags);
        }

        /// <summary>
        ///     A picture whose pixels are all fully opaque or fully transparent gets no alpha plane.
        /// </summary>
        /// <remarks>
        ///     The plane is optional and one full of 0xFF draws exactly like no plane at all - the
        ///     client discards such a plane on load (<c>Class324.java:127-129</c>) - while doubling
        ///     the frame's bytes. So the alpha channel of a 32-bit PNG is not on its own a reason to
        ///     write one, which is the case this pins: the source here carries an alpha channel and
        ///     every value in it is 0 or 255.
        /// </remarks>
        [Fact]
        public void AnAlphaChannelOfNothingButExtremes_ProducesNoPlane()
        {
            SpriteImageImport imported = Convert(Picture(2, 2,
                unchecked((int) 0xFF102030), 0x00000000,
                0x00FFFFFF, unchecked((int) 0xFF405060)));

            Assert.False(imported.CarriesAnAlphaPlane);
            Assert.Equal(0, imported.Set.Frames[0].Flags);
            Assert.Null(imported.Set.Frames[0].Alpha);

            //Transparency is carried by entry 0 instead, and the colour hiding under a transparent
            //pixel claims no palette entry - 0x00FFFFFF here is white and must not become one.
            Assert.Equal(2, imported.PaletteColours);
            Assert.Equal(new byte[] { 1, 0, 0, 2 }, imported.Set.Frames[0].PaletteIndices);

            using (RSBufferedImage frame = imported.Set.GetFrame(0))
            {
                int[] pixels = frame.GetPixels();
                Assert.Equal(unchecked((int) 0xFF102030), pixels[0]);
                Assert.Equal(0, pixels[1]);
                Assert.Equal(0, pixels[2]);
            }
        }

        /// <summary>A single partly transparent pixel is enough to make the plane necessary.</summary>
        /// <remarks>
        ///     Without a plane the format has two states per pixel, opaque or absent, so a value of
        ///     0x80 would have to round to one of them. The plane covers the whole frame because the
        ///     format has no way to state a partial one.
        /// </remarks>
        [Fact]
        public void APartlyTransparentPixel_ForcesAnAlphaPlaneOverTheWholeFrame()
        {
            SpriteImageImport imported = Convert(Picture(2, 2,
                unchecked((int) 0xFF102030), unchecked((int) 0x80405060),
                0x00000000, unchecked((int) 0xFF102030)));

            Assert.True(imported.CarriesAnAlphaPlane);
            SpriteFrame frame = imported.Set.Frames[0];
            Assert.Equal(SpriteFrame.FlagAlpha, frame.Flags);
            Assert.Equal(new byte[] { 0xFF, 0x80, 0x00, 0xFF }, frame.Alpha);
            Assert.False(frame.AlphaPlaneIsRedundant);

            //Read back off the file the import would write, both planes and in the same order.
            SpriteFrame stored = RoundTrip(imported.Set).Frames[0];
            Assert.Equal(frame.PaletteIndices, stored.PaletteIndices);
            Assert.Equal(frame.Alpha, stored.Alpha);
        }

        // ===================================================================
        //  Palette size
        // ===================================================================

        /// <summary>Exactly 255 colours fit the palette and are stored without approximation.</summary>
        [Fact]
        public void TheColourLimitExactly_IsNotQuantised()
        {
            SpriteImageImport imported = Convert(Picture(255, 1, DistinctColours(255)));

            Assert.Equal(255, imported.SourceColours);
            Assert.Equal(255, imported.PaletteColours);
            Assert.False(imported.Quantised);
            Assert.Equal(0, imported.WorstChannelError);

            //Every source colour is present in the palette, so nothing moved. The palette holds
            //24-bit RGB while the source is ARGB, hence the mask.
            int[] palette = imported.Set.PaletteStored.Skip(1).OrderBy(colour => colour).ToArray();
            Assert.Equal(DistinctColours(255).Select(colour => colour & 0xFFFFFF).OrderBy(colour => colour).ToArray(),
                palette);
        }

        /// <summary>
        ///     A picture past the limit is quantised rather than refused, and says by how much.
        /// </summary>
        /// <remarks>
        ///     Refusal was the alternative and would have rejected this editor's own round trip: a
        ///     PNG exported from the sprite tab and touched in any paint program comes back with
        ///     antialiased edges and thousands of colours.
        /// </remarks>
        [Fact]
        public void MoreColoursThanThePaletteHolds_AreQuantisedAndReported()
        {
            SpriteImageImport imported = Convert(Picture(300, 1, DistinctColours(300)));

            Assert.Equal(300, imported.SourceColours);
            Assert.Equal(255, imported.PaletteColours);
            Assert.True(imported.Quantised);
            Assert.True(imported.WorstChannelError > 0,
                "a quantised picture that reports no error is claiming a lossless approximation");

            //The reported error has to bound the real one, or it is worse than no report at all.
            SpriteDefinition set = imported.Set;
            int[] source = DistinctColours(300);
            for (int i = 0; i < source.Length; i++)
            {
                int stored = set.PaletteStored[set.Frames[0].PaletteIndices[i]];
                for (int shift = 0; shift <= 16; shift += 8)
                {
                    int gap = Math.Abs(((source[i] >> shift) & 0xFF) - ((stored >> shift) & 0xFF));
                    Assert.True(gap <= imported.WorstChannelError,
                        $"colour {i} moved {gap} on one channel but the import reported {imported.WorstChannelError}");
                }
            }
        }

        /// <summary>
        ///     A quantised picture still addresses only palette entries that exist.
        /// </summary>
        /// <remarks>
        ///     The client indexes its palette array with the raw byte, so an index past the end is an
        ///     exception in the game rather than a wrong colour. <c>RealCacheSpriteTests</c> asserts
        ///     no shipped file holds one and this asserts the import writes none either.
        /// </remarks>
        [Fact]
        public void AQuantisedPicture_AddressesNoPaletteEntryThatDoesNotExist()
        {
            SpriteDefinition set = Convert(Picture(64, 64, Gradient(64 * 64))).Set;

            Assert.Equal(256, set.PaletteStored.Length);
            foreach (byte index in set.Frames[0].PaletteIndices)
                Assert.InRange(index, 1, set.PaletteStored.Length - 1);

            //And no entry is the transparent slot's colour by accident, which would draw as a hole.
            for (int entry = 1; entry < set.PaletteStored.Length; entry++)
                Assert.NotEqual(0, set.PaletteStored[entry]);
        }

        /// <summary>The same picture converts to the same bytes every time it is imported.</summary>
        /// <remarks>
        ///     A quantiser seeded from a dictionary's enumeration order or from a random start would
        ///     produce a different palette per run, which turns "did this import change anything"
        ///     into a coin toss and rewrites the group CRC on a re-import of the identical file.
        /// </remarks>
        [Fact]
        public void Quantisation_IsDeterministic()
        {
            byte[] first = Convert(Picture(48, 48, Gradient(48 * 48))).Set.Encode().ToArray();
            byte[] second = Convert(Picture(48, 48, Gradient(48 * 48))).Set.Encode().ToArray();

            Assert.Equal(first, second);
        }

        // ===================================================================
        //  Geometry and traversal
        // ===================================================================

        /// <summary>
        ///     A non-square picture keeps its own geometry and is written row by row.
        /// </summary>
        /// <remarks>
        ///     Non-square and asymmetric on purpose. A transposed plane on a square picture is
        ///     still the right length and often still plausible; on a 7x3 it is neither, and the
        ///     expected byte sequence below is what says the traversal is the one the flag claims.
        /// </remarks>
        [Fact]
        public void ANonSquarePicture_IsWrittenRowMajorWithTheFlagClear()
        {
            //A distinct colour per pixel, so the plane's order is readable straight off the indices.
            int[] pixels = DistinctColours(21);
            SpriteImageImport imported = Convert(Picture(7, 3, pixels));

            SpriteDefinition set = imported.Set;
            Assert.Equal(7, set.width);
            Assert.Equal(3, set.height);

            SpriteFrame frame = set.Frames[0];
            Assert.Equal(0, frame.OffsetX);
            Assert.Equal(0, frame.OffsetY);
            Assert.Equal(7, frame.SubWidth);
            Assert.Equal(3, frame.SubHeight);
            Assert.False(frame.IsColumnMajor);
            Assert.Equal(0, frame.Flags);

            //Pixel (x, y) of the picture is at x + y * 7 of the plane, and its colour is the palette
            //entry that index names. A column-major write would put pixel (0, 1) at index 1.
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 7; x++)
                    Assert.Equal(pixels[x + y * 7] & 0xFFFFFF,
                        set.PaletteStored[frame.PaletteIndices[x + y * 7]]);

            //The file the import writes has the flag clear and reads back into the same layout.
            byte[] file = set.Encode().ToArray();
            Assert.Equal(0x00, file[0]);
            Assert.Equal(frame.PaletteIndices, RoundTrip(set).Frames[0].PaletteIndices);
        }

        /// <summary>
        ///     Importing does not disturb the decoder's rule that a stored traversal flag is kept.
        /// </summary>
        /// <remarks>
        ///     The dangerous shape on this index is an encoder that recomputes the flag from the
        ///     pixels: thousands of frames in both caches are too thin for the bytes to state an
        ///     order and every one of them stores the bit clear, so a recomputing encoder sweeps both
        ///     caches clean and corrupts the first frame packed the other way. The import writes new
        ///     frames and must not have taught the encoder to guess, so a set built with the bit set
        ///     on a frame whose pixels cannot state it still comes back with the bit set.
        /// </remarks>
        [Fact]
        public void TheEncoderStillKeepsAStoredTraversalFlagItCannotRecompute()
        {
            var frame = new SpriteFrame
            {
                OffsetX = 0,
                OffsetY = 0,
                SubWidth = 1,
                SubHeight = 4,
                Flags = SpriteFrame.FlagVertical,
                PaletteIndices = new byte[] { 1, 1, 1, 1 }
            };

            SpriteDefinition set = SpriteDefinition.FromFrames(1, 4, new[] { 0, 0x112233 }, new[] { frame });

            Assert.True(set.Frames[0].OrderIsUnrecoverable);
            byte[] file = set.Encode().ToArray();
            Assert.Equal(SpriteFrame.FlagVertical, file[0]);

            SpriteDefinition read = RoundTrip(set);
            Assert.True(read.Frames[0].IsColumnMajor);
            Assert.Equal(file, read.Encode().ToArray());
        }

        /// <summary>A one pixel picture is a legal sprite set.</summary>
        /// <remarks>
        ///     The smallest thing the format can express, and the case where every loop that assumes
        ///     it has a row and a column to work with falls over. Group 1848 of both caches is one.
        /// </remarks>
        [Fact]
        public void AOnePixelPicture_ConvertsAndRoundTrips()
        {
            SpriteImageImport imported = Convert(Picture(1, 1, unchecked((int) 0xFF7F1020)));

            Assert.Equal(1, imported.Set.width);
            Assert.Equal(1, imported.Set.height);
            Assert.Equal(0x7F1020, imported.Set.PaletteStored[1]);

            byte[] file = imported.Set.Encode().ToArray();
            Assert.Equal(file, RoundTrip(imported.Set).Encode().ToArray());
        }

        // ===================================================================
        //  The whole path, through real files
        // ===================================================================

        /// <summary>
        ///     A PNG and a BMP of the same picture import to identical bytes; a JPEG does not.
        /// </summary>
        /// <remarks>
        ///     End to end through GDI+ rather than through a bitmap this test built, so it covers the
        ///     conversion the file format imposes. PNG and BMP are lossless and must agree exactly.
        ///     JPEG is not, and is asserted only to produce a set that decodes - a test that demanded
        ///     the same palette from a JPEG would be asserting the encoder's quality setting.
        /// </remarks>
        [Fact]
        public void PngAndBmp_ImportIdentically_AndJpegImportsAtAll()
        {
            using Bitmap source = Picture(8, 5, Gradient(40));
            string directory = Path.Combine(Path.GetTempPath(), "FlashEditor-sprite-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                string png = Path.Combine(directory, "picture.png");
                string bmp = Path.Combine(directory, "picture.bmp");
                string jpg = Path.Combine(directory, "picture.jpg");
                source.Save(png, ImageFormat.Png);
                source.Save(bmp, ImageFormat.Bmp);
                source.Save(jpg, ImageFormat.Jpeg);

                Assert.True(SpriteImageImporter.LooksLikeAPicture(png));
                Assert.True(SpriteImageImporter.LooksLikeAPicture(bmp));
                Assert.True(SpriteImageImporter.LooksLikeAPicture(jpg));
                Assert.False(SpriteImageImporter.LooksLikeAPicture(Path.Combine(directory, "set.dat")));

                byte[] fromPng = FromFile(png).Set.Encode().ToArray();
                byte[] fromBmp = FromFile(bmp).Set.Encode().ToArray();
                Assert.Equal(fromPng, fromBmp);

                SpriteImageImport fromJpg = FromFile(jpg);
                Assert.Equal(8, fromJpg.Set.width);
                Assert.Equal(5, fromJpg.Set.height);
                Assert.Equal(fromJpg.Set.Encode().ToArray(), RoundTrip(fromJpg.Set).Encode().ToArray());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>
        ///     A 24-bit picture with no alpha channel at all imports as fully opaque.
        /// </summary>
        /// <remarks>
        ///     The pixel format is read rather than assumed: <c>LockBits</c> is asked for
        ///     <c>Format32bppArgb</c> whatever the bitmap holds, so a 24-bit source arrives with an
        ///     alpha of 255 rather than of zero. Reading the source's own format instead would give
        ///     three bytes a pixel and shear every row.
        /// </remarks>
        [Fact]
        public void A24BitPicture_ImportsFullyOpaqueWithNoPlane()
        {
            using var source = new Bitmap(3, 2, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(source))
                graphics.Clear(Color.FromArgb(0x11, 0x22, 0x33));

            SpriteImageImport imported = Convert(source);

            Assert.False(imported.CarriesAnAlphaPlane);
            Assert.Equal(0, imported.TransparentPixels);
            Assert.Equal(1, imported.PaletteColours);
            Assert.Equal(0x112233, imported.Set.PaletteStored[1]);
            Assert.All(imported.Set.Frames[0].PaletteIndices, index => Assert.Equal(1, index));
        }

        // ===================================================================
        //  What the builder refuses
        // ===================================================================

        /// <summary>A pixel pointing past the end of the palette is refused at construction.</summary>
        [Fact]
        public void FromFrames_RefusesAPaletteIndexThePaletteCannotHold()
        {
            var frame = new SpriteFrame
            {
                SubWidth = 1,
                SubHeight = 1,
                PaletteIndices = new byte[] { 5 }
            };

            Assert.Throws<ArgumentException>(() =>
                SpriteDefinition.FromFrames(1, 1, new[] { 0, 0x112233 }, new[] { frame }));
        }

        /// <summary>A flags byte that disagrees with the planes present is refused.</summary>
        /// <remarks>
        ///     The file would encode and then fail to decode: the reader sizes the alpha plane off
        ///     the flag, so a frame claiming a plane it does not carry reads the next frame's bytes
        ///     as its own alpha.
        /// </remarks>
        [Fact]
        public void FromFrames_RefusesAnAlphaFlagWithNoPlaneBehindIt()
        {
            var frame = new SpriteFrame
            {
                SubWidth = 1,
                SubHeight = 1,
                Flags = SpriteFrame.FlagAlpha,
                PaletteIndices = new byte[] { 1 }
            };

            Assert.Throws<ArgumentException>(() =>
                SpriteDefinition.FromFrames(1, 1, new[] { 0, 0x112233 }, new[] { frame }));
        }

        /// <summary>A palette longer than the size byte can state is refused.</summary>
        [Fact]
        public void FromFrames_RefusesAPaletteThatWillNotFitTheSizeByte()
        {
            var frame = new SpriteFrame
            {
                SubWidth = 1,
                SubHeight = 1,
                PaletteIndices = new byte[] { 1 }
            };

            Assert.Throws<ArgumentException>(() =>
                SpriteDefinition.FromFrames(1, 1, new int[257], new[] { frame }));
        }

        // ===================================================================
        //  Builders
        // ===================================================================

        /// <summary>Converts a bitmap and disposes it, which every case here wants.</summary>
        /// <param name="source">The picture.</param>
        /// <returns>The conversion.</returns>
        private static SpriteImageImport Convert(Bitmap source)
        {
            using (source)
                return SpriteImageImporter.FromImage(source);
        }

        /// <summary>Loads and converts a file the way the editor's import path does.</summary>
        /// <param name="path">The picture file.</param>
        /// <returns>The conversion.</returns>
        private static SpriteImageImport FromFile(string path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
            using Image picture = Image.FromStream(file);
            return SpriteImageImporter.FromImage(picture);
        }

        /// <summary>
        ///     Encodes a set and decodes the result, which is what the editor stages and re-reads.
        /// </summary>
        /// <param name="set">The set to write out.</param>
        /// <returns>The set read back off those bytes.</returns>
        private static SpriteDefinition RoundTrip(SpriteDefinition set)
        {
            return SpriteDefinition.DecodeFromStream(new JagStream(set.Encode().ToArray()));
        }

        /// <summary>
        ///     Builds a straight-ARGB bitmap from the pixels given, row by row.
        /// </summary>
        /// <remarks>
        ///     Written through <c>LockBits</c> as <see cref="PixelFormat.Format32bppArgb"/> rather
        ///     than through <c>SetPixel</c>, so the test states the same un-premultiplied convention
        ///     the importer reads with and neither side can quietly assume the other.
        /// </remarks>
        /// <param name="width">The picture's width.</param>
        /// <param name="height">The picture's height.</param>
        /// <param name="pixels">One ARGB value per pixel, row-major.</param>
        /// <returns>The bitmap, owned by the caller.</returns>
        private static Bitmap Picture(int width, int height, params int[] pixels)
        {
            Assert.Equal(width * height, pixels.Length);

            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }

        /// <summary>
        ///     Opaque colours no two of which are equal, and none of which is black.
        /// </summary>
        /// <remarks>
        ///     Spread across all three channels rather than along one, so a quantiser that only ever
        ///     splits on red still has to work. Black is excluded because it is the one colour whose
        ///     stored spelling changes, and it has its own cases.
        /// </remarks>
        /// <param name="count">How many, up to the 336 the lattice below holds.</param>
        /// <returns>The colours, as opaque ARGB.</returns>
        private static int[] DistinctColours(int count)
        {
            //A 6 x 7 x 8 lattice, which is a bijection from i and therefore cannot collide - the
            //first version of this multiplied i by three primes modulo 251 and silently repeated
            //itself every 251 colours, so a test asking for 255 got 251 and failed on the palette
            //size rather than on anything the importer did.
            Assert.InRange(count, 0, 6 * 7 * 8);

            var colours = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                int red = 1 + i % 6 * 40;
                int green = 1 + i / 6 % 7 * 35;
                int blue = 1 + i / 42 * 25;
                colours.Add(unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue);
            }

            Assert.Equal(count, colours.Distinct().Count());
            return colours.ToArray();
        }

        /// <summary>
        ///     A smooth ramp of far more colours than a palette holds, which is what forces a cut.
        /// </summary>
        /// <param name="count">How many pixels.</param>
        /// <returns>The colours, as opaque ARGB.</returns>
        private static int[] Gradient(int count)
        {
            var colours = new int[count];
            for (int i = 0; i < count; i++)
            {
                int red = 1 + i * 255 / Math.Max(1, count - 1);
                int green = 1 + (count - 1 - i) * 200 / Math.Max(1, count - 1);
                int blue = 1 + i % 199;
                colours[i] = unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue;
            }

            return colours;
        }
    }
}
