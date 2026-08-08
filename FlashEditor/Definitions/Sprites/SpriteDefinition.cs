using FlashEditor.cache.util;
using static FlashEditor.Utils.DebugUtil;
using System.Collections.Generic;
using System.Drawing;
using System;
using FlashEditor;

namespace FlashEditor.cache.sprites {
    /// <summary>
    ///     A sprite set from index 8: a shared palette, a canvas size, and one or more frames.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Two representations live here and they are deliberately not the same thing. The
    ///     <b>stored form</b> - <see cref="Frames"/>, <see cref="PaletteStored"/>,
    ///     <see cref="width"/> and <see cref="height"/> - is what the file holds and is the only
    ///     thing <see cref="Encode"/> reads. The <b>rendered form</b> - <see cref="GetFrame"/>,
    ///     <see cref="GetFrames"/> and <see cref="thumb"/> - is derived from it on demand and can
    ///     be thrown away at any time.
    ///     </para>
    ///     <para>
    ///     The split exists because rasterising is lossy in both directions. Palette entry 0 is
    ///     transparent, so a colour stored as black is remapped to 0x000001 on read
    ///     (<c>Class324.java:77-79</c>) and the drawn pixel can no longer say which of the two the
    ///     file held; an alpha plane of all 0xFF draws the same as no plane; and on a one-pixel-wide
    ///     frame the row-major and column-major orders produce identical bytes. An encoder driven
    ///     by pixels therefore rewrites files nobody edited, and because an archive CRC covers the
    ///     stored bytes that drags in the reference-table entry of everything packed alongside.
    ///     </para>
    ///     <para>
    ///     Rasterising is also deferred rather than done during <see cref="Decode"/>: a frame costs
    ///     a pinned pixel buffer and a GDI bitmap, and a sweep over every group in the index has no
    ///     use for either.
    ///     </para>
    ///     <para>
    ///     Layout, from <c>Class324.method3690</c> (<c>Class324.java:43-133</c>), read backwards
    ///     from the end of the file: frame count at <c>[len-2]</c>; canvas width, canvas height and
    ///     <c>paletteSize-1</c> at <c>[len-7-8N]</c>, followed by four <c>N</c>-entry unsigned short
    ///     arrays - offsetX, offsetY, subWidth, subHeight; the palette at
    ///     <c>[len-7-8N-3(paletteSize-1)]</c>, entry 0 reserved as transparent and never stored;
    ///     and the pixel planes from offset 0 forwards, one flags byte each. The right and bottom
    ///     padding the client carries (<c>anInt2719</c>, <c>anInt2724</c>) are computed at
    ///     <c>:70-71</c>, not stored.
    ///     </para>
    /// </remarks>
    public class SpriteDefinition : IDefinition, IDisposable {
        /// <summary>Bytes of metadata after the pixel planes and the palette.</summary>
        /// <remarks>
        ///     Canvas width and height, the palette-size byte, and the two-byte frame count -
        ///     the <c>7</c> in the client's <c>is.length - 7 - i * 8</c> (<c>Class324.java:52</c>).
        /// </remarks>
        private const int TrailerBytes = 7;

        /// <summary>Bytes of per-frame metadata: four unsigned short arrays.</summary>
        /// <remarks>The <c>8</c> in <c>Class324.java:52</c>.</remarks>
        private const int BytesPerFrameHeader = 8;

        /// <summary>Bytes per stored palette entry: 24-bit RGB (<c>Class324.java:76</c>).</summary>
        private const int BytesPerPaletteEntry = 3;

        //The index at which this sprite exists.
        public int index;

        /// <summary>Canvas width as stored, which is not derivable from the frames.</summary>
        /// <remarks>
        ///     Frames are placed within the canvas and routinely do not reach its edges - a third
        ///     of the sets in both caches have a canvas larger than any frame's extent - so it
        ///     cannot be recomputed as <c>max(offsetX + subWidth)</c>.
        /// </remarks>
        public int width;

