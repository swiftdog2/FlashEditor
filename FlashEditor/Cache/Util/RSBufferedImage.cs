using FlashEditor.cache.sprites;
using FlashEditor.Cache.Util;
using System;
using System.Drawing;

namespace FlashEditor.cache.util {
    public class RSBufferedImage : SpriteDefinition {
        public RSBufferedImage(int index, int width, int height) {
            this.index = index;
            thumb = new DirectBitmap(width, height).Bitmap;
        }

        public Bitmap GetSprite() {
            return thumb;
        }

        public void SetRGB(int x, int y, int rgb) {
            if(thumb == null)
                throw new Exception();
            thumb.SetPixel(x, y, Color.FromArgb(rgb));
        }

        internal int GetWidth() {
            return thumb == null ? 0 : thumb.Width;
        }

        internal int GetHeight() {
            return thumb == null ? 0 : thumb.Height;
        }
    }
}