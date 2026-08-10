using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using FlashEditor.Cache;
using FlashEditor.Definitions.Fonts;
using FlashEditor.Definitions.Sprites;

namespace FlashEditor.Definitions.Interfaces.Layout {
    /// <summary>
    ///     Draws interface text in the cache's own font rather than a stand-in.
    /// </summary>
    /// <remarks>
    ///     <b>Why a substitute font was not good enough.</b> The canvas first drew text with the
    ///     editor's Consolas, which is wider than every font the cache holds, so a string that fits
    ///     on one line in the game wrapped onto two here. "Insert a very long name here!" in a
    ///     140-pixel component is the case that showed it: it reads as a layout defect in the file
    ///     and is a defect in the preview. Getting the glyphs right is the only way the canvas can
    ///     answer "will this caption fit", which is most of what a text component is for.
    ///     <para>
    ///     <b>The client wraps to the component's width, and so does this.</b> Settled from
    ///     <c>Class197.method2675</c>, which <c>RSFont.drawText</c> calls before drawing anything:
    ///     it splits the string into lines on the <c>&lt;br&gt;</c> tag and then breaks any line
    ///     still wider than the width it was given. An earlier version of this painter broke only
    ///     on <c>\n</c> and left the rest to the clip, which is why "Dragontooth Island" rendered
    ///     as "ragontooth Islan" - its component is 130 wide and 54 tall, three lines of room, and
    ///     one unwrapped centred line hung off both ends and lost a character at each.
    ///     </para>
    ///     <para>
    ///     <b>Each line is aligned on its own.</b> Centring a wrapped paragraph by the width of its
    ///     widest line leaves every shorter line offset by half the difference, which reads as a
    ///     ragged edge rather than as centred text.
    ///     </para>
    ///     <para>
    ///     <b>The sheets are cached per cache, and they are not small.</b> A glyph sheet is a
    ///     decoded 256-frame sprite set; index 13 holds 25 of them and an interface can name several
    ///     on one screen, so they are loaded once and held for the life of the painter rather than
    ///     per component per paint.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceTextPainter : IDisposable {
        private readonly RSCache cache;
        private readonly Dictionary<int, FontGlyphSheet?> sheets = new();

        /// <summary>Creates a painter over an open cache.</summary>
        /// <param name="cache">The cache to load fonts from.</param>
        public InterfaceTextPainter(RSCache cache) {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        ///     Draws a string inside a box, in the component's own font and colour.
        /// </summary>
        /// <remarks>
        ///     Returns false when the font cannot be loaded, so the caller can fall back to a
        ///     substitute rather than draw nothing - a component with text and no visible text reads
        ///     as an empty component, which is a different record.
        /// </remarks>
        /// <param name="graphics">Where to draw.</param>
        /// <param name="text">The string.</param>
        /// <param name="fontId">The index-13 font the component names, or -1.</param>
        /// <param name="box">The component's rectangle.</param>
        /// <param name="ink">The colour to draw the glyphs in.</param>
        /// <param name="horizontal">0 left, 1 centred, 2 right.</param>
        /// <param name="vertical">0 top, 1 centred, 2 bottom.</param>
        /// <param name="lineHeightOverride">
        ///     The component's stored line height, or 0 to use the font's own.
        /// </param>
        /// <returns>Whether the cache's font was used.</returns>
        public bool Draw(Graphics graphics, string text, int fontId, Rectangle box, Color ink,
            int horizontal, int vertical, int lineHeightOverride = 0) {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));

            if (string.IsNullOrEmpty(text) || fontId < 0)
                return false;

            FontGlyphSheet? sheet = SheetFor(fontId);
            if (sheet == null)
                return false;

            /* The stored string is markup: tags are interpreted rather than drawn, and one of them
               breaks the line. Measuring the raw string counts every tag as characters, which in
               the worst cases overstates the width by more than the box. */
            InterfaceTextMarkup markup = InterfaceTextMarkup.Parse(text);

            /* A stored line height of 0 means "the font's own". The client's test for it reads
               (i_38_ ^ 0xffffffff) == -1 at RSFont.drawText:382, which is ~x == -1, so x == 0 -
               not x == -1, which is what it looks like and what the byte could never hold anyway
               since it is read unsigned. */
            int lineHeight = lineHeightOverride != 0 ? lineHeightOverride : sheet.Metrics.LineHeight;

