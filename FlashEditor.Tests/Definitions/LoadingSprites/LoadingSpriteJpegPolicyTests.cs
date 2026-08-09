using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using FlashEditor.Definitions.LoadingSprites;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions.LoadingSprites
{
    /// <summary>
    ///     Pins what the Loading Sprites tab will and will not store into index 32.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>No sweep can defend this check, so it is defended here or nowhere.</b> A byte-identity
    ///     sweep only sees files that are already in the cache, and every file this check refuses is
    ///     by definition one that is not. So every case below is hand-made: an ordinary
    ///     three-component JFIF, the same file with its JFIF header stripped, a four-component file
    ///     wearing an Adobe marker, a valid four-component file with neither, a truncated file, and
    ///     something that is not a JPEG at all.
    ///     </para>
    ///     <para>
    ///     Measured, not argued. Disabling the policy's component-layout clause on purpose failed
    ///     exactly one test in the whole index-32 family -
    ///     <see cref="AThreeComponentFileWithNoJfifHeader_IsStillRefusedForItsComponents"/> - while
    ///     every cache-backed sweep over both caches' twenty-one images passed clean. That is the
    ///     shape of the argument for every case here: the sweeps cannot see a rule whose triggering
    ///     input is absent from the cache, and every input this rule triggers on is absent by
    ///     definition.
    ///     </para>
    ///     <para>
    ///     <b>The first test is the reason the rest exist.</b>
    ///     <c>JpegRaster.ToArgb</c> reads planes 0, 1 and 2 as Y, Cb and Cr whether the file carries
    ///     three components or four, so an ordinary JFIF previews as a perfectly good picture in this
    ///     editor. "It rendered" is therefore not evidence of anything, and a replace path gated on
    ///     rendering would have stored it.
    ///     </para>
    /// </remarks>
    public class LoadingSpriteJpegPolicyTests
    {
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the per-test output sink, which the shape census below writes to.</summary>
        /// <param name="output">The sink.</param>
        public LoadingSpriteJpegPolicyTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        ///     A three-component image renders here exactly as a four-component one does.
        /// </summary>
        /// <remarks>
        ///     Measured rather than asserted from the source, because it is the premise of the whole
        ///     policy: the render path cannot tell an index-32 image from an ordinary JFIF, so
        ///     nothing downstream of it can either. If this ever starts throwing, the policy's job
        ///     has changed and its doc comment is wrong.
        /// </remarks>
        [Fact]
        public void TheRenderPath_ColoursThreeAndFourComponentImagesIdentically()
        {
            byte[] ordinary = OrdinaryJfif(24, 16);
            JagexJpeg jpeg = JagexJpeg.Decode(ordinary);
            Assert.Equal(3, jpeg.Components.Count);

            JpegRaster raster = BaselineJpegDecoder.Decode(jpeg);
            int[] pixels = raster.ToArgb();

            //No exception, no complaint, a full picture. That is the trap.
            Assert.Equal(3, raster.ComponentCount);
            Assert.Equal(24 * 16, pixels.Length);
            Assert.All(pixels, pixel => Assert.Equal(0xFF, (pixel >> 24) & 0xFF));
        }

        /// <summary>
        ///     The image the client carries inside itself is accepted.
        /// </summary>
        /// <remarks>
        ///     The one file outside the cache that the client itself states an index-32 image looks
        ///     like: <c>Class116.method2162</c> (<c>Class116.java:60-77</c>) gunzips
        ///     <c>Class74.aByteArray546</c> and pushes it through the AWT decoder to decide whether
        ///     to use index 32 at all. A policy that refused it would be refusing the client's own
        ///     definition of the format.
        /// </remarks>
        [Fact]
        public void TheClientProbeImage_IsAccepted()
        {
            Assert.True(LoadingSpriteJpegPolicy.TryAccept(LoadingSpriteCodecTests.ClientProbeImage(),
                out JagexJpeg accepted, out string refusal), refusal);

            Assert.NotNull(accepted);
            Assert.Equal(1, accepted.Width);
            Assert.Equal(1, accepted.Height);
            Assert.Empty(refusal);
        }

        /// <summary>
        ///     What any ordinary tool saves is refused, and refused for a stated reason.
        /// </summary>
        /// <remarks>
        ///     This is the case the whole check exists for. A three-component JFIF is what every
        ///     image editor, every screenshot tool and every library emits by default; it parses, it
        ///     decodes, and it previews as a picture. Storing it would put a file into index 32 that
        ///     is not the shape any index-32 image is, and nothing establishes what the client's AWT
        ///     path would draw for it.
        /// </remarks>
        [Fact]
        public void AnOrdinaryThreeComponentJfif_IsRefusedForItsJfifHeader()
        {
            byte[] ordinary = OrdinaryJfif(24, 16);

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(ordinary, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("JFIF APP0", refusal);
        }

        /// <summary>
        ///     Stripping the JFIF header off an ordinary file does not make it storable.
        /// </summary>
        /// <remarks>
        ///     The obvious way round the previous refusal, and the reason the component layout is
        ///     checked in its own right rather than the marker being treated as the whole test. This
        ///     file has no colour-space marker at all and is still a three-component image, which the
        ///     colour reading this editor draws with does not describe.
        /// </remarks>
        [Fact]
        public void AThreeComponentFileWithNoJfifHeader_IsStillRefusedForItsComponents()
        {
            byte[] stripped = WithoutSegment(OrdinaryJfif(24, 16), 0xE0);

            //It still parses and still renders - that is the point.
            JagexJpeg jpeg = JagexJpeg.Decode(stripped);
            Assert.Equal(3, jpeg.Components.Count);
            Assert.DoesNotContain(jpeg.Segments, segment => segment.Marker == 0xE0);
            Assert.NotEmpty(BaselineJpegDecoder.Decode(jpeg).ToArgb());

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(stripped, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("3 component(s)", refusal);
            Assert.Contains("4 component(s)", refusal);
        }

        /// <summary>
        ///     A four-component file that declares itself CMYK is refused.
        /// </summary>
        /// <remarks>
        ///     An <c>Adobe APP14</c> with transform 0 is a statement that the four components are
        ///     CMYK inks. Everything else about the file below is exactly what index 32 holds, so
        ///     without the marker check it would sail through - and the client's decoder would read
        ///     the marker while this editor read the inference, which is the one disagreement that
        ///     produces a plausible wrong picture on both sides.
        /// </remarks>
        [Fact]
        public void AFourComponentFileWearingAnAdobeMarker_IsRefused()
        {
            byte[] adobe = WithSegmentAfterSoi(LoadingSpriteCodecTests.ClientProbeImage(), AdobeCmykSegment);

            //Everything but the marker is what the cache holds.
            JagexJpeg jpeg = JagexJpeg.Decode(adobe);
            Assert.Equal(4, jpeg.Components.Count);
            Assert.Contains(jpeg.Segments, segment => segment.Marker == 0xEE);

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(adobe, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("Adobe APP14", refusal);
        }

        /// <summary>
        ///     A progressive frame header is refused rather than half-decoded.
        /// </summary>
        /// <remarks>
        ///     Built by changing the client's own image from <c>SOF0</c> to <c>SOF2</c> and nothing
        ///     else, so the only thing under test is the frame marker. A progressive file's scans
        ///     carry successive-approximation coefficients rather than whole blocks, and this
        ///     editor's reader would produce a picture out of them rather than an error.
        /// </remarks>
        [Fact]
        public void AProgressiveFrameHeader_IsRefused()
        {
            byte[] progressive = LoadingSpriteCodecTests.ClientProbeImage();
            int frameAt = IndexOfMarker(progressive, JagexJpeg.MarkerSof0);
            progressive[frameAt + 1] = 0xC2;

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(progressive, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("FFC2", refusal);
        }

        /// <summary>
        ///     A declared restart interval is refused, because no shipped byte exercises that path.
        /// </summary>
        /// <remarks>
        ///     The reader implements restart markers and no index-32 image carries a <c>DRI</c>
        ///     segment, so the implementation is unexercised by every sweep in this suite. A
        ///     replacement relying on it would be the first thing ever to test it, and the test would
        ///     be a user looking at the game.
        /// </remarks>
        [Fact]
        public void ADeclaredRestartInterval_IsRefused()
        {
            byte[] restarting = WithSegmentAfterSoi(LoadingSpriteCodecTests.ClientProbeImage(),
                new byte[] { 0xFF, JagexJpeg.MarkerDri, 0x00, 0x04, 0x00, 0x01 });

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(restarting, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("restart interval", refusal);
        }

        /// <summary>
        ///     Bytes after the EOI marker are refused rather than stored along with the image.
        /// </summary>
        /// <remarks>
        ///     They would be written into the group verbatim, since the save path stores what it is
        ///     given. Some tools append thumbnails or metadata there and nothing establishes what the
        ///     client's decoder does with the tail.
        /// </remarks>
        [Fact]
        public void BytesAfterTheEoiMarker_AreRefused()
        {
            byte[] padded = LoadingSpriteCodecTests.ClientProbeImage().Concat(new byte[] { 0x00, 0x00 }).ToArray();

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(padded, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("EOI marker", refusal);
        }

        /// <summary>
        ///     A file cut off part way through is refused rather than parsed into something plausible.
        /// </summary>
        /// <remarks>
        ///     Two cuts, because they are caught by different clauses: one inside the header
        ///     segments, which the structural parse refuses outright, and one inside the scan, which
        ///     parses cleanly and is caught only because the file no longer ends on <c>FF D9</c>. The
        ///     second is the reason the EOI clause is not merely tidiness - a half-downloaded image
        ///     is a whole valid JPEG right up to the point it stops.
        /// </remarks>
        [Theory]
        [InlineData(8)]
        [InlineData(615)]
        public void ATruncatedFile_IsRefused(int keep)
        {
            byte[] truncated = LoadingSpriteCodecTests.ClientProbeImage().Take(keep).ToArray();

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(truncated, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.NotEmpty(refusal);
            _output.WriteLine($"truncated to {keep} bytes: {refusal}");
        }

        /// <summary>
        ///     A scan with bytes missing from the middle is refused even though the file still ends
        ///     on its EOI marker.
        /// </summary>
        /// <remarks>
        ///     The case the truncation tests cannot reach, and the only one that puts the entropy
        ///     decode to the test: every marker is where it should be, the trailer is intact, and the
        ///     scan simply does not hold enough bits for the blocks the frame header declares. A
        ///     policy that stopped at the structural checks would accept it and the client would be
        ///     handed a file whose picture nothing has ever seen.
        /// </remarks>
        [Fact]
        public void AScanMissingBytesWithItsEoiIntact_IsRefused()
        {
            byte[] probe = LoadingSpriteCodecTests.ClientProbeImage();
            //Everything up to the last five scan bytes, then straight to the EOI marker.
            byte[] starved = probe.Take(probe.Length - 7).Concat(new byte[] { 0xFF, JagexJpeg.MarkerEoi }).ToArray();

            JagexJpeg jpeg = JagexJpeg.Decode(starved);
            Assert.Equal(new byte[] { 0xFF, JagexJpeg.MarkerEoi }, jpeg.Trailer);
            Assert.Equal(LoadingSpriteJpegPolicy.AcceptedComponents, jpeg.Components);

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(starved, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("scan", refusal);
            _output.WriteLine("starved scan: " + refusal);
        }

        /// <summary>
        ///     Something that is not a JPEG at all is refused on its first two bytes.
        /// </summary>
        /// <remarks>
        ///     A file picked through a dialog filtered to <c>*.jpg</c> is not thereby a JPEG - the
        ///     filter is a convenience and "All files" sits beside it. The PNG below is the ordinary
        ///     way this happens: a picture the user believes is the right thing, saved in the wrong
        ///     format.
        /// </remarks>
        [Fact]
        public void AFileThatIsNotAJpeg_IsRefused()
        {
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(png, out JagexJpeg accepted, out string refusal));
            Assert.Null(accepted);
            Assert.Contains("FF D8", refusal);

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(Array.Empty<byte>(), out _, out refusal));
            Assert.Contains("FF D8", refusal);

            Assert.False(LoadingSpriteJpegPolicy.TryAccept(null, out _, out refusal));
            Assert.Contains("FF D8", refusal);
        }

        /// <summary>
        ///     Every refusal says what was wrong, in a sentence a user can act on.
        /// </summary>
        /// <remarks>
        ///     The refusals are the only thing standing between a user and a loading screen that is
        ///     wrong in game, so one that reads as "no" and nothing else pushes the user to hunt for
        ///     a way round it rather than to fix the file. Length is a crude proxy and it is the one
        ///     that catches a message shortened to a code.
        /// </remarks>
        [Fact]
        public void EveryRefusal_NamesWhatIsWrong()
        {
            var refused = new List<byte[]>
            {
                new byte[] { 0x89, 0x50, 0x4E, 0x47 },
                LoadingSpriteCodecTests.ClientProbeImage().Take(8).ToArray(),
                OrdinaryJfif(24, 16),
                WithoutSegment(OrdinaryJfif(24, 16), 0xE0),
                WithSegmentAfterSoi(LoadingSpriteCodecTests.ClientProbeImage(), AdobeCmykSegment)
            };

            foreach (byte[] candidate in refused)
            {
                Assert.False(LoadingSpriteJpegPolicy.TryAccept(candidate, out _, out string refusal));
                Assert.True(refusal.Length > 40, $"a refusal of {refusal.Length} characters: {refusal}");
                Assert.EndsWith(".", refusal);
            }
        }

        /// <summary>
        ///     The shape the policy accepts is the shape the client's probe declares, field for field.
        /// </summary>
        /// <remarks>
        ///     The constants are copied from the probe, so this is what stops them being edited into
        ///     something the client never stated. <c>RealCacheLoadingSpriteTests</c> makes the other
        ///     half of the join, that every image in each cache carries the same shape.
        /// </remarks>
        [Fact]
        public void TheAcceptedShape_IsTheClientProbesOwnShape()
        {
            JagexJpeg probe = JagexJpeg.Decode(LoadingSpriteCodecTests.ClientProbeImage());

            Assert.Equal(LoadingSpriteJpegPolicy.AcceptedComponents, probe.Components);
            Assert.Equal(LoadingSpriteJpegPolicy.AcceptedScanComponents, probe.ScanComponents);
        }

        /// <summary>An Adobe <c>APP14</c> segment declaring a transform of 0, which means CMYK.</summary>
        private static byte[] AdobeCmykSegment => new byte[]
        {
            0xFF, 0xEE, 0x00, 0x0E,
            0x41, 0x64, 0x6F, 0x62, 0x65,  //"Adobe"
            0x00, 0x64,                    //version 100
            0x00, 0x00, 0x00, 0x00,        //flags
            0x00                           //transform 0: no transform, so straight CMYK
        };

        /// <summary>
        ///     A genuine three-component JFIF, produced the way any ordinary tool produces one.
        /// </summary>
        /// <remarks>
        ///     Encoded by GDI+ rather than hand-built, so the file under test is a real encoder's
        ///     output and not this test's idea of one. A hand-built file would only prove the policy
        ///     rejects what the test author expected it to.
        /// </remarks>
        /// <param name="width">Image width.</param>
        /// <param name="height">Image height.</param>
        /// <returns>The encoded file.</returns>
        private static byte[] OrdinaryJfif(int width, int height)
        {
            using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    bitmap.SetPixel(x, y, Color.FromArgb(255, x * 8 % 256, y * 12 % 256, (x + y) * 5 % 256));

            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Jpeg);
            return buffer.ToArray();
        }

        /// <summary>Splices a marker segment in directly after the SOI marker.</summary>
        /// <param name="file">The file.</param>
        /// <param name="segment">The whole segment, marker and length included.</param>
        /// <returns>The new file.</returns>
        private static byte[] WithSegmentAfterSoi(byte[] file, byte[] segment)
        {
            return file.Take(2).Concat(segment).Concat(file.Skip(2)).ToArray();
        }

        /// <summary>
        ///     Cuts one marker segment out of a file.
        /// </summary>
        /// <remarks>
        ///     Walks the segment chain rather than searching for the marker bytes, because a
        ///     <c>0xFF</c> pair can occur inside a payload and cutting there would corrupt the file
        ///     into something that fails for the wrong reason.
        /// </remarks>
        /// <param name="file">The file.</param>
        /// <param name="marker">The marker to remove.</param>
        /// <returns>The file without it.</returns>
        private static byte[] WithoutSegment(byte[] file, byte marker)
        {
            int at = IndexOfMarker(file, marker);
            int length = (file[at + 2] << 8) | file[at + 3];
            return file.Take(at).Concat(file.Skip(at + 2 + length)).ToArray();
        }

        /// <summary>Finds where a marker's segment starts, walking the chain from the SOI.</summary>
        /// <param name="file">The file.</param>
        /// <param name="marker">The marker to find.</param>
        /// <returns>The offset of its <c>0xFF</c>.</returns>
        private static int IndexOfMarker(byte[] file, byte marker)
        {
            int at = 2;
            while (at + 3 < file.Length)
            {
                if (file[at] != 0xFF)
                    throw new InvalidDataException($"No marker at {at} while looking for FF{marker:X2}.");
                if (file[at + 1] == marker)
                    return at;
                if (JpegSegment.IsStandalone(file[at + 1]))
                {
                    at += 2;
                    continue;
                }

                at += 2 + ((file[at + 2] << 8) | file[at + 3]);
            }

            throw new InvalidDataException($"The file carries no FF{marker:X2} segment.");
        }
    }
}
