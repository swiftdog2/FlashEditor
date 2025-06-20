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
using static FlashEditor.utils.DebugUtil;
using FlashEditor.Collections;
using System.Collections.Generic;
using System.Drawing;
using System;
using FlashEditor;

namespace FlashEditor.cache.sprites {
    /// <summary>
    /// Represents a {@link Sprite} which may contain one or more frames.
    /// </summary>
    public class SpriteDefinition : IDefinition {
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
            frames = new List<RSBufferedImage>(frameCount);
        }

        /// <summary>
        /// Decodes the {@link Sprite} from the specified {@link ByteBuffer}.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <returns>The sprite.</returns>
        public void Decode(JagStream stream, int[] xteaKey = null) {
            //Find the size of this sprite set
            stream.Seek(stream.Length - 2);
            int size = stream.ReadUnsignedShort();

            //Read the width, height and palette size
            stream.Seek(stream.Length - size * 8 - 7);
            int width = stream.ReadUnsignedShort();
            int height = stream.ReadUnsignedShort();
            int[] palette = new int[stream.ReadUnsignedByte() + 1];

            Debug("Size: " + size + ", width: " + width + ", height: " + height + ", palette elements: " + palette.Length, LOG_DETAIL.INSANE);

            //And allocate an object for this sprite set
            this.width = width;
            this.height = height;
            this.frameCount = size;
            frames = new List<RSBufferedImage>(size);

            //Read the offsets and dimensions of the individual sprites
            int[] offsetsX = stream.ReadUnsignedShortArray(size);
            int[] offsetsY = stream.ReadUnsignedShortArray(size);
            int[] subWidths = stream.ReadUnsignedShortArray(size);
            int[] subHeights = stream.ReadUnsignedShortArray(size);

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
                Debug("\tReading frame " + id, LOG_DETAIL.INSANE);

                //Grab some frequently used values
                int subWidth = subWidths[id], subHeight = subHeights[id];
                int offsetX = offsetsX[id], offsetY = offsetsY[id];

                Debug("\t\tsubWidth: " + subWidth + ", subHeight: " + subHeight + ", offsetX: " + offsetX + ", offsetY: " + offsetY, LOG_DETAIL.INSANE);

                //Create a BufferedImage to store the resulting image
                RSBufferedImage image = new RSBufferedImage(id, Math.Max(width, subWidth), Math.Max(height, subHeight));

                //Allocate an array for the palette indices
                int[][] indices = ArrayUtil.ReturnRectangularArray<int>(subWidth, subHeight);

                //Read the flags so we know whether to read horizontally or vertically
                int flags = stream.ReadUnsignedByte();
                //Debug("\t\tFlags [alpha: " + (flags & FLAG_ALPHA) + ", vertical: " + (flags & FLAG_VERTICAL) + "]", LOG_DETAIL.INSANE);

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
                                image.SetRGB(x + offsetX, y + offsetY, index == 0 ? 0 : (int) (0xFF000000 | palette[index]));
                            }
                        }
                    } else { //If it's vertical
                        for(int y = 0; y < subHeight; y++) {
                            for(int x = 0; x < subWidth; x++) {
                                int index = indices[x][y];
                                image.SetRGB(x + offsetX, y + offsetY, index == 0 ? 0 : (int) (0xFF000000 | palette[index]));
                            }
                        }
                    }
                } else {
                    if((flags & FLAG_VERTICAL) != 0) {
                        for(int x = 0; x < subWidth; x++) {
                            for(int y = 0; y < subHeight; y++) {
                                int alpha = stream.ReadUnsignedByte();
                                image.SetRGB(x + offsetX, y + offsetY, alpha << 24 | palette[indices[x][y]]);
                            }
                        }
                    } else {
                        for(int y = 0; y < subHeight; y++) {
                            for(int x = 0; x < subWidth; x++) {
                                int alpha = stream.ReadUnsignedByte();
                                image.SetRGB(x + offsetX, y + offsetY, alpha << 24 | palette[indices[x][y]]);
                            }
                        }
                    }
                }

                //First frame in the sprite is the thumb image
                if(id == 0)
                    this.thumb = image.GetSprite();

                this.frames.Add(image);
            }
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

        /// <summary>
        /// Gets the frame with the specified id.
        /// </summary>
        /// <param name="id"></param>
        /// <returns>The frame.</returns>
        public RSBufferedImage GetFrame(int id) {
            return frames[id];
        }

        /// <summary>
        /// Gets the height of this sprite.
        /// </summary>
        /// <returns>The height of this sprite.</returns>
        public int GetHeight() {
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
            if(frame.GetWidth() != width || frame.GetHeight() != height)
                throw new ArgumentException("The frame's dimensions do not match with the sprite's dimensions.");

            frames[id] = frame;
        }

        /// <summary>
        /// Gets the number of frames in this set.
        /// </summary>
        /// <returns>The number of frames.</returns>
        public int GetFrameCount() {
            if(frames != null)
                return frames.Count;
            return 0;
        }

        public List<RSBufferedImage> GetFrames() {
            return frames;
        }

        internal void SetIndex(int index) {
            this.index = index;
        }

        /// <summary>Encodes the sprite set to a stream.</summary>
        /// <returns>Serialized sprite data.</returns>
        public JagStream Encode() {
            throw new NotImplementedException();
        }
    }
}
