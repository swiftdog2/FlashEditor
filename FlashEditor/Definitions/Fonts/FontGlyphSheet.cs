using System;
using System.Drawing;
using System.Drawing.Imaging;
using FlashEditor.cache;
using FlashEditor.cache.sprites;

namespace FlashEditor.Definitions.Fonts {
    /// <summary>
    ///     One font as the client actually has it: index 13's metrics joined to index 8's glyph
    ///     sheet at the same id.
    /// </summary>
    /// <remarks>
    ///     <b>The pixels are not in index 13.</b> That index holds <c>Class197</c> metrics and
    ///     nothing drawable. <c>Class114.java:82,89</c> passes one id <c>i</c> to both archives -
    ///     <c>Class324.method3684(spritesArchive, i)</c> for the 256-frame sheet and
    ///     <c>Class119_Sub1.method2182(fontsArchive, i)</c> for the metrics - and
    ///     <c>InterfaceSettings.java:76,157</c> is where the two are opened. So a font is one asset
    ///     split across two indexes and neither half can be shown without the other.
    ///     <para>
    ///     <b>The join is proved per row, not by coverage.</b> Matching ids and matching name hashes
    ///     say the two indexes share an id space; they do not say the payload at that id is this
    ///     font's glyphs. <see cref="Verify"/> states four content relations that tie one record to
    ///     one sheet, and <c>RealCacheFontGlyphSheetTests</c> runs every font against every sheet to
    ///     see how many wrong pairings survive them. Measured over both supported caches: all 25
    ///     correct pairings pass and <b>none</b> of the 600 wrong ones do. That is the distinction
    ///     <c>CLAUDE.md</c> draws over the track-name join, which agreed on every aggregate and was
    ///     still keyed on the wrong field.
    ///     </para>
    ///     <para>
    ///     <b>The stored ink is near-white and means nothing.</b> Every sheet carries a two-entry
    ///     palette whose only colour is 0xFFFFFF, 0xFEFEFE or 0xFDFDFD, and no frame in any of them
    ///     sets the alpha bit, so a glyph is a one-bit mask that the client tints at draw time.
    ///     Rendering therefore takes the ink colour from the caller rather than from the palette -
    ///     drawing the stored colour would make every glyph in this editor white on white.
    ///     </para>
    /// </remarks>
    public sealed class FontGlyphSheet : IDisposable {
        private readonly SpriteDefinition sheet;

        /// <summary>
        ///     Joins two already decoded halves.
        /// </summary>
        /// <remarks>
        ///     Public so the pairing can be exercised against records built by hand. The kerned
        ///     layout is reachable no other way - no group in either supported cache sets the flag -
        ///     and a join whose only test is the cache it was measured on is a join with no test for
        ///     the shape the cache does not contain.
        /// </remarks>
        /// <param name="metrics">The index-13 record.</param>
        /// <param name="sheet">The index-8 sprite set claimed to draw it.</param>
        public FontGlyphSheet(FontDefinition metrics, SpriteDefinition sheet) {
            Metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            this.sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            FontId = metrics.Id;
        }

        /// <summary>The font id, which is the group id on index 13 and on index 8 alike.</summary>
        public int FontId { get; }

        /// <summary>The index-13 record.</summary>
        public FontDefinition Metrics { get; }

        /// <summary>The index-8 sprite set that draws it.</summary>
        public SpriteDefinition Sheet => sheet;

        /// <summary>Width of the sheet's canvas, which every glyph is placed within.</summary>
        public int CanvasWidth => sheet.width;

        /// <summary>Height of the sheet's canvas.</summary>
        public int CanvasHeight => sheet.height;

        /// <summary>
        ///     The baseline's row within the canvas.
        /// </summary>
        /// <remarks>
        ///     Derived from the join rather than stored: the canvas is exactly
        ///     <c>lineHeight + descent</c> rows tall in all 25 fonts of both caches, so the descent
        ///     hangs off the bottom of the canvas and the baseline sits at <c>height - descent</c>.
        ///     A glyph is drawn at its frame's own offset within that canvas, which is what puts it
        ///     on the baseline without anything having to compute a per-glyph origin.
        /// </remarks>
        public int Baseline => CanvasHeight - Metrics.Descent;

