using System;
using System.IO;
using FlashEditor.cache.sprites;
using FlashEditor.cache.util;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     One index-32 group: the pre-login art store, which holds two unrelated payload formats.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <b>The index is mixed and its constant's name says otherwise.</b>
    ///     <c>RSConstants.LOADING_SPRITES</c> is commented "in jpg format" and five of the
    ///     twenty-six groups are 256-frame Jagex sprite sets - the <c>p11_full</c>, <c>p12_full</c>
    ///     and <c>b12_full</c> glyph sheets <c>Class84.java:20-31</c> asks for by name, plus two
    ///     more. A JPEG-only reader throws on a fifth of the index. Dispatch is therefore on the
    ///     payload's own magic, never on the index id.
    ///     </para>
    ///     <para>
    ///     <b>The two halves have different save rules, and that is deliberate.</b> A sprite set is
    ///     re-encoded from its stored form, which is byte-identical and editable. A JPEG is written
    ///     back as the bytes it was read as, because a JPEG re-encode is no more reproducible than a
    ///     GZip one: the entropy coder, the quantisation and the forward DCT are all encoder
    ///     choices, and re-compressing an unedited image would change the stored bytes, the archive
    ///     CRC, and the reference-table entry of every archive packed alongside it. The parse in
    ///     <see cref="Jpeg"/> is a reading of the file, not a source for rewriting it.
    ///     </para>
    ///     <para>
    ///     <b>A group is a file.</b> Every group in the index declares exactly one file, id 0, and
    ///     the client only ever reaches one through <c>JS5Archive.method2733</c>
    ///     (<c>JS5Archive.java:591-616</c>), which throws unless the group holds exactly one. So the
    ///     whole group payload is the record.
    ///     </para>
    /// </remarks>
    public sealed class LoadingSpriteDefinition : IDefinition, IDisposable {
        /// <summary>The group id, which is the definition id on this index.</summary>
        public int Id { get; set; }

        /// <summary>Which of the two formats this group holds.</summary>
        public LoadingSpriteShape Shape { get; private set; }

        /// <summary>
        ///     The group payload exactly as stored.
        /// </summary>
        /// <remarks>
        ///     Kept for the JPEG half's save path, where it is the only thing that can be written
        ///     back without changing the file. Also what a sweep compares against, so it is a copy
        ///     rather than a reference into the caller's buffer.
        /// </remarks>
        public byte[] StoredBytes { get; private set; } = Array.Empty<byte>();

        /// <summary>The decoded sprite set, or <c>null</c> when this group is a JPEG.</summary>
        public SpriteDefinition? SpriteSet { get; private set; }

        /// <summary>The parsed JPEG structure, or <c>null</c> when this group is a sprite set.</summary>
        public JagexJpeg? Jpeg { get; private set; }

        /// <summary>
        ///     Whether a payload is a JPEG, judged by the two bytes the format opens with.
        /// </summary>
        /// <remarks>
        ///     <c>FF D8</c> is a statement the JPEG format makes about itself. The alternative -
        ///     deciding a sprite set by its first byte being a small flags value - is a property of
        ///     the data rather than of the format, and only two of the flag byte's eight bits are
        ///     defined, so a set whose first frame used an undefined bit would be misfiled by it.
        /// </remarks>
        /// <param name="payload">The group payload.</param>
        /// <returns>Whether the payload opens with the JPEG SOI marker.</returns>
        public static bool LooksLikeJpeg(byte[]? payload) {
            return payload != null && payload.Length >= 2 && payload[0] == 0xFF && payload[1] == JagexJpeg.MarkerSoi;
        }

        /// <summary>
        ///     Decodes a group, dispatching on the payload's shape.
        /// </summary>
        /// <param name="stream">The whole group payload.</param>
        /// <param name="xteaKey">Unused - index 32 is never encrypted in either supported cache.</param>
        /// <exception cref="InvalidDataException">The group holds no bytes at all.</exception>
        public void Decode(JagStream stream, int[]? xteaKey = null) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            StoredBytes = stream.ToArray();
            if (StoredBytes.Length == 0)
                throw new InvalidDataException($"Index 32 group {Id} holds no bytes, so it has no shape.");

            if (LooksLikeJpeg(StoredBytes)) {
                Shape = LoadingSpriteShape.JpegImage;
                Jpeg = JagexJpeg.Decode(StoredBytes);
                SpriteSet = null;
                return;
            }

            Shape = LoadingSpriteShape.SpriteSet;
            Jpeg = null;

            var set = new SpriteDefinition();
            set.Decode(new JagStream(StoredBytes));
            set.SetIndex(Id);
            SpriteSet = set;
        }

        /// <summary>
        ///     Encodes the group back to the bytes the cache stores for it.
        /// </summary>
        /// <remarks>
        ///     The JPEG half returns the stored bytes verbatim, which is the only way it can be
        ///     byte-identical - see the type's remarks. The sprite half goes through
        ///     <see cref="SpriteDefinition.Encode"/>, which replays the stored form rather than
        ///     rasterising, so an edit to the frames is written and everything else comes back
        ///     unchanged.
        /// </remarks>
        /// <returns>The encoded group payload.</returns>
        public JagStream Encode() {
            if (Shape == LoadingSpriteShape.SpriteSet) {
                if (SpriteSet == null)
                    throw new InvalidOperationException("This group holds no decoded sprite set to encode.");
                return SpriteSet.Encode();
            }

            if (StoredBytes.Length == 0)
                throw new InvalidOperationException("This group holds no stored bytes to write back.");

            return new JagStream((byte[]) StoredBytes.Clone());
        }

        /// <summary>
        ///     Renders the group to opaque ARGB pixels, whichever format it holds.
        /// </summary>
        /// <remarks>
        ///     A sprite set is 256 glyphs rather than one picture, so only the frame asked for is
        ///     drawn. A JPEG has a single frame and ignores the index.
        /// </remarks>
        /// <param name="frame">Which frame of a sprite set to draw; ignored for a JPEG.</param>
        /// <param name="width">The rendered width.</param>
        /// <param name="height">The rendered height.</param>
        /// <returns>Row-major ARGB pixels.</returns>
        public int[] Render(int frame, out int width, out int height) {
            if (Shape == LoadingSpriteShape.JpegImage) {
                if (Jpeg == null)
                    throw new InvalidOperationException("This group holds no parsed JPEG to render.");

                JpegRaster raster = BaselineJpegDecoder.Decode(Jpeg);
                width = raster.Width;
                height = raster.Height;
                return raster.ToArgb();
            }

            if (SpriteSet == null)
                throw new InvalidOperationException("This group holds no decoded sprite set to render.");

            RSBufferedImage image = SpriteSet.GetFrame(frame);
            width = image.GetWidth();
            height = image.GetHeight();
            return image.GetPixels();
        }

        /// <summary>How many frames this group draws: 1 for a JPEG, the set's count otherwise.</summary>
        public int FrameCount =>
            Shape == LoadingSpriteShape.JpegImage ? 1 : SpriteSet?.GetFrameCount() ?? 0;

        /// <summary>Releases the rasterised frames a sprite set may be holding.</summary>
        public void Dispose() {
            SpriteSet?.Dispose();
        }
    }
}
