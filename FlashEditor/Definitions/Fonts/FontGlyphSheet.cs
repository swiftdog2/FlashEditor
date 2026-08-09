using System;
using System.Drawing;
using System.Drawing.Imaging;
using FlashEditor.Cache;
using FlashEditor.Definitions.Sprites;
using FlashEditor.IO;

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
    ///     font's glyphs. Four content relations tie one record to one sheet, and
    ///     <c>RealCacheFontGlyphSheetTests</c> runs every font against every sheet to see how many
    ///     wrong pairings survive them. Measured over both supported caches: all 25 correct pairings
    ///     pass and <b>none</b> of the 600 wrong ones do. That is the distinction <c>CLAUDE.md</c>
    ///     draws over the track-name join, which agreed on every aggregate and was still keyed on
    ///     the wrong field.
    ///     </para>
    ///     <para>
    ///     <b>Those four relations are not all the same kind of claim, and they are split by which
    ///     kind they are.</b> <see cref="JoinFailure"/> holds the ones with a reader in the 637
    ///     client, and answering it is a statement that this sheet is not this font's glyphs.
    ///     <see cref="Irregularity"/> holds the ones that are only <i>observed</i> to hold on all 25
    ///     fonts of both supported caches, and answering it is a statement that a pairing is
    ///     unusual. The line matters because there are exactly two supported caches, so "holds in
    ///     both" is a sample of two - and a false negative on valid data is worse than a loose
    ///     check, since a user cannot tell one from a real defect.
    ///     <list type="table">
    ///     <item><term>256 frames - <b>client-backed</b></term><description>Every reader indexes by
    ///     character code masked to a byte (<c>Class197.java:193</c>), and the client pairs one id
    ///     to both archives (<c>Class114.java:82,89</c>). A shorter set puts it out of
    ///     bounds.</description></item>
    ///     <item><term><c>offsetX + subWidth &lt;= advance</c> - <b>client-backed</b></term>
    ///     <description>The pen is stepped by the advance table alone and the glyph is drawn at the
    ///     pen (<c>RSFont.java:576,585,599</c>), so this is what keeps consecutive glyphs from
    ///     overlapping. Nothing measures the ink.</description></item>
    ///     <item><term><c>lineHeight + descent == canvasHeight</c> - <b>client-backed</b></term>
    ///     <description>Both draw paths subtract the line height from the y they are handed before
    ///     any glyph is placed (<c>RSFont.java:190</c> and <c>:483</c>), and that y is the baseline
    ///     - every alignment branch at <c>:421-434</c> computes it as <c>top + ascent</c> or
    ///     <c>bottom - descent</c>. So the canvas top is <c>baseline - lineHeight</c> and the ink
    ///     sits at the frame's own offset inside it, which lands on the baseline only when the
    ///     canvas is <c>lineHeight + descent</c> rows tall.</description></item>
    ///     <item><term><c>canvasWidth == max(advance)</c> - <b>observed only</b></term>
    ///     <description>Exact on all 25 fonts of both caches and it discriminates well, but the
    ///     client never reads a glyph sheet's canvas width when drawing text - <c>method3689</c> is
    ///     the only reader of it and belongs to the cursor path. It is how these sheets were packed,
    ///     not something a font has to satisfy, so it is advisory.</description></item>
    ///     </list>
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
        ///     Why this sheet cannot be these metrics' glyphs, or <c>null</c> when nothing rules it out.
        /// </summary>
        /// <remarks>
        ///     <b>Only the relations with a reader in the client, so that answering this is always a
        ///     defect and never a peculiarity.</b> Each is cited in the type's own remarks. Measured
        ///     over both supported caches by pairing all 25 fonts against all 25 sheets:
        ///     <list type="bullet">
        ///     <item>256 frames - 25/25 identity, 600/600 cross. Alone it says nothing; it is a
        ///     precondition, because everything below indexes by character code.</item>
        ///     <item><c>offsetX + subWidth &lt;= advance</c> for all 256 characters - 25/25 identity,
        ///     20/600 cross. A one-character shift within a font's <i>own</i> sheet breaks it on all
        ///     25, so it is tight rather than merely loose.</item>
        ///     <item><c>lineHeight + descent == canvasHeight</c> - 25/25 identity, 36/600 cross.</item>
        ///     </list>
        ///     What was deliberately left out: the ascent relation
        ///     (<c>offsetY &gt;= canvasHeight - ascent - descent</c>) holds on 25/25 and lets 325 of
        ///     the 600 wrong pairings through, so a join resting on it would have looked conclusive
        ///     and been worth almost nothing - the shape of the track-name join this project got
        ///     wrong. And <see cref="Irregularity"/>, which discriminates well and has no reader.
        /// </remarks>
        /// <returns>The reason, or <c>null</c>.</returns>
        public string? JoinFailure() {
            if (!IsGlyphSheet)
                return "index 8 group " + FontId + " holds " + FrameCount + " frames, not " +
                       FontDefinition.CharacterCount + ", so it is not a glyph sheet";

            for (int character = 0; character < FontDefinition.CharacterCount; character++) {
                SpriteFrame frame = sheet.Frames[character];
                int advance = Metrics.AdvanceOf(character);

                if (frame.OffsetX + frame.SubWidth > advance)
                    return "character " + character + " draws " + frame.SubWidth + " pixels at x=" +
                           frame.OffsetX + ", past its advance of " + advance;
            }

            if (Metrics.LineHeight + Metrics.Descent != CanvasHeight)
                return "line height " + Metrics.LineHeight + " plus descent " + Metrics.Descent +
                       " is " + (Metrics.LineHeight + Metrics.Descent) + ", but the canvas is " +
                       CanvasHeight + " rows tall, so the client's y -= lineHeight would not put the " +
                       "ink on the baseline (RSFont.java:190,483)";

            return null;
        }

        /// <summary>
        ///     What is unusual about this pairing, or <c>null</c> when nothing is.
        /// </summary>
        /// <remarks>
        ///     <b>Not a join failure, and callers must not present it as one.</b> The relation here
        ///     is exact on all 25 fonts of both supported caches and rejects 44 of 600 wrong
        ///     pairings on its own - but there are only two supported caches, so that is a sample of
        ///     two, and the client never reads a glyph sheet's canvas width when drawing text. A
        ///     third cache whose sheet were packed one pixel wider would be perfectly playable, and
        ///     reporting it as "not joined" would be a false negative the user could not tell from a
        ///     real defect.
        ///     <para>
        ///     Kept rather than dropped because it is still worth seeing: it is the only signal this
        ///     project has that a font's sheet was repacked by something other than Jagex's packer.
        ///     </para>
        /// </remarks>
        /// <returns>The observation, or <c>null</c>.</returns>
        public string? Irregularity() {
            if (!IsGlyphSheet)
                return null;

            int widest = 0;
            for (int character = 0; character < FontDefinition.CharacterCount; character++)
                widest = Math.Max(widest, Metrics.AdvanceOf(character));

            if (widest != CanvasWidth)
                return "the widest advance is " + widest + " but the canvas is " + CanvasWidth +
                       " pixels wide; every font in both supported caches has these equal, though " +
                       "the client never reads the canvas width when drawing text";

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
