using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Tests.Cache.RealCache;
using Xunit;
using Xunit.Abstractions;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Proves the index-13 to index-8 font join per row, by falsification rather than by coverage.
    /// </summary>
    /// <remarks>
    ///     <b>Why this file is not just a coverage count.</b> <c>CLAUDE.md</c> records a join in this
    ///     project that agreed on every aggregate and was wrong: the track-name join keyed an index-17
    ///     enum by index-6 group id, 958 of 970 keys landed on a real group and 958 of 963 groups got
    ///     a name, and the key was the music player's list position. So a font join that only reported
    ///     "all 25 font ids exist in index 8" would be exactly that mistake again.
    ///     <para>
    ///     What is done instead: every one of the fonts is paired against every one of the sheets, and
    ///     the pairings that survive <see cref="FontGlyphSheet.Verify"/> are counted. The claim the
    ///     suite makes is that <b>every correct pairing passes and no incorrect one does</b> - which
    ///     is a statement no aggregate can be satisfied by, and which fails the moment a relation is
    ///     weakened. Each relation is also measured on its own so a future reader can see which of
    ///     them is carrying the discrimination.
    ///     </para>
    ///     <para>
    ///     Nothing here writes down how many fonts there are. The population comes from the index-13
    ///     reference table, and the cross-pairing denominator is derived from it, so the assertions
    ///     hold in either supported cache.
    ///     </para>
    /// </remarks>
    [Collection("RealCache")]
    public sealed class RealCacheFontGlyphSheetTests : IClassFixture<RealCacheFixture>
    {
        private readonly RealCacheFixture _fixture;
        private readonly ITestOutputHelper _output;

        /// <summary>Binds the shared open cache and the per-test output sink.</summary>
        public RealCacheFontGlyphSheetTests(RealCacheFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        /// <summary>Groups the index-13 reference table declares.</summary>
        private int GroupsDeclared => _fixture.DeclaredGroups(RSConstants.FONTS_INDEX);

        /// <summary>
        ///     Every declared font joined to the index-8 group at its own id.
        /// </summary>
        /// <remarks>
        ///     Loaded once per test rather than shared through a field: the sheets hold rasterisable
        ///     sprite sets, and a fixture-scoped cache of them would outlive the test that built it.
        /// </remarks>
        /// <returns>The joined fonts, by id.</returns>
        private SortedDictionary<int, FontGlyphSheet> JoinEveryFont()
        {
            RSCache cache = _fixture.OpenCache();
            RSReferenceTable fonts = _fixture.Table(RSConstants.FONTS_INDEX);
            var joined = new SortedDictionary<int, FontGlyphSheet>();

            foreach (int fontId in fonts.GetArchiveEntries().Keys)
            {
                FontGlyphSheet sheet = FontGlyphSheet.Load(cache, fontId);
                Assert.True(sheet != null, $"index 8 declares no usable group at font id {fontId}");
                joined[fontId] = sheet;
            }

            Assert.Equal(GroupsDeclared, joined.Count);
            return joined;
        }

        /// <summary>
        ///     Every font joins to the sheet at its own id, and to no other sheet in the index.
        /// </summary>
        /// <remarks>
        ///     <b>The load-bearing test of the whole join.</b> The identity half alone would be the
        ///     coverage claim that has already misled this project once; the cross half is what makes
        ///     it a proof. Measured in both supported caches: 25 of 25 identity pairings pass and 0 of
        ///     600 cross pairings do.
        ///     <para>
        ///     The cross denominator is <c>n * (n - 1)</c> over whatever the table declares, so this
        ///     does not depend on there being 25 fonts.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EveryFont_JoinsToItsOwnGlyphSheetAndToNoOther()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();
            var identityFailures = new List<string>();
            var crossSurvivors = new List<string>();
            int identityPassed = 0;
            int crossTried = 0;

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                foreach (KeyValuePair<int, FontGlyphSheet> other in joined)
                {
                    var pairing = new FontGlyphSheet(font.Value.Metrics, other.Value.Sheet);
                    string failure = pairing.Verify();

                    if (font.Key == other.Key)
                    {
                        if (failure == null)
                            identityPassed++;
                        else
                            identityFailures.Add($"font {font.Key} does not join to its own sheet: {failure}");
                        continue;
                    }

                    crossTried++;
                    if (failure == null)
                        crossSurvivors.Add($"font {font.Key} accepted the sheet of font {other.Key}");
                }
            }

            _output.WriteLine($"{identityPassed} of {GroupsDeclared} fonts join to their own sheet; " +
                              $"{crossSurvivors.Count} of {crossTried} wrong pairings survived");

            Assert.Equal(GroupsDeclared * (GroupsDeclared - 1), crossTried);
            Assert.Empty(identityFailures);
            Assert.Equal(GroupsDeclared, identityPassed);
            Assert.Empty(crossSurvivors);
        }

        /// <summary>
        ///     Each of the four relations on its own, so it is visible which one discriminates.
        /// </summary>
        /// <remarks>
        ///     <b>Reported and only partly asserted, deliberately.</b> The identity half of every
        ///     relation is asserted, because a relation that fails on a correct pairing is a broken
        ///     decoder. The cross half is printed rather than pinned to a number, because those counts
        ///     describe how similar this cache's fonts happen to be to one another and would be a
        ///     figure about the data rather than about the format.
        ///     <para>
        ///     What it exists to show: the ascent relation holds on 25 of 25 and lets 325 of 600 wrong
        ///     pairings through, so a join built on it would have looked completely convincing and
        ///     been worth almost nothing. That is the same shape as the track-name join, and it is
        ///     printed here so nobody weakens
        ///     <see cref="EveryFont_JoinsToItsOwnGlyphSheetAndToNoOther"/> down to it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EachJoinRelation_HoldsOnEveryCorrectPairing()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();

            var relations = new (string Name, Func<FontDefinition, SpriteDefinition, bool> Holds)[]
            {
                ("the sheet holds one frame per character code",
                    (font, sheet) => sheet.GetFrameCount() == FontDefinition.CharacterCount),
                ("every character's ink fits inside its advance",
                    InkFitsTheAdvance),
                ("line height plus descent is the canvas height",
                    (font, sheet) => font.LineHeight + font.Descent == sheet.height),
                ("the canvas is as wide as the widest advance",
                    (font, sheet) => WidestAdvance(font) == sheet.width),
                ("no ink reaches above the ascent - WEAK, reported not asserted",
                    InkWithinTheAscentBand)
            };

            foreach ((string name, var holds) in relations)
            {
                int identity = 0;
                int cross = 0;

                foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
                    foreach (KeyValuePair<int, FontGlyphSheet> other in joined)
                    {
                        bool ok = holds(font.Value.Metrics, other.Value.Sheet);
                        if (font.Key == other.Key)
                        {
                            if (ok) identity++;
                        }
                        else if (ok)
                        {
                            cross++;
                        }
                    }

                _output.WriteLine($"{name}: {identity}/{GroupsDeclared} correct pairings, " +
                                  $"{cross}/{GroupsDeclared * (GroupsDeclared - 1)} wrong ones");
                Assert.Equal(GroupsDeclared, identity);
            }
        }

        /// <summary>
        ///     The advance relation is tight enough that shifting a sheet by one character breaks it.
        /// </summary>
        /// <remarks>
        ///     <b>A bound that nothing can violate proves nothing.</b> Every wrong pairing this suite
        ///     rejects is a whole different sheet, which leaves open the possibility that the relation
        ///     is merely loose and the sheets merely different sizes. Re-reading a font's <i>own</i>
        ///     sheet one character out of step removes that escape: the metrics and the geometry still
        ///     come from the same asset, only the correspondence is wrong, and the bound still has to
        ///     fail. Measured: it fails on all 25 fonts in both caches.
        /// </remarks>
        [RealCacheFact]
        public void AOneCharacterShift_BreaksTheAdvanceRelationOnEveryFont()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();
            var tolerant = new List<int>();

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                FontDefinition metrics = font.Value.Metrics;
                SpriteDefinition sheet = font.Value.Sheet;
                bool stillHolds = true;

                for (int character = 0; character < FontDefinition.CharacterCount - 1; character++)
                {
                    SpriteFrame shifted = sheet.Frames[character + 1];
                    if (shifted.OffsetX + shifted.SubWidth > metrics.AdvanceOf(character))
                    {
                        stillHolds = false;
                        break;
                    }
                }

                if (stillHolds)
                    tolerant.Add(font.Key);
            }

            _output.WriteLine($"{GroupsDeclared - tolerant.Count} of {GroupsDeclared} fonts reject a " +
                              "one-character shift of their own sheet");
            Assert.Empty(tolerant);
        }

        /// <summary>
        ///     Every glyph sheet is a one-bit mask with a placeholder colour, which is why the editor
        ///     tints rather than paints.
        /// </summary>
        /// <remarks>
        ///     Two facts the rendering depends on and neither of which the format guarantees. Every
        ///     sheet carries a two-entry palette whose only colour is near-white, so drawing the
        ///     stored colour would put white glyphs on a white grid; and no frame sets the alpha bit
        ///     (<c>Class324.java:90</c>), so a glyph has no coverage information beyond "ink or not".
        ///     A cache that broke either would make <see cref="FontGlyphSheet.RenderInk"/> silently
        ///     wrong rather than throw, so it is asserted rather than assumed.
        /// </remarks>
        [RealCacheFact]
        public void EveryGlyphSheet_IsAOneBitMaskInAPlaceholderColour()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();
            var wrong = new List<string>();
            var colours = new SortedDictionary<int, int>();
            int frames = 0;

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                SpriteDefinition sheet = font.Value.Sheet;

                if (sheet.PaletteStored.Length != 2)
                    wrong.Add($"font {font.Key} has a {sheet.PaletteStored.Length} entry palette");
                else
                {
                    colours.TryGetValue(sheet.PaletteStored[1], out int seen);
                    colours[sheet.PaletteStored[1]] = seen + 1;
                }

                foreach (SpriteFrame frame in sheet.Frames)
                {
                    frames++;
                    if (frame.HasAlphaPlane)
                        wrong.Add($"font {font.Key} has a frame carrying an alpha plane");
                }
            }

            _output.WriteLine($"{frames} frames across {GroupsDeclared} sheets; ink colours: " +
                              string.Join(", ", colours.Select(entry => $"0x{entry.Key:X6}={entry.Value}")));

            Assert.Equal(GroupsDeclared * FontDefinition.CharacterCount, frames);
            Assert.Empty(wrong);
        }

        /// <summary>
        ///     Laying a real string out puts every glyph's ink inside the advance box it was given.
        /// </summary>
        /// <remarks>
        ///     The layout is the only place the join is consumed rather than merely checked, and its
        ///     correctness reduces to the same relation: a glyph is drawn at
        ///     <c>penX + frame.OffsetX</c>, so it stays within its own advance exactly when
        ///     <c>offsetX + subWidth &lt;= advance</c>. Asserted over every declared font so a
        ///     regression in either half shows up as overlapping text rather than as nothing.
        /// </remarks>
        [RealCacheFact]
        public void LayingOutAString_KeepsEveryGlyphInsideItsOwnAdvance()
        {
            const string sample = "Sherlock Holmes 0123456789 <>!?";
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();
            var overlaps = new List<string>();
            int placed = 0;

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                FontTextLayout.Layout layout = FontTextLayout.Measure(font.Value.Metrics, sample);
                Assert.Equal(sample.Length, layout.Glyphs.Count);

                foreach (FontTextLayout.PlacedGlyph glyph in layout.Glyphs)
                {
                    placed++;
                    SpriteFrame frame = font.Value.FrameFor(glyph.Character);
                    int inkEnd = glyph.PenX + frame.OffsetX + frame.SubWidth;
                    if (inkEnd > glyph.PenX + glyph.Advance)
                        overlaps.Add($"font {font.Key} character {glyph.Character} draws past its advance");
                }

                //An unkerned font applies no kern at all, which every font in both caches is.
                Assert.All(layout.Glyphs, glyph => Assert.Equal(0, glyph.Kern));
            }

            _output.WriteLine($"{placed} glyphs placed across {GroupsDeclared} fonts, none overlapping");
            Assert.Empty(overlaps);
        }

        private static bool InkFitsTheAdvance(FontDefinition font, SpriteDefinition sheet)
        {
            if (sheet.GetFrameCount() != FontDefinition.CharacterCount)
                return false;

            for (int character = 0; character < FontDefinition.CharacterCount; character++)
            {
                SpriteFrame frame = sheet.Frames[character];
                if (frame.OffsetX + frame.SubWidth > font.AdvanceOf(character))
                    return false;
            }

            return true;
        }

        private static bool InkWithinTheAscentBand(FontDefinition font, SpriteDefinition sheet)
        {
            if (sheet.GetFrameCount() != FontDefinition.CharacterCount)
                return false;

            int ceiling = sheet.height - font.Ascent - font.Descent;
            foreach (SpriteFrame frame in sheet.Frames)
            {
                if (frame.SubWidth == 0 || frame.SubHeight == 0)
                    continue;
                if (frame.OffsetY < ceiling)
                    return false;
            }

            return true;
        }

        private static int WidestAdvance(FontDefinition font)
        {
            int widest = 0;
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                widest = Math.Max(widest, font.AdvanceOf(character));
            return widest;
        }
    }
}
