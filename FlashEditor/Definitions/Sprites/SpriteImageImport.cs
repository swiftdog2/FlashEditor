using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlashEditor.cache.sprites;

namespace FlashEditor.Definitions.Sprites {
    /// <summary>
    ///     What converting one picture into an index-8 sprite set cost, alongside the set itself.
    /// </summary>
    /// <remarks>
    ///     Every field here is something the conversion had to decide and the user cannot see by
    ///     looking at the result. An index-8 sprite is a palette of at most 255 colours, so any
    ///     picture with more than that comes out approximated, and a picture with soft edges either
    ///     gains a plane the format calls optional or loses its translucency. Both are reported
    ///     before anything is written rather than discovered afterwards.
    /// </remarks>
    public sealed class SpriteImageImport {
        /// <summary>The sprite set built from the picture, in the stored form the encoder reads.</summary>
        public required SpriteDefinition Set { get; init; }

        /// <summary>Distinct storable colours the picture held, counting black and 0x000001 as one.</summary>
        /// <remarks>
        ///     Counted after the black promotion, because the two spellings are the same colour once
        ///     drawn - see <see cref="SpriteImageImporter.StorableColour"/>. Counting them apart
        ///     would report 256 colours for a picture that fits a 255 entry palette exactly.
        /// </remarks>
        public required int SourceColours { get; init; }

        /// <summary>Colours in the palette actually written, excluding the reserved entry 0.</summary>
        public required int PaletteColours { get; init; }

        /// <summary>Whether colours had to be merged to fit the palette.</summary>
        public bool Quantised => PaletteColours < SourceColours;

        /// <summary>
        ///     The largest single-channel difference between a source colour and the palette entry
        ///     it was mapped to, out of 255.
        /// </summary>
        /// <remarks>
        ///     A per-channel maximum rather than a mean. A mean error is small for any picture with
        ///     a large flat area and says nothing about the one gradient that got banded, which is
        ///     the thing a user needs to decide whether to accept the import.
        /// </remarks>
        public required int WorstChannelError { get; init; }

        /// <summary>Whether the frame carries an alpha plane.</summary>
        public required bool CarriesAnAlphaPlane { get; init; }

        /// <summary>
        ///     Pixels holding the black the format spells 0x000001, whichever spelling the file used.
        /// </summary>
        /// <remarks>
        ///     Reported because it is the case a reviewer needs to be able to check: pure black is
        ///     the one colour whose stored spelling the format changes, and a count of zero on a
        ///     picture full of black outlines says the promotion went to the wrong place.
        /// </remarks>
        public required int BlackPixels { get; init; }

        /// <summary>Pixels left transparent, which are the ones addressing palette entry 0.</summary>
        public required int TransparentPixels { get; init; }

        /// <summary>A one-line summary for the status strip.</summary>
        /// <returns>The summary.</returns>
        public string Describe() {
            string palette = Quantised
                ? $"{SourceColours} colours quantised to {PaletteColours} (worst channel error {WorstChannelError}/255)"
                : $"{PaletteColours} colours, exact";
            return $"{Set.width}x{Set.height}, {palette}, " +
                   (CarriesAnAlphaPlane ? "alpha plane written" : "no alpha plane");
        }
    }

