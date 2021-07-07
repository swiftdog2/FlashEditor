/**
* Copyright (c) OpenRS
*
* Permission is hereby granted, free of charge, to any person obtaining a copy
* of this software and associated documentation files (the "Software"), to deal
* in the Software without restriction, including without limitation the rights
* to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
* copies of the Software, and to permit persons to whom the Software is
* furnished to do so, subject to the following conditions:
* 
* The above copyright notice and this permission notice shall be included in all
* copies or substantial portions of the Software.
* 
* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
* IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
* FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
* AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
* LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
* OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
* SOFTWARE.
*/

using FlashEditor.cache.util;
using FlashEditor.Collections;
using FlashEditor.utils;
using java.lang;
using System.Collections.Generic;
using System.Drawing;

namespace FlashEditor.cache.sprites {
    /// <summary>
    /// Represents a {@link Sprite} which may contain one or more frames.
    /// </summary>
    public class SpriteDefinition {
        //This flag indicates that the pixels should be read vertically instead of horizontally.
        public static readonly int FLAG_VERTICAL = 0x01;

        //This flag indicates that every pixel has an alpha, as well as red, green and blue, component.
        public static readonly int FLAG_ALPHA = 0x02;

        //The index at which this sprite exists.
        public int index;

        //The width of this sprite.
        public int width;

        //The height of this sprite.
        public int height;

        //The array of animation frames in this sprite.
        private List<RSBufferedImage> frames;
        public int frameCount;
        public Bitmap thumb;

        /// <summary>
        /// Creates a new sprite with one frame.
        /// </summary>
        /// <param name="width">The width of the sprite in pixels.</param>
        /// <param name="height">The height of the sprite in pixels.</param>
        public SpriteDefinition(int width, int height) : this(width, height, 1) { }

        /// <summary>
        /// Creates a new sprite with the specified number of frames.
        /// </summary>
        /// <param name="width">The width of the sprite in pixels.</param>
        /// <param name="height">The height of the sprite in pixels.</param>
        /// <param name="frameCount">The number of animation frames.</param>
        public SpriteDefinition(int width, int height, int frameCount) {
            if(frameCount < 1)
                throw new IllegalArgumentException();

            this.width = width;
            this.height = height;
            this.frameCount = frameCount;
            frames = new List<RSBufferedImage>(frameCount);
        }