            /* <b>Wrapping happens only when the box could hold two lines.</b> RSFont.drawText:387-393
               passes the width to the splitter under exactly this test and passes null otherwise,
               and null means the string is broken on <br> and on nothing else however wide it is.
               Wrapping unconditionally is visibly wrong rather than merely eager: interface 72
               stores "76 Hunter" in a 32x32 box whose font has a 35-pixel line height, so the box
               cannot hold even one full line, and wrapping it produced "76", "Hunt" and "er"
               stacked down the page where the client draws one line and lets it overflow. */
            bool wraps = sheet.Metrics.Ascent + sheet.Metrics.Descent + lineHeight <= box.Height
                || lineHeight + lineHeight <= box.Height;

            string wrapped = wraps ? Wrap(sheet, markup.Text, box.Width) : markup.Text;

            FontTextLayout.Layout layout = FontTextLayout.Measure(sheet.Metrics, wrapped);
            if (layout.Glyphs.Count == 0)
                return true;

            int offsetY = vertical switch {
                1 => (box.Height - layout.Height) / 2,
                2 => box.Height - layout.Height,
                _ => 0
            };

            /* Alignment and glyph placement are stated in two different coordinate systems, and the
               conversion between them is this one term.

               The client aligns a block by its ascent and descent - Class197.method2672:170-176,
               and RSFont.drawText:417-435 for the three alignment arms - so layout.Height above is
               the right height to align with. But a glyph's stored offset is measured from the top
               of its canvas, not from the baseline, and the two origins are only the same font when
               the baseline sits exactly one ascent below the canvas top.

               For most fonts it does, and this term is zero. For the large ones it is nowhere near:
               font 4040 has ascent 14 and a 38-pixel canvas whose baseline is at 35, so 21 pixels
               of headroom sit above every glyph. Aligning in ascent space and then drawing in
               canvas space pushed "Undiscovered" 21 pixels down a 40-pixel box and the bottom of
               every letter was clipped away - which is what interfaces 8, 35 and 72 all showed. */
            int baselineShift = sheet.Metrics.Ascent - sheet.Baseline;

            var lineWidths = new Dictionary<int, int>();
            foreach (FontTextLayout.PlacedGlyph glyph in layout.Glyphs) {
                int end = glyph.PenX + glyph.Advance;
                if (!lineWidths.TryGetValue(glyph.LineTop, out int widest) || end > widest)
                    lineWidths[glyph.LineTop] = end;
            }

            foreach (FontTextLayout.PlacedGlyph glyph in layout.Glyphs) {
                using Bitmap? rendered = sheet.RenderInk(glyph.Character, ink);
                if (rendered == null)
                    continue;

                SpriteFrame? frame = sheet.FrameFor(glyph.Character);
                if (frame == null)
                    continue;

                int offsetX = horizontal switch {
                    1 => (box.Width - lineWidths[glyph.LineTop]) / 2,
                    2 => box.Width - lineWidths[glyph.LineTop],
                    _ => 0
                };

                /* The frame's own offsets place the ink inside the glyph's box - a comma sits low
                   and a quote sits high - so they are added rather than the bitmap being drawn at
                   the pen position, which would sit every glyph on the same top edge. */
                graphics.DrawImageUnscaled(rendered,
                    box.X + offsetX + glyph.PenX + frame.OffsetX,
                    box.Y + offsetY + glyph.LineTop + frame.OffsetY + baselineShift);
            }

