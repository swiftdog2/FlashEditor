using System;

namespace FlashEditor.Map {
    /// <summary>
    ///     The polygons an overlay shape paints over a tile.
    /// </summary>
    /// <remarks>
    ///     A tile is split into an overlay region and an underlay region by its shape code. The
    ///     client describes both as triangle lists over a shared 13-entry vertex table, with the
    ///     overlay triangles always listed first.
    ///
    ///     There are three parallel triangulations in the client, chosen by detail level and by
    ///     whether a neighbouring overlay blends across the edge. All three cover the same overlay
    ///     region for every shape - only the triangulation differs - so a 2D renderer only needs the
    ///     simplest of them, which is the one transcribed here (Class64_Sub28, Node_Sub45,
    ///     Class93_Sub1, with the counts from Node_Sub46_Sub20_Sub2 and Node_Sub31_Sub4).
    ///
    ///     Shape codes in map data run 0..11, since <c>(opcode - 2) / 4</c> over opcodes 2..49 caps
    ///     at 11. Shapes 12..14 exist only inside the scene builder; 12 is the full-underlay tile
    ///     substituted when a tile has shape 0 and no overlay definition, which is 90% of all tiles.
    ///
    ///     See <c>reference/hydra-637-maps/05-colour-and-rendering.md</c>.
    /// </remarks>
    public static class TileShapes {
        /// <summary>Tile edge length in the client's world units.</summary>
        public const int TileSize = 512;

        /// <summary>Shape codes a terrain file can carry, 0..11.</summary>
        public const int FileShapeCount = 12;

        /// <summary>The internal shape meaning "all underlay, no overlay".</summary>
        public const int ShapeFullUnderlay = 12;

        /// <summary>Vertex X, in 512-unit tile space. Class305.java:118.</summary>
        private static readonly int[] VertexX = { 0, 256, 512, 512, 512, 256, 0, 0, 128, 256, 128, 384, 256 };

        /// <summary>Vertex Y, in 512-unit tile space. Class305.java:103.</summary>
        private static readonly int[] VertexY = { 0, 0, 0, 256, 512, 512, 512, 256, 256, 384, 128, 128, 256 };

        /// <summary>Overlay triangles per shape. Node_Sub46_Sub20_Sub2.java:16.</summary>
        private static readonly int[] OverlayTriCount = { 2, 1, 1, 1, 2, 2, 2, 1, 3, 3, 3, 2, 0, 4, 0 };

        /// <summary>Triangle vertex A. Class64_Sub28.java:8-10.</summary>
        private static readonly int[][] TriA = {
            new[]{0,2},         new[]{0,2},         new[]{0,0,2},       new[]{2,0,0},
            new[]{0,2,0},       new[]{0,0,2},       new[]{0,5,1,4},     new[]{0,4,4,4},
            new[]{4,4,4,0},     new[]{6,6,6,2,2,2}, new[]{2,2,2,6,6,6}, new[]{0,11,6,6,6,4},
            new[]{0,2},         new[]{0,4,4,4},     new[]{0,4,4,4}
        };

        /// <summary>Triangle vertex B. Node_Sub45.java:22-24.</summary>
        private static readonly int[][] TriB = {
            new[]{2,4},         new[]{2,4},         new[]{5,2,4},       new[]{4,5,2},
            new[]{2,4,5},       new[]{5,2,4},       new[]{1,6,2,5},     new[]{1,6,7,1},
            new[]{6,7,1,1},     new[]{0,8,9,8,9,4}, new[]{8,9,4,0,8,9}, new[]{2,10,0,10,11,11},
            new[]{2,4},         new[]{1,6,7,1},     new[]{1,6,7,1}
        };

        /// <summary>Triangle vertex C. Class93_Sub1.java:11-13.</summary>
        private static readonly int[][] TriC = {
            new[]{6,6},         new[]{6,6},         new[]{6,5,5},       new[]{5,6,5},
            new[]{5,5,6},       new[]{6,5,5},       new[]{5,0,4,1},     new[]{7,7,1,2},
            new[]{7,1,2,7},     new[]{8,9,4,0,8,9}, new[]{0,8,9,8,9,4}, new[]{11,0,10,11,4,2},
            new[]{6,6},         new[]{7,7,1,2},     new[]{7,7,1,2}
        };

        /// <summary>Whether a shape covers the whole tile with overlay, leaving no underlay showing.</summary>
        /// <param name="shape">A shape code.</param>
        /// <returns><c>true</c> when the shape has no underlay region.</returns>
        public static bool IsFullOverlay(int shape) => shape == 0;

        /// <summary>
        ///     The overlay triangles for a shape and rotation, in unit tile space.
        /// </summary>
        /// <remarks>
        ///     Rotation is a coordinate transform, not a permutation of vertex indices. The
        ///     perimeter vertices 0..7 do rotate as <c>(v - 2 * rotation) &amp; 7</c>, but vertices
        ///     8..11 are not closed under rotation, so a port that only permutes indices produces
        ///     wrong geometry for shapes 9, 10 and 11.
        /// </remarks>
        /// <param name="shape">Shape code. Values outside 0..14 yield no triangles.</param>
        /// <param name="rotation">Rotation 0..3.</param>
        /// <returns>
        ///     Triangles as flat <c>(x0,y0,x1,y1,x2,y2)</c> groups, each coordinate in 0..1.
        /// </returns>
        public static float[][] OverlayTriangles(int shape, int rotation) {
            if (shape < 0 || shape >= TriA.Length)
                return Array.Empty<float[]>();

            int count = OverlayTriCount[shape];
            var triangles = new float[count][];

            for (int i = 0; i < count; i++) {
                triangles[i] = new float[6];
                WriteVertex(triangles[i], 0, TriA[shape][i], rotation);
                WriteVertex(triangles[i], 2, TriB[shape][i], rotation);
                WriteVertex(triangles[i], 4, TriC[shape][i], rotation);
            }

            return triangles;
        }

        private static void WriteVertex(float[] target, int offset, int vertex, int rotation) {
            int x = VertexX[vertex];
            int y = VertexY[vertex];

            switch (rotation & 3) {
                case 1:
                    (x, y) = (y, TileSize - x);
                    break;
                case 2:
                    (x, y) = (TileSize - x, TileSize - y);
                    break;
                case 3:
                    (x, y) = (TileSize - y, x);
                    break;
            }

            target[offset] = x / (float) TileSize;
            target[offset + 1] = y / (float) TileSize;
        }
    }
}
