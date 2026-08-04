using System;
using System.Collections.Generic;
using System.Numerics;
using FlashEditor.Definitions;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins what the cursor picks, and which index space each half of the answer is in.
    /// </summary>
    /// <remarks>
    ///     The feature this supports is choosing a particle attachment point, and a particle emitter
    ///     attaches to a <b>face</b> while an effector attaches to a <b>vertex</b>. The two are
    ///     numbered independently and overlap in range, so a picker that reported one where the other
    ///     was wanted would be accepted silently by every layer below and produce an effect coming
    ///     out of the wrong part of the model. Several tests here exist only to state which is which.
    /// </remarks>
    public class PickMeshTests
    {
        /// <summary>
        ///     A model coordinate becomes a world coordinate by one divisor and two negations.
        /// </summary>
        /// <remarks>
        ///     Pinned because three separate consumers depend on agreeing with the uploader, and
        ///     disagreement is invisible: the picker still returns a face, just not the one on
        ///     screen.
        /// </remarks>
        [Fact]
        public void RenderSpace_DividesAndFlipsYAndZ()
        {
            Vector3 world = RenderSpace.ToWorld(256, 128, 384);

            Assert.Equal(2.0, world.X, 5);
            Assert.Equal(-1.0, world.Y, 5);
            Assert.Equal(-3.0, world.Z, 5);
            Assert.Equal(128f, RenderSpace.ModelUnitsPerWorldUnit);
        }

        /// <summary>
        ///     The hit names the face it landed on and the three vertices of that face.
        /// </summary>
        /// <remarks>
        ///     The model is built so the two are impossible to confuse: face 1 is made of vertices 3,
        ///     4 and 5, so a picker reporting face indices where vertex indices belong produces
        ///     numbers that cannot be right.
        /// </remarks>
        [Fact]
        public void Pick_ReportsTheFaceAndItsThreeVertices()
        {
            var mesh = new PickMesh(new[] { TwoSeparateTriangles() });

            Assert.True(mesh.TryPick(RayAt(0.2f, 0.2f), out FaceHit first));
            Assert.True(first.Found);
            Assert.Equal(0, first.FaceIndex);
            Assert.Equal(0, first.VertexA);
            Assert.Equal(1, first.VertexB);
            Assert.Equal(2, first.VertexC);

            Assert.True(mesh.TryPick(RayAt(5.2f, 0.2f), out FaceHit second));
            Assert.Equal(1, second.FaceIndex);
            Assert.Equal(3, second.VertexA);
            Assert.Equal(4, second.VertexB);
            Assert.Equal(5, second.VertexC);
        }

        /// <summary>A ray through empty space hits nothing and says so.</summary>
        [Fact]
        public void Pick_MissesEmptySpace()
        {
            var mesh = new PickMesh(new[] { TwoSeparateTriangles() });

            Assert.False(mesh.TryPick(RayAt(50f, 50f), out FaceHit hit));
            Assert.False(hit.Found);
        }

        /// <summary>
        ///     Two faces on the same ray resolve to the nearer one.
        /// </summary>
        /// <remarks>
        ///     The reason picking is two-sided and depth-ordered rather than one-sided: the back face
        ///     of a closed model is on the same ray as the front and would otherwise win at random.
        /// </remarks>
        [Fact]
        public void Pick_TakesTheNearestOfTwoFacesOnTheSameRay()
        {
            //Two triangles stacked in depth. Model z becomes world -z, so the larger model z is the
            //further away, and face 0 is the one the ray reaches first.
            ModelDefinition model = Triangles(
                vertices: new[]
                {
                    (0, 0, 0), (128, 0, 0), (0, 128, 0),
                    (0, 0, 512), (128, 0, 512), (0, 128, 512)
                },
                faces: new[] { (0, 1, 2), (3, 4, 5) });

            var mesh = new PickMesh(new[] { model });

            Assert.True(mesh.TryPick(RayAt(0.2f, 0.2f), out FaceHit hit));
            Assert.Equal(0, hit.FaceIndex);
        }

        /// <summary>
        ///     Faces the client never draws are out of the pick set by default, and counted.
        /// </summary>
        /// <remarks>
        ///     Render type 2 gates both of the client's renderers before anything else, and
        ///     <c>ModelRenderer</c> drops those faces too. Picking one would highlight geometry that
        ///     is not on screen, which reads as a broken highlight. The count is exposed so a panel
        ///     can explain why a face in the model's face list is not selectable, and the flag opens
        ///     them up for an attachment that deliberately wants one.
        /// </remarks>
        [Fact]
        public void Pick_ExcludesUndrawnFacesUnlessAsked()
        {
            ModelDefinition model = TwoSeparateTriangles();
            model.FaceRenderType = new sbyte[] { 2, 0 };

            var excluded = new PickMesh(new[] { model });
            Assert.Equal(1, excluded.UndrawnFaceCount);
            Assert.Equal(1, excluded.TriangleCount);
            Assert.False(excluded.TryPick(RayAt(0.2f, 0.2f), out _));

            var included = new PickMesh(new[] { model }, includeUndrawnFaces: true);
            Assert.Equal(1, included.UndrawnFaceCount);
            Assert.Equal(2, included.TriangleCount);
            Assert.True(included.TryPick(RayAt(0.2f, 0.2f), out FaceHit hit));
            Assert.Equal(0, hit.FaceIndex);
        }

        /// <summary>A face naming a vertex the model does not have is dropped and counted.</summary>
        [Fact]
        public void Pick_DropsAFaceWithAnUnreachableVertex()
        {
            ModelDefinition model = Triangles(
                vertices: new[] { (0, 0, 0), (128, 0, 0), (0, 128, 0) },
                faces: new[] { (0, 1, 2), (0, 1, 99) });

            var mesh = new PickMesh(new[] { model });

            Assert.Equal(1, mesh.MalformedFaceCount);
            Assert.Equal(1, mesh.TriangleCount);
        }

        /// <summary>
        ///     A pose moves the pick target, and dropping it puts the target back.
        /// </summary>
        /// <remarks>
        ///     Without this the cursor picks the rest mesh while the screen shows the posed one - a
        ///     picker that is wrong by an amount that grows with the animation, which is worse than
        ///     one that is wrong all the time because it looks correct while the model is still.
        /// </remarks>
        [Fact]
        public void Pose_MovesWhatThePickerSees()
        {
            ModelDefinition model = TwoSeparateTriangles();
            var mesh = new PickMesh(new[] { model });
            PosedMesh pose = new SkinnedModel(model).CreatePose();

            //Slide the whole first triangle four world units along x, onto where nothing was.
            pose.Reset();
            for (int v = 0; v < 3; v++)
                pose.VertexX[v] += 512;
            pose.Finish();

            Assert.True(mesh.TryPick(RayAt(0.2f, 0.2f), out _));

            mesh.ApplyPose(new List<PosedMesh> { pose });
            Assert.True(mesh.IsPosed);
            Assert.False(mesh.TryPick(RayAt(0.2f, 0.2f), out _));
            Assert.True(mesh.TryPick(RayAt(4.2f, 0.2f), out FaceHit moved));
            Assert.Equal(0, moved.FaceIndex);

            mesh.ResetPose();
            Assert.False(mesh.IsPosed);
            Assert.True(mesh.TryPick(RayAt(0.2f, 0.2f), out _));
        }

        /// <summary>Several models keep their own face and vertex numbering.</summary>
        [Fact]
        public void Pick_ReportsWhichModelOfTheSetItHit()
        {
            ModelDefinition first = TwoSeparateTriangles();
            ModelDefinition second = Triangles(
                vertices: new[] { (1280, 0, 0), (1408, 0, 0), (1280, 128, 0) },
                faces: new[] { (0, 1, 2) });

            var mesh = new PickMesh(new[] { first, second });

            Assert.Equal(2, mesh.ModelCount);
            Assert.True(mesh.TryPick(RayAt(10.2f, 0.2f), out FaceHit hit));
            Assert.Equal(1, hit.ModelIndex);
            Assert.Equal(0, hit.FaceIndex);
        }

        /// <summary>
        ///     The overlay labels a face by its face index and its corners by their vertex indices.
        /// </summary>
        /// <remarks>
        ///     The one test that would fail if the two spaces were crossed. Face 1 is made of
        ///     vertices 3, 4 and 5, so "face 1" appearing on a corner or "v1" appearing in the middle
        ///     would both be caught.
        /// </remarks>
        [Fact]
        public void Labels_NameTheFaceInTheMiddleAndTheVerticesAtTheCorners()
        {
            var mesh = new PickMesh(new[] { TwoSeparateTriangles() });
            Assert.True(mesh.TryPick(RayAt(5.2f, 0.2f), out FaceHit hit));

            IReadOnlyList<IndexLabel> labels =
                FaceLabelLayout.Build(mesh, hit, Matrix4x4.Identity, 800, 600);

            Assert.Equal(4, labels.Count);
            Assert.Equal(IndexLabelKind.Face, labels[0].Kind);
            Assert.Equal(1, labels[0].Value);
            Assert.Equal("face 1", labels[0].Text);

            Assert.Equal(new[] { 3, 4, 5 }, new[] { labels[1].Value, labels[2].Value, labels[3].Value });
            Assert.Equal(new[] { "v3", "v4", "v5" }, new[] { labels[1].Text, labels[2].Text, labels[3].Text });

            for (int i = 1; i < labels.Count; i++)
                Assert.Equal(IndexLabelKind.Vertex, labels[i].Kind);
        }

        /// <summary>A corner label is pushed away from the face centre by a fixed number of pixels.</summary>
        /// <remarks>
        ///     So three labels on a face that projects to a handful of pixels are still three
        ///     readable labels rather than one pile.
        /// </remarks>
        [Fact]
        public void Labels_NudgeEachCornerAwayFromTheCentre()
        {
            var mesh = new PickMesh(new[] { TwoSeparateTriangles() });
            Assert.True(mesh.TryPick(RayAt(0.2f, 0.2f), out FaceHit hit));

            IReadOnlyList<IndexLabel> labels =
                FaceLabelLayout.Build(mesh, hit, Matrix4x4.Identity, 800, 600);

            Vector2 centre = labels[0].Pixel;
            Assert.True(mesh.TryFaceCorners(hit.ModelIndex, hit.FaceIndex, out Vector3 a, out Vector3 b,
                out Vector3 c));

            Vector3[] corners = { a, b, c };
            for (int i = 0; i < 3; i++)
            {
                Assert.True(ViewportMath.TryProject(Matrix4x4.Identity, corners[i], 800, 600,
                    out Vector2 unnudged));

                float moved = (labels[i + 1].Pixel - unnudged).Length();
                Assert.Equal(FaceLabelLayout.CornerNudgePixels, moved, 2);

                //And it moved outwards, not inwards.
                Assert.True((labels[i + 1].Pixel - centre).Length() > (unnudged - centre).Length());
            }
        }

        /// <summary>
        ///     An existing attachment is named on the label of the thing it is actually attached to.
        /// </summary>
        /// <remarks>
        ///     The emitter rides face 1 and the effector rides vertex 4, which is a corner of face 1,
        ///     so both notes are on screen at once and each has to land on the right label. Crossing
        ///     the two would put "emitter" on a vertex, and that is the mistake the whole overlay
        ///     exists to make impossible.
        /// </remarks>
        [Fact]
        public void Labels_PutTheEmitterOnTheFaceAndTheEffectorOnTheVertex()
        {
            ModelDefinition model = TwoSeparateTriangles();
            model.Emitters = new[] { new ModelParticleEmitter(37, 1) };
            model.Effectors = new[] { new ModelParticleEffector(9, 4) };

            var mesh = new PickMesh(new[] { model });
            Assert.True(mesh.TryPick(RayAt(5.2f, 0.2f), out FaceHit hit));

            var attachments = new ModelAttachments(model);
            Assert.Equal(1, attachments.FacesWithEmitters);
            Assert.Equal(1, attachments.VerticesWithEffectors);
            Assert.Equal(new[] { 37 }, attachments.EmittersOnFace(1));
            Assert.Empty(attachments.EmittersOnFace(4));
            Assert.Equal(new[] { 9 }, attachments.EffectorsOnVertex(4));
            Assert.Empty(attachments.EffectorsOnVertex(1));

            IReadOnlyList<IndexLabel> labels =
                FaceLabelLayout.Build(mesh, hit, Matrix4x4.Identity, 800, 600, attachments);

            Assert.Equal("face 1 [emitter 37]", labels[0].Text);
            Assert.Equal("v3", labels[1].Text);
            Assert.Equal("v4 [effector 9]", labels[2].Text);
            Assert.Equal("v5", labels[3].Text);
        }

        /// <summary>
        ///     The wireframe outlines every pickable triangle, with three edges each.
        /// </summary>
        /// <remarks>
        ///     Edges are not shared between neighbouring faces, so the count is exactly three per
        ///     triangle - which is what keeps every face's own outline complete.
        /// </remarks>
        [Fact]
        public void Wireframe_EmitsThreeEdgesPerPickableTriangle()
        {
            var mesh = new PickMesh(new[] { TwoSeparateTriangles() });

            float[] vertices = OverlayGeometry.BuildWireframe(mesh, new Vector3(0f, 0f, 1f),
                out uint[] indices);

            Assert.Equal(mesh.TriangleCount * 3 * OverlayGeometry.FloatsPerVertex, vertices.Length);
            Assert.Equal(mesh.TriangleCount * 6, indices.Length);
            Assert.Equal(new uint[] { 0, 1, 1, 2, 2, 0, 3, 4, 4, 5, 5, 3 }, indices);
        }

        /// <summary>
        ///     An overlay colour is pre-divided by the lighting the shader will apply to it.
        /// </summary>
        /// <remarks>
        ///     The overlay shares the model shader, which lights what it draws. The way to a flat
        ///     colour is arithmetic: a vertex whose normal is the light direction is lit by exactly
        ///     <see cref="OverlayGeometry.FullIncidenceLighting"/>, so dividing by it first lands the
        ///     wanted colour. These constants mirror <c>texture.vert</c> and move with it.
        /// </remarks>
        [Fact]
        public void Overlay_PreDividesItsColourByTheShadersLighting()
        {
            Assert.Equal(1.2, OverlayGeometry.FullIncidenceLighting, 5);

            Vector3 wanted = new Vector3(0.6f, 0.6f, 0.6f);
            Vector3 stored = OverlayGeometry.Unlit(wanted);

            Assert.Equal(wanted.X, stored.X * OverlayGeometry.FullIncidenceLighting, 5);
        }

        /// <summary>The highlight is one triangle at the hovered face's corners, short of opaque.</summary>
        [Fact]
        public void Highlight_IsTheHoveredTriangleAtLessThanFullOpacity()
        {
            var a = new Vector3(1f, 2f, 3f);
            var b = new Vector3(4f, 5f, 6f);
            var c = new Vector3(7f, 8f, 9f);

            float[] vertices = OverlayGeometry.BuildHighlight(a, b, c, new Vector3(0f, 0f, 1f));

            Assert.Equal(3 * OverlayGeometry.FloatsPerVertex, vertices.Length);
            Assert.Equal(1f, vertices[0]);
            Assert.Equal(2f, vertices[1]);
            Assert.Equal(3f, vertices[2]);
            Assert.Equal(OverlayGeometry.HighlightOpacity, vertices[8]);
            Assert.True(OverlayGeometry.HighlightOpacity < 1f,
                "An opaque highlight would erase the geometry it is meant to point at.");
        }

        /// <summary>
        ///     Two triangles four world units apart, whose vertex numbering differs from its faces.
        /// </summary>
        /// <returns>The model.</returns>
        private static ModelDefinition TwoSeparateTriangles() => Triangles(
            vertices: new[]
            {
                (0, 0, 0), (128, 0, 0), (0, 128, 0),
                (640, 0, 0), (768, 0, 0), (640, 128, 0)
            },
            faces: new[] { (0, 1, 2), (3, 4, 5) });

        /// <summary>Builds a model from model-space vertices and face index triples.</summary>
        /// <param name="vertices">Model-space coordinates.</param>
        /// <param name="faces">Vertex index triples.</param>
        /// <returns>The model.</returns>
        private static ModelDefinition Triangles((int x, int y, int z)[] vertices,
            (int a, int b, int c)[] faces)
        {
            var model = new ModelDefinition
            {
                VertX = new int[vertices.Length],
                VertY = new int[vertices.Length],
                VertZ = new int[vertices.Length],
                faceIndices1 = new int[faces.Length],
                faceIndices2 = new int[faces.Length],
                faceIndices3 = new int[faces.Length]
            };

            for (int v = 0; v < vertices.Length; v++)
            {
                model.VertX[v] = vertices[v].x;
                model.VertY[v] = vertices[v].y;
                model.VertZ[v] = vertices[v].z;
            }

            for (int f = 0; f < faces.Length; f++)
            {
                model.faceIndices1[f] = faces[f].a;
                model.faceIndices2[f] = faces[f].b;
                model.faceIndices3[f] = faces[f].c;
            }

            return model;
        }

        /// <summary>
        ///     A ray straight down the world -z axis through a world x and y.
        /// </summary>
        /// <remarks>
        ///     Model y becomes world -y, so a triangle spanning model y 0 to 128 spans world y 0 to
        ///     -1. The callers pass a positive y and this negates it, which keeps the test bodies
        ///     reading in model terms.
        /// </remarks>
        /// <param name="worldX">Where to aim, in world units.</param>
        /// <param name="modelY">Where to aim, in world units before the flip.</param>
        /// <returns>The ray.</returns>
        private static PickRay RayAt(float worldX, float modelY) =>
            new PickRay(new Vector3(worldX, -modelY, 10f), new Vector3(0f, 0f, -20f));
    }
}
