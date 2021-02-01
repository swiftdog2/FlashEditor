using FlashEditor.Cache.Util;
using System;
using System.Drawing;

namespace FlashEditor.cache.util {
    public class RSBufferedImage {
        public int index;
        public DirectBitmap sprite;

        public RSBufferedImage(int index, int width, int height) {
            this.index = index;
            sprite = new DirectBitmap(width, height);
        }

        public DirectBitmap getSprite() {
            return sprite;
        }

        public void setRGB(int x, int y, int rgb) {
            if(sprite == null)
                throw new Exception();
            sprite.SetPixel(x, y, Color.FromArgb(rgb));
        }

        internal int getWidth() {
            return sprite == null ? 0 : sprite.Width;
        }

        internal int getHeight() {
            return sprite == null ? 0 : sprite.Height;
        }
    }
}