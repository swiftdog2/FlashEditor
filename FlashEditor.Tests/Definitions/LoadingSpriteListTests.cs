using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.LoadingSprites;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;
using FlashEditor.IO;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     What the Loading Sprites tab puts on screen for index 32, checked against the cache the tab
    ///     reads.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Nothing in the suite covers WinForms, so what can be tested is the layer beneath the
    ///     controls: the descriptor that enumerates the index, the row that decides which of the two
    ///     formats a group holds, and the pixels the row hands the preview. Those are where every
    ///     defect that matters would live, because the tab's own code from there on is a block copy
    ///     into a bitmap.
    ///     </para>
    ///     <para>
    ///     <b>The colour assertion is the reason this file exists.</b> An index-32 JPEG is
    ///     four-component with no <c>JFIF APP0</c> and no <c>Adobe APP14</c>, so a general-purpose
    ///     decoder falls back to CMYK and renders a recognisable, plausible, wrong picture. A
    ///     screenshot cannot falsify that and neither can a reviewer. What can is an image whose
    ///     chroma planes are flat at the level-shift midpoint: under YCbCr it must come out grey, and
    ///     under CMYK the same samples are magenta and yellow at half strength. That check is applied
    ///     here to the pixels the <i>row</i> carries, not to the codec's, so it fails if the tab is
    ///     ever rewired to something else.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheLoadingSpriteListTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheLoadingSpriteListTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>
        ///     Every row the tab shows, built exactly the way the list panel builds them.
        /// </summary>
        /// <remarks>
        ///     Through <see cref="RSCache.ReadGroup"/> rather than file by file, which is what the
        ///     panel does: <c>ReadFile</c> releases the container per call and would re-inflate each
        ///     group once per file it holds.
        /// </remarks>
        /// <returns>The rows, in enumeration order.</returns>
        private List<LoadingSpriteListing> LoadRows()
        {
            RSCache cache = _fixture.OpenCache();
            var descriptor = new LoadingSpriteListDescriptor();
            var rows = new List<LoadingSpriteListing>();

            foreach (DefinitionAddress address in descriptor.Enumerate(cache))
            {
                IReadOnlyDictionary<int, JagStream> files =
                    cache.ReadGroup(descriptor.IndexId, address.GroupId);
                rows.Add(descriptor.Decode(cache, address, files[address.FileId]));
            }

            return rows;
        }

        /// <summary>
        ///     The descriptor names the index the tab is registered against.
        /// </summary>
        /// <remarks>
        ///     A tab states its own cache index, and the registration and the descriptor are two
        ///     separate statements of it. A descriptor pointed at another index would load someone
        ///     else's rows under this tab's heading, which is the exact failure the positional
        ///     <c>editorTypes</c> array used to produce.
        /// </remarks>
        [Fact]
        public void TheDescriptor_NamesIndex32()
        {
            var descriptor = new LoadingSpriteListDescriptor();

            Assert.Equal(RSConstants.LOADING_SPRITES, descriptor.IndexId);
            Assert.Equal(32, descriptor.IndexId);
        }

        /// <summary>
        ///     The tab shows every declared group, and sorts the mixed index into both of its shapes.
        /// </summary>
        /// <remarks>
        ///     The split is measured rather than written down. It is 21 JPEG images and 5 glyph sheets
        ///     in both caches, which <c>RealCacheProfile</c> records - but the assertion that matters
        ///     is that <b>both</b> branches occur, because a tab that only ever showed one of them
        ///     would look entirely healthy while hiding a fifth of the index or throwing on it.
        /// </remarks>
        [RealCacheFact]
        public void TheTabsRows_CoverEveryDeclaredGroupAndBothShapes()
        {
            List<LoadingSpriteListing> rows = LoadRows();

            int jpegs = rows.Count(row => row.Shape == LoadingSpriteShape.JpegImage);
            int sheets = rows.Count(row => row.Shape == LoadingSpriteShape.SpriteSet);
            _output.WriteLine($"{rows.Count} rows: {jpegs} JPEG images, {sheets} glyph sheets");

            Assert.Equal(_fixture.DeclaredGroups(RSConstants.LOADING_SPRITES), rows.Count);
            Assert.Equal(rows.Count, jpegs + sheets);
            Assert.True(jpegs > 0, "no row was dispatched to the JPEG codec, so that branch was not exercised");
            Assert.True(sheets > 0,
                "no row was dispatched to the sprite codec, so a JPEG-only tab would have passed this");

            //Every row says which shape it is in words, because the column is what tells a user that
            //five of these groups are not the JPEGs the index constant claims.
            Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.ShapeName)));
            Assert.Equal(rows.Count, rows.Select(row => row.GroupId).Distinct().Count());

            _fixture.Profile.AssertCensus(_output, "loadingSprite.jpegGroups", jpegs);
            _fixture.Profile.AssertCensus(_output, "loadingSprite.spriteSetGroups", sheets);
        }

        /// <summary>
        ///     Every row hands the preview a picture whose pixel count matches the geometry it states.
        /// </summary>
        /// <remarks>
        ///     The tab block-copies these pixels into a bitmap row by row, so a plane that is short by
        ///     one row does not throw - it reads past its own buffer or leaves a stripe. Both halves
        ///     are covered: a JPEG must render at the size its frame header declares, and a glyph
        ///     sheet's contact sheet must be a whole number of cells across and down.
        /// </remarks>
        [RealCacheFact]
        public void EveryRow_CarriesAPictureItsGeometryAccountsFor()
        {
            var failures = new List<string>();
            long previewPixels = 0;

            foreach (LoadingSpriteListing row in LoadRows())
            {
                if (!row.Preview.HasImage)
                {
                    failures.Add($"group {row.GroupId}: no picture at all - {row.Preview.Note}");
                    continue;
                }

                previewPixels += row.Preview.Pixels.Length;

                if (row.Preview.Pixels.Length != row.Preview.Width * row.Preview.Height)
                {
                    failures.Add($"group {row.GroupId}: {row.Preview.Pixels.Length} pixels for a " +
                                 $"{row.Preview.Width}x{row.Preview.Height} picture");
                }

                //Opaque throughout. The preview draws onto a flat background rather than onto a
                //checkerboard, so a pixel left transparent shows the control's colour and reads as a
                //hole in the picture.
                if (row.Preview.Pixels.Any(pixel => ((pixel >> 24) & 0xFF) != 0xFF))
                    failures.Add($"group {row.GroupId}: the picture carries transparent pixels");

                if (row.Shape == LoadingSpriteShape.JpegImage)
                {
                    if (row.Preview.Width != row.Width || row.Preview.Height != row.Height)
                    {
                        failures.Add($"group {row.GroupId}: rendered {row.Preview.Width}x{row.Preview.Height} " +
                                     $"from a frame header declaring {row.Width}x{row.Height}");
                    }

                    if (row.Frames != 1)
                        failures.Add($"group {row.GroupId}: a JPEG claiming {row.Frames} frames");
                    continue;
                }

                int columns = Math.Min(LoadingSpriteListing.ContactSheetColumns, row.Frames);
                int sheetRows = (row.Frames + columns - 1) / columns;
                if (row.Preview.Width % columns != 0 || row.Preview.Height % sheetRows != 0)
                {
                    failures.Add($"group {row.GroupId}: a {row.Preview.Width}x{row.Preview.Height} contact sheet " +
                                 $"does not divide into {columns}x{sheetRows} cells");
                }
            }

            _output.WriteLine($"{previewPixels} preview pixels across the index");

            Assert.Empty(failures);
            Assert.True(previewPixels > 0, "nothing was rendered, so nothing was checked");
        }

        /// <summary>
        ///     A row whose chroma is neutral previews grey, so the tab is not reading these files as
        ///     CMYK.
        /// </summary>
        /// <remarks>
        ///     The one check that separates the two readings on real data rather than by argument, and
        ///     the reason it is applied to <see cref="LoadingSpritePreview.Pixels"/> is that those are
        ///     the bytes the tab actually draws. Swapping the row's render for <c>System.Drawing</c>,
        ///     WIC or any library decoder - each of which falls back to CMYK on a four-component file
        ///     with no Adobe marker - produces a tinted picture that looks entirely like a loading
        ///     screen and fails only here.
        /// </remarks>
        [RealCacheFact]
        public void AJpegRowWithNeutralChroma_PreviewsGrey()
        {
            var failures = new List<string>();
            int neutral = 0;

            foreach (LoadingSpriteListing row in LoadRows())
            {
                if (row.Shape != LoadingSpriteShape.JpegImage)
                    continue;

                //Re-derived from the stored bytes rather than taken from the row, so the premise of
                //the test does not come through the code it is testing.
                JpegRaster raster = BaselineJpegDecoder.Decode(JagexJpeg.Decode(row.StoredBytes));
                if (raster.ComponentCount < 3 || !IsFlatAt(raster.Plane(1), 128) || !IsFlatAt(raster.Plane(2), 128))
                    continue;

                neutral++;

                foreach (int pixel in row.Preview.Pixels)
                {
                    int red = (pixel >> 16) & 0xFF;
                    int green = (pixel >> 8) & 0xFF;
                    int blue = pixel & 0xFF;
                    if (red == green && green == blue)
                        continue;

                    failures.Add($"group {row.GroupId}: chroma is neutral over the whole image but the tab draws " +
                                 $"({red},{green},{blue}), so its colour path is not the cache's YCbCr one");
                    break;
                }
            }

            _output.WriteLine($"{neutral} rows carry neutral chroma over their whole area");

            Assert.Empty(failures);
            Assert.True(neutral > 0,
                "no row in this cache carries neutral chroma, so this run did not discriminate between the " +
                "YCbCr and CMYK readings");
        }

        /// <summary>
        ///     A row's stored bytes are the bytes the cache holds for it.
        /// </summary>
        /// <remarks>
        ///     What "export stored bytes" writes and what "replace" compares an incoming file against.
        ///     A row carrying a re-encoded or truncated copy would export a file that is not what the
        ///     client reads, and would let a replace that changes nothing still stage a write - which
        ///     rewrites the archive CRC and the reference-table entry of everything packed beside it.
        /// </remarks>
        [RealCacheFact]
        public void EveryRowsStoredBytes_AreTheBytesTheCacheHolds()
        {
            RSCache cache = _fixture.OpenCache();
            var failures = new List<string>();
            int compared = 0;

            foreach (LoadingSpriteListing row in LoadRows())
            {
                byte[] stored = cache.ReadFileBytes(RSConstants.LOADING_SPRITES, row.Address.GroupId,
                    row.Address.FileId);
                compared++;

                if (!row.StoredBytes.AsSpan().SequenceEqual(stored))
                    failures.Add($"group {row.GroupId}: the row carries {row.StoredLength} bytes, the cache {stored.Length}");
            }

            Assert.Empty(failures);
            Assert.True(compared > 0, "no row was compared");
        }

        /// <summary>
        ///     The descriptor offers no cell edit, and refuses to encode a row.
        /// </summary>
        /// <remarks>
        ///     Not because the format is unknown - both halves re-encode to their stored bytes. There
        ///     is simply nothing in a row a grid cell could write: a JPEG's payload is an
        ///     entropy-coded scan and a glyph sheet's is 256 pixel planes. An editable column here
        ///     would offer an edit with nowhere to go, and <c>DefinitionListPanel</c> gates cell
        ///     editing on both of these agreeing.
        /// </remarks>
        [Fact]
        public void TheDescriptor_IsReadOnlyAndRefusesToEncode()
        {
            var descriptor = new LoadingSpriteListDescriptor();

            Assert.False(descriptor.IsEditable);
            Assert.DoesNotContain(descriptor.Columns, column => column.IsEditable);
            Assert.Throws<NotSupportedException>(() => descriptor.Encode(null!));
        }

        /// <summary>
        ///     A column hands back an empty cell for a null row and throws for a row of the wrong type.
        /// </summary>
        /// <remarks>
        ///     ObjectListView evaluates aspect getters for rows being recycled during a scroll and for
        ///     cells measured before a model is attached, so a null row is a legitimate state and
        ///     throwing there surfaces as an exception while merely scrolling. A row of the
        ///     <b>wrong</b> type still has to throw, because that can only mean the descriptor wired
        ///     its columns to a different row type than it produces.
        /// </remarks>
        [Fact]
        public void EveryColumn_RendersANullRowAndRefusesAForeignOne()
        {
            foreach (DefinitionColumn column in new LoadingSpriteListDescriptor().Columns)
            {
                Assert.Null(column.Read(null!));
                Assert.Throws<ArgumentException>(() => column.Read("not a loading sprite row"));
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