    /// <summary>
    ///     Builds an index-8 sprite set out of a PNG, JPEG or BMP.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The pixel formats are stated, not inherited.</b> The source is read through
    ///     <c>LockBits</c> as <see cref="PixelFormat.Format32bppArgb"/> - straight,
    ///     un-premultiplied ARGB - whichever format the file itself is in, so an indexed PNG, a
    ///     24-bit JPEG and a 32-bit BMP all arrive in one layout and GDI+ does the conversion.
    ///     Deliberately not <see cref="PixelFormat.Format32bppPArgb"/>: <c>DirectBitmap</c> declares
    ///     its buffer premultiplied while <c>RSBufferedImage.SetRGB</c> writes straight ARGB into
    ///     it, and reading a source premultiplied here would mean two different conventions meeting
    ///     in the middle of the import with nothing to say which one won. Every alpha value in this
    ///     file is straight.
    ///     </para>
    ///     <para>
    ///     <b>Quantisation is median cut, and it is reported rather than silent.</b> The format
    ///     stores <c>paletteSize - 1</c> in one byte with entry 0 reserved for transparency, so a
    ///     frame can address 255 colours and no more. Refusing anything larger was the alternative
    ///     and was rejected: a PNG exported from this editor and touched in any paint program comes
    ///     back with antialiased edges and several thousand colours, so refusal would reject the
    ///     editor's own round trip. Median cut was chosen over a fixed colour cube because these are
    ///     small pieces of UI art whose colours cluster in a few narrow bands - a cube spends most
    ///     of its entries on colours the picture does not contain - and over k-means because it is
    ///     deterministic with no seed, which is what lets a test assert an exact palette. Boxes are
    ///     split on the widest channel at the pixel-weighted median, and each box's representative
    ///     is its pixel-weighted mean.
    ///     </para>
    ///     <para>
    ///     <b>Pure black is stored as 0x000001.</b> The client promotes a palette entry of zero to
    ///     one on read (<c>Class324.java:76-79</c>) precisely so that "the palette value is zero"
    ///     can mean "transparent" and nothing else, so both spellings draw the same near-black and
    ///     both occur in shipped palettes - 1337 entries stored as 0x000000 against 73 stored as
    ///     0x000001 in the vanilla b639 capture, 1334 against 74 in the repack. Writing the promoted
    ///     spelling makes the stored palette equal the palette that will be drawn, so exporting a
    ///     set as PNG and importing it back is a fixed point in colour; writing 0x000000 would come
    ///     back one unit brighter every round trip. The choice never reaches the decoder, which
    ///     still keeps whichever spelling a shipped file uses.
    ///     </para>
    ///     <para>
    ///     <b>An alpha plane is written only when the picture needs one.</b> The flag is optional
    ///     and a plane of nothing but 0xFF draws exactly like no plane at all - the client discards
    ///     one on load for that reason (<c>Class324.java:127-129</c>) - while doubling the frame's
    ///     bytes. Shipped data agrees that a plane is the exception: 180 of 11,177 frames in the
    ///     vanilla capture carry one at all. So a picture whose every pixel is either fully opaque
    ///     or fully transparent is written with no plane, expressing its transparency through
    ///     palette entry 0, and only a picture with a genuinely partial alpha gets a plane. This is
    ///     a choice about bytes nobody has written yet and says nothing about the decoder, which
    ///     still keeps a redundant plane a shipped file carries rather than inferring it away.
    ///     </para>
    ///     <para>
    ///     <b>The traversal flag is written clear.</b> A new frame has no stored flag to preserve,
    ///     and row-major is both the majority in the shipped data and the order this code writes.
    ///     Nothing here recomputes the flag of an existing frame; the import replaces a set rather
    ///     than editing one, and the encoder's rule that a stored flag is kept verbatim is untouched.
    ///     </para>
    /// </remarks>
    public static partial class SpriteImageImporter {
        /// <summary>The most colours a frame can address, entry 0 being the transparent slot.</summary>
        /// <remarks>
        ///     <c>paletteSize - 1</c> is written as one unsigned byte (<c>Class324.java:55</c>), so
        ///     the palette holds at most 256 entries and 255 of them are colours.
        /// </remarks>
        public const int MaxColours = 255;

        /// <summary>The largest canvas or plane dimension the format can state.</summary>
        /// <remarks>Every dimension is an unsigned short (<c>Class324.java:53-54, 58-70</c>).</remarks>
        public const int MaxDimension = 0xFFFF;

