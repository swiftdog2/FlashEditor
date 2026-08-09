using System.Collections.Generic;
using System.Numerics;
using System;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Rendering
{
    /// <summary>What the cursor landed on.</summary>
    /// <remarks>
    ///     Carries the face index <b>and</b> the three vertex indices, because the two are what the
    ///     caller has to choose between: a particle emitter attaches to a face and an effector attaches
    ///     to a vertex. They are numbered independently and overlap in range, so returning only one
    ///     would leave the caller to guess.
    /// </remarks>
    public readonly struct FaceHit
    {
        /// <summary>Whether anything was hit. Every other member is meaningless when this is false.</summary>
        public bool Found { get; init; }

        /// <summary>Which model of the set, indexed as they were handed to the constructor.</summary>
        public int ModelIndex { get; init; }

        /// <summary>The face index within that model, as a particle emitter attachment names it.</summary>
        public int FaceIndex { get; init; }

        /// <summary>First corner's vertex index, as a particle effector attachment names it.</summary>
        public int VertexA { get; init; }

        /// <summary>Second corner's vertex index.</summary>
        public int VertexB { get; init; }

        /// <summary>Third corner's vertex index.</summary>
        public int VertexC { get; init; }

        /// <summary>How far along the ray the hit was, in the ray's own units.</summary>
        public float Distance { get; init; }

        /// <summary>Where the hit was, in world space.</summary>
        public Vector3 Position { get; init; }

        /// <summary>A miss.</summary>
        public static FaceHit None => default;
    }

    /// <summary>
    ///     A flattened, world-space triangle list of one or more models, for ray picking.
    /// </summary>
    /// <remarks>
    ///     Separate from the render mesh on purpose. The picker needs a triangle soup with a way back
    ///     to the model and face each triangle came from; the renderer needs interleaved vertex
    ///     attributes in GPU buffers. Deriving one from the other would couple the cursor to the
    ///     upload format, and the upload format is the one thing in this project no test can see.
    ///     <para>
    ///     Corners are cached in three parallel arrays rather than fetched per pick. A pick runs on
    ///     every mouse move over the viewport and walks every triangle; re-resolving each face's three
    ///     indices through the model each time doubles the work for no gain, since the corners only
    ///     change when the pose does.
    ///     </para>
    /// </remarks>
    public sealed class PickMesh
    {
        /// <summary>
        ///     The face render type the client refuses to draw.
        /// </summary>
        /// <remarks>
        ///     Both of the client's renderers gate their draw list on it before anything else touches
        ///     the face - <c>Renderable_Sub2.java:397</c> and <c>Renderable_Sub3.java:172</c>, the
        ///     latter spelling <c>!= 2</c> as <c>(x ^ 0xffffffff) != -3</c>. It is a visibility flag,
        ///     not a shading mode.
        /// </remarks>
        public const int UndrawnRenderType = 2;

        /// <summary>The models, in the order they were given.</summary>
        private readonly ModelDefinition[] models;

        /// <summary>Which model each pickable triangle came from.</summary>
        private readonly int[] triangleModel;

        /// <summary>Which face of that model each pickable triangle is.</summary>
        private readonly int[] triangleFace;

        /// <summary>Cached first corner per pickable triangle, in world space.</summary>
        private readonly Vector3[] cornerA;

        /// <summary>Cached second corner per pickable triangle.</summary>
        private readonly Vector3[] cornerB;

        /// <summary>Cached third corner per pickable triangle.</summary>
        private readonly Vector3[] cornerC;

        /// <summary>Rest-pose world vertices per model, kept so <see cref="ResetPose"/> is a copy.</summary>
        private readonly Vector3[][] restVertices;

        /// <summary>Current world vertices per model, rewritten by <see cref="ApplyPose"/>.</summary>
        private readonly Vector3[][] vertices;

        /// <summary>How many models this mesh spans.</summary>
        public int ModelCount => models.Length;

        /// <summary>How many triangles are actually pickable, after exclusions.</summary>
        /// <remarks>Below the models' total face count whenever anything was excluded.</remarks>
        public int TriangleCount => triangleModel.Length;

        /// <summary>Whether faces of <see cref="UndrawnRenderType"/> were kept.</summary>
        public bool IncludesUndrawnFaces { get; }

        /// <summary>How many faces carry <see cref="UndrawnRenderType"/>, whether kept or not.</summary>
        /// <remarks>
        ///     Exposed so a panel can explain why a face in the model's face list is not selectable.
        ///     They are spread across roughly one model in five, so "that face number does not exist"
        ///     would be a common and misleading answer.
        /// </remarks>
        public int UndrawnFaceCount { get; }

        /// <summary>How many faces name a vertex their model does not have.</summary>
        public int MalformedFaceCount { get; }

        /// <summary>Whether the picker is currently following a pose rather than the rest mesh.</summary>
        public bool IsPosed { get; private set; }

        /// <summary>Flattens a set of models into a pickable triangle list.</summary>
        /// <param name="models">The models. Their order becomes <see cref="FaceHit.ModelIndex"/>.</param>
        /// <param name="includeUndrawnFaces">
        ///     Whether to keep faces the client will not draw. Off by default: picking one would
        ///     highlight geometry that is not on screen, which reads as a broken highlight rather than
        ///     as a selection. On, for an attachment that deliberately wants one - the attachment
        ///     format has no opinion about render type.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="models"/> is null.</exception>
        public PickMesh(IReadOnlyList<ModelDefinition> models, bool includeUndrawnFaces = false)
        {
            if (models == null)
            {
                throw new ArgumentNullException(nameof(models));
            }

            this.models = new ModelDefinition[models.Count];
            restVertices = new Vector3[models.Count][];
            vertices = new Vector3[models.Count][];
            IncludesUndrawnFaces = includeUndrawnFaces;

            List<int> pickableModel = new List<int>();
            List<int> pickableFace = new List<int>();

            for (int modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                ModelDefinition model = models[modelIndex];
                this.models[modelIndex] = model;

                Vector3[] rest = new Vector3[model.VertX.Length];
                for (int vertex = 0; vertex < rest.Length; vertex++)
                {
                    rest[vertex] = RenderSpace.ToWorld(model, vertex);
                }

                restVertices[modelIndex] = rest;

                //Cloned rather than aliased, so ApplyPose can write into one without destroying the
                //other and ResetPose stays a copy rather than a re-derivation.
                vertices[modelIndex] = (Vector3[])rest.Clone();

                for (int face = 0; face < model.faceIndices1.Length; face++)
                {
                    if (model.FaceRenderType != null
                        && face < model.FaceRenderType.Length
                        && model.FaceRenderType[face] == UndrawnRenderType)
                    {
                        //Counted whether or not it is kept, so the count describes the model rather
                        //than this mesh's configuration.
                        UndrawnFaceCount++;

                        if (!includeUndrawnFaces)
                        {
                            continue;
                        }
                    }

                    if (!IndicesInRange(model, face))
                    {
                        MalformedFaceCount++;
                    }
                    else
                    {
                        pickableModel.Add(modelIndex);
                        pickableFace.Add(face);
                    }
                }
            }

            triangleModel = pickableModel.ToArray();
            triangleFace = pickableFace.ToArray();
            cornerA = new Vector3[triangleModel.Length];
            cornerB = new Vector3[triangleModel.Length];
            cornerC = new Vector3[triangleModel.Length];

            RefreshCorners();
        }

        /// <summary>Moves the pick targets onto the current pose.</summary>
        /// <remarks>
        ///     Without this the cursor picks the rest mesh while the screen shows the posed one - a
        ///     picker whose error grows with the animation, which is worse than one that is wrong all
        ///     the time because it looks correct whenever the model is standing still.
        ///     <para>
        ///     A pose shorter than the model, or a set with fewer poses than models, falls back to the
        ///     rest positions for whatever it does not cover rather than refusing the whole call.
        ///     </para>
        /// </remarks>
        /// <param name="poses">One pose per model, in the same order. Null or empty resets.</param>
        public void ApplyPose(IReadOnlyList<PosedMesh>? poses)
        {
            if (poses == null || poses.Count == 0)
            {
                ResetPose();
                return;
            }

            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                Vector3[] current = vertices[modelIndex];

                if (modelIndex >= poses.Count)
                {
                    Array.Copy(restVertices[modelIndex], current, current.Length);
                    continue;
                }

                PosedMesh pose = poses[modelIndex];
                int posed = Math.Min(current.Length, pose.VertexX.Length);

                for (int vertex = 0; vertex < posed; vertex++)
                {
                    current[vertex] = RenderSpace.ToWorld(pose, vertex);
                }

                for (int vertex = posed; vertex < current.Length; vertex++)
                {
                    current[vertex] = restVertices[modelIndex][vertex];
                }
            }

            IsPosed = true;
            RefreshCorners();
        }

        /// <summary>Puts the pick targets back on the rest mesh.</summary>
        public void ResetPose()
        {
            for (int modelIndex = 0; modelIndex < models.Length; modelIndex++)
            {
                Array.Copy(restVertices[modelIndex], vertices[modelIndex], vertices[modelIndex].Length);
            }

            IsPosed = false;
            RefreshCorners();
        }

        /// <summary>Finds the nearest triangle the ray hits.</summary>
        /// <remarks>
        ///     A linear sweep of every triangle, with no spatial index. Deliberate for now: the models
        ///     this viewer opens are a few thousand triangles and the sweep runs once per mouse move,
        ///     while a bounding hierarchy would have to be rebuilt on every frame of an animation
        ///     because <see cref="ApplyPose"/> moves every corner.
        ///     <para>
        ///     Nearest rather than first: picking is two-sided, so the back face of a closed model is
        ///     on the same ray as the front and would otherwise win depending on face order.
        ///     </para>
        /// </remarks>
        /// <param name="ray">The ray, from <see cref="ViewportMath.TryBuildRay"/>.</param>
        /// <param name="hit">What was hit, when this returns <c>true</c>.</param>
        /// <returns><c>true</c> when something was hit.</returns>
        public bool TryPick(in PickRay ray, out FaceHit hit)
        {
            hit = FaceHit.None;

            float nearestDistance = float.MaxValue;
            int nearestTriangle = -1;

            for (int triangle = 0; triangle < triangleModel.Length; triangle++)
            {
                if (RayTriangle.Intersect(in ray, in cornerA[triangle], in cornerB[triangle], in cornerC[triangle],
                        out float distance, out _, out _)
                    && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestTriangle = triangle;
                }
            }

            if (nearestTriangle < 0)
            {
                return false;
            }

            int modelIndex = triangleModel[nearestTriangle];
            int faceIndex = triangleFace[nearestTriangle];
            ModelDefinition model = models[modelIndex];

            hit = new FaceHit
            {
                Found = true,
                ModelIndex = modelIndex,
                FaceIndex = faceIndex,
                VertexA = model.faceIndices1[faceIndex],
                VertexB = model.faceIndices2[faceIndex],
                VertexC = model.faceIndices3[faceIndex],
                Distance = nearestDistance,
                Position = ray.At(nearestDistance)
            };

            return true;
        }

        /// <summary>The current world-space corners of one face.</summary>
        /// <remarks>
        ///     Takes a face index rather than a triangle index, so a caller holding a
        ///     <see cref="FaceHit"/> can ask for the corners again after a pose has moved them - which
        ///     is what the highlight does every frame. Works for a face that was excluded from the
        ///     pick set, since an excluded face still has corners worth drawing.
        /// </remarks>
        /// <param name="modelIndex">Which model.</param>
        /// <param name="faceIndex">Which face of it.</param>
        /// <param name="a">First corner.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        /// <returns><c>false</c> when the face does not exist or names a vertex that does not.</returns>
        public bool TryFaceCorners(int modelIndex, int faceIndex, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = default;
            b = default;
            c = default;

            if ((uint)modelIndex >= (uint)models.Length)
            {
                return false;
            }

            ModelDefinition model = models[modelIndex];

            if ((uint)faceIndex >= (uint)model.faceIndices1.Length || !IndicesInRange(model, faceIndex))
            {
                return false;
            }

            Vector3[] current = vertices[modelIndex];
            a = current[model.faceIndices1[faceIndex]];
            b = current[model.faceIndices2[faceIndex]];
            c = current[model.faceIndices3[faceIndex]];
            return true;
        }

        /// <summary>The current world-space position of one vertex.</summary>
        /// <remarks>
        ///     The effector half of the attachment picture: an effector rides a vertex, so a panel
        ///     showing where one sits asks for it by vertex index rather than by face.
        /// </remarks>
        /// <param name="modelIndex">Which model.</param>
        /// <param name="vertexIndex">Which vertex of it.</param>
        /// <param name="position">The world position.</param>
        /// <returns><c>false</c> when the vertex does not exist.</returns>
        public bool TryVertex(int modelIndex, int vertexIndex, out Vector3 position)
        {
            position = default;

            if ((uint)modelIndex >= (uint)models.Length)
            {
                return false;
            }

            Vector3[] current = vertices[modelIndex];

            if ((uint)vertexIndex >= (uint)current.Length)
            {
                return false;
            }

            position = current[vertexIndex];
            return true;
        }

        /// <summary>The corners of one pickable triangle, by its position in the pick list.</summary>
        /// <remarks>
        ///     Indexed by triangle rather than by face, which is what the wireframe builder wants -
        ///     it draws exactly the triangles that are pickable, so the outline on screen and the set
        ///     the cursor can reach are the same set.
        /// </remarks>
        /// <param name="triangle">Position in the pick list, below <see cref="TriangleCount"/>.</param>
        /// <param name="a">First corner.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        public void TriangleCorners(int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = cornerA[triangle];
            b = cornerB[triangle];
            c = cornerC[triangle];
        }

        /// <summary>Which model and face a pickable triangle came from.</summary>
        /// <param name="triangle">Position in the pick list, below <see cref="TriangleCount"/>.</param>
        /// <param name="modelIndex">Which model.</param>
        /// <param name="faceIndex">Which face of it.</param>
        public void TriangleSource(int triangle, out int modelIndex, out int faceIndex)
        {
            modelIndex = triangleModel[triangle];
            faceIndex = triangleFace[triangle];
        }

        /// <summary>Re-reads every pickable triangle's corners out of the current vertex arrays.</summary>
        /// <remarks>
        ///     Unconditional, because the triangle list was filtered at construction and every entry in
        ///     it is known to be in range. That is why the bounds checks live in the constructor rather
        ///     than here, where they would run on every frame of an animation.
        /// </remarks>
        private void RefreshCorners()
        {
            for (int triangle = 0; triangle < triangleModel.Length; triangle++)
            {
                ModelDefinition model = models[triangleModel[triangle]];
                Vector3[] current = vertices[triangleModel[triangle]];
                int face = triangleFace[triangle];

                cornerA[triangle] = current[model.faceIndices1[face]];
                cornerB[triangle] = current[model.faceIndices2[face]];
                cornerC[triangle] = current[model.faceIndices3[face]];
            }
        }

        /// <summary>Whether a face's three indices all name vertices the model has.</summary>
        /// <param name="model">The model.</param>
        /// <param name="face">The face index.</param>
        /// <returns><c>true</c> when the face can be resolved to three positions.</returns>
        private static bool IndicesInRange(ModelDefinition model, int face)
        {
            return (uint)model.faceIndices1[face] < (uint)model.VertX.Length
                && (uint)model.faceIndices2[face] < (uint)model.VertX.Length
                && (uint)model.faceIndices3[face] < (uint)model.VertX.Length;
        }
    }
}
