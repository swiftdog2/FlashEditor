using System;
using System.IO;
using System.Linq;
using FlashEditor.Definitions.LoadingSprites;
using Xunit;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-32 codec against bytes it did not produce, and against the one image the
    ///     637 client carries inside itself.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Round-tripping this decoder against this encoder would prove nothing here, and less than
    ///     usual: the JPEG half's encoder returns the stored bytes, so byte identity on it is true
    ///     by construction and says nothing about whether the file was understood. The claims that
    ///     do carry weight are that the structural parse accounts for every byte, that the entropy
    ///     decode consumes the scan exactly, and that the colour model is the one the file's own
    ///     tables describe.
    ///     </para>
    ///     <para>
    ///     The probe blob below is the anchor for all three. It is not from the cache - it is
    ///     <c>Class74.aByteArray546</c> (<c>Class74.java:6-33</c>), gunzipped, the 1x1 image
    ///     <c>Class116.method2162</c> pushes through the AWT decoder to decide whether to open index
    ///     32 or fall back to index 34 (<c>InterfaceSettings.java:72-74</c>). The client is
    ///     therefore stating what an index-32 image looks like, and
    ///     <c>RealCacheLoadingSpriteTests</c> holds the cache's twenty-one images against it.
    ///     </para>
    /// </remarks>
    public class LoadingSpriteCodecTests
    {
        /// <summary>
        ///     The client's own capability-probe image, gunzipped from <c>Class74.java:6-33</c>.
        /// </summary>
        /// <remarks>
        ///     622 bytes: SOI, two DQT segments, an <c>SOF0</c> declaring a 1x1 four-component
        ///     image, four DHT segments, the scan, EOI. No <c>JFIF APP0</c> and no <c>Adobe
        ///     APP14</c> - the absence that makes every general decoder guess CMYK.
        /// </remarks>
        private const string ProbeHex =
            "FFD8FFDB004300080606070605080707070909080A0C140D0C0B0B0C1912130F" +
            "141D1A1F1E1D1A1C1C20242E2720222C231C1C2837292C30313434341F27393D" +
            "38323C2E333432FFDB0043010909090C0B0C180D0D1832211C21323232323232" +
            "3232323232323232323232323232323232323232323232323232323232323232" +
            "323232323232323232323232FFC0001408000100010401220002110103110104" +
            "2200FFC4001F0000010501010101010100000000000000000102030405060708" +
            "090A0BFFC400B5100002010303020403050504040000017D0102030004110512" +
            "2131410613516107227114328191A1082342B1C11552D1F02433627282090A16" +
            "1718191A25262728292A3435363738393A434445464748494A53545556575859" +
            "5A636465666768696A737475767778797A838485868788898A92939495969798" +
            "999AA2A3A4A5A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4" +
            "D5D6D7D8D9DAE1E2E3E4E5E6E7E8E9EAF1F2F3F4F5F6F7F8F9FAFFC4001F0100" +
            "030101010101010101010000000000000102030405060708090A0BFFC400B511" +
            "0002010204040304070504040001027700010203110405213106124151076171" +
            "1322328108144291A1B1C109233352F0156272D10A162434E125F11718191A26" +
            "2728292A35363738393A434445464748494A535455565758595A636465666768" +
            "696A737475767778797A82838485868788898A92939495969798999AA2A3A4A5" +
            "A6A7A8A9AAB2B3B4B5B6B7B8B9BAC2C3C4C5C6C7C8C9CAD2D3D4D5D6D7D8D9DA" +
            "E2E3E4E5E6E7E8E9EAF2F3F4F5F6F7F8F9FAFFDA000E04010002110311040000" +
            "3F00F9FE8A28A00F9FE8A28AFFD9";

        /// <summary>The ITU T.81 Annex K luminance quantisation table, in natural order.</summary>
        /// <remarks>
        ///     Transcribed from the specification, so the test derives the expected table rather
        ///     than copying what the file happens to hold. That is the whole point: a table read out
        ///     of the file and compared against itself would confirm nothing.
        /// </remarks>
        private static readonly int[] AnnexKLuminance =
        {
            16, 11, 10, 16,  24,  40,  51,  61,
            12, 12, 14, 19,  26,  58,  60,  55,
            14, 13, 16, 24,  40,  57,  69,  56,
            14, 17, 22, 29,  51,  87,  80,  62,
            18, 22, 37, 56,  68, 109, 103,  77,
            24, 35, 55, 64,  81, 104, 113,  92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103,  99
        };

        /// <summary>The ITU T.81 Annex K chrominance quantisation table, in natural order.</summary>
        private static readonly int[] AnnexKChrominance =
        {
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99
        };

        /// <summary>The client's probe image bytes.</summary>
        /// <returns>The gunzipped blob from <c>Class74.java</c>.</returns>
        public static byte[] ClientProbeImage() => Convert.FromHexString(ProbeHex);

        /// <summary>
        ///     Scales a base quantisation table the way the IJG encoder does for a quality setting.
        /// </summary>
        /// <remarks>
        ///     <c>jcparam.c</c>: below quality 50 the factor is <c>5000 / quality</c>, at or above
        ///     it is <c>200 - 2 * quality</c>, and each entry becomes
        ///     <c>(value * factor + 50) / 100</c> clamped to 1..255.
        /// </remarks>
        /// <param name="table">The base table.</param>
        /// <param name="quality">The quality setting.</param>
        /// <returns>The scaled table.</returns>
        private static int[] ScaleQuantisationTable(int[] table, int quality)
        {
            int factor = quality < 50 ? 5000 / quality : 200 - quality * 2;
            return table.Select(value =>
            {
                int scaled = (value * factor + 50) / 100;
                return scaled < 1 ? 1 : (scaled > 255 ? 255 : scaled);
            }).ToArray();
        }

        /// <summary>
        ///     The structural parse accounts for every byte of the client's own image.
        /// </summary>
        /// <remarks>
        ///     The only claim that defends the parse. A segment sized wrongly, a scan that ended in
        ///     the wrong place or a dropped trailer are all invisible to a decode-then-render test
        ///     and all show up here as a byte difference.
        /// </remarks>
        [Fact]
        public void TheClientProbeImage_ReassemblesFromItsParsedParts()
        {
            byte[] stored = ClientProbeImage();
            JagexJpeg jpeg = JagexJpeg.Decode(stored);

            Assert.Equal(stored, jpeg.ToBytes());
            Assert.Equal(new byte[] { 0xFF, JagexJpeg.MarkerEoi }, jpeg.Trailer);
        }

        /// <summary>
        ///     The client's probe declares the same four-component shape the cache's images do.
        /// </summary>
        /// <remarks>
        ///     This is what ties the shape to the client rather than to an inference about it. The
        ///     client gunzips this blob and pushes it through the AWT image decoder purely to find
        ///     out whether the JVM can handle it; if it cannot, index 32 is abandoned for index 34.
        ///     So the client is asserting that an index-32 image looks exactly like this.
        /// </remarks>
        [Fact]
        public void TheClientProbeImage_IsAFourComponentBaselineJpegWithNoColourSpaceMarker()
        {
            JagexJpeg jpeg = JagexJpeg.Decode(ClientProbeImage());

            Assert.True(jpeg.IsBaseline);
            Assert.Equal(1, jpeg.Width);
            Assert.Equal(1, jpeg.Height);
            Assert.Equal(8, jpeg.Precision);
            Assert.Equal(0, jpeg.RestartInterval);

            Assert.Equal(new[] { 1, 2, 3, 4 }, jpeg.Components.Select(c => c.Id).ToArray());
            Assert.Equal(new[] { 2, 1, 1, 2 }, jpeg.Components.Select(c => c.HorizontalSampling).ToArray());
            Assert.Equal(new[] { 2, 1, 1, 2 }, jpeg.Components.Select(c => c.VerticalSampling).ToArray());
            Assert.Equal(new[] { 0, 1, 1, 0 }, jpeg.Components.Select(c => c.QuantisationTableId).ToArray());

            //APP0 would be a JFIF header and APP14 an Adobe colour-transform marker. Their absence
            //is what sends every general-purpose decoder down the CMYK branch.
            Assert.DoesNotContain(jpeg.Segments, segment => segment.Marker == 0xE0);
            Assert.DoesNotContain(jpeg.Segments, segment => segment.Marker == 0xEE);
        }

        /// <summary>
        ///     The file's own quantisation tables say which components are luma and which are chroma.
        /// </summary>
        /// <remarks>
        ///     This is the evidence the colour model rests on, and it is the file's rather than the
        ///     reader's. Table 0 is the Annex K <b>luminance</b> table and table 1 the Annex K
        ///     <b>chrominance</b> table, both at IJG quality 75; table 0 is assigned to components 1
        ///     and 4 and table 1 to components 2 and 3 alone. An encoder that picked the chrominance
        ///     table for two of four inks of a CMYK image, and subsampled exactly those two, would be
        ///     doing something with no explanation. Reading them as Cb and Cr explains all of it.
        /// </remarks>
        [Fact]
        public void TheQuantisationTables_AreTheStandardLumaAndChromaTables()
        {
            JagexJpeg jpeg = JagexJpeg.Decode(ClientProbeImage());

            Assert.Equal(ScaleQuantisationTable(AnnexKLuminance, 75), jpeg.QuantisationTables[0]);
            Assert.Equal(ScaleQuantisationTable(AnnexKChrominance, 75), jpeg.QuantisationTables[1]);

            //The luma table on components 1 and 4, the chroma table on 2 and 3 alone.
            Assert.Equal(new[] { 0, 1, 1, 0 }, jpeg.Components.Select(c => c.QuantisationTableId).ToArray());
        }

        /// <summary>
        ///     The entropy decode consumes the scan exactly, and the fourth plane carries nothing.
        /// </summary>
        /// <remarks>
        ///     Exact consumption is the sharp instrument on a JPEG for the same reason it is on an
        ///     opcode stream: nothing states how long the entropy-coded data is, so a decoder using
        ///     the wrong table or the wrong MCU geometry desynchronises and stops somewhere else.
        /// </remarks>
        [Fact]
        public void TheClientProbeImage_DecodesToOneConstantPlanePerComponent()
        {
            JpegRaster raster = BaselineJpegDecoder.Decode(JagexJpeg.Decode(ClientProbeImage()));

            Assert.Equal(raster.ScanBytesAvailable, raster.ScanBytesConsumed);
            Assert.Equal(4, raster.ComponentCount);
            Assert.Equal(1, raster.Width);
            Assert.Equal(1, raster.Height);

            //Luma 0, both chroma planes at the level-shift midpoint, and a fourth plane that is
            //flat - so the probe is one opaque black pixel and nothing else.
            Assert.Equal(0, (int) raster.Plane(0)[0]);
            Assert.Equal(128, (int) raster.Plane(1)[0]);
            Assert.Equal(128, (int) raster.Plane(2)[0]);
            Assert.True(raster.IsConstant(3));
            Assert.Equal(new[] { unchecked((int) 0xFF000000) }, raster.ToArgb());
        }

        /// <summary>
        ///     Chroma at the midpoint renders neutral grey, which a CMYK reading of the same file
        ///     could not produce.
        /// </summary>
        /// <remarks>
        ///     The one test that discriminates between the two readings on a single file rather than
        ///     by argument. The probe carries Cb and Cr at exactly 128 with luma 0, so under YCbCr
        ///     the red, green and blue channels have to come out equal. Under a CMYK reading the same
        ///     samples are a cyan of 0 with magenta and yellow at half and black flat, which is not
        ///     grey at all. <c>RealCacheLoadingSpriteTests</c> makes the same check on a real
        ///     interface image that carries a picture rather than one pixel.
        /// </remarks>
        [Fact]
        public void ChromaAtTheMidpoint_RendersNeutralGrey()
        {
            JpegRaster raster = BaselineJpegDecoder.Decode(JagexJpeg.Decode(ClientProbeImage()));
            int pixel = raster.ToArgb()[0];

            int red = (pixel >> 16) & 0xFF;
            int green = (pixel >> 8) & 0xFF;
            int blue = pixel & 0xFF;

            Assert.Equal(red, green);
            Assert.Equal(green, blue);
        }

        /// <summary>
        ///     A fourth component that carries a picture is refused rather than discarded.
        /// </summary>
        /// <remarks>
        ///     The branch no cache can reach, and exactly the shape of defect this project has
        ///     shipped before: every index-32 image has a flat fourth plane, so an encoder or
        ///     renderer that dropped the component unconditionally would sweep both caches clean and
        ///     be wrong about the first file that used it. The synthetic case builds one by taking
        ///     the client's own image and forcing the fourth plane to vary.
        /// </remarks>
        [Fact]
        public void AVaryingFourthComponent_IsRefusedRatherThanDropped()
        {
            JpegRaster raster = BaselineJpegDecoder.Decode(JagexJpeg.Decode(ClientProbeImage()));
            var planes = new byte[4][];
            for (int component = 0; component < 4; component++)
                planes[component] = new byte[] { raster.Plane(component)[0], raster.Plane(component)[0] };

            //Two pixels wide, and the fourth component now differs between them.
            planes[3][1] = (byte) (planes[3][0] ^ 0xFF);

            var varying = new JpegRaster(2, 1, planes, raster.ScanBytesConsumed, raster.ScanBytesAvailable);

            Assert.False(varying.IsConstant(3));
            InvalidDataException failure = Assert.Throws<InvalidDataException>(() => varying.ToArgb());
            Assert.Contains("fourth component varies", failure.Message);
        }

        /// <summary>
        ///     The shape dispatcher answers on the payload's magic and nothing else.
        /// </summary>
        /// <remarks>
        ///     Dispatching on the index id breaks five of the twenty-six groups, and dispatching on
        ///     "the first byte is small" would misfile a sprite set whose first frame set a flag bit
        ///     the format does not define. Only two of that byte's eight bits are ever read.
        /// </remarks>
        [Fact]
        public void TheShapeDispatcher_ReadsTheSoiMarkerAndNotTheIndex()
        {
            Assert.True(LoadingSpriteDefinition.LooksLikeJpeg(new byte[] { 0xFF, 0xD8, 0xFF, 0xDB }));
            Assert.False(LoadingSpriteDefinition.LooksLikeJpeg(new byte[] { 0x00, 0x00 }));
            Assert.False(LoadingSpriteDefinition.LooksLikeJpeg(new byte[] { 0xFF }));
            Assert.False(LoadingSpriteDefinition.LooksLikeJpeg(Array.Empty<byte>()));

            //A sprite set whose first frame carries an undefined flag bit still is not a JPEG.
            Assert.False(LoadingSpriteDefinition.LooksLikeJpeg(new byte[] { 0xFF, 0x00 }));
        }

        /// <summary>
        ///     A JPEG group writes back the bytes it was read as, byte for byte.
        /// </summary>
        /// <remarks>
        ///     True by construction, and that is the design rather than an accident: a JPEG
        ///     re-encode is no more reproducible than a GZip one, so keeping the stored bytes is the
        ///     only way an unedited image survives a save. Asserted anyway, because the alternative -
        ///     an encoder that quietly started re-compressing - would change the archive CRC and
        ///     therefore the reference-table entry of everything packed beside it.
        /// </remarks>
        [Fact]
        public void AJpegGroup_WritesBackTheBytesItWasReadAs()
        {
            byte[] stored = ClientProbeImage();
            var definition = new LoadingSpriteDefinition { Id = 3769 };
            definition.Decode(new JagStream(stored));

            Assert.Equal(LoadingSpriteShape.JpegImage, definition.Shape);
            Assert.Equal(stored, definition.Encode().ToArray());
            Assert.Equal(stored, definition.StoredBytes);
        }

        /// <summary>
        ///     A sprite-set group goes through the index-8 codec rather than the JPEG path.
        /// </summary>
        /// <remarks>
        ///     The synthetic set is the smallest legal one - a 1x1 canvas, one frame, one palette
        ///     entry - laid out to the read order in <c>Class324.method3690</c>. It exists to prove
        ///     the dispatch, not the sprite codec, which <c>SpriteDefinitionCodecTests</c> owns.
        /// </remarks>
        [Fact]
        public void ASpriteSetGroup_IsDecodedByTheSpriteCodec()
        {
            byte[] stored =
            {
                0x00, 0x01,                    //frame 0: flags, one palette index
                0x2A, 0x3B, 0x4C,              //palette entry 1
                0x00, 0x01, 0x00, 0x01,        //canvas 1 x 1
                0x01,                          //paletteSize - 1
                0x00, 0x00, 0x00, 0x00,        //offsetX, offsetY
                0x00, 0x01, 0x00, 0x01,        //subWidth, subHeight
                0x00, 0x01                     //one frame
            };

            var definition = new LoadingSpriteDefinition { Id = 494 };
            definition.Decode(new JagStream(stored));

            Assert.Equal(LoadingSpriteShape.SpriteSet, definition.Shape);
            Assert.Null(definition.Jpeg);
            Assert.Equal(1, definition.FrameCount);
            Assert.Equal(stored, definition.Encode().ToArray());
        }

        /// <summary>
        ///     A group holding no bytes is refused rather than given a shape.
        /// </summary>
        /// <remarks>
        ///     Neither format can be recognised in zero bytes, and both decoders would read past the
        ///     end rather than say so - the sprite decoder seeks backwards from the end of the file
        ///     and would find a frame count in whatever preceded it.
        /// </remarks>
        [Fact]
        public void AnEmptyGroup_IsRefused()
        {
            var definition = new LoadingSpriteDefinition { Id = 0 };
            Assert.Throws<InvalidDataException>(() => definition.Decode(new JagStream(Array.Empty<byte>())));
        }

        /// <summary>
        ///     A truncated segment length is refused rather than parsed into something plausible.
        /// </summary>
        /// <remarks>
        ///     A parse that tolerated it would have to guess where the segment ended, and a guessed
        ///     boundary cannot be held against the stored bytes afterwards - which is the only
        ///     evidence the JPEG half has that it understood the file.
        /// </remarks>
        [Fact]
        public void ASegmentLongerThanTheFile_IsRefused()
        {
            byte[] truncated = ClientProbeImage().Take(8).ToArray();
            Assert.Throws<InvalidDataException>(() => JagexJpeg.Decode(truncated));
        }

        /// <summary>
        ///     The names this recovers hash to what the client asks for by name.
        /// </summary>
        /// <remarks>
        ///     <c>Class84.java:20-31</c> resolves <c>p11_full</c>, <c>p12_full</c> and
        ///     <c>b12_full</c> against index 32 by name, so the hashes have to be reachable through
        ///     the same table the reference table stores. Checking the round trip here means the
        ///     cache-backed test can assert they land on real groups without also having to prove
        ///     the hash.
        /// </remarks>
        [Fact]
        public void TheClientsOwnNames_AreRecoverableFromTheirHashes()
        {
            foreach (string name in new[] { "p11_full", "p12_full", "b12_full" })
            {
                Assert.Contains(name, LoadingSpriteNames.KnownNames);
                Assert.True(LoadingSpriteNames.TryGetName(
                    FlashEditor.Cache.Util.NameHasher.GetNameHash(name), out string recovered));
                Assert.Equal(name, recovered);
            }

            Assert.False(LoadingSpriteNames.TryGetName(0, out string none));
            Assert.Null(none);
        }
    }
}
