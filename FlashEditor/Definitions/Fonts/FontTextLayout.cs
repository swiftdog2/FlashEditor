using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FlashEditor.Definitions.Sprites;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     Lays a string out with a font's own metrics, the way the client walks one.
    /// </summary>
    /// <remarks>
    ///     <b>A port of the client's walk, not an approximation of it.</b>
    ///     <c>Class197.method2675:245-252</c> adds the character's advance and then, when the font
    ///     carries a kerning matrix and there is a preceding character, the matrix entry
    ///     <c>[previous][current]</c> - so the kern belongs to the pair and is applied before the
    ///     glyph is drawn, and the matrix's first subscript is the <i>left</i> character. Getting
    ///     that subscript order backwards produces text that looks laid out and is wrong on every
    ///     asymmetric pair, which nothing in the cache could tell you.
    ///     <para>
    ///     <b>What this deliberately does not do.</b> The client's own walk also interprets
    ///     <c>&lt;br&gt;</c>, <c>&lt;lt&gt;</c>, <c>&lt;img=n&gt;</c> and colour tags out of the same
    ///     string (<c>:236-268</c>), and it maps the string through
    ///     <c>ScriptRuntime.method3843</c> into the cache's own character encoding. Neither is done
    ///     here: this is a metrics preview, so a <c>&lt;</c> is drawn as the glyph at code 60 and a
    ///     newline is the only break. The panel says so, because a user comparing this against the
    ///     game otherwise has no way to tell a documented choice from a defect.
    ///     </para>
    /// </remarks>
    public static class FontTextLayout {
        /// <summary>Where one character of a laid-out string ends up.</summary>
        /// <remarks>
        ///     Carried out of the layout rather than recomputed by the painter, because the kern is
        ///     the difference between the pen positions and would have to be walked again to find it.
        /// </remarks>
        public readonly struct PlacedGlyph {
            /// <summary>Places one character.</summary>
            /// <param name="character">The character code.</param>
            /// <param name="penX">The pen's x before this glyph is drawn.</param>
            /// <param name="lineTop">The top of the canvas box this glyph's line occupies.</param>
            /// <param name="advance">The character's own advance width.</param>
            /// <param name="kern">The kern applied for the pair ending at this character.</param>
            public PlacedGlyph(int character, int penX, int lineTop, int advance, int kern) {
                Character = character;
                PenX = penX;
                LineTop = lineTop;
                Advance = advance;
                Kern = kern;
            }

            /// <summary>The character code drawn.</summary>
            public int Character { get; }

            /// <summary>The pen's x, which is where the glyph's canvas box starts.</summary>
            public int PenX { get; }

            /// <summary>The top of the canvas box, which is the line's top.</summary>
            public int LineTop { get; }

            /// <summary>The advance this character contributes.</summary>
            public int Advance { get; }

            /// <summary>The kern applied for the pair ending here, zero on an unkerned font.</summary>
            public int Kern { get; }
        }

        /// <summary>The result of laying a string out: where every glyph went and how big the block is.</summary>
        public sealed class Layout {
            internal Layout(IReadOnlyList<PlacedGlyph> glyphs, int width, int height, int lines) {
                Glyphs = glyphs;
                Width = width;
                Height = height;
                Lines = lines;
            }

            /// <summary>Every character placed, in string order.</summary>
            public IReadOnlyList<PlacedGlyph> Glyphs { get; }

            /// <summary>The widest line's pen width.</summary>
            public int Width { get; }

            /// <summary>
            ///     The block's height.
            /// </summary>
            /// <remarks>
            ///     <c>(lines - 1) * lineHeight + ascent + descent</c>, which is
            ///     <c>Class197.method2672:170-176</c> exactly. The line step is the line height and
            ///     the last line still needs its whole glyph box, which is why the two are not the
            ///     same term.
            /// </remarks>
            public int Height { get; }

            /// <summary>How many lines the string broke into.</summary>
            public int Lines { get; }
        }

        /// <summary>
        ///     Walks a string and records where each character lands.
        /// </summary>
        /// <param name="metrics">The font's metrics.</param>
        /// <param name="text">The text, with <c>\n</c> as the only break.</param>
        /// <returns>The layout.</returns>
        public static Layout Measure(FontDefinition metrics, string? text) {
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));

            sbyte[,]? kerning = metrics.KerningMatrix();
            var placed = new List<PlacedGlyph>(text?.Length ?? 0);

            int lineHeight = Math.Max(1, metrics.LineHeight);
            int penX = 0;
            int widest = 0;
            int lines = 1;
            int lineTop = 0;
            int previous = -1;

            foreach (char raw in text ?? string.Empty) {
                if (raw == '\r')
                    continue;

                if (raw == '\n') {
                    widest = Math.Max(widest, penX);
                    penX = 0;
                    previous = -1;
                    lines++;
                    lineTop += lineHeight;
                    continue;
                }

                //A character outside the table has no glyph and no advance at all. Dropping it is
                //what the client's 0xff mask does to anything above 255 anyway.
                int code = raw & 0xFF;
                if (raw > 0xFF)
                    continue;

                int kern = kerning != null && previous >= 0 ? kerning[previous, code] : 0;
                int advance = metrics.AdvanceOf(code);

                placed.Add(new PlacedGlyph(code, penX + kern, lineTop, advance, kern));
                penX += advance + kern;
                previous = code;
            }

            widest = Math.Max(widest, penX);

            //Class197.method2672:170-176: the step between lines is the line height, and the block
            //still needs the whole glyph box for the last one.
            int height = (lines - 1) * lineHeight + metrics.Ascent + metrics.Descent;

            return new Layout(placed, widest, Math.Max(1, height), lines);
        }

        /// <summary>
        ///     Draws a string with its own font, at an integer zoom.
        /// </summary>
        /// <remarks>
        ///     Nearest-neighbour and no smoothing. These are one-bit masks between 8 and 61 pixels
        ///     tall, and any interpolation at all turns an advance-width edit - which moves a glyph
        ///     by one pixel - into something the eye cannot resolve, which is the one judgement this
        ///     view exists to support.
        ///     <para>
        ///     The glyph's canvas box is drawn at the pen, so each frame's own stored offset is what
        ///     puts it on the baseline. That works because the canvas is <c>lineHeight + descent</c>
        ///     tall and the ink fits inside the advance - both relations
        ///     <see cref="FontGlyphSheet.Verify"/> checks, and both false for a mispaired sheet.
        ///     </para>
        /// </remarks>
        /// <param name="font">The joined metrics and glyph sheet.</param>
        /// <param name="text">The text to draw.</param>
        /// <param name="ink">The colour to tint the masks with.</param>
        /// <param name="background">The colour behind the text.</param>
        /// <param name="zoom">The integer magnification, at least 1.</param>
        /// <param name="showBaselines">Whether to rule the baseline of each line.</param>
        /// <returns>The rendered block.</returns>
        public static Bitmap Render(FontGlyphSheet font, string? text, Color ink, Color background,
            int zoom, bool showBaselines) {
            if (font == null)
                throw new ArgumentNullException(nameof(font));

            zoom = Math.Max(1, zoom);
            Layout layout = Measure(font.Metrics, text);

            //The canvas box hangs its descent below the baseline, so the block has to be tall enough
            //for the last line's whole box rather than for its ascent.
            int lineHeight = Math.Max(1, font.Metrics.LineHeight);
            int blockWidth = Math.Max(1, layout.Width + font.CanvasWidth);
            int blockHeight = Math.Max(1, (layout.Lines - 1) * lineHeight + font.CanvasHeight);

            var picture = new Bitmap(blockWidth * zoom, blockHeight * zoom, PixelFormat.Format32bppArgb);
            using Graphics graphics = Graphics.FromImage(picture);

            graphics.Clear(background);
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.SmoothingMode = SmoothingMode.None;

            if (showBaselines) {
                using var rule = new Pen(Color.FromArgb(0x60, ink));
                for (int line = 0; line < layout.Lines; line++) {
                    int y = (line * lineHeight + font.Baseline) * zoom;
                    graphics.DrawLine(rule, 0, y, picture.Width, y);
                }
            }

            foreach (PlacedGlyph glyph in layout.Glyphs) {
                using Bitmap? drawn = font.RenderInk(glyph.Character, ink);
                if (drawn == null)
                    continue;

                SpriteFrameBox box = BoxOf(font, glyph);
                graphics.DrawImage(drawn,
                    new Rectangle(box.X * zoom, box.Y * zoom, drawn.Width * zoom, drawn.Height * zoom),
                    new Rectangle(0, 0, drawn.Width, drawn.Height), GraphicsUnit.Pixel);
            }

            return picture;
        }

        /// <summary>Where a placed glyph's ink lands, once its frame offset is added to the pen.</summary>
        /// <param name="font">The joined font.</param>
        /// <param name="glyph">The placed character.</param>
        /// <returns>The ink's top left in block coordinates.</returns>
        private static SpriteFrameBox BoxOf(FontGlyphSheet font, PlacedGlyph glyph) {
            SpriteFrame? frame = font.FrameFor(glyph.Character);
            int offsetX = frame?.OffsetX ?? 0;
            int offsetY = frame?.OffsetY ?? 0;
            return new SpriteFrameBox(glyph.PenX + offsetX, glyph.LineTop + offsetY);
        }

        /// <summary>An ink rectangle's origin in block coordinates.</summary>
        private readonly struct SpriteFrameBox {
            internal SpriteFrameBox(int x, int y) {
                X = x;
                Y = y;
            }

            internal int X { get; }

            internal int Y { get; }
        }
    }
}