        /// <summary>Canvas height as stored. See <see cref="width"/>.</summary>
        public int height;

        /// <summary>The rendered frames, built on first use and released by <see cref="Dispose()"/>.</summary>
        private List<RSBufferedImage> rendered;

        /// <summary>The rendered first frame, backing the lazy <see cref="thumb"/>.</summary>
        private Bitmap thumbnail;

        /// <summary>Frame count as stored, which is what <see cref="Encode"/> writes.</summary>
        public int frameCount;

        /// <summary>
        ///     The frames in stored order.
        /// </summary>
        /// <remarks>Null on a <see cref="RSBufferedImage"/>, which is a rendered frame rather than a set.</remarks>
        public List<SpriteFrame> Frames { get; private set; }

        /// <summary>
        ///     The palette exactly as stored: entry 0 unused, entries 1 upwards 24-bit RGB.
        /// </summary>
        /// <remarks>
        ///     Verbatim, including entries stored as 0x000000 and entries no pixel references.
        ///     Both occur in both supported caches, and neither survives a palette rebuilt by
        ///     scanning the drawn pixels. <see cref="RenderPalette"/> is the read-side view.
        /// </remarks>
        public int[] PaletteStored { get; private set; } = System.Array.Empty<int>();

        /// <summary>
        ///     The palette as the client draws with it: entry 0 transparent, a stored black
        ///     promoted to 0x000001.
        /// </summary>
        /// <remarks>
        ///     The promotion is the client's (<c>Class324.java:77-79</c>) and exists so that "the
        ///     palette value is zero" means "transparent" and nothing else. Kept apart from
        ///     <see cref="PaletteStored"/> so the remap never reaches the encoder.
        /// </remarks>
        public int[] RenderPalette { get; private set; } = System.Array.Empty<int>();

        /// <summary>
        ///     Bytes sitting between the last pixel plane and the palette, normally empty.
        /// </summary>
        /// <remarks>
        ///     The format has no length field between the two - the planes run forwards from 0 and
        ///     the palette is found by seeking back from the end - so a packer can leave a gap that
        ///     nothing reads. Thirteen groups in the private-server repack do, three zero bytes
        ///     each; the vanilla b639 capture has none. Captured rather than dropped, because a
        ///     re-encode without them is three bytes short of the file it came from.
        /// </remarks>
        public byte[] PixelPlaneTrailer { get; private set; } = System.Array.Empty<byte>();

        /// <summary>
        ///     Where the decoder stopped reading pixel planes, as an offset into the file.
        /// </summary>
        /// <remarks>
        ///     Read off the stream rather than computed, so a test can hold it against the length
        ///     the frame metadata implies. The two agreeing is what says every plane was sized
        ///     correctly; the format offers no other check, since a mis-sized plane simply eats
        ///     into the next one.
        /// </remarks>
        public long PixelPlaneEnd { get; private set; }

        /// <summary>Where the palette block begins, derived from the frame count and palette size.</summary>
        public long PaletteOffset { get; private set; }

        /// <summary>Length of the buffer this set was decoded from.</summary>
        public long StoredLength { get; private set; }

        /// <summary>
        /// Creates a new sprite with one frame.
        /// </summary>
        /// <param name="width">The width of the sprite in pixels.</param>
        /// <param name="height">The height of the sprite in pixels.</param>
        public SpriteDefinition(int width, int height) : this(width, height, 1) { }

        public SpriteDefinition() {

        }

        /// <summary>
        /// Creates a new sprite with the specified number of frames.
        /// </summary>
        /// <param name="width">The width of the sprite in pixels.</param>
        /// <param name="height">The height of the sprite in pixels.</param>
        /// <param name="frameCount">The number of animation frames.</param>
        public SpriteDefinition(int width, int height, int frameCount) {
            this.width = width;
            this.height = height;
            this.frameCount = frameCount;
            Frames = new List<SpriteFrame>(frameCount);
        }

