using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Util {
    /// <summary>
    ///     Lightweight bitmap providing fast pixel access via managed memory.
    /// </summary>
    public class DirectBitmap : IDisposable {
        /// <summary>Backed <see cref="Bitmap"/> instance.</summary>
        public Bitmap Bitmap { get; private set; }
        /// <summary>Pixel buffer in ARGB format.</summary>
        public int[] Bits { get; private set; }
        /// <summary>Gets whether the bitmap has been disposed.</summary>
        public bool Disposed { get; private set; }
        /// <summary>Bitmap height in pixels.</summary>
        public int Height { get; private set; }
        /// <summary>Bitmap width in pixels.</summary>
        public int Width { get; private set; }

        /// <summary>Handle pinning the <see cref="Bits"/> array in memory.</summary>
        protected GCHandle BitsHandle { get; private set; }

        /// <summary>
        /// Initializes a new instance sized to the specified dimensions.
        /// </summary>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        public DirectBitmap(int width, int height) {
            Width = width;
            Height = height;
            Bits = new int[width * height];
            BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, BitsHandle.AddrOfPinnedObject());
        }

        /// <summary>Saves the bitmap to a file.</summary>
        /// <param name="directory">Destination file path.</param>
        public void Save(string directory) {
            Bitmap.Save(directory);
        }

        /// <summary>Sets the pixel at the given coordinates.</summary>
        /// <param name="x">X position.</param>
        /// <param name="y">Y position.</param>
        /// <param name="colour">Colour value.</param>
        public void SetPixel(int x, int y, Color colour) {
            int index = x + (y * Width);
            int col = colour.ToArgb();

            Bits[index] = col;
        }

        /// <summary>Gets the colour of the specified pixel.</summary>
        /// <param name="x">X position.</param>
        /// <param name="y">Y position.</param>
        /// <returns>The colour value.</returns>
        public Color GetPixel(int x, int y) {
            int index = x + (y * Width);
            int col = Bits[index];
            Color result = Color.FromArgb(col);

            return result;
        }

        /// <summary>Releases resources used by this instance.</summary>
        public void Dispose() {
            if(Disposed)
                return;
            Disposed = true;
            Bitmap.Dispose();
            BitsHandle.Free();
        }
    }
}
