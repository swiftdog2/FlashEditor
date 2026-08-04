using System;
using System.Collections.Generic;
using FlashEditor.Definitions;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins the CPU skeletal transform against numbers worked out by hand from the client.
    /// </summary>
    /// <remarks>
    ///     Nothing in this suite can see the GL surface, and on this machine nothing can - no BitBlt
    ///     capture reaches an OpenGL viewport here. So the drawing is checked by a human and the
    ///     <b>maths</b> is checked here, which is the half that can be. Every expected value below is
    ///     derived from <c>Renderable_Sub2.method2344</c> and stated in the test rather than produced
    ///     by running the code under test: round-tripping this implementation against itself would
    ///     prove nothing at all.
    /// </remarks>
    public class SkeletalTransformTests
    {
        /// <summary>A quarter turn in the client's 16384-step angle unit.</summary>
        private const int QuarterTurn = SkeletalTrig.AngleSteps / 4;

        /// <summary>
        ///     The trig tables are the client's: a full turn is 16384 steps and 1.0 is 16384.
        /// </summary>
        /// <remarks>
        ///     Asserted at the two angles whose double-precision values are exact, plus the Pythagorean
        ///     identity over a sweep, plus the wrap. Asserting every entry against a recomputation here
        ///     would only restate the constructor.
        /// </remarks>
        [Fact]
        public void TrigTables_UseTheClientsAngleUnitAndScale()
        {
            Assert.Equal(0, SkeletalTrig.Sin(0));
            Assert.Equal(SkeletalTrig.One, SkeletalTrig.Cos(0));

            Assert.Equal(SkeletalTrig.One, SkeletalTrig.Sin(QuarterTurn));
            Assert.Equal(0, SkeletalTrig.Cos(QuarterTurn));

            //An angle past a full turn is the same angle. The type-7 colour arm hands over values
            //that were never masked, so the table has to do it.
            Assert.Equal(SkeletalTrig.Sin(0), SkeletalTrig.Sin(SkeletalTrig.AngleSteps));
            Assert.Equal(SkeletalTrig.Cos(QuarterTurn), SkeletalTrig.Cos(QuarterTurn + SkeletalTrig.AngleSteps));

            for (int angle = 0; angle < SkeletalTrig.AngleSteps; angle += 97)
            {
                long sin = SkeletalTrig.Sin(angle);
                long cos = SkeletalTrig.Cos(angle);
                long unit = (long)SkeletalTrig.One * SkeletalTrig.One;
                //The table truncates toward zero, so the identity can fall short by up to twice the
                //sum of the two magnitudes and can never exceed unit. A part in 4000 covers that.
                Assert.InRange(sin * sin + cos * cos, unit - unit / 4000, unit + unit / 4000);
            }
        }

        /// <summary>
        ///     Type 0 puts the pivot on the centroid of the vertices its labels own.
        /// </summary>
        /// <remarks>
        ///     Also pins the promotion to sixteenths and the reduction back: the centroid of
        ///     (10,20,30) and (30,40,50) is (20,30,40) in model units whatever space it was computed
        ///     in, so a wrong shift shows up here rather than three transforms later.
        /// </remarks>
        [Fact]
        public void Pivot_IsTheCentroidOfTheLabelledVertices()
        {
            PosedMesh mesh = Mesh(
                new[] { 10, 30 }, new[] { 20, 40 }, new[] { 30, 50 }, new[] { 0, 0 });

            Assert.Equal(TransformOutcome.Applied, mesh.Apply(PosedMesh.TypePivot, Labels(0), 0, 0, 0));

            //Still in sixteenths at this point, which is what the translate arm needs.
            Assert.True(mesh.IsScaled);
            Assert.Equal(20 * 16, mesh.PivotX);
            Assert.Equal(30 * 16, mesh.PivotY);
            Assert.Equal(40 * 16, mesh.PivotZ);

            mesh.Finish();
            Assert.False(mesh.IsScaled);
            Assert.Equal(20, mesh.PivotX);
            Assert.Equal(30, mesh.PivotY);
            Assert.Equal(40, mesh.PivotZ);

            //And a promote-then-reduce leaves the mesh exactly where it started.
            Assert.Equal(new[] { 10, 30 }, mesh.VertexX);
            Assert.Equal(new[] { 20, 40 }, mesh.VertexY);
            Assert.Equal(new[] { 30, 50 }, mesh.VertexZ);
        }

        /// <summary>
        ///     A type-0 whose labels own nothing takes the offset alone rather than keeping the old
        ///     pivot.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:2823-2826</c>. The distinction matters for an unskinned model
        ///     loaded against a full entity skeleton, which is most of what a model viewer shows.
        /// </remarks>
        [Fact]
        public void Pivot_WithNoVerticesFallsBackToTheOffset()
        {
            PosedMesh mesh = Mesh(new[] { 10 }, new[] { 20 }, new[] { 30 }, new[] { 0 });

            mesh.Apply(PosedMesh.TypePivot, Labels(0), 1, 1, 1);
            Assert.Equal(10 * 16 + 16, mesh.PivotX);

            Assert.Equal(TransformOutcome.NoTargets, mesh.Apply(PosedMesh.TypePivot, Labels(9), 7, 8, 9));
            Assert.Equal(7 * 16, mesh.PivotX);
            Assert.Equal(8 * 16, mesh.PivotY);
            Assert.Equal(9 * 16, mesh.PivotZ);
        }

        /// <summary>Type 1 moves only the vertices its labels own.</summary>
        [Fact]
        public void Translate_MovesTheLabelledVerticesAndNothingElse()
        {
            PosedMesh mesh = Mesh(
                new[] { 10, 30 }, new[] { 20, 40 }, new[] { 30, 50 }, new[] { 0, 1 });

            Assert.Equal(TransformOutcome.Applied, mesh.Apply(PosedMesh.TypeTranslate, Labels(1), 5, -6, 7));
            mesh.Finish();

            Assert.Equal(new[] { 10, 35 }, mesh.VertexX);
            Assert.Equal(new[] { 20, 34 }, mesh.VertexY);
            Assert.Equal(new[] { 30, 57 }, mesh.VertexZ);
        }

        /// <summary>
        ///     Type 2 turns a vertex about the pivot, in the client's sense.
        /// </summary>
        /// <remarks>
        ///     A quarter turn about z takes (100, 0) to (0, -100): the client's matrix is
        ///     <c>x' = cx + sy</c>, <c>y' = cy - sx</c> (<c>Renderable_Sub2.java:2857-2864</c>), which
        ///     is a clockwise turn in the x-y plane, not the counter-clockwise one the usual
        ///     convention gives. Getting the sign wrong mirrors every animation and is invisible on a
        ///     symmetrical model.
        /// </remarks>
        [Fact]
        public void Rotate_AboutTheOriginMatchesTheClientsSenseOfRotation()
        {
            PosedMesh mesh = Mesh(new[] { 100 }, new[] { 0 }, new[] { 0 }, new[] { 0 });
            mesh.Apply(PosedMesh.TypeRotate, Labels(0), 0, 0, QuarterTurn);

            Assert.Equal(0, mesh.VertexX[0]);
            Assert.Equal(-100, mesh.VertexY[0]);
            Assert.Equal(0, mesh.VertexZ[0]);

            PosedMesh back = Mesh(new[] { 0 }, new[] { 100 }, new[] { 0 }, new[] { 0 });
            back.Apply(PosedMesh.TypeRotate, Labels(0), 0, 0, QuarterTurn);

            Assert.Equal(100, back.VertexX[0]);
            Assert.Equal(0, back.VertexY[0]);
            Assert.Equal(0, back.VertexZ[0]);
        }

        /// <summary>
        ///     A rotation slot applies z, then x, then y - not x, y, z.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:2864-2896</c> tests the z value first. Rotations do not commute,
        ///     so the order is not a detail: (0, 100, 0) turned a quarter about both z and x lands on
        ///     (100, 0, 0) in the client's order and on (0, 0, 100) in the written order of the
        ///     fields. Both are plausible-looking poses; only one is the client's.
        /// </remarks>
        [Fact]
        public void Rotate_AppliesZThenXThenY()
        {
            PosedMesh mesh = Mesh(new[] { 0 }, new[] { 100 }, new[] { 0 }, new[] { 0 });
            mesh.Apply(PosedMesh.TypeRotate, Labels(0), QuarterTurn, 0, QuarterTurn);

            Assert.Equal(new[] { 100 }, mesh.VertexX);
            Assert.Equal(new[] { 0 }, mesh.VertexY);
            Assert.Equal(new[] { 0 }, mesh.VertexZ);
        }

        /// <summary>Type 3 scales about the pivot, with 128 meaning unchanged.</summary>
        [Fact]
        public void Scale_IsAboutThePivotAndUsesOneTwentyEightAsUnity()
        {
            PosedMesh mesh = Mesh(
                new[] { 10, 30 }, new[] { 20, 40 }, new[] { 30, 50 }, new[] { 0, 0 });

            mesh.Apply(PosedMesh.TypePivot, Labels(0), 0, 0, 0);
            mesh.Apply(PosedMesh.TypeScale, Labels(0), 256, 128, 64);
            mesh.Finish();

            //Centroid is (20, 30, 40); each vertex is 10 from it on every axis, so doubling x moves
            //it to 20 away, leaving y alone and halving z moves it to 5.
            Assert.Equal(new[] { 0, 40 }, mesh.VertexX);
            Assert.Equal(new[] { 20, 40 }, mesh.VertexY);
            Assert.Equal(new[] { 35, 45 }, mesh.VertexZ);
        }

        /// <summary>
        ///     Type 5 shifts face alpha in steps of eight and clamps to the byte.
        /// </summary>
        /// <remarks>
        ///     Face labels, not vertex labels. A mesh whose vertex label 0 owns everything and whose
        ///     face label 0 owns nothing must not fade, which is what the second half asserts.
        /// </remarks>
        [Fact]
        public void Alpha_ShiftsByEightPerUnitAndClampsToTheByte()
        {
            PosedMesh mesh = MeshWithFaces(faceSkins: new[] { 0, 1 });

            Assert.Equal(TransformOutcome.Applied, mesh.Apply(PosedMesh.TypeAlpha, Labels(0), 10, 0, 0));
            Assert.Equal((byte)80, mesh.FaceAlpha[0]);
            Assert.Equal((byte)0, mesh.FaceAlpha[1]);
            Assert.True(mesh.FaceAlphaChanged);

            mesh.Apply(PosedMesh.TypeAlpha, Labels(0), 30, 0, 0);
            Assert.Equal((byte)255, mesh.FaceAlpha[0]);

            mesh.Apply(PosedMesh.TypeAlpha, Labels(0), -100, 0, 0);
            Assert.Equal((byte)0, mesh.FaceAlpha[0]);

            Assert.Equal(TransformOutcome.NoTargets, mesh.Apply(PosedMesh.TypeAlpha, Labels(5), 10, 0, 0));
        }

        /// <summary>
        ///     Type 7 wraps hue and clamps saturation and lightness, and quarters the saturation delta.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:3150-3170</c>. The three fields are treated differently from one
        ///     another, which is the part a re-derivation would get wrong.
        /// </remarks>
        [Fact]
        public void Colour_WrapsHueAndClampsSaturationAndLightness()
        {
            PosedMesh mesh = MeshWithFaces(faceSkins: new[] { 0, 0 });
            mesh.FaceColour[0] = unchecked((short)((60 << 10) | (3 << 7) | 100));
            mesh.FaceColour[1] = unchecked((short)((1 << 10) | (1 << 7) | 10));

            mesh.Apply(PosedMesh.TypeColour, Labels(0), 8, 20, 50);

            //Hue 60 + 8 wraps to 4; saturation 3 + 20/4 clamps at 7; lightness 100 + 50 clamps at 127.
            Assert.Equal((4 << 10) | (7 << 7) | 127, mesh.FaceColour[0] & 0xFFFF);
            //And a value with room to move just moves: hue 1 + 8, saturation 1 + 5, lightness 10 + 50.
            Assert.Equal((9 << 10) | (6 << 7) | 60, mesh.FaceColour[1] & 0xFFFF);
            Assert.True(mesh.FaceColourChanged);
        }

        /// <summary>
        ///     Types the client transforms live particles with are reported, not silently applied.
        /// </summary>
        /// <remarks>
        ///     8, 9 and 10 walk the model's spawned particle instances, which nothing here simulates.
        ///     Reporting them keeps a count a panel can show, so an animation that visibly does nothing
        ///     says why rather than looking broken.
        /// </remarks>
        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public void UnsupportedTypes_AreReportedRatherThanIgnored(int transformType)
        {
            PosedMesh mesh = Mesh(new[] { 10 }, new[] { 20 }, new[] { 30 }, new[] { 0 });

            Assert.Equal(TransformOutcome.Unsupported, mesh.Apply(transformType, Labels(0), 1, 2, 3));
            Assert.Equal(new[] { 10 }, mesh.VertexX);
        }

        /// <summary>
        ///     Label groups invert the model's per-vertex and per-face skins, and skip the negatives.
        /// </summary>
        /// <remarks>
        ///     A -1 skin means the vertex belongs to no label. Letting it fall into group 0 would
        ///     attach it to a real bone, and the client's guard is explicitly <c>&gt;= 0</c>
        ///     (<c>Renderable_Sub2.java:1015</c>).
        /// </remarks>
        [Fact]
        public void LabelGroups_InvertTheSkinArraysAndDropTheUnassigned()
        {
            var model = new ModelDefinition
            {
                VertX = new[] { 0, 0, 0, 0 },
                VertY = new[] { 0, 0, 0, 0 },
                VertZ = new[] { 0, 0, 0, 0 },
                VertSkins = new[] { 2, -1, 0, 2 },
                faceIndices1 = new[] { 0, 1 },
                faceIndices2 = new[] { 1, 2 },
                faceIndices3 = new[] { 2, 3 },
                FaceSkin = new[] { 1, -1 }
            };

            var skin = new SkinnedModel(model);

            Assert.Equal(new[] { 2 }, skin.VerticesFor(0));
            Assert.Empty(skin.VerticesFor(1));
            Assert.Equal(new[] { 0, 3 }, skin.VerticesFor(2));
            Assert.Empty(skin.VerticesFor(3));

            Assert.Empty(skin.FacesFor(0));
            Assert.Equal(new[] { 0 }, skin.FacesFor(1));
            Assert.Empty(skin.FacesFor(99));
        }

        /// <summary>A mesh whose coordinates and vertex labels are given directly.</summary>
        /// <param name="x">Rest x coordinates.</param>
        /// <param name="y">Rest y coordinates.</param>
        /// <param name="z">Rest z coordinates.</param>
        /// <param name="vertexSkins">Label id per vertex.</param>
        /// <returns>A pose over a one-face model, since a face array has to exist to be sized from.</returns>
        private static PosedMesh Mesh(int[] x, int[] y, int[] z, int[] vertexSkins)
        {
            var model = new ModelDefinition
            {
                VertX = x,
                VertY = y,
                VertZ = z,
                VertSkins = vertexSkins,
                faceIndices1 = new[] { 0 },
                faceIndices2 = new[] { 0 },
                faceIndices3 = new[] { 0 }
            };

            return new SkinnedModel(model).CreatePose();
        }

        /// <summary>A mesh with as many faces as it has face skins, and one vertex.</summary>
        /// <param name="faceSkins">Label id per face.</param>
        /// <returns>The pose.</returns>
        private static PosedMesh MeshWithFaces(int[] faceSkins)
        {
            var indices = new int[faceSkins.Length];
            var model = new ModelDefinition
            {
                VertX = new[] { 0 },
                VertY = new[] { 0 },
                VertZ = new[] { 0 },
                faceIndices1 = indices,
                faceIndices2 = (int[])indices.Clone(),
                faceIndices3 = (int[])indices.Clone(),
                FaceSkin = faceSkins
            };

            return new SkinnedModel(model).CreatePose();
        }

        /// <summary>One label id, as the list a bone would hand over.</summary>
        /// <param name="label">The label id.</param>
        /// <returns>A single-entry list.</returns>
        private static IReadOnlyList<int> Labels(int label) => new List<int> { label };
    }
}
