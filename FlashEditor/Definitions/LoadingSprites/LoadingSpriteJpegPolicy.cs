using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     What an index-32 JPEG has to look like before this editor will store it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Nothing else in the codebase enforces a shape, and the shape is the whole safety
    ///     margin.</b> These files are four-component with no <c>JFIF APP0</c> and no
    ///     <c>Adobe APP14</c>, which is a combination every general-purpose decoder resolves as
    ///     CMYK; the reading this editor draws with - luma, Cb, Cr and a discarded fourth plane - is
    ///     inferred from the files' own quantisation tables and sampling factors rather than stated
    ///     anywhere. That inference is what makes the preview trustworthy, and it only holds for
    ///     files built the way these were. <see cref="JpegRaster.ToArgb"/> does not defend it: it
    ///     reads planes 0, 1 and 2 as Y, Cb and Cr for a three-component file exactly as it does for
    ///     a four-component one, so an ordinary three-component JFIF - which is what every ordinary
    ///     tool emits - previews as a perfectly good picture here and would be stored with nothing
    ///     said.
    ///     </para>
    ///     <para>
    ///     <b>What the client's AWT path does was measured, not assumed.</b> This remark used to end
    ///     "untested and untestable from this side", which was wrong on both counts: the question is
    ///     "what does a JVM do with these bytes", and a JVM can simply be asked. Replaying
    ///     <c>Class271.method3277</c> - <c>Toolkit.createImage</c>, <c>MediaTracker.waitForAll</c>,
    ///     <c>PixelGrabber</c> - over every shipped index-32 payload on JDK 8 reproduces
    ///     <see cref="JpegRaster.ToArgb"/> to within three levels on any channel, so the inference
    ///     above is the client's reading and not merely a defensible one. An ordinary three-component
    ///     JFIF decodes on that path too. It is still refused below, because the rule is the shape
    ///     the client demonstrated rather than the set of files a JVM happens to accept, but the
    ///     refusal rests on that choice and no longer on an untested worry.
    ///     </para>
    ///     <para>
    ///     <b>The rule is "the shape the client demonstrated it can decode", not "a JPEG".</b> The
    ///     client ships one image inside itself for exactly this purpose:
    ///     <c>Class74.aByteArray546</c>, gunzipped and pushed through the AWT decoder by
    ///     <c>Class116.method2162</c> (<c>Class116.java:60-77</c>) to decide whether to open index 32
    ///     at all or fall back to index 34 (<c>InterfaceSettings.java:72-74</c>). That probe is the
    ///     client's own statement of what an index-32 image looks like, and every one of the
    ///     twenty-one images in each cache matches it field for field. So the accepted shape is
    ///     copied from the probe and the sweep in <c>RealCacheLoadingSpriteTests</c> holds the cache
    ///     against it.
    ///     </para>
    ///     <para>
    ///     <b>Measured before it was written, over both caches.</b> All twenty-one images in the
    ///     vanilla b639 capture and all twenty-one in the repack carry the identical marker sequence
    ///     <c>SOI, DQT, DQT, SOF0, DHT, DHT, DHT, DHT, SOS</c> ending <c>FF D9</c>, eight-bit
    ///     precision, no <c>APPn</c> segment of any kind, no <c>DRI</c>, one scan, and components
    ///     1/2x2/q0, 2/1x1/q1, 3/1x1/q1, 4/2x2/q0 selected in that order against DC and AC tables
    ///     0/0, 1/1, 1/1, 0/0. They differ only in geometry, from 5x18 to 384x254. Every clause
    ///     below is one of those measurements, so the check cannot reject the cache's own contents.
    ///     </para>
    ///     <para>
    ///     <b>What is deliberately not pinned.</b> The quantisation and Huffman tables themselves.
    ///     All twenty-one carry the client's, at IJG quality 75, but nothing about the colour
    ///     reading depends on the values - only on <i>which</i> table each component uses, which the
    ///     component clause covers. Pinning the values would fix a replacement's quality at 75 for
    ///     no gain.
    ///     </para>
    /// </remarks>
    public static class LoadingSpriteJpegPolicy {
        /// <summary>
        ///     The frame components an acceptable file declares, in order.
        /// </summary>
        /// <remarks>
        ///     The chroma reading rests on this and on nothing else. Components 2 and 3 are the only
        ///     two subsampled and the only two on quantisation table 1, which in these files is the
        ///     Annex K <i>chrominance</i> table; components 1 and 4 are full resolution on the
        ///     luminance table. An encoder that halved exactly two of four inks of a genuine CMYK
        ///     image, and gave those two the chrominance table, would be doing something with no
        ///     explanation.
        /// </remarks>
        public static readonly IReadOnlyList<JpegComponent> AcceptedComponents = new[] {
            new JpegComponent(1, 2, 2, 0),
            new JpegComponent(2, 1, 1, 1),
            new JpegComponent(3, 1, 1, 1),
            new JpegComponent(4, 2, 2, 0)
        };

        /// <summary>
        ///     The scan's component selectors an acceptable file declares, in order.
        /// </summary>
        /// <remarks>
        ///     One interleaved scan covering all four components. A file that split the components
        ///     across several scans would decode through a path no shipped image exercises, and the
        ///     preview would be the first thing ever to test it.
        /// </remarks>
        public static readonly IReadOnlyList<JpegScanComponent> AcceptedScanComponents = new[] {
            new JpegScanComponent(1, 0, 0),
            new JpegScanComponent(2, 1, 1),
            new JpegScanComponent(3, 1, 1),
            new JpegScanComponent(4, 0, 0)
        };

        /// <summary>
        ///     The accepted shape in one sentence, for a user interface to state.
        /// </summary>
        /// <remarks>
        ///     Exported rather than retyped into the tab so that the limit the surface states and the
        ///     limit the code enforces cannot drift apart. A refusal a user was never warned about
        ///     reads as the editor being broken.
        /// </remarks>
        public const string AcceptedShapeInWords =
            "baseline (SOF0) JPEG, 8-bit, four components sampled 2x2, 1x1, 1x1, 2x2 on quantisation " +
            "tables 0, 1, 1, 0, one interleaved scan, no JFIF APP0 or Adobe APP14 marker, no restart " +
            "interval, ending FF D9";

        /// <summary>
        ///     Decides whether a file may be stored as an index-32 image.
        /// </summary>
        /// <remarks>
        ///     Every refusal names what the file declares and why that is not storable, because the
        ///     alternative to a clear refusal here is not a helpful error later - it is an image
        ///     which looks right in this editor and is wrong in game, with nothing on either side
        ///     saying so.
        /// </remarks>
        /// <param name="bytes">The candidate file, exactly as it will be stored.</param>
        /// <param name="jpeg">The parsed file when accepted, otherwise <c>null</c>.</param>
        /// <param name="refusal">Why the file was refused, or empty when it was accepted.</param>
        /// <returns>Whether the file may be stored.</returns>
        public static bool TryAccept(byte[]? bytes, out JagexJpeg? jpeg, out string refusal) {
            jpeg = null;
            refusal = string.Empty;

            if (!LoadingSpriteDefinition.LooksLikeJpeg(bytes)) {
                refusal = "that file does not open with the JPEG SOI marker FF D8, so it is not a JPEG at all " +
                          "and the client's reader for this group would not treat it as an image.";
                return false;
            }

            JagexJpeg parsed;
            try {
                parsed = JagexJpeg.Decode(bytes!);
            }
            catch (InvalidDataException ex) {
                refusal = "that file does not parse as a JPEG whose every byte can be accounted for: " +
                          ex.Message;
                return false;
            }

            refusal = RefuseStructure(parsed) ?? RefusePixels(parsed) ?? string.Empty;
            if (refusal.Length > 0)
                return false;

            jpeg = parsed;
            return true;
        }

        /// <summary>
        ///     Checks what the file's headers declare about itself.
        /// </summary>
        /// <param name="jpeg">The parsed file.</param>
        /// <returns>The refusal, or <c>null</c> when the declared shape is acceptable.</returns>
        private static string? RefuseStructure(JagexJpeg jpeg) {
            if (!jpeg.IsBaseline) {
                return $"that file's frame header is FF{jpeg.FrameMarker:X2}, not the baseline sequential FF C0 " +
                       "every index-32 image and the client's own probe image carry. A progressive or " +
                       "arithmetic-coded file has a different scan structure, and this editor could not show " +
                       "you what it holds before storing it.";
            }

            if (jpeg.Precision != 8) {
                return $"that file declares {jpeg.Precision}-bit samples. Every index-32 image is 8-bit, and " +
                       "nothing in this cache or in the client says what the client's decoder does with " +
                       "anything else.";
            }

            string? colourMarker = RefuseColourSpaceMarker(jpeg);
            if (colourMarker != null)
                return colourMarker;

            if (jpeg.RestartInterval != 0) {
                return $"that file declares a restart interval of {jpeg.RestartInterval}. No index-32 image " +
                       "carries a DRI segment, so no shipped byte exercises the restart path in this " +
                       "editor's reader and the picture above would be the first thing ever to test it.";
            }

            if (!jpeg.Components.SequenceEqual(AcceptedComponents)) {
                return $"that file declares {Describe(jpeg.Components)}, not the {Describe(AcceptedComponents)} " +
                       "every index-32 image carries. That layout is not a detail: the colour this editor " +
                       "draws is inferred from it, because components 2 and 3 are the only two subsampled and " +
                       "the only two on the chrominance quantisation table, which is what makes them Cb and " +
                       "Cr rather than two inks of a CMYK image. A file laid out differently would be drawn " +
                       "here by a reading nothing supports and drawn in game by whatever the client's JVM " +
                       "decides on its own.";
            }

            if (!jpeg.ScanComponents.SequenceEqual(AcceptedScanComponents)) {
                return "that file's scan does not select all four components in one interleaved pass the way " +
                       "every index-32 image does, so it decodes through a path no shipped byte exercises.";
            }

            int scans = jpeg.Segments.Count(segment => segment.Marker == JagexJpeg.MarkerSos);
            if (scans != 1)
                return $"that file carries {scans} scans. Every index-32 image carries exactly one.";

            if (jpeg.Trailer.Length != 2 || jpeg.Trailer[0] != 0xFF || jpeg.Trailer[1] != JagexJpeg.MarkerEoi) {
                return $"that file does not end on the EOI marker FF D9 - it carries {jpeg.Trailer.Length} " +
                       "byte(s) after its scan. Trailing bytes would be stored verbatim into the cache and " +
                       "there is no evidence what the client's decoder does with them.";
            }

            return null;
        }

        /// <summary>
        ///     Refuses any marker that states a colour model, which is the failure this whole check exists for.
        /// </summary>
        /// <remarks>
        ///     A <c>JFIF APP0</c> or an <c>Adobe APP14</c> is a decoder's instruction about how to read the
        ///     components, and it overrides the inference this editor draws with. The two disagree by
        ///     construction: an Adobe transform of 0 means straight CMYK and 2 means YCCK, and neither is
        ///     the "three planes as YCbCr, discard the fourth" this editor applies. Every other
        ///     <c>APPn</c> is refused with them rather than allowed through, because no index-32 image
        ///     carries an application segment of any kind and letting an unknown one past would be
        ///     guessing that it says nothing.
        /// </remarks>
        /// <param name="jpeg">The parsed file.</param>
        /// <returns>The refusal, or <c>null</c> when the file carries no application segment.</returns>
        private static string? RefuseColourSpaceMarker(JagexJpeg jpeg) {
            foreach (JpegSegment segment in jpeg.Segments) {
                if (segment.Marker < 0xE0 || segment.Marker > 0xEF)
                    continue;

                string named = segment.Marker switch {
                    0xE0 => "a JFIF APP0 header, which declares an ordinary JFIF image",
                    0xEE => "an Adobe APP14 marker, which declares a CMYK or YCCK colour transform",
                    _ => $"an APP{segment.Marker - 0xE0} application segment"
                };

                return $"that file carries {named}. No index-32 image carries an application segment of any " +
                       "kind, and the absence of one is load-bearing: the colour this editor draws is " +
                       "inferred from the component layout precisely because nothing in the file states a " +
                       "colour model. A file that does state one would be read by the client's decoder the " +
                       "way the marker says and by this editor the way the inference says, and the two do " +
                       "not agree.";
            }

            return null;
        }

        /// <summary>
        ///     Checks that the file actually decodes into something this editor can show.
        /// </summary>
        /// <remarks>
        ///     Exact scan consumption is the sharp instrument. Nothing in a JPEG states how long the
        ///     entropy-coded data is - it runs until a marker - so a decoder that stops anywhere but on
        ///     the last byte read the blocks differently to the way they were written, and a picture it
        ///     produced anyway would be a guess.
        /// </remarks>
        /// <param name="jpeg">The parsed file.</param>
        /// <returns>The refusal, or <c>null</c> when the file renders.</returns>
        private static string? RefusePixels(JagexJpeg jpeg) {
            JpegRaster raster;
            try {
                raster = BaselineJpegDecoder.Decode(jpeg);
            }
            catch (InvalidDataException ex) {
                return "that file's scan does not decode: " + ex.Message;
            }
            catch (EndOfStreamException ex) {
                return "that file's scan ends before the last block is decoded: " + ex.Message;
            }

            if (raster.ScanBytesConsumed != raster.ScanBytesAvailable) {
                return $"that file's entropy decode read {raster.ScanBytesConsumed} of its scan's " +
                       $"{raster.ScanBytesAvailable} bytes, so it desynchronised. Every index-32 image is " +
                       "consumed to its last byte, and a picture decoded from a scan that was not is a guess " +
                       "rather than a reading.";
            }

            try {
                raster.ToArgb();
            }
            catch (InvalidDataException ex) {
                return "that file decodes but cannot be coloured: " + ex.Message;
            }

            return null;
        }

        private static string Describe(IReadOnlyList<JpegComponent> components) {
            return components.Count.ToString(CultureInfo.InvariantCulture) + " component(s) " +
                   string.Join(", ", components.Select(component =>
                       component.Id.ToString(CultureInfo.InvariantCulture) + " sampled " +
                       component.HorizontalSampling + "x" + component.VerticalSampling + " on table " +
                       component.QuantisationTableId));
        }
    }
}
