using System;
using System.Drawing;

namespace FlashEditor.cache.util {
    public class RSBufferedImage : java.awt.image.BufferedImage {
        public int index;
        public int width, height;
        public Bitmap thumb;

        public RSBufferedImage(int index, int frameCount, int width, int height, int imageType) : base(width, height, imageType) {
            this.index = index;
            this.width = width;
            this.height = height;
        }

        internal void setThumb(Bitmap thumb) {
            this.thumb = thumb;
        }
    }
}
