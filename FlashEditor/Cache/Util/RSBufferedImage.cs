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

        /// <summary>Returns a copy of the raw ARGB pixel data as an int array.</summary>
        public int[] GetPixels() {
            if (_directBitmap == null || _directBitmap.Bits == null)
                return Array.Empty<int>();
            return (int[])_directBitmap.Bits.Clone();
        }

        /// <summary>Gets the underlying pixel buffer directly (no copy).</summary>
        internal int[] GetPixelsDirect() {
            if (_directBitmap == null)
                return null;
            return _directBitmap.Bits;
        }

        /// <summary>Sets the pixel at (<paramref name="x"/>, <paramref name="y"/>) to the given ARGB value.</summary>
        public void SetRGB(int x, int y, int rgb) {
            if(_directBitmap == null)
                throw new Exception();
            _directBitmap.SetPixel(x, y, Color.FromArgb(rgb));
        }

        /// <summary>Gets the width of the underlying bitmap, or 0 if uninitialised.</summary>
        /// <remarks>
        /// Overrides the base accessor, which reads the inherited <c>width</c> field.
        /// A frame never populates that field, so the bitmap is the only source of truth.
        /// </remarks>
        public override int GetWidth() {
            return thumb == null ? 0 : thumb.Width;
        }

        /// <summary>Gets the height of the underlying bitmap, or 0 if uninitialised.</summary>
        /// <remarks>
        /// Overrides the base accessor, which reads the inherited <c>height</c> field.
        /// A frame never populates that field, so the bitmap is the only source of truth.
        /// </remarks>
        public override int GetHeight() {
            return thumb == null ? 0 : thumb.Height;
        }

        /// <summary>
        /// Releases the backing <see cref="DirectBitmap"/> - a pinned pixel buffer plus a
        /// GDI bitmap. This overrides the base implementation instead of hiding it, so the
        /// buffer is still freed when the frame is disposed through a
        /// <see cref="Definitions.Sprites.SpriteDefinition"/> or <see cref="IDisposable"/>
        /// reference.
        /// </summary>
        /// <param name="disposing">True when called from <c>Dispose()</c>.</param>
        protected override void Dispose(bool disposing) {
            if(disposing) {
                _directBitmap?.Dispose();
                _directBitmap = null;
            }
            base.Dispose(disposing);
        }
    }
}