        /// <summary>
        ///     Decodes a sprite set into the form the file stores, without rasterising anything.
        /// </summary>
        /// <param name="stream">The whole group payload; the format is located from its end.</param>
        /// <param name="xteaKey">Unused - index 8 is opened unencrypted (<c>InterfaceSettings.java:157</c>).</param>
        public void Decode(JagStream stream, int[] xteaKey = null) {
            Debug("Decoding sprite", LOG_DETAIL.ADVANCED);

            StoredLength = stream.Length;

            //Frame count first: everything else is positioned relative to it.
            stream.Seek(stream.Length - 2);
            int size = stream.ReadUnsignedShort();

            long headerOffset = stream.Length - TrailerBytes - (long) size * BytesPerFrameHeader;
            if (headerOffset < 0)
                throw new InvalidOperationException(
                    $"A sprite set of {size} frames needs {TrailerBytes + size * BytesPerFrameHeader} bytes of " +
                    $"metadata but the file is only {stream.Length}.");

            stream.Seek(headerOffset);
            width = stream.ReadUnsignedShort();
            height = stream.ReadUnsignedShort();
            int paletteSize = stream.ReadByte() + 1;
            frameCount = size;

            Debug("Size: " + size + ", width: " + width + ", height: " + height + ", palette elements: " + paletteSize, LOG_DETAIL.INSANE);

            int[] offsetsX = stream.ReadUnsignedShortArray(size);
            int[] offsetsY = stream.ReadUnsignedShortArray(size);
            int[] subWidths = stream.ReadUnsignedShortArray(size);
            int[] subHeights = stream.ReadUnsignedShortArray(size);

            PaletteOffset = headerOffset - (long) (paletteSize - 1) * BytesPerPaletteEntry;
            if (PaletteOffset < 0)
                throw new InvalidOperationException(
                    $"A palette of {paletteSize} entries does not fit before the metadata at {headerOffset}.");

            //Entry 0 is never stored: it is the transparent index.
            PaletteStored = new int[paletteSize];
            RenderPalette = new int[paletteSize];
            stream.Seek(PaletteOffset);
            for (int entry = 1; entry < paletteSize; entry++) {
                int colour = stream.ReadMedium();
                PaletteStored[entry] = colour;
                RenderPalette[entry] = colour == 0 ? 1 : colour;
            }

            Frames = new List<SpriteFrame>(size);
            stream.Seek(0);
            for (int id = 0; id < size; id++) {
                var frame = new SpriteFrame {
                    OffsetX = offsetsX[id],
                    OffsetY = offsetsY[id],
                    SubWidth = subWidths[id],
                    SubHeight = subHeights[id]
                };

                frame.Flags = stream.ReadByte();
                frame.PaletteIndices = ReadPlane(stream, frame);
                if (frame.HasAlphaPlane)
                    frame.Alpha = ReadPlane(stream, frame);

                Debug($"\tFrame {id}: {frame.SubWidth}x{frame.SubHeight} at {frame.OffsetX},{frame.OffsetY} flags {frame.Flags}", LOG_DETAIL.INSANE);
                Frames.Add(frame);
            }

            PixelPlaneEnd = stream.Position;
            if (PixelPlaneEnd > PaletteOffset)
                throw new InvalidOperationException(
                    $"The pixel planes ran to {PixelPlaneEnd}, past the palette at {PaletteOffset}, so at least " +
                    "one plane was sized wrongly.");

            PixelPlaneTrailer = new byte[PaletteOffset - PixelPlaneEnd];
            if (PixelPlaneTrailer.Length > 0) {
                stream.Seek(PixelPlaneEnd);
                stream.Read(PixelPlaneTrailer, 0, PixelPlaneTrailer.Length);
            }

            Debug("Sprite decode complete", LOG_DETAIL.ADVANCED);
        }

