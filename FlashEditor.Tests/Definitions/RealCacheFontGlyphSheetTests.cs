using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
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
    ///     the pairings that survive are counted. The claim the suite makes is that <b>every correct
    ///     pairing passes and no incorrect one does</b> - which is a statement no aggregate can be
    ///     satisfied by, and which fails the moment a relation is weakened. Each relation is also
    ///     measured on its own so a future reader can see which of them is carrying the
    ///     discrimination.
    ///     </para>
    ///     <para>
    ///     <b>The relations are split by what kind of claim they are, and the assertions follow that
    ///     split.</b> Three have a reader in the 637 client and live in
    ///     <see cref="FontGlyphSheet.JoinFailure"/>; those are asserted against every correct
    ///     pairing, because failing one is a defect. One - <c>canvasWidth == max(advance)</c> - is
    ///     only <i>observed</i> to hold, on 25 fonts of each of the two supported caches, and lives
    ///     in <see cref="FontGlyphSheet.Irregularity"/>; it is measured and printed but never
    ///     asserted on a correct pairing. Two caches is a sample of two, and a suite that failed a
    ///     valid font over a packing habit would be a false negative nobody could tell from a real
    ///     defect. It is still used as a <i>discriminator</i> against wrong pairings, where a false
    ///     negative is impossible by construction.
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
        ///     Every font joins to the sheet at its own id, and no wrong pairing survives.
        /// </summary>
        /// <remarks>
        ///     <b>The load-bearing test of the whole join.</b> The identity half alone would be the
        ///     coverage claim that has already misled this project once; the cross half is what makes
        ///     it a proof.
        ///     <para>
        ///     The two halves deliberately apply different bars, and the asymmetry is the point. A
        ///     correct pairing is only ever failed for a <b>client-backed</b> relation
        ///     (<see cref="FontGlyphSheet.JoinFailure"/>), so this test cannot reject a font that a
        ///     future cache ships with a differently packed sheet. A wrong pairing is rejected by
        ///     everything available including the advisory equality, because there is no such thing
        ///     as a false negative against a pairing that is wrong by construction. Measured in both
        ///     supported caches: 25 of 25 correct pairings pass the client-backed relations, and 0 of
        ///     600 wrong ones survive the full set.
        ///     </para>
        ///     <para>
        ///     The client-backed-only cross count is printed beside it, so that if the advisory
        ///     relation is ever dropped a reader can see exactly what discrimination goes with it.
        ///     The cross denominator is <c>n * (n - 1)</c> over whatever the table declares, so none
        ///     of this depends on there being 25 fonts.
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
            int crossSurvivingClientBackedAlone = 0;

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                foreach (KeyValuePair<int, FontGlyphSheet> other in joined)
                {
                    var pairing = new FontGlyphSheet(font.Value.Metrics, other.Value.Sheet);
                    string failure = pairing.JoinFailure();

                    if (font.Key == other.Key)
                    {
                        //Client-backed only. An advisory irregularity on a font's own sheet is
                        //reported by the panel and must never fail it here.
                        if (failure == null)
                            identityPassed++;
                        else
                            identityFailures.Add($"font {font.Key} does not join to its own sheet: {failure}");
                        continue;
                    }

                    crossTried++;
                    if (failure != null)
                        continue;

                    crossSurvivingClientBackedAlone++;
                    if (pairing.Irregularity() == null)
                        crossSurvivors.Add($"font {font.Key} accepted the sheet of font {other.Key}");
                }
            }

            _output.WriteLine($"{identityPassed} of {GroupsDeclared} fonts join to their own sheet on the " +
                              "client-backed relations alone");
            _output.WriteLine($"wrong pairings surviving: {crossSurvivingClientBackedAlone}/{crossTried} on " +
                              $"the client-backed relations, {crossSurvivors.Count}/{crossTried} once the " +
                              "advisory equality is added");

            Assert.Equal(GroupsDeclared * (GroupsDeclared - 1), crossTried);
            Assert.Empty(identityFailures);
            Assert.Equal(GroupsDeclared, identityPassed);
            Assert.Empty(crossSurvivors);
        }

        /// <summary>
        ///     The advisory equality is measured over the loaded cache and never asserted.
        /// </summary>
        /// <remarks>
        ///     <b>Here so that dropping it would be a visible decision rather than a silent one.</b>
        ///     <c>canvasWidth == max(advance)</c> is exact on all 25 fonts of both supported caches
        ///     and rejects 44 of 600 wrong pairings on its own, which is worth knowing - but the
        ///     client never reads a glyph sheet's canvas width when drawing text, so a cache that
        ///     broke it would still play. Asserting it would turn a packing habit into a test
        ///     failure on valid data, and a user cannot tell that from a real defect.
        ///     <para>
        ///     The one thing that <i>is</i> asserted is the contract between the two methods: a font
        ///     paired with its own sheet may report an irregularity, but it must never report a join
        ///     failure because of one.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void TheAdvisoryEquality_IsReportedAndNeverFailsAFontsOwnSheet()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();
            var irregular = new List<string>();

            foreach (KeyValuePair<int, FontGlyphSheet> font in joined)
            {
                string irregularity = font.Value.Irregularity();
                if (irregularity != null)
                    irregular.Add($"font {font.Key}: {irregularity}");

                Assert.Null(font.Value.JoinFailure());
            }

            _output.WriteLine($"{joined.Count - irregular.Count} of {GroupsDeclared} fonts have a canvas as " +
                              "wide as their widest advance");
            foreach (string line in irregular)
                _output.WriteLine("  irregular: " + line);
        }

        /// <summary>
        ///     Each relation on its own, so it is visible which one is carrying the discrimination.
        /// </summary>
        /// <remarks>
        ///     <b>This table is the evidence, and it survives whatever the assertions do.</b> Every
        ///     relation is measured against all correct and all wrong pairings and printed. Only the
        ///     <b>client-backed</b> ones are asserted on the correct pairings, because only those
        ///     mean something is broken when they fail; the observed-only ones are printed and left
        ///     alone, so a cache that packs its sheets differently is reported rather than failed.
        ///     <para>
        ///     No cross count is asserted at all. Those describe how similar this cache's fonts
        ///     happen to be to one another, which is a figure about the data rather than about the
        ///     format.
        ///     </para>
        ///     <para>
        ///     What the table exists to show: the ascent relation holds on 25 of 25 and lets 325 of
        ///     600 wrong pairings through, so a join built on it would have looked completely
        ///     convincing and been worth almost nothing. That is the same shape as the track-name
        ///     join, and it is printed here so nobody weakens
        ///     <see cref="EveryFont_JoinsToItsOwnGlyphSheetAndToNoOther"/> down to it.
        ///     </para>
        /// </remarks>
        [RealCacheFact]
        public void EachJoinRelation_HoldsOnEveryCorrectPairing()
        {
            SortedDictionary<int, FontGlyphSheet> joined = JoinEveryFont();

            //Asserted is the client-backed flag. It is not a measure of how well a relation
            //discriminates - the ascent relation below discriminates worst and the advisory equality
            //discriminates well - but of whether failing it means anything is wrong.
            var relations = new (string Name, bool Asserted, Func<FontDefinition, SpriteDefinition, bool> Holds)[]
            {
                ("CLIENT-BACKED  one frame per character code (Class197.java:193)", true,
                    (font, sheet) => sheet.GetFrameCount() == FontDefinition.CharacterCount),
                ("CLIENT-BACKED  ink fits inside its advance (RSFont.java:576,599)", true,
                    InkFitsTheAdvance),
                ("CLIENT-BACKED  lineHeight + descent == canvasHeight (RSFont.java:190,483)", true,
                    (font, sheet) => font.LineHeight + font.Descent == sheet.height),
                ("OBSERVED ONLY  canvasWidth == max(advance), no client reader", false,
                    (font, sheet) => WidestAdvance(font) == sheet.width),
                ("OBSERVED ONLY  no ink above the ascent - and it admits over half the wrong pairings", false,
                    InkWithinTheAscentBand)
            };

            foreach ((string name, bool asserted, var holds) in relations)
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

                if (asserted)
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
