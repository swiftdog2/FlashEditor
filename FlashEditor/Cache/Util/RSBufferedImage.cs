using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using System;
using System.Drawing;

namespace FlashEditor.cache.util {
    /// <summary>
    /// A single rasterised sprite frame backed by a <see cref="Bitmap"/>.
    /// Extends <see cref="SpriteDefinition"/> to participate in the sprite
    /// decoding pipeline.
    /// </summary>
    public class RSBufferedImage : SpriteDefinition, IDisposable {
        private DirectBitmap _directBitmap;

        /// <summary>Creates a new buffered image with the given dimensions.</summary>
        /// <param name="index">The frame index within the parent sprite set.</param>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        public RSBufferedImage(int index, int width, int height) {
            this.index = index;
            _directBitmap = new DirectBitmap(width, height);
            thumb = _directBitmap.Bitmap;
        }

        /// <summary>Returns the underlying bitmap for this frame.</summary>
        public Bitmap GetSprite() {
            return thumb;
        }

        /// <summary>Sets the pixel at (<paramref name="x"/>, <paramref name="y"/>) to the given ARGB value.</summary>
        public void SetRGB(int x, int y, int rgb) {
            if(_directBitmap == null)
                throw new Exception();
            _directBitmap.SetPixel(x, y, Color.FromArgb(rgb));
        }

        /// <summary>Gets the width of the underlying bitmap, or 0 if uninitialised.</summary>
        internal int GetWidth() {
            return thumb == null ? 0 : thumb.Width;
        }

        /// <summary>Gets the height of the underlying bitmap, or 0 if uninitialised.</summary>
        internal int GetHeight() {
            return thumb == null ? 0 : thumb.Height;
        }

        public void Dispose() {
            _directBitmap?.Dispose();
            _directBitmap = null;
            thumb = null;
        }
    }
}
