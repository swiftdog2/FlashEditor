using System;
using System.Collections.Generic;
using System.Drawing;
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
    ///     <b>Only <c>\n</c> breaks a line.</b> <see cref="FontTextLayout.Measure"/> places glyphs
    ///     and breaks on nothing else, which is what the client's own layout does with a stored
    ///     string; the interface draw path also carries a line step, so the client can lay a
    ///     paragraph out across several lines, but the rule that decides where it breaks is not
    ///     settled here. Not guessing is the safer error: an unwrapped line that runs past its box
    ///     is clipped and visibly too long, where an invented wrap silently shows a layout the game
    ///     never produces.
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
        /// <returns>Whether the cache's font was used.</returns>
        public bool Draw(Graphics graphics, string text, int fontId, Rectangle box, Color ink,
            int horizontal, int vertical) {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));

            if (string.IsNullOrEmpty(text) || fontId < 0)
                return false;

            FontGlyphSheet? sheet = SheetFor(fontId);
            if (sheet == null)
                return false;

            FontTextLayout.Layout layout = FontTextLayout.Measure(sheet.Metrics, text);
            if (layout.Glyphs.Count == 0)
                return true;

            int offsetX = horizontal switch {
                1 => (box.Width - layout.Width) / 2,
                2 => box.Width - layout.Width,
                _ => 0
            };

            int offsetY = vertical switch {
                1 => (box.Height - layout.Height) / 2,
                2 => box.Height - layout.Height,
                _ => 0
            };

            foreach (FontTextLayout.PlacedGlyph glyph in layout.Glyphs) {
                using Bitmap? rendered = sheet.RenderInk(glyph.Character, ink);
                if (rendered == null)
                    continue;

                SpriteFrame? frame = sheet.FrameFor(glyph.Character);
                if (frame == null)
                    continue;

                /* The frame's own offsets place the ink inside the glyph's box - a comma sits low
                   and a quote sits high - so they are added rather than the bitmap being drawn at
                   the pen position, which would sit every glyph on the same top edge. */
                graphics.DrawImageUnscaled(rendered,
                    box.X + offsetX + glyph.PenX + frame.OffsetX,
                    box.Y + offsetY + glyph.LineTop + frame.OffsetY);
            }

            return true;
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