        /// <summary>
        ///     Encodes the set back to the bytes index 8 stores for it.
        /// </summary>
        /// <remarks>
        ///     Everything comes off the stored form. Nothing is recomputed from the rendered
        ///     bitmaps, and nothing is normalised: the flags byte, the palette and the trailing gap
        ///     all go back exactly as they arrived, because each of them has more than one spelling
        ///     that decodes to the same picture.
        /// </remarks>
        /// <returns>The encoded sprite set.</returns>
        public JagStream Encode() {
            if (Frames == null)
                throw new InvalidOperationException("This sprite set holds no decoded frames to encode.");

            JagStream stream = new JagStream();

            foreach (SpriteFrame frame in Frames) {
                stream.WriteByte((byte) frame.Flags);
                WritePlane(stream, frame, frame.PaletteIndices);
                if (frame.HasAlphaPlane)
                    WritePlane(stream, frame, frame.Alpha);
            }

            stream.Write(PixelPlaneTrailer, 0, PixelPlaneTrailer.Length);

            for (int entry = 1; entry < PaletteStored.Length; entry++)
                stream.WriteMedium(PaletteStored[entry]);

            stream.WriteShort(width);
            stream.WriteShort(height);
            stream.WriteByte((byte) (PaletteStored.Length - 1));

            foreach (SpriteFrame frame in Frames)
                stream.WriteShort(frame.OffsetX);
            foreach (SpriteFrame frame in Frames)
                stream.WriteShort(frame.OffsetY);
            foreach (SpriteFrame frame in Frames)
                stream.WriteShort(frame.SubWidth);
            foreach (SpriteFrame frame in Frames)
                stream.WriteShort(frame.SubHeight);

            stream.WriteShort(Frames.Count);

            return stream.Flip();
        }

        /// <summary>
        ///     Reads one plane, honouring the frame's stored traversal order.
        /// </summary>
        /// <remarks>
        ///     Both branches fill the same canonical <c>x + y * SubWidth</c> layout the client uses
        ///     (<c>Class324.java:92-100</c>), so the flag is needed again only on the way out.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the start of the plane.</param>
        /// <param name="frame">The frame whose geometry sizes the plane.</param>
        /// <returns>The plane bytes.</returns>
        private static byte[] ReadPlane(JagStream stream, SpriteFrame frame) {
            byte[] plane = new byte[frame.Area];

            if (frame.IsColumnMajor) {
                for (int x = 0; x < frame.SubWidth; x++)
                    for (int y = 0; y < frame.SubHeight; y++)
                        plane[x + y * frame.SubWidth] = ReadPlaneByte(stream);
            } else {
                for (int i = 0; i < plane.Length; i++)
                    plane[i] = ReadPlaneByte(stream);
            }

            return plane;
        }

        /// <summary>Reads one plane byte, refusing to treat the end of the buffer as data.</summary>
        /// <remarks>
        ///     <see cref="JagStream.ReadByte"/> answers -1 past the end without advancing, which
        ///     would silently fill the rest of a plane with 0xFF and leave the position looking
        ///     correct.
        /// </remarks>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The byte.</returns>
        private static byte ReadPlaneByte(JagStream stream) {
            int value = stream.ReadByte();
            if (value < 0)
                throw new System.IO.EndOfStreamException("A sprite pixel plane ran past the end of the file.");
            return (byte) value;
        }

        /// <summary>Writes one plane back in the traversal order the frame was stored in.</summary>
        /// <param name="stream">The destination.</param>
        /// <param name="frame">The frame whose flags decide the order.</param>
        /// <param name="plane">The plane in canonical layout.</param>
        private static void WritePlane(JagStream stream, SpriteFrame frame, byte[]? plane) {
            if (plane == null || plane.Length != frame.Area)
                throw new InvalidOperationException(
                    $"A {frame.SubWidth}x{frame.SubHeight} frame needs a {frame.Area} byte plane, not " +
                    (plane == null ? "null" : plane.Length.ToString()) + ".");

            if (frame.IsColumnMajor) {
                for (int x = 0; x < frame.SubWidth; x++)
                    for (int y = 0; y < frame.SubHeight; y++)
                        stream.WriteByte(plane[x + y * frame.SubWidth]);
            } else {
                for (int i = 0; i < plane.Length; i++)
                    stream.WriteByte(plane[i]);
            }
        }

