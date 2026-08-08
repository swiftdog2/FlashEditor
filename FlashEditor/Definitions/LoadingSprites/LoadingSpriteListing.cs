using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FlashEditor.cache;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;
using FlashEditor.Definitions.Editing;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     What the Loading Sprites tab draws for one group, and the note saying what it is a picture
    ///     of.
    /// </summary>
    /// <remarks>
    ///     Pixels rather than a <c>Bitmap</c> because this is built on the load worker and a bitmap is
    ///     a GDI+ handle the UI thread owns. The note travels with the pixels for the same reason the
    ///     tab carries one at all: the two halves of index 32 are drawn by two different routes, and
    ///     one of them is a layout this editor invented rather than anything the client draws.
    ///     <para>
    ///     <see cref="Pixels"/> is empty when the image could not be rendered, and <see cref="Note"/>
    ///     then says why. That is deliberately not a decode failure - the row keeps its metadata and
    ///     stays in the list, because a group that vanishes from a 26-row index reads as a bug in the
    ///     tab rather than as a refusal to guess at a colour model.
    ///     </para>
    /// </remarks>
    public sealed class LoadingSpritePreview {
        /// <summary>Binds rendered pixels to the geometry they are laid out in.</summary>
        /// <param name="width">The rendered width.</param>
        /// <param name="height">The rendered height.</param>
        /// <param name="pixels">Row-major ARGB pixels, or empty when there is no image.</param>
        /// <param name="note">What the picture is, or why there is not one.</param>
        public LoadingSpritePreview(int width, int height, int[] pixels, string note) {
            Width = width;
            Height = height;
            Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
            Note = note ?? string.Empty;
        }

        /// <summary>The rendered width in pixels.</summary>
        public int Width { get; }

        /// <summary>The rendered height in pixels.</summary>
        public int Height { get; }

        /// <summary>Row-major ARGB pixels, empty when nothing could be rendered.</summary>
        public int[] Pixels { get; }

        /// <summary>What the picture is, or why there is none.</summary>
        public string Note { get; }

        /// <summary>Whether there is anything to draw.</summary>
        public bool HasImage => Pixels.Length > 0 && Width > 0 && Height > 0;
    }

    /// <summary>
    ///     One index-32 group as a list row: its shape, its geometry, the picture the tab shows for
    ///     it, and the bytes it is stored as.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The row carries the stored bytes and not the decoded definition.</b> A glyph sheet
    ///     rasterises 256 frames to be drawn once, and holding all five sets' frames alive for the
    ///     life of the tab keeps 1280 bitmaps for a picture already flattened into
    ///     <see cref="Preview"/>. The stored bytes are what every action on the row actually needs -
    ///     export writes them, and replace compares against them.
    ///     </para>
    ///     <para>
    ///     <b>The colour path is the cache's own.</b> The JPEG preview comes from
    ///     <see cref="JpegRaster.ToArgb"/>, which reads the middle two components as Cb and Cr on the
    ///     file's own evidence. These images are four-component with no <c>JFIF APP0</c> and no
    ///     <c>Adobe APP14</c>, so every general-purpose decoder falls back to CMYK and renders a
    ///     recognisable, plausible, wrong picture - the failure that survives review because it looks
    ///     like an image. Nothing here may be swapped for a library decoder on the grounds that it is
    ///     shorter.
    ///     </para>
    /// </remarks>
    public sealed class LoadingSpriteListing : IDetailRow {
        /// <summary>How many glyphs the contact sheet lays out across.</summary>
        /// <remarks>
        ///     Sixteen, so a 256-frame sheet is a square whose row is the high nibble of the byte
        ///     value and whose column is the low nibble. Reading a glyph's index off the grid is then
        ///     possible by eye, which is the only reason to prefer a fixed width to a fitted one.
        /// </remarks>
        public const int ContactSheetColumns = 16;

        private LoadingSpriteListing(DefinitionAddress address, string? name, LoadingSpriteShape shape,
            int width, int height, int frames, byte[] stored, LoadingSpritePreview preview,
            IReadOnlyList<DetailField> fields) {
            Address = address;
            Name = name ?? string.Empty;
            Shape = shape;
            Width = width;
            Height = height;
            Frames = frames;
            StoredBytes = stored;
            Preview = preview;
            Fields = fields;
        }

        /// <summary>Where the record lives in the cache.</summary>
        public DefinitionAddress Address { get; }

        /// <summary>The group id, which is the record id on this index.</summary>
        public int GroupId => Address.GroupId;

        /// <summary>
        ///     The recovered group name, or empty.
        /// </summary>
        /// <remarks>
        ///     Empty rather than a placeholder id. Index 32 stores <c>hash(name)</c> and never the
        ///     name, so a name is only ever recovered by hashing a candidate and requiring an exact
        ///     match; the twenty-one image groups have matched nothing, and inventing a plausible
        ///     name for a loading screen is the mistake this cache rewards.
        /// </remarks>
        public string Name { get; }

        /// <summary>Which of the two payload formats the group holds.</summary>
        public LoadingSpriteShape Shape { get; }

        /// <summary>The shape in words, for the column that answers "which is this".</summary>
        public string ShapeName =>
            Shape == LoadingSpriteShape.JpegImage ? "JPEG image" : "Glyph sheet";

        /// <summary>The source width: the image's, or a glyph sheet's canvas.</summary>
        public int Width { get; }

        /// <summary>The source height: the image's, or a glyph sheet's canvas.</summary>
        public int Height { get; }

        /// <summary>1 for a JPEG, the frame count for a glyph sheet.</summary>
        public int Frames { get; }

        /// <summary>The group payload exactly as the cache stores it.</summary>
        public byte[] StoredBytes { get; }

        /// <summary>The picture the tab draws, and what it is a picture of.</summary>
        public LoadingSpritePreview Preview { get; }

        /// <summary>The source geometry as one string, for the grid.</summary>
        public string Geometry => Width + "x" + Height;

        /// <summary>How many bytes the group is stored as.</summary>
        public int StoredLength => StoredBytes.Length;

        /// <inheritdoc/>
        public string Summary =>
            "Group " + GroupId + (Name.Length == 0 ? string.Empty : " \"" + Name + "\"") + " - " +
            ShapeName + " " + Geometry + ", " + StoredLength.ToString("N0", CultureInfo.InvariantCulture) +
            " bytes stored";

        /// <inheritdoc/>
        public IReadOnlyList<DetailField> Fields { get; }

        /// <summary>
        ///     Builds a row from a group payload, rendering its picture on whichever path its shape
        ///     asks for.
        /// </summary>
        /// <remarks>
        ///     Called on the list panel's load worker, so everything expensive - the entropy decode,
        ///     the inverse DCT, the 256 glyph rasterisations - happens here and the UI thread only
        ///     ever copies finished pixels into a bitmap.
        /// </remarks>
        /// <param name="cache">The open cache, for the reference table the name is recovered from.</param>
        /// <param name="address">Where the payload came from.</param>
        /// <param name="payload">The stored group payload.</param>
        /// <returns>The row.</returns>
        public static LoadingSpriteListing Build(RSCache cache, DefinitionAddress address, JagStream payload) {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            var definition = new LoadingSpriteDefinition { Id = address.GroupId };
            try {
                definition.Decode(payload);

                string? name = SafeName(cache, address.GroupId);
                return definition.Shape == LoadingSpriteShape.JpegImage
                    ? FromJpeg(address, name, definition)
                    : FromSpriteSet(address, name, definition);
            }
            finally {
                //The rasterised frames are flattened into the preview above, so nothing below this
                //needs them and holding 1280 bitmaps for the life of the tab buys nothing.
                definition.Dispose();
            }
        }

        /// <summary>
        ///     The recovered name, or null, without letting a name lookup cost the row.
        /// </summary>
        /// <remarks>
        ///     A name is decoration on this tab and the picture is not. A reference table that will
        ///     not answer must not be the reason a group disappears from a 26-row list.
        /// </remarks>
        private static string? SafeName(RSCache cache, int groupId) {
            try {
                return LoadingSpriteNames.NameOf(cache, groupId);
            }
            catch (Exception) {
                return null;
            }
        }

        private static LoadingSpriteListing FromJpeg(DefinitionAddress address, string? name,
            LoadingSpriteDefinition definition) {
            JagexJpeg jpeg = definition.Jpeg!;
            var fields = new List<DetailField> {
                new DetailField("Shape", "JPEG image (payload opens FF D8)"),
                new DetailField("Frame header", "FF" + jpeg.FrameMarker.ToString("X2", CultureInfo.InvariantCulture) +
                                                (jpeg.IsBaseline ? " (baseline)" : " (not baseline)")),
                new DetailField("Size", jpeg.Width + "x" + jpeg.Height),
                new DetailField("Sample precision", jpeg.Precision + " bits"),
                new DetailField("Components", jpeg.Components.Count.ToString(CultureInfo.InvariantCulture)),
                new DetailField("Restart interval", jpeg.RestartInterval.ToString(CultureInfo.InvariantCulture)),
                new DetailField("Marker segments", jpeg.Segments.Count.ToString(CultureInfo.InvariantCulture)),
                new DetailField("Entropy-coded bytes",
                    jpeg.EntropyCodedData.Length.ToString("N0", CultureInfo.InvariantCulture))
            };

            foreach (JpegComponent component in jpeg.Components) {
                fields.Add(new DetailField("  component " + component.Id,
                    "sampling " + component.HorizontalSampling + "x" + component.VerticalSampling +
                    ", quantisation table " + component.QuantisationTableId));
            }

            LoadingSpritePreview preview;
            try {
                JpegRaster raster = BaselineJpegDecoder.Decode(jpeg);
                fields.Add(new DetailField("Scan consumed",
                    raster.ScanBytesConsumed + " of " + raster.ScanBytesAvailable + " bytes" +
                    (raster.ScanBytesConsumed == raster.ScanBytesAvailable
                        ? " (exact)"
                        : " (SHORT - the entropy decode desynchronised)")));

                if (raster.ComponentCount == 4 && raster.IsConstant(3)) {
                    fields.Add(new DetailField("Fourth component",
                        "flat at " + raster.Plane(3)[0] + ", so it carries no picture and is discarded"));
                }

                preview = new LoadingSpritePreview(raster.Width, raster.Height, raster.ToArgb(),
                    "Rendered through the cache's own YCbCr path. These files carry no JFIF and no Adobe " +
                    "marker, so a general-purpose decoder reads their four components as CMYK and produces a " +
                    "plausible, wrong picture.");
            }
            catch (InvalidDataException ex) {
                //Refused rather than guessed. A colour model this cache has not established would
                //come out as a picture, and a picture is exactly what nobody would question.
                fields.Add(new DetailField("Render", "refused: " + ex.Message));
                preview = new LoadingSpritePreview(jpeg.Width, jpeg.Height, Array.Empty<int>(),
                    "Not rendered: " + ex.Message);
            }

            return new LoadingSpriteListing(address, name, LoadingSpriteShape.JpegImage,
                jpeg.Width, jpeg.Height, 1, definition.StoredBytes, preview, fields);
        }

        private static LoadingSpriteListing FromSpriteSet(DefinitionAddress address, string? name,
            LoadingSpriteDefinition definition) {
            SpriteDefinition set = definition.SpriteSet!;
            int glyphColour = set.RenderPalette.Length > 1 ? set.RenderPalette[1] : 0xFFFFFF;

            var fields = new List<DetailField> {
                new DetailField("Shape", "Jagex sprite set (payload does not open FF D8)"),
                new DetailField("Canvas", set.width + "x" + set.height),
                new DetailField("Frames", set.GetFrameCount().ToString(CultureInfo.InvariantCulture)),
                new DetailField("Palette entries", set.PaletteStored.Length.ToString(CultureInfo.InvariantCulture)),
                new DetailField("Glyph colour", "#" + (glyphColour & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture)),
                new DetailField("Pixel-plane trailer",
                    set.PixelPlaneTrailer.Length + " byte(s) between the planes and the palette")
            };

            return new LoadingSpriteListing(address, name, LoadingSpriteShape.SpriteSet,
                set.width, set.height, set.GetFrameCount(), definition.StoredBytes,
                ContactSheet(set, glyphColour), fields);
        }

        /// <summary>
        ///     Lays every frame of a glyph sheet out in one picture.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     A contact sheet rather than a frame spinner because the question this tab has to answer
        ///     about a sprite-set group is whether it is a glyph sheet at all, and one frame at a time
        ///     answers that 256 times more slowly. The client never draws this layout - it draws one
        ///     glyph at a time, at a position the text run decides - so the sheet is stated as the
        ///     editor's own in the preview note.
        ///     </para>
        ///     <para>
        ///     The background is picked from the palette rather than fixed. A glyph sheet is one
        ///     colour plus a transparent index and the client recolours it at draw time, so a sheet
        ///     whose stored colour is white is invisible on white and one stored black is invisible on
        ///     black. Choosing by luminance is what keeps both legible without recolouring the glyphs,
        ///     which would be an edit to what is on screen.
        ///     </para>
        /// </remarks>
        /// <param name="set">The decoded sprite set.</param>
        /// <param name="glyphColour">The set's single non-transparent palette colour.</param>
        /// <returns>The contact sheet.</returns>
        private static LoadingSpritePreview ContactSheet(SpriteDefinition set, int glyphColour) {
            List<RSBufferedImage> frames = set.GetFrames();
            if (frames == null || frames.Count == 0) {
                return new LoadingSpritePreview(0, 0, Array.Empty<int>(),
                    "This sprite set holds no frames, so there is nothing to lay out.");
            }

            int cellWidth = Math.Max(1, frames.Max(frame => frame.GetWidth()));
            int cellHeight = Math.Max(1, frames.Max(frame => frame.GetHeight()));
            int columns = Math.Min(ContactSheetColumns, frames.Count);
            int rows = (frames.Count + columns - 1) / columns;

            int background = BackgroundFor(glyphColour);
            int sheetWidth = columns * cellWidth;
            int sheetHeight = rows * cellHeight;
            int[] pixels = new int[sheetWidth * sheetHeight];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = background;

            for (int id = 0; id < frames.Count; id++) {
                RSBufferedImage frame = frames[id];
                int[] source = frame.GetPixels();
                int originX = id % columns * cellWidth;
                int originY = id / columns * cellHeight;

                for (int y = 0; y < frame.GetHeight(); y++) {
                    for (int x = 0; x < frame.GetWidth(); x++) {
                        int argb = source[x + y * frame.GetWidth()];
                        int alpha = (argb >> 24) & 0xFF;
                        if (alpha == 0)
                            continue;

                        pixels[originX + x + (originY + y) * sheetWidth] =
                            alpha == 0xFF ? argb : Blend(background, argb, alpha);
                    }
                }
            }

            return new LoadingSpritePreview(sheetWidth, sheetHeight, pixels,
                "All " + frames.Count + " frames laid out " + columns + " across, on a background chosen for " +
                "contrast against the sheet's single palette colour. This layout is the editor's own - the " +
                "client draws one glyph at a time and recolours it.");
        }

        /// <summary>Picks a background the glyph colour is legible against.</summary>
        /// <param name="glyphColour">The palette's single colour, as 0xRRGGBB.</param>
        /// <returns>An opaque ARGB background.</returns>
        private static int BackgroundFor(int glyphColour) {
            int red = (glyphColour >> 16) & 0xFF;
            int green = (glyphColour >> 8) & 0xFF;
            int blue = glyphColour & 0xFF;
            int luminance = (red * 299 + green * 587 + blue * 114) / 1000;
            return luminance < 128 ? unchecked((int) 0xFFE8E8E8) : unchecked((int) 0xFF181818);
        }

        private static int Blend(int background, int foreground, int alpha) {
            int inverse = 255 - alpha;
            int red = (((foreground >> 16) & 0xFF) * alpha + ((background >> 16) & 0xFF) * inverse) / 255;
            int green = (((foreground >> 8) & 0xFF) * alpha + ((background >> 8) & 0xFF) * inverse) / 255;
            int blue = ((foreground & 0xFF) * alpha + (background & 0xFF) * inverse) / 255;
            return unchecked((int) 0xFF000000) | (red << 16) | (green << 8) | blue;
        }
    }

    /// <summary>
    ///     Index 32 as a definition list: one row per group, each saying which of the index's two
    ///     formats it holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>Read only, and not because the format is unknown.</b> Both halves round-trip - the
    ///     sweep in <c>RealCacheLoadingSpriteTests</c> re-encodes all twenty-six groups to their
    ///     stored bytes. There is simply nothing in a row a grid cell could edit: a JPEG's payload is
    ///     an entropy-coded scan and a glyph sheet's is 256 pixel planes, and neither is a number in a
    ///     column. Replacing a group's bytes wholesale is offered by the tab instead, where it can
    ///     state what it costs.
    ///     </para>
    ///     <para>
    ///     <b>One file per group.</b> Both of the client's readers reach a group through
    ///     <c>JS5Archive.method2733</c> (<c>JS5Archive.java:591-616</c>), which throws unless the
    ///     group holds exactly one file, so the group payload is the record and the file id is always
    ///     zero. <see cref="DefinitionListDescriptor{TRow}.Enumerate"/> is left as it is rather than
    ///     hard-coding that, so a cache that disagreed would show its extra files instead of hiding
    ///     them.
    ///     </para>
    /// </remarks>
    public sealed class LoadingSpriteListDescriptor : DefinitionListDescriptor<LoadingSpriteListing> {
        /// <inheritdoc/>
        public override int IndexId => RSConstants.LOADING_SPRITES;

        /// <inheritdoc/>
        public override string RowNoun => "loading sprite";

        /// <inheritdoc/>
        public override IReadOnlyList<DefinitionColumn> Columns { get; } = new[] {
            DefinitionColumn.Number<LoadingSpriteListing>("Group", row => row.GroupId, width: 60),
            /* The shape column is the point of the tab. The index is mixed and its constant is
               commented "in jpg format", so a user with no column here would read five of the
               twenty-six groups as broken JPEGs. */
            DefinitionColumn.Text<LoadingSpriteListing>("Shape", row => row.ShapeName, width: 110),
            DefinitionColumn.Text<LoadingSpriteListing>("Name", row => row.Name, width: 170),
            DefinitionColumn.Text<LoadingSpriteListing>("Size", row => row.Geometry, width: 100),
            DefinitionColumn.Number<LoadingSpriteListing>("Frames", row => row.Frames, width: 70),
            DefinitionColumn.Number<LoadingSpriteListing>("Stored bytes", row => row.StoredLength, width: 110)
        };

        /// <inheritdoc/>
        public override LoadingSpriteListing Decode(RSCache cache, DefinitionAddress address, JagStream payload) {
            return LoadingSpriteListing.Build(cache, address, payload);
        }

        /// <inheritdoc/>
        public override DefinitionAddress AddressOf(LoadingSpriteListing row) {
            if (row == null)
                throw new ArgumentNullException(nameof(row));

            return row.Address;
        }
    }
}
