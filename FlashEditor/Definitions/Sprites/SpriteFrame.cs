using System;

namespace FlashEditor.cache.sprites {
    /// <summary>
    ///     One frame of a sprite set, held in the form index 8 stores it rather than as pixels.
    /// </summary>
    /// <remarks>
    ///     Nothing here can be recovered from a rasterised bitmap, which is the whole reason the
    ///     type exists. Three of the fields are aliased against the drawn result - the flags byte
    ///     is a free choice on a one-pixel-wide or one-pixel-tall frame, an alpha plane of all
    ///     0xFF draws exactly like no plane at all, and a palette entry stored as black is remapped
    ///     on read - so a re-encode driven by the pixels rewrites files nobody edited. The
    ///     rasterised <see cref="System.Drawing.Bitmap"/> is derived from this, never the reverse.
    /// </remarks>
    public sealed class SpriteFrame {
        /// <summary>
        ///     Flag bit saying the pixel plane is stored column by column rather than row by row.
        /// </summary>
        /// <remarks>Tested at <c>Class324.java:91</c> and <c>:105</c>.</remarks>
        public const int FlagVertical = 0x01;

        /// <summary>Flag bit saying an alpha plane follows the palette-index plane.</summary>
        /// <remarks>Tested at <c>Class324.java:90</c>.</remarks>
        public const int FlagAlpha = 0x02;

        /// <summary>
        ///     The frame's flags byte exactly as stored.
        /// </summary>
        /// <remarks>
        ///     Kept whole rather than rebuilt from <see cref="IsColumnMajor"/> and
        ///     <see cref="HasAlphaPlane"/>. The client reads only bits 0 and 1
        ///     (<c>Class324.java:90-91</c>), so any other bit is invisible to it and would be lost
        ///     on the first save if the byte were reconstructed. No frame in either supported cache
        ///     sets one, which is exactly why nothing else would notice.
        /// </remarks>
        public int Flags { get; set; }

        /// <summary>Left edge of the frame within the set's canvas (<c>anInt2725</c>).</summary>
        public int OffsetX { get; set; }

        /// <summary>Top edge of the frame within the set's canvas (<c>anInt2721</c>).</summary>
        public int OffsetY { get; set; }

        /// <summary>Width of the stored pixel plane (<c>anInt2722</c>).</summary>
        public int SubWidth { get; set; }

        /// <summary>Height of the stored pixel plane (<c>anInt2720</c>).</summary>
        public int SubHeight { get; set; }

        /// <summary>
        ///     Palette indices, one byte per pixel, always laid out as <c>x + y * SubWidth</c>.
        /// </summary>
        /// <remarks>
        ///     Held in the client's own canonical layout (<c>aByteArray2717</c>) whichever way the
        ///     bytes arrived, so <see cref="Flags"/> alone decides the traversal on read and write.
        ///     Storing the file order instead would make every consumer branch on the flag.
        /// </remarks>
        public byte[] PaletteIndices { get; set; } = Array.Empty<byte>();

        /// <summary>
        ///     The alpha plane in the same layout, or <c>null</c> when the frame stores none.
        /// </summary>
        /// <remarks>
        ///     Not nulled when every byte is 0xFF. The client does exactly that
        ///     (<c>Class324.java:127-129</c>) because it only wants to know whether to take the
        ///     blending path, but the plane is still on disk and dropping it shortens the file.
        /// </remarks>
        public byte[]? Alpha { get; set; }

        /// <summary>Whether the plane is stored column-major.</summary>
        public bool IsColumnMajor => (Flags & FlagVertical) != 0;

        /// <summary>Whether an alpha plane is stored for this frame.</summary>
        public bool HasAlphaPlane => (Flags & FlagAlpha) != 0;

        /// <summary>Pixels in the stored plane.</summary>
        public int Area => SubWidth * SubHeight;

        /// <summary>Bytes this frame occupies in the file, flags byte included.</summary>
        public int StoredLength => 1 + Area + (HasAlphaPlane ? Area : 0);

        /// <summary>
        ///     Whether row-major and column-major would produce the same bytes for this frame, so
        ///     <see cref="FlagVertical"/> cannot be recovered from the pixels.
        /// </summary>
        /// <remarks>
        ///     A single row, a single column or an empty plane all traverse in one order only. The
        ///     shipped data leaves this latent rather than live: thousands of frames are ambiguous
        ///     and every one of them stores the bit clear, so an encoder that guessed "row-major"
        ///     would round-trip both caches and still be wrong on the first frame that is not.
        /// </remarks>
        public bool OrderIsUnrecoverable => SubWidth <= 1 || SubHeight <= 1;

        /// <summary>
        ///     Whether this frame stores an alpha plane that leaves every pixel fully opaque.
        /// </summary>
        /// <remarks>
        ///     Such a frame draws identically to one with no plane at all, so presence has to come
        ///     off <see cref="Flags"/> rather than being inferred. This one is live in both caches.
        /// </remarks>
        public bool AlphaPlaneIsRedundant {
            get {
                if (Alpha == null || Alpha.Length == 0)
                    return false;
                foreach (byte value in Alpha)
                    if (value != 0xFF)
                        return false;
                return true;
            }
        }
    }
}
