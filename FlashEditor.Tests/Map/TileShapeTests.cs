using System;
using System.Collections.Generic;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the overlay shape geometry and the rotation transform.
    /// </summary>
    /// <remarks>
    ///     Shape geometry fails silently: a wrong triangle still fills, just in the wrong place, and
    ///     no cache byte disagrees. These tests assert the invariants the client's own tables
    ///     satisfy - area, containment and rotational symmetry - which a transcription error breaks.
    /// </remarks>
    public sealed class TileShapeTests
    {
        /// <summary>Every triangle stays inside the tile at every rotation.</summary>
        [Fact]
        public void AllTrianglesStayInsideTheTile()
        {
            for (int shape = 0; shape < TileShapes.FileShapeCount; shape++)
            {
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    foreach (float[] tri in TileShapes.OverlayTriangles(shape, rotation))
                    {
                        for (int i = 0; i < 6; i++)
                            Assert.InRange(tri[i], 0f, 1f);
                    }
                }
            }
        }

        /// <summary>
        ///     Shape 0 covers the whole tile; the rest cover part of it.
        /// </summary>
        /// <remarks>
        ///     Shape 0 is 85% of every overlay opcode in the cache, so if any single shape has to be
        ///     right it is this one. It must tile the full unit square, or every path and road in
        ///     the world renders with gaps.
        /// </remarks>
        [Fact]
        public void ShapeZeroCoversTheWholeTile()
        {
            Assert.True(TileShapes.IsFullOverlay(0));
            Assert.Equal(1.0, Area(0, 0), 3);

            for (int shape = 1; shape < TileShapes.FileShapeCount; shape++)
            {
                double area = Area(shape, 0);
                Assert.True(area > 0, $"shape {shape} has no overlay area");
                Assert.True(area < 1.0, $"shape {shape} covers the whole tile but is not shape 0");
            }
        }

        /// <summary>Rotation preserves area, since it is a rigid transform.</summary>
        [Fact]
        public void RotationPreservesArea()
        {
            for (int shape = 0; shape < TileShapes.FileShapeCount; shape++)
            {
                double expected = Area(shape, 0);
                for (int rotation = 1; rotation < 4; rotation++)
                    Assert.Equal(expected, Area(shape, rotation), 3);
            }
        }

        /// <summary>
        ///     Four rotations return to the start.
        /// </summary>
        /// <remarks>
        ///     The transform is applied to coordinates, not to vertex indices. Vertices 8 to 11 are
        ///     not closed under the index permutation, so a port that permutes indices instead
        ///     passes the area check above and fails this one for shapes 9, 10 and 11.
        /// </remarks>
        [Fact]
        public void FourRotationsAreIdentity()
        {
            for (int shape = 0; shape < TileShapes.FileShapeCount; shape++)
            {
                float[][] start = TileShapes.OverlayTriangles(shape, 0);
                float[][] wrapped = TileShapes.OverlayTriangles(shape, 4);

                Assert.Equal(start.Length, wrapped.Length);
                for (int t = 0; t < start.Length; t++)
                    for (int i = 0; i < 6; i++)
                        Assert.Equal(start[t][i], wrapped[t][i], 4);
            }
        }

        /// <summary>
        ///     Rotating by 2 is a point reflection through the tile centre.
        /// </summary>
        [Fact]
        public void HalfTurnMirrorsThroughTheCentre()
        {
            for (int shape = 0; shape < TileShapes.FileShapeCount; shape++)
            {
                float[][] start = TileShapes.OverlayTriangles(shape, 0);
                float[][] turned = TileShapes.OverlayTriangles(shape, 2);

                for (int t = 0; t < start.Length; t++)
                    for (int i = 0; i < 6; i++)
                        Assert.Equal(1f - start[t][i], turned[t][i], 4);
            }
        }

        /// <summary>Shapes outside the table yield nothing rather than throwing.</summary>
        [Fact]
        public void OutOfRangeShapesAreEmpty()
        {
            Assert.Empty(TileShapes.OverlayTriangles(-1, 0));
            Assert.Empty(TileShapes.OverlayTriangles(99, 0));

            //Shape 12 is the full-underlay tile: it exists, and it has no overlay region.
            Assert.Empty(TileShapes.OverlayTriangles(TileShapes.ShapeFullUnderlay, 0));
        }

        /// <summary>The shape-to-slot routing table matches the client's.</summary>
        [Theory]
        [InlineData(0, LocGroup.Wall)]
        [InlineData(3, LocGroup.Wall)]
        [InlineData(4, LocGroup.WallDecoration)]
        [InlineData(8, LocGroup.WallDecoration)]
        [InlineData(9, LocGroup.GameObject)]
        [InlineData(21, LocGroup.GameObject)]
        [InlineData(22, LocGroup.GroundDecoration)]
        public void ShapesRouteToTheRightSlot(int shape, LocGroup expected)
        {
            Assert.Equal(expected, LocGroups.Of(shape));
        }

        /// <summary>The signed area of a shape's overlay region, in unit tile space.</summary>
        private static double Area(int shape, int rotation)
        {
            double total = 0;
            foreach (float[] t in TileShapes.OverlayTriangles(shape, rotation))
                total += Math.Abs((t[2] - t[0]) * (t[5] - t[1]) - (t[4] - t[0]) * (t[3] - t[1])) / 2.0;
            return total;
        }
    }
}