        /// <summary>
        ///     Builds a set in stored form from frames that came from somewhere other than a file.
        /// </summary>
        /// <remarks>
        ///     The one way to construct a set the encoder will write, and the reason the import path
        ///     does not lay out the format itself: <see cref="Encode"/> stays the only code that
        ///     knows the byte order, so an import cannot drift away from what a decode expects.
        ///     <para>
        ///     Everything the format cannot express is rejected here rather than at the point the
        ///     bytes are stored. A palette index past the end of the palette is the one that matters:
        ///     the client indexes its palette array with the raw byte
        ///     (<c>Class324.method3686</c>), so an out-of-range index is an exception in the game
        ///     rather than a wrong colour, and <c>RealCacheSpriteTests</c> asserts no shipped file
        ///     holds one.
        ///     </para>
        /// </remarks>
        /// <param name="canvasWidth">The canvas width to store.</param>
        /// <param name="canvasHeight">The canvas height to store.</param>
        /// <param name="paletteStored">The palette as stored, entry 0 reserved and never written.</param>
        /// <param name="frames">The frames, in the order they are to be stored.</param>
        /// <param name="pixelPlaneTrailer">
        ///     The unread gap the file kept between its last plane and its palette, for a set built
        ///     by editing one that was read from disk. Null for a set built from nothing.
        /// </param>
        /// <returns>The set.</returns>
        /// <exception cref="ArgumentNullException">A required argument is null.</exception>
        /// <exception cref="ArgumentException">The set cannot be expressed by the format.</exception>
        public static SpriteDefinition FromFrames(int canvasWidth, int canvasHeight, int[] paletteStored,
                                                  IReadOnlyList<SpriteFrame> frames,
                                                  byte[] pixelPlaneTrailer = null) {
            if (paletteStored == null)
                throw new ArgumentNullException(nameof(paletteStored));
            if (frames == null)
                throw new ArgumentNullException(nameof(frames));

            if (canvasWidth < 0 || canvasWidth > 0xFFFF || canvasHeight < 0 || canvasHeight > 0xFFFF)
                throw new ArgumentException(
                    $"A canvas of {canvasWidth}x{canvasHeight} does not fit the unsigned shorts the format stores it in.");
            if (paletteStored.Length < 1 || paletteStored.Length > 256)
                throw new ArgumentException(
                    $"A palette holds 1 to 256 entries including the reserved entry 0, not {paletteStored.Length}.");
            if (frames.Count < 1 || frames.Count > 0xFFFF)
                throw new ArgumentException($"A set holds 1 to 65535 frames, not {frames.Count}.");

            var sprite = new SpriteDefinition(canvasWidth, canvasHeight, frames.Count);
            //Carried rather than dropped. Thirteen groups in the repack leave a three byte gap here
            //that nothing reads, so an edit to one frame of such a set would otherwise come back
            //three bytes shorter for a reason that has nothing to do with the edit.
            sprite.PixelPlaneTrailer = pixelPlaneTrailer == null
                ? System.Array.Empty<byte>()
                : (byte[]) pixelPlaneTrailer.Clone();
            sprite.PaletteStored = (int[]) paletteStored.Clone();
            sprite.RenderPalette = new int[paletteStored.Length];
            //Entry 0 stays zero on both, which is what "transparent" is; every other entry takes the
            //client's own promotion so a stored black still draws.
            for (int entry = 1; entry < paletteStored.Length; entry++)
                sprite.RenderPalette[entry] = paletteStored[entry] == 0 ? 1 : paletteStored[entry];

            foreach (SpriteFrame frame in frames) {
                if (frame == null)
                    throw new ArgumentException("A set cannot hold a null frame.", nameof(frames));
                if (frame.OffsetX < 0 || frame.OffsetY < 0 || frame.SubWidth < 0 || frame.SubHeight < 0 ||
                    frame.OffsetX > 0xFFFF || frame.OffsetY > 0xFFFF ||
                    frame.SubWidth > 0xFFFF || frame.SubHeight > 0xFFFF)
                    throw new ArgumentException("A frame's geometry does not fit the unsigned shorts it is stored in.");
                if (frame.PaletteIndices == null || frame.PaletteIndices.Length != frame.Area)
                    throw new ArgumentException(
                        $"A {frame.SubWidth}x{frame.SubHeight} frame needs a {frame.Area} byte plane.");
                if (frame.HasAlphaPlane != (frame.Alpha != null))
                    throw new ArgumentException(
                        "A frame's alpha flag and its alpha plane disagree, so the file would not read back.");
                if (frame.Alpha != null && frame.Alpha.Length != frame.Area)
                    throw new ArgumentException(
                        $"A {frame.SubWidth}x{frame.SubHeight} frame needs a {frame.Area} byte alpha plane.");

                foreach (byte index in frame.PaletteIndices)
                    if (index >= paletteStored.Length)
                        throw new ArgumentException(
                            $"A pixel addresses palette entry {index} of a {paletteStored.Length} entry palette.");

                sprite.Frames.Add(frame);
            }

            return sprite;
        }

