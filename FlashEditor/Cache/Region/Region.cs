using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlashEditor.Cache.Region {
    /// <summary>
    ///     Represents a 64×64 map region and exposes methods for
    ///     decoding its terrain and location data.
    /// </summary>
    public class Region {
        public static int WIDTH = 64;
        public static int HEIGHT = 64;

        private int regionID;
        private int baseX;
        private int baseY;

        private int[, ,] tileHeights = new int[4,104,104];
        private byte[, ,] renderRules = new byte[4,104,104];
        private byte[, ,] overlayIds = new byte[4,104,104];
        private byte[, ,] overlayPaths = new byte[4,104,104];
        private byte[, ,] overlayRotations = new byte[4,104,104];
        private byte[, ,] underlayIds = new byte[4,104,104];

        private List<Location> locations = new List<Location>();

        public Region(int id) {
            this.regionID = id;
            this.baseX = (id >> 8 & 0xFF) << 6;
            this.baseY = (id & 0xFF) << 6;
        }

        /**
        * Decodes terrain data stored in the specified {@link JagStream}.
        *
        * @param buf
        *      The JagStream.
        */
        public void LoadTerrain(JagStream buf) {
            for(int z = 0; z < 4; z++) {
                for(int x = 0; x < 64; x++) {
                    for(int y = 0; y < 64; y++) {
                        while(true) {
                            int attribute = buf.ReadByte();
                            switch(attribute) {
                                case 0:
                                    if(z == 0) {
                                        // TODO: Verify the height calculation matches the game client
                                        tileHeights[0, x, y] = HeightCalc.Calculate(baseX, baseY, x, y) << 3;
                                    } else {
                                        tileHeights[z, x, y] = tileHeights[z - 1, x, y] - 240;
                                    }
                                    break;
                                case 1:
                                    int height = buf.ReadByte();
                                    if(height == 1)
                                        height = 0;

                                    if(z == 0)
                                        tileHeights[0, x, y] = -height << 3;
                                    else
                                        tileHeights[z, x, y] = tileHeights[z - 1, x, y] - height << 3;

                                    break;
                                case <= 49:
                                    overlayIds[z, x, y] = (byte) buf.ReadByte();
                                    overlayPaths[z, x, y] = (byte) ((attribute - 2) / 4);
                                    overlayRotations[z, x, y] = (byte) (attribute - 2 & 0x3);
                                    continue;
                                case <= 81:
                                    renderRules[z, x, y] = (byte) (attribute - 49);
                                    continue;
                                default:
                                    underlayIds[z, x, y] = (byte) (attribute - 81);
                                    continue;
                            }
                        }
                    }
                }
            }
        }

        /**
        * Decodes location data stored in the specified {@link JagStream}.
        *
        * @param buf
        *      The JagStream.
        */
        public void LoadLocations(JagStream buf) {
            int id = -1;
            int idOffset;

            while((idOffset = buf.ReadUnsignedSmart()) != 0) {
                id += idOffset;

                int position = 0;
                int positionOffset;

                while((positionOffset = buf.ReadUnsignedSmart()) != 0) {
                    position += positionOffset - 1;

                    int localY = position & 0x3F;
                    int localX = position >> 6 & 0x3F;
                    int height = position >> 12 & 0x3;

                    int attributes = buf.ReadByte() & 0xFF;
                    int type = attributes >> 2;
                    int orientation = attributes & 0x3;

                    locations.Add(new Location(id, type, orientation, new Position(baseX + localX, baseY + localY, height)));
                }
            }
        }

        public int GetRegionID() {
            return regionID;
        }

        public int GetBaseX() {
            return baseX;
        }

        public int GetBaseY() {
            return baseY;
        }

        public int GetTileHeight(int z, int x, int y) {
            return tileHeights[z, x, y];
        }

        public byte GetRenderRule(int z, int x, int y) {
            return renderRules[z, x, y];
        }

        public int GetOverlayId(int z, int x, int y) {
            return overlayIds[z, x, y] & 0xFF;
        }

        public byte GetOverlayPath(int z, int x, int y) {
            return overlayPaths[z, x, y];
        }

        public byte GetOverlayRotation(int z, int x, int y) {
            return overlayRotations[z, x, y];
        }

        public int GetUnderlayId(int z, int x, int y) {
            return underlayIds[z, x, y] & 0xFF;
        }

        public bool IsLinkedBelow(int z, int x, int y) {
            return (GetRenderRule(z, x, y) & 0x2) != 0;
        }

        public bool IsVisibleBelow(int z, int x, int y) {
            return (GetRenderRule(z, x, y) & 0x8) != 0;
        }

        public List<Location> GetLocations() {
            return locations;
        }

        public string GetLocationsIdentifier() {
            return "l" + (regionID >> 8) + "_" + (regionID & 0xFF);
        }

        public string GetTerrainIdentifier() {
            return "m" + (regionID >> 8) + "_" + (regionID & 0xFF);
        }
    }
}

