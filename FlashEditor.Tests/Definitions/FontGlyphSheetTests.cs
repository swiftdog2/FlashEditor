using System;
using System.Collections.Generic;
using System.Drawing;
using FlashEditor;
using FlashEditor.cache.sprites;
using FlashEditor.Definitions.Fonts;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Pins the index-13 to index-8 join and the text layout against records built by hand.
    /// </summary>
    /// <remarks>
    ///     <b>Two things no sweep over either cache can reach, and they are the reason this file
    ///     exists.</b>
    ///     <list type="bullet">
    ///     <item><b>The kerned layout.</b> No group in either supported cache sets the kerning flag -
    ///     <c>RealCacheFontTests.NoFontInThisCache_SetsTheKerningFlag</c> asserts it rather than
    ///     merely printing it - so the whole kerning path through the layout is synthetic-only. The
    ///     matrix could be transposed, or applied after the advance instead of before, and every
    ///     cache-backed test would still pass.</item>
    ///     <item><b>A rejected pairing.</b> The real-cache suite proves that no wrong pairing of the
    ///     25 fonts survives <see cref="FontGlyphSheet.Verify"/>, which says the conjunction
    ///     discriminates but not which relation rejected what. Here each relation is broken on its
    ///     own, against an otherwise valid pair, so a relation that quietly stopped being checked
    ///     shows up as a failure rather than as an unchanged pass.</item>
    ///     </list>
    ///     <para>
    ///     The sprite sets are built as bytes and decoded, never encoded from an object graph, for the
    ///     reason <c>CLAUDE.md</c> gives: round-tripping an encoder against its own decoder proves
    ///     nothing, and two real defects in this project survived exactly that.
    ///     </para>
    /// </remarks>
    public class FontGlyphSheetTests
    {
        private const int Space = FontDefinition.SpaceCharacter;
        private const int A = 65;
        private const int B = 66;

        // ===================================================================
        //  The join, and each relation that can reject one
        // ===================================================================

        /// <summary>A sheet built to match its metrics joins cleanly and reports nothing unusual.</summary>
        /// <remarks>
        ///     The control case. Without it, a <see cref="FontGlyphSheet.JoinFailure"/> that rejected
        ///     everything would pass every rejection test below.
        /// </remarks>
        [Fact]
        public void AMatchedPair_Joins()
        {
            FontGlyphSheet font = MatchedFont();

            Assert.Null(font.JoinFailure());
            Assert.Null(font.Irregularity());
        }

        /// <summary>A sprite set that is not 256 frames is not a glyph sheet, whatever its id says.</summary>
        /// <remarks>
        ///     Index 8 holds 4,593 groups and only 25 of them are glyph sheets, so "the ids line up"
        ///     is the weakest possible evidence here. This is also the relation everything else rests
        ///     on: the other three index the frame array by character code.
        /// </remarks>
        [Fact]
        public void ASetThatIsNot256Frames_IsRejected()
        {
            FontDefinition metrics = MetricsFor(Advances(), lineHeight: 12, descent: 3);
            SpriteDefinition sheet = SpriteSet(canvasWidth: 20, canvasHeight: 15,
                frames: new[] { Frame(0, 0, 1, 1) });

            var joined = new FontGlyphSheet(metrics, sheet);

            Assert.False(joined.IsGlyphSheet);
            Assert.Contains("not a glyph sheet", joined.JoinFailure());

            //And nothing advisory, because a set this shape cannot be indexed by character code at
            //all - reporting it as merely unusual would understate it.
            Assert.Null(joined.Irregularity());
        }

        /// <summary>
        ///     Ink that reaches past its character's advance rejects the pairing.
        /// </summary>
        /// <remarks>
        ///     The per-character relation, and the only one of the three that is checked 256 times
        ///     rather than once. One character out of range is enough, because in a correct pairing
        ///     every character's glyph is drawn inside the box the metrics reserve for it - which is
        ///     precisely what lets the layout draw a glyph at the pen without measuring anything
        ///     (<c>RSFont.java:576</c> draws, <c>:599</c> steps by the advance and nothing between
        ///     them looks at the ink).
        /// </remarks>
        [Fact]
        public void InkPastItsAdvance_IsRejected()
        {
            Dictionary<int, byte> advances = Advances();
            SpriteFrame[] frames = MatchedFrames();

            //One pixel wider than character A's advance of 10, at its stored bearing of 1.
            frames[A] = Frame(offsetX: 1, offsetY: 4, width: 10, height: 8);

            var joined = new FontGlyphSheet(
                MetricsFor(advances, lineHeight: 12, descent: 3),
                SpriteSet(canvasWidth: 20, canvasHeight: 15, frames: frames));

            Assert.Contains("character 65", joined.JoinFailure());
        }

        /// <summary>A canvas that is not the line height plus the descent rejects the pairing.</summary>
        /// <remarks>
        ///     <b>Client-backed, which is why it rejects rather than merely reports.</b> Both draw
        ///     paths subtract the line height from the y they are handed before placing a glyph
        ///     (<c>RSFont.java:190</c>, <c>:483</c>) and that y is the baseline - every alignment
        ///     branch at <c>:421-434</c> builds it as <c>top + ascent</c> or <c>bottom - descent</c>.
        ///     So the canvas top is <c>baseline - lineHeight</c>, and the ink only lands on the
        ///     baseline when the canvas is <c>lineHeight + descent</c> rows tall. Off by one is
        ///     enough: it moves every glyph of the font by a pixel.
        /// </remarks>
        [Fact]
        public void ACanvasThatIsNotTheLineBox_IsRejected()
        {
            var joined = new FontGlyphSheet(
                MetricsFor(Advances(), lineHeight: 12, descent: 3),
                SpriteSet(canvasWidth: 20, canvasHeight: 16, frames: MatchedFrames()));

            Assert.Contains("the canvas is 16 rows tall", joined.JoinFailure());
        }

        /// <summary>
        ///     A canvas that is not the widest advance is reported as unusual, and is NOT a join
        ///     failure.
        /// </summary>
        /// <remarks>
        ///     <b>The distinction this test exists to pin.</b> The equality is exact on all 25 fonts
        ///     of both supported caches and rejects 44 of 600 wrong pairings, so it is tempting to
        ///     treat it as a join relation - but the client never reads a glyph sheet's canvas width
        ///     when drawing text, so a cache that packed one pixel wider would still play. Failing
        ///     the join over it would be a false negative on valid data, which a user cannot tell
        ///     from a real defect.
        ///     <para>
        ///     Both halves are asserted: that it is reported, and that it does not contaminate
        ///     <see cref="FontGlyphSheet.JoinFailure"/>. Only the second could regress silently.
        ///     </para>
        /// </remarks>
        [Fact]
        public void ACanvasThatIsNotTheWidestAdvance_IsUnusualRatherThanAJoinFailure()
        {
            var joined = new FontGlyphSheet(
                MetricsFor(Advances(), lineHeight: 12, descent: 3),
                SpriteSet(canvasWidth: 21, canvasHeight: 15, frames: MatchedFrames()));

            Assert.Null(joined.JoinFailure());
            Assert.Contains("the canvas is 21 pixels wide", joined.Irregularity());
        }

        /// <summary>The baseline is the canvas bottom less the descent.</summary>
        /// <remarks>
        ///     Derived rather than stored, so it is worth pinning: a glyph is placed by its own frame
        ///     offset within the canvas, and the baseline is only ever needed to rule a line on the
        ///     preview. Getting it wrong would draw a rule through the middle of the text and look
        ///     like a layout defect rather than a cosmetic one.
        /// </remarks>
        [Fact]
        public void TheBaseline_IsTheCanvasBottomLessTheDescent()
        {
            FontGlyphSheet font = MatchedFont();

            Assert.Equal(15, font.CanvasHeight);
            Assert.Equal(12, font.Baseline);
            Assert.Equal(font.Metrics.LineHeight, font.Baseline);
        }

        /// <summary>
        ///     Rendering draws the non-zero palette indices in the caller's colour and nothing else.
        /// </summary>
        /// <remarks>
        ///     Palette index 0 is the transparent entry (<c>Class324.java:77-79</c>) and the stored
        ///     colour is a near-white placeholder the client recolours per draw, so the ink colour has
        ///     to come from the caller. A renderer that used the stored palette would draw white on
        ///     white in this editor, and one that treated index 0 as a colour would fill every glyph's
        ///     bounding box.
        /// </remarks>
        [Fact]
        public void RenderingAGlyph_TintsOnlyTheNonZeroPaletteIndices()
        {
            SpriteFrame[] frames = MatchedFrames();

            //A 2x2 frame with ink on one diagonal only.
            frames[A] = Frame(offsetX: 1, offsetY: 4, width: 2, height: 2,
                pixels: new byte[] { 1, 0, 0, 1 });
            frames[B] = Frame(offsetX: 0, offsetY: 4, width: 0, height: 0);

            var font = new FontGlyphSheet(
                MetricsFor(Advances(), lineHeight: 12, descent: 3),
                SpriteSet(canvasWidth: 20, canvasHeight: 15, frames: frames));

            using Bitmap ink = font.RenderInk(A, Color.Red);

            Assert.Equal(2, ink.Width);
            Assert.Equal(2, ink.Height);
            Assert.Equal(Color.Red.ToArgb(), ink.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.Red.ToArgb(), ink.GetPixel(1, 1).ToArgb());
            Assert.Equal(0, ink.GetPixel(1, 0).ToArgb());
            Assert.Equal(0, ink.GetPixel(0, 1).ToArgb());

            //A character with no ink has no bitmap at all, rather than a zero-sized one that every
            //caller would then have to guard against.
            Assert.Null(font.RenderInk(B, Color.Red));
        }

        // ===================================================================
        //  Layout - the unkerned arm, which both caches do exercise
        // ===================================================================

        /// <summary>An unkerned font advances by the advance width and by nothing else.</summary>
        [Fact]
        public void AnUnkernedFont_AdvancesByTheAdvanceWidthAlone()
        {
            FontDefinition metrics = MetricsFor(Advances(), lineHeight: 12, descent: 3);

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "AB");

            Assert.Equal(2, layout.Glyphs.Count);
            Assert.Equal(0, layout.Glyphs[0].PenX);
            Assert.Equal(0, layout.Glyphs[0].Kern);
            Assert.Equal(10, layout.Glyphs[1].PenX);
            Assert.Equal(0, layout.Glyphs[1].Kern);
            Assert.Equal(30, layout.Width);
        }

        /// <summary>
        ///     A newline steps by the line height and resets the pen and the kerning context.
        /// </summary>
        /// <remarks>
        ///     The reset matters even on an unkerned font, because it is the same variable that would
        ///     carry a kern across a line break - the first character of a line has no predecessor
        ///     (<c>Class197.method2675:250</c> guards on that), and a layout that kept the previous
        ///     line's last character would indent every line but the first by a kern.
        /// </remarks>
        [Fact]
        public void ANewline_StepsByTheLineHeightAndResetsThePen()
        {
            FontDefinition metrics = MetricsFor(Advances(), lineHeight: 12, descent: 3);

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "AB\nA");

            Assert.Equal(2, layout.Lines);
            Assert.Equal(3, layout.Glyphs.Count);
            Assert.Equal(0, layout.Glyphs[2].PenX);
            Assert.Equal(12, layout.Glyphs[2].LineTop);
            Assert.Equal(0, layout.Glyphs[2].Kern);

            //The widest line, not the last one.
            Assert.Equal(30, layout.Width);
        }

        /// <summary>
        ///     The block height is the client's formula, not the line height times the line count.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.method2672:170-176</c>: <c>(lines - 1) * step + ascent + descent</c>. The
        ///     last line still needs its whole glyph box, so the two terms are different quantities
        ///     and a block sized as <c>lines * lineHeight</c> is wrong on every font whose ascent plus
        ///     descent is not its line height - which is 21 of the 25 in both caches.
        /// </remarks>
        [Fact]
        public void TheBlockHeight_IsTheClientsFormula()
        {
            FontDefinition metrics = MetricsFor(Advances(), lineHeight: 12, descent: 3, ascent: 9);

            Assert.Equal(12, FontTextLayout.Measure(metrics, "A").Height);
            Assert.Equal(24, FontTextLayout.Measure(metrics, "A\nA").Height);
            Assert.Equal(36, FontTextLayout.Measure(metrics, "A\nA\nA").Height);
        }

        /// <summary>A character above 255 is dropped rather than masked into a different glyph.</summary>
        /// <remarks>
        ///     The client indexes the advance table with <c>0xff &amp;</c> a value it has already
        ///     mapped through its own encoder (<c>Class197.method2675:232</c>), so masking a raw UTF-16
        ///     code point here would silently draw an unrelated glyph. Dropping says "this editor
        ///     cannot show that character", which is true.
        /// </remarks>
        [Fact]
        public void ACharacterAbove255_IsDropped()
        {
            FontDefinition metrics = MetricsFor(Advances(), lineHeight: 12, descent: 3);

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "AŁB");

            Assert.Equal(2, layout.Glyphs.Count);
            Assert.Equal(A, layout.Glyphs[0].Character);
            Assert.Equal(B, layout.Glyphs[1].Character);
        }

        // ===================================================================
        //  Layout - the kerned arm, which neither cache can reach at all
        // ===================================================================

        /// <summary>
        ///     A kerned font moves the pen by the pair's kern before the glyph is drawn.
        /// </summary>
        /// <remarks>
        ///     <c>Class197.method2675:245-252</c> adds the advance and then the matrix entry to the
        ///     same accumulator, so the kern belongs to the pair rather than to either character and
        ///     applies to where the second one lands. The record here gives A and B a clearance of 5
        ///     over their overlapping rows, so the pair closes by 5.
        /// </remarks>
        [Fact]
        public void AKernedFont_ClosesThePairBeforeDrawingTheSecondGlyph()
        {
            FontDefinition metrics = KernedMetrics();
            Assert.True(metrics.IsKerned);
            Assert.Equal(-5, metrics.KerningMatrix()[A, B]);

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "AB");

            Assert.Equal(0, layout.Glyphs[0].PenX);
            Assert.Equal(-5, layout.Glyphs[1].Kern);

            //A advances 10 and the pair closes by 5, so B is drawn at 5 rather than at 10.
            Assert.Equal(5, layout.Glyphs[1].PenX);
            Assert.Equal(17, layout.Width);
        }

        /// <summary>
        ///     The matrix's first subscript is the left character, so an asymmetric pair lays out
        ///     differently in each order.
        /// </summary>
        /// <remarks>
        ///     <b>The transposition no cache could catch.</b> <c>Class197.method2675:250</c> reads
        ///     <c>aByteArrayArray1516[previous][current]</c>, and the matrix is genuinely asymmetric -
        ///     the two characters' profiles are consulted from opposite blocks, so <c>AB</c> kerns by
        ///     5 and <c>BA</c> by 8. A layout with the subscripts swapped produces text that looks
        ///     laid out and is wrong on every pair, and nothing in either cache stores a kerning table
        ///     to disagree with it.
        /// </remarks>
        [Fact]
        public void TheKern_IsIndexedLeftThenRight()
        {
            FontDefinition metrics = KernedMetrics();

            Assert.Equal(-5, FontTextLayout.Measure(metrics, "AB").Glyphs[1].Kern);
            Assert.Equal(-8, FontTextLayout.Measure(metrics, "BA").Glyphs[1].Kern);
        }

        /// <summary>The first character of a line takes no kern, on either line.</summary>
        /// <remarks>
        ///     Guarded in the client by <c>(i_19_ ^ 0xffffffff) != 0</c>, its spelling of
        ///     "previous is not -1". Without the reset, the second line's first character would be
        ///     kerned against the first line's last one.
        /// </remarks>
        [Fact]
        public void AKernedFont_TakesNoKernAtTheStartOfALine()
        {
            FontDefinition metrics = KernedMetrics();

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "AB\nBA");

            Assert.Equal(0, layout.Glyphs[0].Kern);
            Assert.Equal(0, layout.Glyphs[2].Kern);
            Assert.Equal(0, layout.Glyphs[2].PenX);
            Assert.Equal(-8, layout.Glyphs[3].Kern);
        }

        /// <summary>
        ///     A kerned font's line step is the height it derives from the space glyph.
        /// </summary>
        /// <remarks>
        ///     A kerned record stores no line-height byte at all - <c>Class197.java:84</c> computes it
        ///     as <c>rows[32] + tops[32]</c> - so a layout that read a stored field would step by zero
        ///     and pile every line on top of the first. The record here gives space 5 rows starting at
        ///     row 7, so the step is 12: a number that appears nowhere in its bytes.
        /// </remarks>
        [Fact]
        public void AKernedFont_StepsByTheLineHeightItDerives()
        {
            FontDefinition metrics = KernedMetrics();
            Assert.Equal(12, metrics.LineHeight);

            FontTextLayout.Layout layout = FontTextLayout.Measure(metrics, "A\nA");

            Assert.Equal(12, layout.Glyphs[1].LineTop);
        }

        /// <summary>
        ///     An advance-width edit moves the kerning the layout applies, because the matrix is
        ///     derived from it.
        /// </summary>
        /// <remarks>
        ///     The advance is the clearance cap (<c>Class378.method4003:55-57</c>), so narrowing a
        ///     character below the profile-derived gap makes it the binding constraint. This is the
        ///     one place the editor's two editable surfaces meet: the glyph grid writes an advance and
        ///     the kerning grid beside it has to move. A cached matrix that was not invalidated would
        ///     keep laying the old kern out.
        /// </remarks>
        [Fact]
        public void AnAdvanceEdit_MovesTheKernTheLayoutApplies()
        {
            FontDefinition metrics = KernedMetrics();
            Assert.Equal(-5, FontTextLayout.Measure(metrics, "AB").Glyphs[1].Kern);

            metrics.SetAdvance(A, 2);

            Assert.Equal(-2, FontTextLayout.Measure(metrics, "AB").Glyphs[1].Kern);
        }

        // ===================================================================
        //  Record builders - every layout stated as bytes, never encoded
        // ===================================================================

        /// <summary>Advance widths for the synthetic unkerned font, by character code.</summary>
        /// <remarks>
        ///     A is 10, B is 20 and space is 4. The widest is 20, which is what the matched sheet's
        ///     canvas width has to be.
        /// </remarks>
        private static Dictionary<int, byte> Advances()
        {
            return new Dictionary<int, byte> { [Space] = 4, [A] = 10, [B] = 20 };
        }

        /// <summary>A joined pair that satisfies all four relations.</summary>
        /// <returns>The joined font.</returns>
        private static FontGlyphSheet MatchedFont()
        {
            return new FontGlyphSheet(
                MetricsFor(Advances(), lineHeight: 12, descent: 3),
                SpriteSet(canvasWidth: 20, canvasHeight: 15, frames: MatchedFrames()));
        }

        /// <summary>Frames that fit inside the advances <see cref="Advances"/> states.</summary>
        /// <remarks>
        ///     A is 8 wide at a bearing of 1, so it ends at 9 against an advance of 10; B is 19 wide
        ///     at a bearing of 1, ending at 20 against an advance of 20 and therefore exactly tight.
        ///     Space has no ink at all, which is what every font in both caches does with it.
        /// </remarks>
        private static SpriteFrame[] MatchedFrames()
        {
            var frames = new SpriteFrame[FontDefinition.CharacterCount];
            for (int character = 0; character < frames.Length; character++)
                frames[character] = Frame(0, 0, 0, 0);

            frames[A] = Frame(offsetX: 1, offsetY: 4, width: 8, height: 8);
            frames[B] = Frame(offsetX: 1, offsetY: 4, width: 19, height: 8);
            return frames;
        }

        /// <summary>
        ///     Lays out an unkerned record by hand, per <c>Class197.java:22-31,86-92</c>.
        /// </summary>
        /// <param name="advances">The characters that carry a non-zero advance.</param>
        /// <param name="lineHeight">The stored line height.</param>
        /// <param name="descent">Rows below the baseline.</param>
        /// <param name="ascent">Rows above it.</param>
        /// <returns>The decoded record.</returns>
        private static FontDefinition MetricsFor(IReadOnlyDictionary<int, byte> advances,
            byte lineHeight, byte descent, byte ascent = 9)
        {
            var record = new List<byte> { 0, 0 };

            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                record.Add(advances.TryGetValue(character, out byte advance) ? advance : (byte) 0);

            record.Add(lineHeight);
            record.Add(0);
            record.Add(0);
            record.Add(ascent);
            record.Add(descent);

            var font = new FontDefinition { Id = 3793 };
            font.Decode(new JagStream(record.ToArray()));
            return font;
        }

        /// <summary>
        ///     Lays out a kerned record by hand, per <c>Class197.java:33-69</c> and the tail at
        ///     <c>:89-92</c>.
        /// </summary>
        /// <remarks>
        ///     The same shape <c>FontDefinitionCodecTests</c> uses and deliberately so: its kerning
        ///     figures were worked out by hand from <c>Class378.method4003:43-69</c>, so a layout test
        ///     that reproduces them is checking the layout rather than re-deriving the matrix. Space
        ///     gets 5 rows starting at row 7, which is what makes the derived line height 12.
        /// </remarks>
        /// <returns>The decoded record.</returns>
        private static FontDefinition KernedMetrics()
        {
            var advances = new Dictionary<int, byte> { [Space] = 4, [A] = 10, [B] = 12 };
            var rows = new Dictionary<int, byte> { [Space] = 5, [A] = 3, [B] = 2 };
            var tops = new Dictionary<int, byte> { [Space] = 7, [A] = 1, [B] = 2 };
            var leftDeltas = new Dictionary<int, byte[]>
            {
                [Space] = new byte[] { 0, 0, 0, 0, 0 },
                [A] = new byte[] { 0x04, 0xFD, 0x05 },
                [B] = new byte[] { 0x02, 0x03 }
            };
            var rightDeltas = new Dictionary<int, byte[]>
            {
                [Space] = new byte[] { 0, 0, 0, 0, 0 },
                [A] = new byte[] { 0x01, 0x02, 0xFD },
                [B] = new byte[] { 0x07, 0xFB }
            };

            var record = new List<byte> { 0, 1 };
            AppendTable(record, advances);
            AppendTable(record, rows);
            AppendTable(record, tops);
            AppendProfiles(record, leftDeltas);
            AppendProfiles(record, rightDeltas);
            record.Add(0);
            record.Add(0);
            record.Add(9);
            record.Add(3);

            var font = new FontDefinition { Id = 1 };
            font.Decode(new JagStream(record.ToArray()));
            return font;
        }

        private static void AppendTable(List<byte> record, IReadOnlyDictionary<int, byte> values)
        {
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                record.Add(values.TryGetValue(character, out byte value) ? value : (byte) 0);
        }

        private static void AppendProfiles(List<byte> record, IReadOnlyDictionary<int, byte[]> profiles)
        {
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                if (profiles.TryGetValue(character, out byte[] profile))
                    record.AddRange(profile);
        }

        private static SpriteFrame Frame(int offsetX, int offsetY, int width, int height, byte[] pixels = null)
        {
            //A SpriteFrame here is only a carrier for the builder below - the set under test is always
            //decoded from bytes, never assembled from these.
            return new SpriteFrame
            {
                OffsetX = offsetX,
                OffsetY = offsetY,
                SubWidth = width,
                SubHeight = height,
                PaletteIndices = pixels ?? SolidInk(width * height)
            };
        }

        private static byte[] SolidInk(int area)
        {
            var plane = new byte[Math.Max(0, area)];
            for (int at = 0; at < plane.Length; at++)
                plane[at] = 1;
            return plane;
        }

        /// <summary>
        ///     Lays a sprite set out as the bytes index 8 stores, per <c>Class324.java:43-133</c>.
        /// </summary>
        /// <remarks>
        ///     Built as bytes and handed to the production decoder rather than assembled as an object
        ///     graph. Round-tripping <c>SpriteDefinition.Encode</c> against its own <c>Decode</c>
        ///     would agree with itself whatever either of them did, and this file's whole job is to
        ///     check something the cache cannot.
        ///     <para>
        ///     Read backwards from the end: frame count at the last two bytes, then the canvas size,
        ///     the palette-size byte and four per-frame short arrays, then the palette, and the pixel
        ///     planes from offset 0 forwards.
        ///     </para>
        /// </remarks>
        /// <param name="canvasWidth">The stored canvas width.</param>
        /// <param name="canvasHeight">The stored canvas height.</param>
        /// <param name="frames">The frames, in stored order.</param>
        /// <returns>The decoded set.</returns>
        private static SpriteDefinition SpriteSet(int canvasWidth, int canvasHeight, SpriteFrame[] frames)
        {
            var bytes = new List<byte>();

            foreach (SpriteFrame frame in frames)
            {
                //Flags 0: row major, no alpha plane, which is what every real glyph frame with more
                //than one column stores.
                bytes.Add(0);
                bytes.AddRange(frame.PaletteIndices);
            }

            //Palette entry 0 is the transparent index and is never stored, so one entry follows.
            bytes.Add(0xFF);
            bytes.Add(0xFF);
            bytes.Add(0xFF);

            AppendShort(bytes, canvasWidth);
            AppendShort(bytes, canvasHeight);
            bytes.Add(1);

            foreach (SpriteFrame frame in frames) AppendShort(bytes, frame.OffsetX);
            foreach (SpriteFrame frame in frames) AppendShort(bytes, frame.OffsetY);
            foreach (SpriteFrame frame in frames) AppendShort(bytes, frame.SubWidth);
            foreach (SpriteFrame frame in frames) AppendShort(bytes, frame.SubHeight);

            AppendShort(bytes, frames.Length);

            var set = new SpriteDefinition();
            set.Decode(new JagStream(bytes.ToArray()));
            return set;
        }

        private static void AppendShort(List<byte> bytes, int value)
        {
            bytes.Add((byte) (value >> 8));
            bytes.Add((byte) value);
        }
    }
}