        /// <summary>Frames the sheet holds. A glyph sheet holds one per character code.</summary>
        public int FrameCount => sheet.GetFrameCount();

        /// <summary>
        ///     Whether the sheet is shaped like a glyph sheet at all.
        /// </summary>
        /// <remarks>
        ///     A sprite set at a font's id that is not 256 frames is not that font's glyphs, whatever
        ///     the name hash says, and every view here has to be able to say so rather than index
        ///     past the end of a shorter set.
        /// </remarks>
        public bool IsGlyphSheet => FrameCount == FontDefinition.CharacterCount;

        /// <summary>
        ///     The stored frame for a character code, or <c>null</c> when the sheet is too short.
        /// </summary>
        /// <param name="character">The character code, 0..255.</param>
        /// <returns>The frame, or null.</returns>
        public SpriteFrame? FrameFor(int character) {
            if (character < 0 || character >= FontDefinition.CharacterCount)
                throw new ArgumentOutOfRangeException(nameof(character), character,
                    "A font covers character codes 0.." + (FontDefinition.CharacterCount - 1) + ".");
            return character < FrameCount ? sheet.Frames[character] : null;
        }

        /// <summary>Whether a character has ink at all, as opposed to being an empty frame.</summary>
        /// <param name="character">The character code.</param>
        /// <returns>Whether the frame covers a non-zero area.</returns>
        public bool HasInk(int character) {
            SpriteFrame? frame = FrameFor(character);
            return frame != null && frame.SubWidth > 0 && frame.SubHeight > 0;
        }

        /// <summary>
        ///     Opens a font and the sheet that draws it.
        /// </summary>
        /// <remarks>
        ///     The sheet is optional and the metrics are not. An index-8 group missing at a font's id
        ///     is a cache defect worth showing the metrics for, so it comes back as a sheet-less
        ///     instance rather than as an exception - the panel then says the glyphs are unavailable
        ///     instead of showing an empty tab.
        /// </remarks>
        /// <param name="cache">The open cache.</param>
        /// <param name="metrics">The already decoded index-13 record.</param>
        /// <returns>The joined font, whose <see cref="Sheet"/> may be null-shaped.</returns>
        public static FontGlyphSheet? TryLoadSheet(RSCache cache, FontDefinition metrics) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (metrics == null)
                throw new ArgumentNullException(nameof(metrics));

            RSArchiveEntry entry = cache.GetReferenceTable(RSConstants.SPRITES_INDEX)
                .GetArchiveEntry(metrics.Id);
            if (entry == null)
                return null;

            int[] fileIds = entry.GetValidFileIds();
            if (fileIds.Length != 1)
                return null;

            var set = new SpriteDefinition();
            set.Decode(new JagStream(cache.ReadFileBytes(RSConstants.SPRITES_INDEX, metrics.Id, fileIds[0])));
            return new FontGlyphSheet(metrics, set);
        }

        /// <summary>
        ///     Loads both halves of a font by id.
        /// </summary>
        /// <param name="cache">The open cache.</param>
        /// <param name="fontId">The font id, which addresses both indexes.</param>
        /// <returns>The joined font, or null when index 8 carries no sheet at that id.</returns>
        public static FontGlyphSheet? Load(RSCache cache, int fontId) {
            return TryLoadSheet(cache, FontDefinition.Load(cache, fontId));
        }