        /// <summary>
        /// Decodes the {@link Sprite} from the specified {@link ByteBuffer}.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The sprite.</returns>
        internal static SpriteDefinition Decode(JagStream stream) {
            //Find the size of this sprite set
            stream.Seek(stream.Length - 2);
            int size = stream.ReadUnsignedShort();

            //Allocate arrays to store info
            int[] offsetsX = new int[size];
            int[] offsetsY = new int[size];
            int[] subWidths = new int[size];
            int[] subHeights = new int[size];

            //Read the width, height and palette size
            stream.Seek(stream.Length - size * 8 - 7);
            int width = stream.ReadShort();
            int height = stream.ReadShort();
            int[] palette = new int[stream.ReadUnsignedByte() + 1];

            DebugUtil.Debug("Size: " + size + ", width: " + width + ", height: " + height + ", palette elements: " + palette.Length);

            //And allocate an object for this sprite set
            SpriteDefinition set = new SpriteDefinition(width, height, size);

            //Read the offsets and dimensions of the individual sprites
            for(int i = 0; i < size; i++)
                offsetsX[i] = stream.ReadUnsignedShort();

            for(int i = 0; i < size; i++)
                offsetsY[i] = stream.ReadUnsignedShort();

            for(int i = 0; i < size; i++)
                subWidths[i] = stream.ReadUnsignedShort();

            for(int i = 0; i < size; i++)
                subHeights[i] = stream.ReadUnsignedShort();

            //Read the palette
            stream.Seek(stream.Length - size * 8 - 7 - (palette.Length - 1) * 3);
            for(int index = 1; index < palette.Length; index++) {
                palette[index] = stream.ReadMedium();
                if(palette[index] == 0)
                    palette[index] = 1;
            }

            //Read the pixels themselves
            stream.Seek(0);
            for(int id = 0; id < size; id++) {
                //DebugUtil.Debug("\tReading frame " + id);

                //Grab some frequently used values
                int subWidth = subWidths[id], subHeight = subHeights[id];
                int offsetX = offsetsX[id], offsetY = offsetsY[id];

                //DebugUtil.Debug("\t\tsubWidth: " + subWidth + ", subHeight: " + subHeight + ", offsetX: " + offsetX + ", offsetY: " + offsetY);

                //Create a BufferedImage to store the resulting image
                util.RSBufferedImage image = new RSBufferedImage(id, size, java.lang.Math.max(width, subWidth), java.lang.Math.max(height, subHeight), java.awt.image.BufferedImage.TYPE_INT_ARGB);
                set.frames.Add(image);

                //Allocate an array for the palette indices
                int[][] indices = ArrayUtil.ReturnRectangularArray<int>(subWidth, subHeight);

                //Read the flags so we know whether to read horizontally or vertically
                int flags = stream.ReadUnsignedByte();
                //DebugUtil.Debug("\t\tFlags [alpha: " + (flags & FLAG_ALPHA) + ", vertical: " + (flags & FLAG_VERTICAL) + "]");

                //Read the palette indices
                if((flags & FLAG_VERTICAL) != 0) {
                    for(int x = 0; x < subWidth; x++)
                        for(int y = 0; y < subHeight; y++)
                            indices[x][y] = stream.ReadUnsignedByte();
                } else {
                    for(int y = 0; y < subHeight; y++)
                        for(int x = 0; x < subWidth; x++)
                            indices[x][y] = stream.ReadUnsignedByte();
                }

                //Read the alpha (if there is alpha) and convert values to ARGB
                if((flags & FLAG_ALPHA) == 0) {
                    //If it's horizontal
                    if((flags & FLAG_VERTICAL) == 0) {
                        for(int x = 0; x < subWidth; x++) {
                            for(int y = 0; y < subHeight; y++) {
                                int index = indices[x][y];
                                image.setRGB(x + offsetX, y + offsetY, index == 0 ? 0 : (int) (0xFF000000 | palette[index]));
                            }
                        }
                    } else { //If it's vertical
                        for(int y = 0; y < subHeight; y++) {
                            for(int x = 0; x < subWidth; x++) {
                                int index = indices[x][y];
                                image.setRGB(x + offsetX, y + offsetY, index == 0 ? 0 : (int) (0xFF000000 | palette[index]));
                            }
                        }
                    }
                } else {
                    if((flags & FLAG_VERTICAL) != 0) {
                        for(int x = 0; x < subWidth; x++) {
                            for(int y = 0; y < subHeight; y++) {
                                int alpha = stream.ReadUnsignedByte();
                                image.setRGB(x + offsetX, y + offsetY, alpha << 24 | palette[indices[x][y]]);
                            }
                        }
                    } else {
                        for(int y = 0; y < subHeight; y++) {
                            for(int x = 0; x < subWidth; x++) {
                                int alpha = stream.ReadUnsignedByte();
                                image.setRGB(x + offsetX, y + offsetY, alpha << 24 | palette[indices[x][y]]);
                            }
                        }
                    }
                }

                if(id == 0)
                    set.thumb = image.getBitmap();
                else
                    image.setThumb(image.getBitmap());
            }
            return set;
        }

        /// <summary>
        /// Gets the frame with the specified id.
        /// </summary>
        /// <param name="id"></param>
        /// <returns>The frame.</returns>
        public java.awt.image.BufferedImage getFrame(int id) {
            return frames[id];
        }

        /// <summary>
        /// Gets the height of this sprite.
        /// </summary>
        /// <returns>The height of this sprite.</returns>
        public int getHeight() {
            return height;
        }

        /**
         * Gets the width of this sprite.
         * 
         * @return The width of this sprite.
         */
        public int GetWidth() {
            return width;
        }

        /**
         * Sets the frame with the specified id.
         * 
         * @param id
         *            The id.
         * @param frame
         *            The frame.
         */
        public void SetFrame(int id, RSBufferedImage frame) {
            if(frame.getWidth() != width || frame.getHeight() != height)
                throw new IllegalArgumentException("The frame's dimensions do not match with the sprite's dimensions.");

            frames[id] = frame;
        }

        /// <summary>
        /// Gets the number of frames in this set.
        /// </summary>
        /// <returns>The number of frames.</returns>
        public int GetFrameCount() {
            return frames.Count;
        }

        public List<RSBufferedImage> GetFrames() {
            return frames;
        }

        internal void setIndex(int index) {
            this.index = index;
        }
    }
}
