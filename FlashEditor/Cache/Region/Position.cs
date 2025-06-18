using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Represents a coordinate within a map region and provides
    ///     convenience methods for converting between absolute and
    ///     local coordinates.
    /// </summary>
    public class Position {
        enum RegionSize {
            DEFAULT = 104,
            LARGE = 120,
            XLARGE = 136,
            XXLARGE = 168
        }

        private int size;
        private int mapSize;
        private int x;
        private int y;
        private int height;

        public Position(int x, int y, int height) : this(x, y, height, (int) RegionSize.DEFAULT) { }

        public Position(int x, int y, int height, int mapSize) {
            this.x = x;
            this.y = y;
            this.height = height;
            this.size = mapSize;
        }

        public Position(int localX, int localY, int height, int regionId, int mapSize) : this(localX + (((regionId >> 8) & 0xFF) << 6), localY + ((regionId & 0xff) << 6), height, mapSize) { }

        public int GetXInRegion() {
            return x & 0x3F;
        }

        public int GetYInRegion() {
            return y & 0x3F;
        }

        public int GetLocalX() {
            return x - 8 * (GetChunkX() - (mapSize >> 4));
        }

        public int GetLocalY() {
            return y - 8 * (GetChunkY() - (mapSize >> 4));
        }

        public int GetLocalX(Position pos) {
            return x - 8 * (pos.GetChunkX() - (mapSize >> 4));
        }

        public int GetLocalY(Position pos) {
            return y - 8 * (pos.GetChunkY() - (mapSize >> 4));
        }

        public int GetChunkX() {
            return (x >> 3);
        }

        public int GetChunkY() {
            return (y >> 3);
        }

        public int GetRegionX() {
            return (x >> 6);
        }

        public int GetRegionY() {
            return (y >> 6);
        }

        public int GetRegionID() {
            return ((GetRegionX() << 8) + GetRegionY());
        }

        public int GetX() {
            return x;
        }

        public int GetY() {
            return y;
        }

        public int GetHeight() {
            return height;
        }

        public int GetMapSize() {
            return mapSize;
        }

        public int ToRegionPacked() {
            return GetRegionY() + (GetRegionX() << 8) + (height << 16);
        }

        public int ToPositionPacked() {
            return y + (x << 14) + (height << 28);
        }

        public Position ToAbsolute() {
            int xOff = x % 8;
            int yOff = y % 8;
            return new Position(x - xOff, y - yOff, height);
        }

        public override string ToString() {
            return "X: " + GetX() + ", Y: " + GetY() + ", Height: " + GetHeight();
        }
    }
}