        /// <summary>
        /// Creates a <see cref="SpriteDefinition"/> from an encoded stream.
        /// </summary>
        /// <param name="stream">Stream containing the sprite set.</param>
        /// <returns>The decoded sprite definition.</returns>
        internal static SpriteDefinition DecodeFromStream(JagStream stream) {
            var sprite = new SpriteDefinition();
            sprite.Decode(stream);
            return sprite;
        }

        // ===================================================================
        //  Derived state: the rasterised frames
        // ===================================================================

        /// <summary>
        ///     The rasterised first frame, built on first use.
        /// </summary>
        /// <remarks>
        ///     Derived, and therefore never read by <see cref="Encode"/>. A set with no frames has
        ///     none, which is what the sprite list and the map icon path already check for.
        /// </remarks>
        public Bitmap thumb {
            get {
                if (thumbnail != null)
                    return thumbnail;
                List<RSBufferedImage> frames = GetFrames();
                if (frames != null && frames.Count > 0)
                    thumbnail = frames[0].GetSprite();
                return thumbnail;
            }
            set { thumbnail = value; }
        }

        /// <summary>
        ///     Whether a frame's stored geometry reaches outside the stored canvas.
        /// </summary>
        /// <remarks>
        ///     The client would throw on one of these: it allocates exactly canvas width by canvas
        ///     height (<c>Class324.method3686</c>, sized by <c>:313-316</c> and <c>:154-157</c>) and
        ///     writes at <c>offset + pixel</c>. The vanilla capture has none. The repack has eleven,
        ///     all in one group, so this is reported rather than fatal - the raster is grown to fit
        ///     instead, which is what the previous decoder did by accident.
        /// </remarks>
        /// <param name="frame">The frame to test.</param>
        /// <returns>Whether it overflows.</returns>
        public bool Overflows(SpriteFrame frame) {
            return frame.OffsetX + frame.SubWidth > width || frame.OffsetY + frame.SubHeight > height;
        }

        /// <summary>
        /// Gets the frame with the specified id.
        /// </summary>
        /// <param name="id">The frame index.</param>
        /// <returns>The frame.</returns>
        public RSBufferedImage GetFrame(int id) {
            return GetFrames()[id];
        }

        /// <summary>
        /// Gets the height of this sprite.
        /// </summary>
        /// <remarks>Virtual so frame types that do not populate the
        /// <see cref="height"/> field can supply the real value.</remarks>
        /// <returns>The height of this sprite.</returns>
        public virtual int GetHeight() {
            return height;
        }

        /// <summary>Gets the width of this sprite in pixels.</summary>
        /// <remarks>Virtual so frame types that do not populate the
        /// <see cref="width"/> field can supply the real value.</remarks>
        /// <returns>The width.</returns>
        public virtual int GetWidth() {
            return width;
        }