        /// <summary>
        ///     Checks the four content relations that tie these metrics to this sheet.
        /// </summary>
        /// <remarks>
        ///     <b>Every one of them is a per-row statement, and the conjunction is what makes the
        ///     join self-proving.</b> Measured over both supported caches by pairing all 25 fonts
        ///     against all 25 sheets, identity pairings against wrong ones:
        ///     <list type="bullet">
        ///     <item>256 frames - 25/25 identity, 600/600 cross. Alone it says nothing; it is here
        ///     because everything below indexes by character code.</item>
        ///     <item><c>offsetX + subWidth &lt;= advance</c> for all 256 characters - 25/25 identity,
        ///     20/600 cross. The ink, at its own left bearing, fits inside the advance box. A
        ///     one-character shift within a font's own sheet breaks it on all 25.</item>
        ///     <item><c>lineHeight + descent == canvasHeight</c> - 25/25 identity, 36/600 cross. An
        ///     exact equality between a byte in index 13 and a short in index 8.</item>
        ///     <item><c>canvasWidth == max(advance)</c> - 25/25 identity, 44/600 cross. The second
        ///     exact equality.</item>
        ///     </list>
        ///     Together: <b>25 of 25 correct pairings pass and 0 of 600 wrong ones do.</b> Coverage
        ///     alone would not have earned that - the ascent relation
        ///     (<c>offsetY &gt;= canvasHeight - ascent - descent</c>) also holds on 25/25 and lets
        ///     325 of the 600 wrong pairings through, which is exactly the shape of the track-name
        ///     join that this project got wrong.
        /// </remarks>
        /// <returns>Why the pairing is not a font and its glyphs, or null when all four hold.</returns>
        public string? Verify() {
            if (!IsGlyphSheet)
                return "index 8 group " + FontId + " holds " + FrameCount + " frames, not " +
                       FontDefinition.CharacterCount + ", so it is not a glyph sheet";

            int widest = 0;
            for (int character = 0; character < FontDefinition.CharacterCount; character++) {
                SpriteFrame frame = sheet.Frames[character];
                int advance = Metrics.AdvanceOf(character);
                widest = Math.Max(widest, advance);

                if (frame.OffsetX + frame.SubWidth > advance)
                    return "character " + character + " draws " + frame.SubWidth + " pixels at x=" +
                           frame.OffsetX + ", past its advance of " + advance;
            }

            if (Metrics.LineHeight + Metrics.Descent != CanvasHeight)
                return "line height " + Metrics.LineHeight + " plus descent " + Metrics.Descent +
                       " is " + (Metrics.LineHeight + Metrics.Descent) + ", but the canvas is " +
                       CanvasHeight + " rows tall";

            if (widest != CanvasWidth)
                return "the widest advance is " + widest + " but the canvas is " + CanvasWidth +
                       " pixels wide";

            return null;
        }

        /// <summary>
        ///     Draws one character's ink, tinted, on a transparent bitmap the size of its frame.
        /// </summary>
        /// <remarks>
        ///     The frame rather than the canvas, so a caller can place it itself. Palette index 0 is
        ///     the transparent entry (<c>Class324.java:77-79</c>) and every other index is ink,
        ///     because these sheets have no alpha plane and exactly one colour.
        /// </remarks>
        /// <param name="character">The character code.</param>
        /// <param name="ink">The colour to draw the mask in.</param>
        /// <returns>The glyph, or null when the character has no ink.</returns>
        public Bitmap? RenderInk(int character, Color ink) {
            SpriteFrame? frame = FrameFor(character);
            if (frame == null || frame.SubWidth <= 0 || frame.SubHeight <= 0)
                return null;

            var glyph = new Bitmap(frame.SubWidth, frame.SubHeight, PixelFormat.Format32bppArgb);
            int argb = ink.ToArgb();

            for (int y = 0; y < frame.SubHeight; y++) {
                for (int x = 0; x < frame.SubWidth; x++) {
                    if (frame.PaletteIndices[x + y * frame.SubWidth] != 0)
                        glyph.SetPixel(x, y, Color.FromArgb(argb));
                }
            }

            return glyph;
        }

        /// <summary>Releases the sprite set's rasterised frames.</summary>
        /// <remarks>
        ///     The metrics own nothing unmanaged, so only the sheet is released. Nothing here ever
        ///     asks the sprite set to rasterise, but disposing it is still the contract.
        /// </remarks>
        public void Dispose() {
            sheet.Dispose();
        }
    }
}