            return true;
        }

        /// <summary>
        ///     Breaks lines that do not fit the width they are given.
        /// </summary>
        /// <remarks>
        ///     Greedy, which is what <c>Class197.method2675</c> does: it tracks the last break
        ///     opportunity and cuts there the moment the running width exceeds the limit.
        ///     <para>
        ///     <b>A word with no break opportunity in it is cut mid-word</b>, at
        ///     <c>method2675:+162-171</c>, which takes the substring ending at the character that
        ///     overflowed. An earlier version of this comment claimed the client left such a word
        ///     whole; it does not, and the code here was already right.
        ///     </para>
        ///     <para>
        ///     A hyphen is a break opportunity as well as a space (<c>:+186-190</c>), and the two
        ///     are consumed differently: the space is dropped and the hyphen is kept on the line it
        ///     ended. The client also records a hyphen only <i>after</i> testing the width and a
        ///     space <i>before</i>, so a hyphen sitting exactly on the overflow does not rescue that
        ///     line but is available to the next - which is why this walks the string in that order
        ///     rather than collecting both up front.
        ///     </para>
        /// </remarks>
        /// <param name="sheet">The font, for measuring.</param>
        /// <param name="text">The text, stripped of markup, with newlines for the hard breaks.</param>
        /// <param name="width">The width to fit.</param>
        /// <returns>The text with soft breaks inserted.</returns>
        private static string Wrap(FontGlyphSheet sheet, string text, int width) {
            if (width <= 0 || string.IsNullOrEmpty(text))
                return text;

            var wrapped = new StringBuilder(text.Length + 8);
            bool first = true;

            foreach (string hardLine in text.Split('\n')) {
                if (!first)
                    wrapped.Append('\n');
                first = false;

                if (FontTextLayout.Measure(sheet.Metrics, hardLine).Width <= width) {
                    wrapped.Append(hardLine);
                    continue;
                }

                string remaining = hardLine;
                bool firstPiece = true;

                while (remaining.Length > 0) {
                    if (!firstPiece)
                        wrapped.Append('\n');
                    firstPiece = false;

                    (int keep, int skip) = LongestPrefixThatFits(sheet, remaining, width);
                    wrapped.Append(remaining, 0, keep);
                    remaining = remaining.Substring(Math.Min(remaining.Length, keep + skip));
                }
            }

            return wrapped.ToString();
        }

        /// <summary>
        ///     How much of a line fits, cut at the last space rather than mid-word.
        /// </summary>
        /// <remarks>
        ///     Measures a growing prefix rather than summing per-character advances, so kerning is
        ///     included: the font carries a kerning matrix, and a sum of bare advances overestimates
        ///     every line and wraps earlier than the client does.
        /// </remarks>
        /// <param name="sheet">The font, for measuring.</param>
        /// <param name="line">The line to cut.</param>
        /// <param name="width">The width to fit.</param>
        /// <returns>
        ///     How many characters stay on this line, and how many to drop before the next.
        /// </returns>
        private static (int Keep, int Skip) LongestPrefixThatFits(FontGlyphSheet sheet, string line,
            int width) {
            int keepAtBreak = -1;
            int skipAtBreak = 0;

            for (int i = 1; i <= line.Length; i++) {
                char c = line[i - 1];

                //Recorded before the width test, so a space at the overflow still breaks the line.
                if (c == ' ') {
                    keepAtBreak = i - 1;
                    skipAtBreak = 1;
                }

                /* A growing prefix rather than a sum of per-character advances, so the font's
                   kerning matrix is included. Summing bare advances overstates every line and wraps
                   earlier than the client does. */
                if (FontTextLayout.Measure(sheet.Metrics, line.Substring(0, i)).Width > width) {
                    return keepAtBreak > 0
                        ? (keepAtBreak, skipAtBreak)
                        : (Math.Max(1, i - 1), 0);
                }

                //Recorded after it, and kept on this line rather than dropped.
                if (c == '-') {
                    keepAtBreak = i;
                    skipAtBreak = 0;
                }
            }

            return (line.Length, 0);
        }

        /// <summary>Releases every glyph sheet loaded.</summary>
        public void Dispose() {
            foreach (FontGlyphSheet? sheet in sheets.Values)
                sheet?.Dispose();

            sheets.Clear();
        }

        /// <summary>
        ///     A font's glyph sheet, loaded once.
        /// </summary>
        /// <remarks>
        ///     A failure is cached as null as well as a success. A font that will not load will not
        ///     load on the next paint either, and retrying it would re-read the cache on every
        ///     frame for every component that names it.
        /// </remarks>
        private FontGlyphSheet? SheetFor(int fontId) {
            if (sheets.TryGetValue(fontId, out FontGlyphSheet? sheet))
                return sheet;

            try {
                sheet = FontGlyphSheet.Load(cache, fontId);
            }
            catch (Exception) {
                sheet = null;
            }

            sheets[fontId] = sheet;
            return sheet;
        }
    }
}