        /// <summary>Replaces a rendered frame.</summary>
        /// <remarks>
        ///     Touches the derived form only, so it does not change what <see cref="Encode"/>
        ///     writes. Editing the stored form means editing <see cref="Frames"/>.
        /// </remarks>
        /// <param name="id">The frame index.</param>
        /// <param name="frame">The replacement frame image.</param>
        public void SetFrame(int id, RSBufferedImage frame) {
            if(frame.GetWidth() != width || frame.GetHeight() != height)
                throw new ArgumentException("The frame's dimensions do not match with the sprite's dimensions.");

            GetFrames()[id] = frame;
        }

        /// <summary>
        /// Gets the number of frames in this set.
        /// </summary>
        /// <remarks>Answered from the stored form, so it costs no rasterisation.</remarks>
        /// <returns>The number of frames.</returns>
        public int GetFrameCount() {
            return Frames == null ? 0 : Frames.Count;
        }

        /// <summary>
        ///     The rasterised frames, built on first use.
        /// </summary>
        /// <returns>The rendered frames, or <c>null</c> when this instance holds no stored frames.</returns>
        public List<RSBufferedImage> GetFrames() {
            if (rendered != null || Frames == null)
                return rendered;

            rendered = new List<RSBufferedImage>(Frames.Count);
            for (int id = 0; id < Frames.Count; id++)
                rendered.Add(Rasterise(id, Frames[id]));
            return rendered;
        }

        /// <summary>
        ///     Draws one stored frame onto its own canvas-sized bitmap.
        /// </summary>
        /// <remarks>
        ///     The colour rule is the client's <c>Class324.method3686</c> (<c>:196-225</c>): with an
        ///     alpha plane the pixel is <c>alpha &lt;&lt; 24 | colour</c>, and without one a palette
        ///     value of zero is transparent and anything else is opaque. That is why the promoted
        ///     palette is used here and the stored one is not - a colour stored as black would
        ///     otherwise vanish.
        /// </remarks>
        /// <param name="id">The frame index, carried onto the image.</param>
        /// <param name="frame">The stored frame.</param>
        /// <returns>The rendered frame.</returns>
        private RSBufferedImage Rasterise(int id, SpriteFrame frame) {
            //Grown to fit rather than clipped: a frame that reaches outside the canvas is malformed
            //and only occurs in the repack, and dropping its pixels would be a silent edit.
            int canvasWidth = Math.Max(width, frame.OffsetX + frame.SubWidth);
            int canvasHeight = Math.Max(height, frame.OffsetY + frame.SubHeight);

            var image = new RSBufferedImage(id, canvasWidth, canvasHeight);

            for (int y = 0; y < frame.SubHeight; y++) {
                for (int x = 0; x < frame.SubWidth; x++) {
                    int at = x + y * frame.SubWidth;
                    int colour = RenderPalette[frame.PaletteIndices[at]];

                    int argb;
                    if (frame.Alpha == null)
                        argb = colour == 0 ? 0 : (int) (0xFF000000 | (uint) colour);
                    else
                        argb = frame.Alpha[at] << 24 | colour;

                    image.SetRGB(x + frame.OffsetX, y + frame.OffsetY, argb);
                }
            }

            return image;
        }

        internal void SetIndex(int index) {
            this.index = index;
        }

        /// <summary>Releases the frames and thumbnail held by this sprite set.</summary>
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the resources held by this definition. Derived types (notably
        /// <see cref="RSBufferedImage"/>) override this rather than hiding
        /// <see cref="Dispose()"/>, which is what guarantees the derived cleanup still
        /// runs when the instance is disposed through a <see cref="SpriteDefinition"/>
        /// or <see cref="IDisposable"/> reference.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing) {
            if(!disposing)
                return;

            if(rendered != null) {
                foreach(var frame in rendered)
                    frame?.Dispose();
                rendered = null;
            }
            thumbnail = null;
        }
    }
}