        /// <summary>File extensions the import treats as a picture rather than as a sprite set.</summary>
        /// <remarks>
        ///     Extension rather than content sniffing, so the same file always takes the same path.
        ///     A sprite set has no magic number at all - it is located from the end of the file
        ///     backwards - so "does this parse as a sprite set" is not a reliable discriminator and
        ///     guessing wrongly would either quantise a cache file or store a PNG as one.
        /// </remarks>
        public static readonly string[] PictureExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        /// <summary>The picker filter, listing both the pictures and the cache's own container.</summary>
        public const string FileFilter =
            "Picture (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|" +
            "Sprite set (*.dat)|*.dat|All files (*.*)|*.*";

        /// <summary>The picker filter for a target that only a picture can describe.</summary>
        /// <remarks>
        ///     One frame of a set, where a <c>.dat</c> would be a whole set and there is nothing
        ///     sensible to do with it. Offering the filter and then refusing the file is worse than
        ///     not offering it.
        /// </remarks>
        public const string PictureFilter =
            "Picture (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*";

        /// <summary>Whether a path names a picture the importer will convert.</summary>
        /// <param name="path">The chosen file.</param>
        /// <returns>Whether to take the conversion path rather than storing the bytes verbatim.</returns>
        public static bool LooksLikeAPicture(string path) {
            if (string.IsNullOrEmpty(path))
                return false;

            string extension = System.IO.Path.GetExtension(path);
            foreach (string known in PictureExtensions)
                if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>
        ///     Converts a picture into a one-frame sprite set filling its own canvas.
        /// </summary>
        /// <remarks>
        ///     One frame at offset 0,0 on a canvas the size of the picture. The alternative - fitting
        ///     the picture into the canvas the replaced set declared - was rejected because a set's
        ///     canvas is frequently larger than any frame in it and there is nothing in a picture
        ///     that says where inside such a canvas it belongs; guessing would move artwork silently.
        /// </remarks>
        /// <param name="image">The decoded picture. Not disposed here.</param>
        /// <returns>The set and what the conversion cost.</returns>
        /// <exception cref="ArgumentNullException">The image is null.</exception>
        /// <exception cref="InvalidOperationException">The picture cannot be expressed by the format.</exception>
        public static SpriteImageImport FromImage(Image image) {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            int[] pixels = ReadStraightArgb(image, out int width, out int height);

            if (width <= 0 || height <= 0)
                throw new InvalidOperationException("That picture has no pixels.");
            if (width > MaxDimension || height > MaxDimension)
                throw new InvalidOperationException(
                    $"A sprite frame states its size in unsigned shorts, so {width}x{height} cannot be stored - " +
                    $"the largest is {MaxDimension}x{MaxDimension}.");

            //Harvested after the black promotion, so 0x000000 and 0x000001 are one colour rather
            //than two that would draw identically and waste a palette entry apiece.
            Dictionary<int, long> counts = Harvest(pixels, out bool needsAlphaPlane, out int transparentPixels);

            int[] palette = BuildPalette(counts, out int worstChannelError);
            byte[] indices = MapPixels(pixels, palette, out int blackPixels);

            var frame = new SpriteFrame {
                OffsetX = 0,
                OffsetY = 0,
                SubWidth = width,
                SubHeight = height,
                //Row-major, and an alpha plane only if the picture has a pixel that needs one.
                Flags = needsAlphaPlane ? SpriteFrame.FlagAlpha : 0,
                PaletteIndices = indices,
                Alpha = needsAlphaPlane ? AlphaPlane(pixels) : null
            };

            //Entry 0 is reserved and never stored, so the written palette is one longer than the
            //colour count - the same relationship Decode reads back out of the paletteSize byte.
            int[] stored = new int[palette.Length + 1];
            Array.Copy(palette, 0, stored, 1, palette.Length);

            return new SpriteImageImport {
                Set = SpriteDefinition.FromFrames(width, height, stored, new[] { frame }),
                SourceColours = counts.Count,
                PaletteColours = palette.Length,
                WorstChannelError = worstChannelError,
                CarriesAnAlphaPlane = needsAlphaPlane,
                BlackPixels = blackPixels,
                TransparentPixels = transparentPixels
            };
        }

        /// <summary>
        ///     The spelling a colour is stored under, promoting pure black to 0x000001.
        /// </summary>
        /// <remarks>
        ///     See the type remarks. Both spellings are legal and both are shipped; this one is
        ///     chosen because it equals what the client will draw, which keeps an export and a
        ///     re-import in agreement.
        /// </remarks>
        /// <param name="rgb">The 24-bit colour.</param>
        /// <returns>The colour to store.</returns>
        public static int StorableColour(int rgb) {
            return rgb == 0 ? 1 : rgb;
        }

        /// <summary>
        ///     Reads any image into straight, un-premultiplied 32-bit ARGB.
        /// </summary>
        /// <remarks>
        ///     <c>LockBits</c> is asked for a format the bitmap need not already be in, which is
        ///     what makes GDI+ convert rather than us. Passing the bitmap's own format instead would
        ///     hand back an indexed or 24-bit buffer and every subsequent line would be reading the
        ///     wrong number of bytes per pixel.
        ///     <para>
        ///     The rows are copied one at a time because the stride <c>LockBits</c> reports may be
        ///     wider than the row, and a single copy of <c>width * height</c> ints across a padded
        ///     stride shears the picture progressively down its own height.
        ///     </para>
        /// </remarks>
        /// <param name="image">The picture.</param>
        /// <param name="width">Receives its width.</param>
        /// <param name="height">Receives its height.</param>
        /// <returns>One int per pixel, row-major, in 0xAARRGGBB.</returns>
        public static int[] ReadStraightArgb(Image image, out int width, out int height) {
            if (image == null)
                throw new ArgumentNullException(nameof(image));

            //Copied only when the source is not already a Bitmap. LockBits converts whatever format
            //the bitmap is in, so a copy would buy nothing and new Bitmap(Image) is a redraw - one
            //more resampling step between the file and the palette for no reason.
            Bitmap bitmap = image as Bitmap ?? new Bitmap(image);
            bool owned = !ReferenceEquals(bitmap, image);
            try {
                width = bitmap.Width;
                height = bitmap.Height;
                if (width <= 0 || height <= 0)
                    return Array.Empty<int>();

                int[] pixels = new int[width * height];
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try {
                    for (int y = 0; y < height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width, width);
                }
                finally {
                    bitmap.UnlockBits(data);
                }

                return pixels;
            }
            finally {
                if (owned)
                    bitmap.Dispose();
            }
        }

        /// <summary>
        ///     Chooses the palette: every colour when they fit, a median cut when they do not.
        /// </summary>
        /// <param name="counts">Distinct storable colours and how many pixels hold each.</param>
        /// <param name="worstChannelError">Receives the largest per-channel approximation error.</param>
        /// <returns>The colours, entry 0 not included.</returns>
        private static int[] BuildPalette(Dictionary<int, long> counts, out int worstChannelError) {
            worstChannelError = 0;

            if (counts.Count == 0)
                return Array.Empty<int>();

            var colours = new ColourCount[counts.Count];
            int at = 0;
            foreach (KeyValuePair<int, long> entry in counts)
                colours[at++] = new ColourCount(entry.Key, entry.Value);

            //Sorted by colour so the palette is a function of the picture alone. A dictionary's
            //enumeration order is not contracted, and a palette that depended on it would make the
            //bytes an import writes differ between runs of the same file.
            Array.Sort(colours, (left, right) => left.Rgb.CompareTo(right.Rgb));

            if (colours.Length <= MaxColours) {
                int[] exact = new int[colours.Length];
                for (int i = 0; i < colours.Length; i++)
                    exact[i] = colours[i].Rgb;
                return exact;
            }

            int[] palette = MedianCut(colours, MaxColours);
            worstChannelError = WorstError(colours, palette);
            return palette;
        }

        /// <summary>
        ///     Median cut: split the colour cloud into boxes and take each box's weighted mean.
        /// </summary>
        /// <remarks>
        ///     The box to split next is the one whose widest channel spans the most, so a picture
        ///     with one wide gradient and a dozen flat colours spends its entries on the gradient.
        ///     The split point is the pixel-weighted median rather than the midpoint of the range,
        ///     which is what stops a handful of outlying pixels claiming half a box.
        /// </remarks>
        /// <param name="colours">Distinct colours with pixel counts, sorted by colour.</param>
        /// <param name="target">How many colours to end with.</param>
        /// <returns>The palette.</returns>
        private static int[] MedianCut(ColourCount[] colours, int target) {
            var boxes = new List<ColourBox> { ColourBox.Over(colours, 0, colours.Length) };

            while (boxes.Count < target) {
                int widest = -1;
                int chosen = -1;
                for (int i = 0; i < boxes.Count; i++) {
                    if (boxes[i].Count < 2)
                        continue;
                    int span = boxes[i].WidestSpan;
                    if (span > widest) {
                        widest = span;
                        chosen = i;
                    }
                }

                //Every remaining box holds one colour, or several spellings of the same one, so
                //there is nothing left to divide and the palette comes out shorter than asked for.
                //Splitting anyway would emit duplicate entries and spend the budget on nothing.
                if (chosen < 0 || widest <= 0)
                    break;

                ColourBox box = boxes[chosen];
                int split = Split(colours, box);
                boxes[chosen] = ColourBox.Over(colours, box.Start, split - box.Start);
                boxes.Add(ColourBox.Over(colours, split, box.Start + box.Count - split));
            }

            var palette = new int[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
                palette[i] = StorableColour(boxes[i].Representative(colours));

            //Sorted so the palette is stated by the picture rather than by the order the boxes
            //happened to be split in, which a later change to the split order would otherwise move.
            Array.Sort(palette);
            return palette;
        }

        /// <summary>
        ///     Sorts a box's colours on its widest channel and returns the pixel-weighted median.
        /// </summary>
        /// <param name="colours">The backing array, sorted in place over the box's range only.</param>
        /// <param name="box">The box to divide.</param>
        /// <returns>The index the second half starts at, always inside the box.</returns>
        private static int Split(ColourCount[] colours, ColourBox box) {
            int shift = box.WidestShift;
            Array.Sort(colours, box.Start, box.Count,
                Comparer<ColourCount>.Create((left, right) => {
                    int order = ((left.Rgb >> shift) & 0xFF).CompareTo((right.Rgb >> shift) & 0xFF);
                    //Tie broken on the whole colour, so equal channel values still sort stably and
                    //the split point does not depend on Array.Sort's introsort partitioning.
                    return order != 0 ? order : left.Rgb.CompareTo(right.Rgb);
                }));

            long half = box.Pixels / 2;
            long running = 0;
            for (int i = box.Start; i < box.Start + box.Count - 1; i++) {
                running += colours[i].Pixels;
                if (running >= half)
                    return i + 1;
            }

            //Reached only when one colour holds every pixel in the box, which still has to divide.
            return box.Start + box.Count - 1;
        }

        /// <summary>The largest per-channel gap between a source colour and its nearest entry.</summary>
        /// <param name="colours">The distinct source colours.</param>
        /// <param name="palette">The chosen palette.</param>
        /// <returns>The worst error, 0 to 255.</returns>
        private static int WorstError(ColourCount[] colours, int[] palette) {
            int worst = 0;
            foreach (ColourCount colour in colours) {
                int nearest = palette[NearestIndex(colour.Rgb, palette)];
                for (int shift = 0; shift <= 16; shift += 8) {
                    int gap = Math.Abs(((colour.Rgb >> shift) & 0xFF) - ((nearest >> shift) & 0xFF));
                    if (gap > worst)
                        worst = gap;
                }
            }
            return worst;
        }

        /// <summary>
        ///     Turns every pixel into a palette index, entry 0 meaning transparent.
        /// </summary>
        /// <remarks>
        ///     A fully transparent pixel takes entry 0 whatever colour its file recorded under the
        ///     transparency, which is what stops an editor's throwaway background colour claiming a
        ///     palette entry. Every other pixel takes an entry of 1 or above: entry 0 is the
        ///     transparent slot with no colour stored for it at all, so an opaque pixel pointed at
        ///     it would vanish where there is no alpha plane and come out black where there is.
        /// </remarks>
        /// <param name="pixels">Straight ARGB, row-major.</param>
        /// <param name="palette">The colours, entry 0 not included.</param>
        /// <param name="blackPixels">Receives how many pixels resolved to the promoted black.</param>
        /// <returns>One index per pixel.</returns>
        private static byte[] MapPixels(int[] pixels, int[] palette, out int blackPixels) {
            blackPixels = 0;
            byte[] indices = new byte[pixels.Length];
            var resolved = new Dictionary<int, byte>();

            for (int i = 0; i < pixels.Length; i++) {
                int alpha = (pixels[i] >> 24) & 0xFF;
                if (alpha == 0)
                    continue;

                int colour = StorableColour(pixels[i] & 0xFFFFFF);
                if (!resolved.TryGetValue(colour, out byte index)) {
                    index = (byte) (NearestIndex(colour, palette) + 1);
                    resolved[colour] = index;
                }

                indices[i] = index;
                //Counted from the source colour rather than from the entry it landed on, so a
                //near-black produced by quantising is not reported as a black the picture held.
                if (colour == 1)
                    blackPixels++;
            }

            return indices;
        }

        /// <summary>The palette entry nearest a colour by squared distance in RGB.</summary>
        /// <param name="rgb">The colour to place.</param>
        /// <param name="palette">The colours, entry 0 not included.</param>
        /// <returns>Its index within <paramref name="palette"/>.</returns>
        private static int NearestIndex(int rgb, int[] palette) {
            int red = (rgb >> 16) & 0xFF;
            int green = (rgb >> 8) & 0xFF;
            int blue = rgb & 0xFF;

            int best = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < palette.Length; i++) {
                int dr = red - ((palette[i] >> 16) & 0xFF);
                int dg = green - ((palette[i] >> 8) & 0xFF);
                int db = blue - (palette[i] & 0xFF);
                int distance = dr * dr + dg * dg + db * db;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = i;
                if (distance == 0)
                    break;
            }

            return best;
        }

        /// <summary>The alpha channel of every pixel, in the frame's canonical layout.</summary>
        /// <param name="pixels">Straight ARGB, row-major.</param>
        /// <returns>One byte per pixel.</returns>
        private static byte[] AlphaPlane(int[] pixels) {
            byte[] plane = new byte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                plane[i] = (byte) ((pixels[i] >> 24) & 0xFF);
            return plane;
        }

        /// <summary>One distinct colour and how many pixels hold it.</summary>
        private readonly struct ColourCount {
            /// <summary>Builds a colour and its weight.</summary>
            /// <param name="rgb">The 24-bit colour, already promoted.</param>
            /// <param name="pixels">How many pixels hold it.</param>
            public ColourCount(int rgb, long pixels) {
                Rgb = rgb;
                Pixels = pixels;
            }

            /// <summary>The 24-bit colour.</summary>
            public int Rgb { get; }

            /// <summary>How many pixels hold it.</summary>
            public long Pixels { get; }
        }

        /// <summary>A contiguous run of the colour array, with the bounds of the colours in it.</summary>
        /// <remarks>
        ///     A range rather than its own list, so a split sorts only the colours it divides. Held
        ///     as a struct because a median cut to 255 entries builds 255 of these and none of them
        ///     outlives the loop.
        /// </remarks>
        private readonly struct ColourBox {
            private readonly int _redSpan;
            private readonly int _greenSpan;
            private readonly int _blueSpan;

            private ColourBox(int start, int count, long pixels, int redSpan, int greenSpan, int blueSpan) {
                Start = start;
                Count = count;
                Pixels = pixels;
                _redSpan = redSpan;
                _greenSpan = greenSpan;
                _blueSpan = blueSpan;
            }

            /// <summary>First index of the run.</summary>
            public int Start { get; }

            /// <summary>Colours in the run.</summary>
            public int Count { get; }

            /// <summary>Pixels the run accounts for.</summary>
            public long Pixels { get; }

            /// <summary>How far the box reaches along its widest channel.</summary>
            public int WidestSpan => Math.Max(_redSpan, Math.Max(_greenSpan, _blueSpan));

            /// <summary>The bit shift of the widest channel, so a split can sort on it.</summary>
            public int WidestShift => _redSpan >= _greenSpan && _redSpan >= _blueSpan ? 16
                : _greenSpan >= _blueSpan ? 8
                : 0;

            /// <summary>Measures a run of the colour array.</summary>
            /// <param name="colours">The backing array.</param>
            /// <param name="start">First index.</param>
            /// <param name="count">How many colours.</param>
            /// <returns>The box.</returns>
            public static ColourBox Over(ColourCount[] colours, int start, int count) {
                int redLow = 255, redHigh = 0, greenLow = 255, greenHigh = 0, blueLow = 255, blueHigh = 0;
                long pixels = 0;

                for (int i = start; i < start + count; i++) {
                    int rgb = colours[i].Rgb;
                    int red = (rgb >> 16) & 0xFF, green = (rgb >> 8) & 0xFF, blue = rgb & 0xFF;
                    if (red < redLow) redLow = red;
                    if (red > redHigh) redHigh = red;
                    if (green < greenLow) greenLow = green;
                    if (green > greenHigh) greenHigh = green;
                    if (blue < blueLow) blueLow = blue;
                    if (blue > blueHigh) blueHigh = blue;
                    pixels += colours[i].Pixels;
                }

                return count <= 0
                    ? new ColourBox(start, 0, 0, 0, 0, 0)
                    : new ColourBox(start, count, pixels, redHigh - redLow, greenHigh - greenLow, blueHigh - blueLow);
            }

            /// <summary>The colour standing for the whole box: its pixel-weighted mean.</summary>
            /// <remarks>
            ///     Weighted by pixel count rather than by distinct colour, so a box holding one
            ///     dominant flat colour and a scatter of antialiased neighbours comes back as the
            ///     flat colour rather than as a blend of it and the scatter.
            /// </remarks>
            /// <param name="colours">The backing array.</param>
            /// <returns>The 24-bit representative.</returns>
            public int Representative(ColourCount[] colours) {
                if (Count <= 0 || Pixels <= 0)
                    return 0;

                long red = 0, green = 0, blue = 0;
                for (int i = Start; i < Start + Count; i++) {
                    ColourCount colour = colours[i];
                    red += ((colour.Rgb >> 16) & 0xFF) * colour.Pixels;
                    green += ((colour.Rgb >> 8) & 0xFF) * colour.Pixels;
                    blue += (colour.Rgb & 0xFF) * colour.Pixels;
                }

                //Rounded rather than truncated: truncation biases every entry towards black, which
                //over a whole palette reads as the import having darkened the picture.
                int r = (int) ((red + Pixels / 2) / Pixels);
                int g = (int) ((green + Pixels / 2) / Pixels);
                int b = (int) ((blue + Pixels / 2) / Pixels);
                return (r << 16) | (g << 8) | b;
            }
        }
    }
}
