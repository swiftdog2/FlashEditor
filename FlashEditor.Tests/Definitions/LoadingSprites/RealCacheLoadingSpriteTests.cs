using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Definitions.LoadingSprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions.LoadingSprites
{
    /// <summary>
    ///     Sweeps every group index 32 declares, through whichever of its two codecs the payload
    ///     asks for.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The index is mixed, so the first thing that has to hold is that the dispatcher sends each
    ///     group to the right decoder. After that the two halves are defended differently, because
    ///     only one of them can be. A sprite set re-encodes to its stored bytes and byte identity is
    ///     the whole claim; a JPEG is written back verbatim, so byte identity on it is true by
    ///     construction and proves nothing. What defends the JPEG half instead is that the
    ///     structural parse reassembles into the stored file and that the entropy decode consumes
    ///     the scan to its last byte - the JPEG equivalent of an exact-consumption sweep.
    ///     </para>
    ///     <para>
    ///     The colour model gets its own test rather than being taken on trust, because these files
    ///     are the case where a wrong answer looks right: four components, no <c>JFIF</c> and no
    ///     <c>Adobe</c> marker, so every general-purpose decoder renders them as CMYK and produces a
    ///     recognisable, plausible, wrong picture.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheLoadingSpriteTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheLoadingSpriteTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the reference table declares, read from the table rather than written down.</summary>
        private int DeclaredGroups => _fixture.DeclaredGroups(RSConstants.LOADING_SPRITES);

        /// <summary>
        ///     The index bound to the production codec, across every declared group.
        /// </summary>
        /// <remarks>
        ///     Every group rather than the 250-group sample: the index holds far fewer than the cap,
        ///     so the two runs are the same walk, and only a full walk lets the assertions below be
        ///     statements about the index rather than about a sample of it.
        /// </remarks>
        /// <returns>A sweep over every group.</returns>
        private DefinitionSweep<LoadingSpriteDefinition> Sweep()
        {
            return new DefinitionSweep<LoadingSpriteDefinition>(_fixture, _output, RSConstants.LOADING_SPRITES,
                new DefinitionCodec<LoadingSpriteDefinition>("loading sprite", DecodeGroup,
                    definition => definition.Encode()))
                .AcrossEveryGroup()
                .NotOpcodeTerminated();
        }

        /// <summary>Decodes one group the way the editor would.</summary>
        /// <param name="definitionId">The group id, which is the definition id on this index.</param>
        /// <param name="stream">The stored payload.</param>
        /// <returns>The decoded group.</returns>
        private static LoadingSpriteDefinition DecodeGroup(int definitionId, JagStream stream)
        {
            var definition = new LoadingSpriteDefinition { Id = definitionId };
            definition.Decode(stream);
            return definition;
        }

        /// <summary>
        ///     Index 32 addresses one file per group, so a loading-sprite id is a group id.
        /// </summary>
        /// <remarks>
        ///     The whole read path depends on it. Both of the client's readers reach a group through
        ///     <c>JS5Archive.method2733</c> (<c>JS5Archive.java:591-616</c>), which throws unless the
        ///     group holds exactly one file, so the group payload is the record. A second file would
        ///     put the multi-file size table where the sprite metadata is read from and where a JPEG
        ///     expects its SOI marker.
        /// </remarks>
        [RealCacheFact]
        public void TheLoadingSpriteIndex_HoldsExactlyOneFilePerGroup()
        {
            Assert.Equal(CacheIdShape.GroupPerId, CacheAddressing.For(RSConstants.LOADING_SPRITES).Shape);

            RSReferenceTable table = _fixture.Table(RSConstants.LOADING_SPRITES);
            var wrong = new List<string>();

            foreach (KeyValuePair<int, RSArchiveEntry> group in table.GetArchiveEntries())
            {
                int[] files = group.Value.GetValidFileIds();
                if (files.Length != 1 || files[0] != 0)
                    wrong.Add($"group {group.Key} declares files [{string.Join(" ", files)}]");
            }

            Assert.Empty(wrong);
            Assert.Equal(DeclaredGroups, _fixture.DeclaredFiles(RSConstants.LOADING_SPRITES));
            Assert.True(DeclaredGroups > 0, "index 32 declares no groups, so nothing below checked anything");
        }

        /// <summary>Every group re-encodes to the bytes it was decoded from.</summary>
        /// <remarks>
        ///     Sharp on the five sprite sets, where the encoder replays a stored form that has more
        ///     than one spelling per picture. Trivially true on the twenty-one JPEGs, whose encoder
        ///     returns the stored bytes - which is the point, since a JPEG re-encode is no more
        ///     reproducible than a GZip one and rewriting one would change the archive CRC and the
        ///     reference-table entry of everything packed beside it.
        /// </remarks>
        [RealCacheFact]
        public void EveryLoadingSpriteGroup_ReEncodesToTheCapturedBytes()
        {
            DefinitionSweepResult swept = Sweep().AssertReEncodesToCapturedBytes();

            Assert.Equal(DeclaredGroups, swept.Records);
            Assert.Equal(DeclaredGroups, swept.Groups);
            Assert.Equal(DeclaredGroups, swept.Passed);
        }

        /// <summary>
        ///     The index really is mixed, and the dispatcher sorts it by the payload's own magic.
        /// </summary>
        /// <remarks>
        ///     <c>RSConstants.LOADING_SPRITES</c> is commented "in jpg format" and part of the index
        ///     is not JPEG at all. A reader that trusted the name would throw on the glyph sheets;
        ///     one that trusted the index id in the other direction would hand JPEG bytes to a
        ///     decoder that reads a frame count out of their last two bytes and produce nonsense
        ///     rather than fail. Both shapes are required to occur, so neither branch can quietly
        ///     stop being exercised.
        /// </remarks>
        [RealCacheFact]
        public void TheLoadingSpriteIndex_HoldsBothShapesAndSortsThemByMagic()
        {
            int jpegs = 0;
            int spriteSets = 0;
            int glyphFrames = 0;
            long jpegPixels = 0;
            var failures = new List<string>();

            DefinitionSweepResult swept = Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape == LoadingSpriteShape.JpegImage)
                {
                    jpegs++;
                    jpegPixels += (long) definition.Jpeg.Width * definition.Jpeg.Height;
                    Assert.Null(definition.SpriteSet);

                    if (!definition.Jpeg.IsBaseline)
                        failures.Add($"group {record.Id}: frame header FF{definition.Jpeg.FrameMarker:X2} is not baseline");
                    if (!record.Bytes.AsSpan(record.Bytes.Length - 2).SequenceEqual(new byte[] { 0xFF, 0xD9 }))
                        failures.Add($"group {record.Id}: a JPEG that does not end on the EOI marker");
                    return;
                }

                spriteSets++;
                Assert.Null(definition.Jpeg);
                glyphFrames += definition.SpriteSet.GetFrameCount();
                definition.Dispose();
            });

            _output.WriteLine($"{jpegs} JPEG groups covering {jpegPixels} pixels, {spriteSets} sprite sets " +
                              $"holding {glyphFrames} frames");

            Assert.Empty(failures);
            Assert.Equal(DeclaredGroups, swept.Records);
            Assert.Equal(DeclaredGroups, jpegs + spriteSets);
            Assert.True(jpegs > 0, "no group was dispatched to the JPEG codec, so that branch was not exercised");
            Assert.True(spriteSets > 0, "no group was dispatched to the sprite codec, so a JPEG-only reader " +
                                        "would have passed this");

            _fixture.Profile.AssertCensus(_output, "loadingSprite.jpegGroups", jpegs);
            _fixture.Profile.AssertCensus(_output, "loadingSprite.spriteSetGroups", spriteSets);
            _fixture.Profile.AssertCensus(_output, "loadingSprite.jpegPixels", jpegPixels);
            _fixture.Profile.AssertCensus(_output, "loadingSprite.glyphFrames", glyphFrames);
        }

        /// <summary>
        ///     Every JPEG reassembles from its parsed parts into the file it came from.
        /// </summary>
        /// <remarks>
        ///     The JPEG half's save path returns the stored bytes, so byte identity cannot say
        ///     whether the file was understood. This can: a segment sized wrongly, a scan boundary
        ///     found in the wrong place or a dropped trailer are each a byte difference here and
        ///     invisible everywhere else.
        /// </remarks>
        [RealCacheFact]
        public void EveryJpeg_ReassemblesFromItsParsedSegments()
        {
            var failures = new List<string>();
            int checkedImages = 0;
            long segments = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.JpegImage)
                    return;

                checkedImages++;
                segments += definition.Jpeg.Segments.Count;

                byte[] rebuilt = definition.Jpeg.ToBytes();
                if (!rebuilt.AsSpan().SequenceEqual(record.Bytes))
                {
                    failures.Add($"group {record.Id}: reassembled {rebuilt.Length} bytes from a stored " +
                                 $"{record.Bytes.Length}");
                }
            });

            _output.WriteLine($"{checkedImages} JPEG images reassembled from {segments} marker segments");

            Assert.Empty(failures);
            Assert.True(checkedImages > 0, "no JPEG was parsed, so nothing was checked");
        }

        /// <summary>
        ///     Every JPEG's entropy decode consumes its scan to the last byte, and its fourth
        ///     component carries nothing.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Nothing in a JPEG states how long the entropy-coded data is - it runs until a marker
        ///     - so a decoder using the wrong Huffman table, the wrong MCU geometry or the wrong
        ///     sampling factors desynchronises and stops somewhere else. Landing on the last byte
        ///     across every image is what says the blocks were read the way they were written.
        ///     </para>
        ///     <para>
        ///     The fourth component is measured rather than assumed. Discarding it is only justified
        ///     while it is a flat filler plane; one that varied would be information, and dropping it
        ///     would be a silent edit. <c>LoadingSpriteCodecTests</c> pins the refusal for the case
        ///     no cache reaches.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryJpeg_ConsumesItsScanExactlyAndCarriesAFlatFourthComponent()
        {
            var failures = new List<string>();
            var fourthComponentValues = new SortedDictionary<int, int>();
            int decoded = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.JpegImage)
                    return;

                JpegRaster raster;
                try
                {
                    raster = BaselineJpegDecoder.Decode(definition.Jpeg);
                }
                catch (Exception ex)
                {
                    failures.Add($"group {record.Id}: decoding threw {ex.GetType().Name}: {ex.Message}");
                    return;
                }

                decoded++;

                if (raster.ScanBytesConsumed != raster.ScanBytesAvailable)
                {
                    failures.Add($"group {record.Id}: the entropy decode read {raster.ScanBytesConsumed} of the " +
                                 $"scan's {raster.ScanBytesAvailable} bytes");
                }

                if (raster.Width != definition.Jpeg.Width || raster.Height != definition.Jpeg.Height)
                {
                    failures.Add($"group {record.Id}: decoded {raster.Width}x{raster.Height} from a frame header " +
                                 $"declaring {definition.Jpeg.Width}x{definition.Jpeg.Height}");
                }

                for (int component = 0; component < raster.ComponentCount; component++)
                {
                    if (raster.Plane(component).Length != raster.Width * raster.Height)
                    {
                        failures.Add($"group {record.Id}: component {component} came out " +
                                     $"{raster.Plane(component).Length} samples for a {raster.Width}x{raster.Height} " +
                                     "image");
                    }
                }

                if (raster.ComponentCount != 4)
                {
                    failures.Add($"group {record.Id}: {raster.ComponentCount} components, not the four every " +
                                 "index-32 image and the client's own probe carry");
                    return;
                }

                if (!raster.IsConstant(3))
                {
                    failures.Add($"group {record.Id}: the fourth component varies, so discarding it is no longer " +
                                 "justified");
                    return;
                }

                int value = raster.Plane(3)[0];
                fourthComponentValues.TryGetValue(value, out int seen);
                fourthComponentValues[value] = seen + 1;
            });

            _output.WriteLine("fourth-component constant values: " +
                              string.Join(", ", fourthComponentValues.Select(v => $"{v.Key}={v.Value}")));

            Assert.Empty(failures);
            Assert.True(decoded > 0, "no JPEG was decoded, so nothing was checked");
            Assert.Equal(decoded, fourthComponentValues.Values.Sum());
        }

        /// <summary>
        ///     Every JPEG carries the same quantisation and Huffman tables the client ships inside
        ///     itself.
        /// </summary>
        /// <remarks>
        ///     This is the join that ties the format to the client rather than to an inference about
        ///     it. <c>Class116.method2162</c> (<c>Class116.java:60-77</c>) gunzips
        ///     <c>Class74.aByteArray546</c> and pushes it through the AWT image decoder to decide
        ///     whether to open index 32 or fall back to index 34
        ///     (<c>InterfaceSettings.java:72-74</c>) - so the client is stating what an index-32
        ///     image looks like, and the tables agreeing byte for byte is what says the two came out
        ///     of the same encoder.
        ///     <para>
        ///     It matters because those tables are the evidence for the colour model. The luminance
        ///     table on components 1 and 4 and the chrominance table on 2 and 3 alone is what makes
        ///     the middle two Cb and Cr rather than two inks of a CMYK image.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryJpeg_CarriesTheClientsOwnTables()
        {
            JagexJpeg probe = JagexJpeg.Decode(LoadingSpriteCodecTests.ClientProbeImage());
            var failures = new List<string>();
            int compared = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.JpegImage)
                    return;

                compared++;
                JagexJpeg jpeg = definition.Jpeg;

                foreach (KeyValuePair<int, int[]> table in probe.QuantisationTables)
                {
                    if (!jpeg.QuantisationTables.TryGetValue(table.Key, out int[] mine)
                        || !mine.AsSpan().SequenceEqual(table.Value))
                    {
                        failures.Add($"group {record.Id}: quantisation table {table.Key} differs from the " +
                                     "client's probe");
                    }
                }

                foreach (KeyValuePair<int, JpegHuffmanTable> table in probe.DcTables)
                    CompareHuffman(failures, record.Id, "DC", table.Key, table.Value, jpeg.DcTables);
                foreach (KeyValuePair<int, JpegHuffmanTable> table in probe.AcTables)
                    CompareHuffman(failures, record.Id, "AC", table.Key, table.Value, jpeg.AcTables);

                //Same component layout too: the sampling factors are half of why the middle two
                //components read as chroma.
                if (!jpeg.Components.Select(c => (c.Id, c.HorizontalSampling, c.VerticalSampling,
                        c.QuantisationTableId))
                    .SequenceEqual(probe.Components.Select(c => (c.Id, c.HorizontalSampling, c.VerticalSampling,
                        c.QuantisationTableId))))
                {
                    failures.Add($"group {record.Id}: a component layout the client's probe does not share");
                }
            });

            Assert.Empty(failures);
            Assert.True(compared > 0, "no JPEG was compared against the client's probe");
        }

        /// <summary>
        ///     The replacement policy accepts every image already in this cache.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     The half of <see cref="LoadingSpriteJpegPolicy"/> that no synthetic test can make. Its
        ///     clauses were measured off these files, so a clause that drifted into rejecting one of
        ///     them would be a check that refuses the cache's own contents - which is the failure mode
        ///     a shape check invites, and the one nobody notices until a replace is attempted.
        ///     <c>LoadingSpriteJpegPolicyTests</c> makes the other half, over files that are not here.
        ///     </para>
        ///     <para>
        ///     The census is printed rather than written down. Measured across both caches when the
        ///     policy was written, the twenty-one images agreed field for field on everything except
        ///     geometry: marker sequence <c>D8 DB DB C0 C4 C4 C4 C4 DA</c> ending <c>FF D9</c>,
        ///     eight-bit, no <c>APPn</c>, no <c>DRI</c>, one scan, components 1/2x2/q0, 2/1x1/q1,
        ///     3/1x1/q1, 4/2x2/q0. That agreement is asserted below rather than quoted, so a cache
        ///     that disagreed would fail here rather than silently widen the claim.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryJpeg_IsAcceptedByTheReplacementPolicy()
        {
            var failures = new List<string>();
            var shapes = new SortedDictionary<string, int>();
            int accepted = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.JpegImage)
                    return;

                if (!LoadingSpriteJpegPolicy.TryAccept(definition.StoredBytes, out JagexJpeg parsed,
                        out string refusal))
                {
                    failures.Add($"group {record.Id}: the policy refuses an image this cache ships - {refusal}");
                    return;
                }

                accepted++;
                Assert.NotNull(parsed);

                string markers = string.Join(" ", parsed.Segments.Select(segment => segment.Marker.ToString("X2")));
                string trailer = string.Concat(parsed.Trailer.Select(b => b.ToString("X2")));
                shapes.TryGetValue($"{markers} | {trailer} | p{parsed.Precision}", out int seen);
                shapes[$"{markers} | {trailer} | p{parsed.Precision}"] = seen + 1;
            });

            foreach (KeyValuePair<string, int> shape in shapes)
                _output.WriteLine($"{shape.Value} image(s): markers {shape.Key}");

            Assert.Empty(failures);
            Assert.True(accepted > 0, "no JPEG was put to the policy, so nothing was checked");
            Assert.Single(shapes);
            _fixture.Profile.AssertCensus(_output, "loadingSprite.jpegGroups", accepted);
        }

        /// <summary>
        ///     An image whose chroma sits at the level-shift midpoint renders neutral grey.
        /// </summary>
        /// <remarks>
        ///     The check that separates the two readings on real data rather than by argument. Some
        ///     of the small interface pieces in this index carry Cb and Cr at exactly 128 over the
        ///     whole image, which under YCbCr has to come out with red, green and blue equal
        ///     everywhere. Under the CMYK reading every standard decoder falls back to, the same
        ///     samples are magenta and yellow at half strength and the picture is tinted - which is
        ///     the plausible, wrong image this test exists to fail on.
        /// </remarks>
        [RealCacheFact]
        public void AnImageWithNeutralChroma_RendersGrey()
        {
            var failures = new List<string>();
            int neutralImages = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.JpegImage)
                    return;

                JpegRaster raster = BaselineJpegDecoder.Decode(definition.Jpeg);
                if (raster.ComponentCount < 3 || !IsFlatAt(raster.Plane(1), 128) || !IsFlatAt(raster.Plane(2), 128))
                    return;

                neutralImages++;
                int[] pixels = raster.ToArgb();
                for (int i = 0; i < pixels.Length; i++)
                {
                    int red = (pixels[i] >> 16) & 0xFF;
                    int green = (pixels[i] >> 8) & 0xFF;
                    int blue = pixels[i] & 0xFF;
                    if (red == green && green == blue)
                        continue;

                    failures.Add($"group {record.Id} pixel {i}: chroma is neutral but the pixel came out " +
                                 $"({red},{green},{blue}), so the colour model is not YCbCr");
                    break;
                }
            });

            _output.WriteLine($"{neutralImages} images carry neutral chroma over their whole area");

            Assert.Empty(failures);
            Assert.True(neutralImages > 0,
                "no image in this cache carries neutral chroma, so this run did not discriminate between the " +
                "YCbCr and CMYK readings; LoadingSpriteCodecTests still does it on the client's own probe");
        }

        /// <summary>
        ///     The names the client asks for by name land on groups this index declares.
        /// </summary>
        /// <remarks>
        ///     <c>Class84.java:20-31</c> resolves <c>p11_full</c>, <c>p12_full</c> and
        ///     <c>b12_full</c> against this archive, so each has to hash to a group that exists -
        ///     and each of them is a glyph sheet rather than a JPEG, which is the other half of why
        ///     the index cannot be read as JPEG-only. Nothing is asserted about the twenty-one image
        ///     groups' names: their identifiers are non-zero and no wordlist has matched one, and a
        ///     plausible invented name is the easiest thing in this cache to confirm by accident.
        /// </remarks>
        [RealCacheFact]
        public void TheClientsNamedGroups_ExistAndAreGlyphSheets()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable table = _fixture.Table(RSConstants.LOADING_SPRITES);
            int named = 0;

            foreach (string name in new[] { "p11_full", "p12_full", "b12_full" })
            {
                int hash = FlashEditor.Cache.Util.NameHasher.GetNameHash(name);
                KeyValuePair<int, RSArchiveEntry> match = table.GetArchiveEntries()
                    .FirstOrDefault(entry => entry.Value.GetIdentifier() == hash);

                Assert.True(match.Value != null, $"index 32 declares no group whose identifier is hash(\"{name}\")");
                Assert.Equal(name, LoadingSpriteNames.NameOf(cache, match.Key));

                var definition = new LoadingSpriteDefinition { Id = match.Key };
                definition.Decode(new JagStream(cache.ReadFileBytes(RSConstants.LOADING_SPRITES, match.Key, 0)));
                Assert.Equal(LoadingSpriteShape.SpriteSet, definition.Shape);
                definition.Dispose();
                named++;
            }

            //Every recovered name has to belong to a group that exists, or the candidate list has
            //drifted from what this index holds.
            foreach (string candidate in LoadingSpriteNames.KnownNames)
            {
                int hash = FlashEditor.Cache.Util.NameHasher.GetNameHash(candidate);
                Assert.Contains(table.GetArchiveEntries(), entry => entry.Value.GetIdentifier() == hash);
            }

            Assert.Equal(3, named);
        }

        /// <summary>
        ///     Every glyph sheet holds the 256-frame, two-colour shape a font sheet has.
        /// </summary>
        /// <remarks>
        ///     Not a check on the sprite codec, which <c>RealCacheSpriteTests</c> owns over index 8.
        ///     It is a check that the five groups the dispatcher sends down the sprite path really
        ///     are glyph sheets - one frame per byte value, drawn from a palette of one colour plus
        ///     the transparent index - rather than something else that happens not to start
        ///     <c>FF D8</c>.
        /// </remarks>
        [RealCacheFact]
        public void EveryGlyphSheet_HasOneFramePerByteValue()
        {
            var failures = new List<string>();
            int sheets = 0;

            Sweep().ForEachDecoded((record, definition) =>
            {
                if (definition.Shape != LoadingSpriteShape.SpriteSet)
                    return;

                sheets++;
                SpriteDefinition set = definition.SpriteSet;

                if (set.GetFrameCount() != 256)
                    failures.Add($"group {record.Id}: {set.GetFrameCount()} frames, not one per byte value");
                if (set.PaletteStored.Length != 2)
                    failures.Add($"group {record.Id}: a palette of {set.PaletteStored.Length} entries");
                if (set.width <= 0 || set.height <= 0)
                    failures.Add($"group {record.Id}: a {set.width}x{set.height} canvas");

                definition.Dispose();
            });

            Assert.Empty(failures);
            Assert.True(sheets > 0, "no glyph sheet was examined");
        }

        private static void CompareHuffman(List<string> failures, int groupId, string kind, int id,
            JpegHuffmanTable expected, IReadOnlyDictionary<int, JpegHuffmanTable> actual)
        {
            if (!actual.TryGetValue(id, out JpegHuffmanTable mine))
            {
                failures.Add($"group {groupId}: {kind} Huffman table {id} is absent");
                return;
            }

            if (!mine.Counts.AsSpan().SequenceEqual(expected.Counts)
                || !mine.Symbols.AsSpan().SequenceEqual(expected.Symbols))
            {
                failures.Add($"group {groupId}: {kind} Huffman table {id} differs from the client's probe");
            }
        }

        private static bool IsFlatAt(byte[] plane, byte value)
        {
            foreach (byte sample in plane)
                if (sample != value)
                    return false;
            return plane.Length > 0;
        }
    }
}
