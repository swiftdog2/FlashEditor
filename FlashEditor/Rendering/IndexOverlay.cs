using System.Collections.Generic;
using System.Numerics;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    /// <summary>Which index space a label is naming.</summary>
    /// <remarks>
    ///     The distinction the whole overlay exists for. Face indices and vertex indices are numbered
    ///     independently and overlap in range, so a label reading "1" is ambiguous without this and a
    ///     reader would have no way to tell a mislabelled overlay from a correct one.
    /// </remarks>
    public enum IndexLabelKind
    {
        /// <summary>A face index, which is what a particle emitter attachment names.</summary>
        Face,

        /// <summary>A vertex index, which is what a particle effector attachment names.</summary>
        Vertex
    }

    /// <summary>One piece of text to draw over the viewport.</summary>
    /// <param name="Kind">Whether the value is a face or a vertex index.</param>
    /// <param name="Value">The index itself, so a caller can act on it without parsing the text.</param>
    /// <param name="Text">What to draw.</param>
    /// <param name="Pixel">Where to centre it, in viewport pixels from the top left.</param>
    public readonly record struct IndexLabel(IndexLabelKind Kind, int Value, string Text, Vector2 Pixel);

    /// <summary>
    ///     A model's existing particle attachments, indexed by what each one rides.
    /// </summary>
    /// <remarks>
    ///     Built so the overlay can annotate a label with what is already attached there, which is how
    ///     someone choosing an attachment point sees that they are about to double up.
    ///     <para>
    ///     The two lists sit next to each other in the same tail block of a model file and store the
    ///     same-looking pair of numbers, and the client indexes them into different arrays:
    ///     <c>Model.java:755-773</c> takes the emitter's second value as a <b>face</b> and expands it
    ///     into that face's three vertices at load, while <c>Renderable_Sub1.java:1461-1472</c> uses
    ///     the effector's to index the <b>vertex</b> coordinate arrays. Crossing the two produces an
    ///     effect coming out of the wrong part of the model, and every layer below accepts it silently.
    ///     </para>
    ///     <para>
    ///     Both are one-to-many. A face may carry several emitters and a vertex several effectors, so
    ///     the values are lists rather than single ids.
    ///     </para>
    /// </remarks>
    public sealed class ModelAttachments
    {
        /// <summary>Emitter ids per face index.</summary>
        private readonly Dictionary<int, List<int>> emittersByFace = new Dictionary<int, List<int>>();

        /// <summary>Effector ids per vertex index.</summary>
        private readonly Dictionary<int, List<int>> effectorsByVertex = new Dictionary<int, List<int>>();

        /// <summary>How many distinct faces carry at least one emitter.</summary>
        public int FacesWithEmitters => emittersByFace.Count;

        /// <summary>How many distinct vertices carry at least one effector.</summary>
        public int VerticesWithEffectors => effectorsByVertex.Count;

        /// <summary>Indexes a model's attachments.</summary>
        /// <remarks>
        ///     Accepts a null model, and a model with neither list, because most models have neither
        ///     and the overlay should not need a special case for the ordinary one.
        /// </remarks>
        /// <param name="model">The model, or null.</param>
        public ModelAttachments(ModelDefinition? model)
        {
            if (model?.Emitters != null)
            {
                foreach (ModelParticleEmitter emitter in model.Emitters)
                {
                    Add(emittersByFace, emitter.FaceIndex, emitter.EmitterId);
                }
            }

            if (model?.Effectors != null)
            {
                foreach (ModelParticleEffector effector in model.Effectors)
                {
                    Add(effectorsByVertex, effector.VertexIndex, effector.EffectorId);
                }
            }
        }

        /// <summary>The emitters riding a face.</summary>
        /// <param name="faceIndex">The face index.</param>
        /// <returns>Their ids, or an empty list.</returns>
        public IReadOnlyList<int> EmittersOnFace(int faceIndex)
        {
            return emittersByFace.TryGetValue(faceIndex, out List<int>? ids) ? ids : Array.Empty<int>();
        }

        /// <summary>The effectors riding a vertex.</summary>
        /// <param name="vertexIndex">The vertex index.</param>
        /// <returns>Their ids, or an empty list.</returns>
        public IReadOnlyList<int> EffectorsOnVertex(int vertexIndex)
        {
            return effectorsByVertex.TryGetValue(vertexIndex, out List<int>? ids) ? ids : Array.Empty<int>();
        }

        /// <summary>Appends an id to the list for a key, creating the list on first use.</summary>
        /// <param name="map">The index being built.</param>
        /// <param name="key">Face or vertex index.</param>
        /// <param name="id">Emitter or effector id.</param>
        private static void Add(Dictionary<int, List<int>> map, int key, int id)
        {
            if (!map.TryGetValue(key, out List<int>? ids))
            {
                ids = new List<int>();
                map[key] = ids;
            }

            ids.Add(id);
        }
    }

    /// <summary>
    ///     Lays out the labels drawn over the face the cursor is on.
    /// </summary>
    /// <remarks>
    ///     Four labels: the face index at the middle of the triangle, and each corner's vertex index
    ///     beside that corner. That arrangement is the point of the overlay - it puts the two index
    ///     spaces on screen at once, in positions that say which is which, so choosing a particle
    ///     attachment point does not come down to remembering which kind of number the dialog wanted.
    ///     <para>
    ///     Layout only. The projection is <see cref="ViewportMath"/>'s and the drawing is
    ///     <see cref="IndexLabelPainter"/>'s, which is what lets the positions be asserted without a
    ///     device context.
    ///     </para>
    /// </remarks>
    public static class FaceLabelLayout
    {
        /// <summary>
        ///     How far a corner label is pushed away from the face centre, in pixels.
        /// </summary>
        /// <remarks>
        ///     In screen pixels rather than world units on purpose: the point is legibility, so the
        ///     separation must not shrink when the model is far away. Without it, a face that projects
        ///     to a handful of pixels puts three corner labels and one face label in one unreadable
        ///     pile - and models in this cache are dense enough that most faces do.
        /// </remarks>
        public const float CornerNudgePixels = 14f;

        /// <summary>Builds the labels for one hit face.</summary>
        /// <remarks>
        ///     Each label is dropped individually when its position does not project - a face
        ///     straddling the near plane has corners on both sides of it - rather than the whole set
        ///     being refused. The face label is always first when it is present, which is what lets a
        ///     caller treat <c>labels[0]</c> as the face.
        /// </remarks>
        /// <param name="mesh">The pick mesh the hit came from.</param>
        /// <param name="hit">The hit.</param>
        /// <param name="modelViewProjection">The composed matrix the viewport drew with.</param>
        /// <param name="width">Viewport width in pixels.</param>
        /// <param name="height">Viewport height in pixels.</param>
        /// <param name="attachments">Existing attachments to annotate with, or null for none.</param>
        /// <returns>The labels, face first.</returns>
        public static IReadOnlyList<IndexLabel> Build(PickMesh mesh, in FaceHit hit,
            Matrix4x4 modelViewProjection, int width, int height, ModelAttachments? attachments = null)
        {
            List<IndexLabel> labels = new List<IndexLabel>(4);

            if (mesh == null || !hit.Found)
            {
                return labels;
            }

            //Asked for again rather than taken off the hit, so the labels follow the pose the mesh is
            //in now. A hit is captured on a mouse move and drawn on every frame after it.
            if (!mesh.TryFaceCorners(hit.ModelIndex, hit.FaceIndex, out Vector3 a, out Vector3 b, out Vector3 c))
            {
                return labels;
            }

            Vector3 centre = (a + b + c) / 3f;

            if (ViewportMath.TryProject(modelViewProjection, centre, width, height, out Vector2 centrePixel))
            {
                labels.Add(new IndexLabel(IndexLabelKind.Face, hit.FaceIndex,
                    "face " + hit.FaceIndex + EmitterNote(attachments, hit.FaceIndex), centrePixel));
            }

            AddCorner(labels, modelViewProjection, width, height, a, hit.VertexA, centre, attachments);
            AddCorner(labels, modelViewProjection, width, height, b, hit.VertexB, centre, attachments);
            AddCorner(labels, modelViewProjection, width, height, c, hit.VertexC, centre, attachments);

            return labels;
        }

        /// <summary>Projects one corner and pushes its label outwards from the face centre.</summary>
        /// <remarks>
        ///     The nudge is computed in <b>pixel</b> space, from the projected centre to the projected
        ///     corner, rather than in world space. Nudging the world position outwards first and then
        ///     projecting would move a label by an amount that depends on how the face is angled to
        ///     the camera, and an edge-on face would get no separation at all - which is precisely the
        ///     case where the three corners project on top of one another.
        ///     <para>
        ///     A corner that projects onto the centre has no outward direction, so it is left where it
        ///     is rather than pushed in an arbitrary one.
        ///     </para>
        /// </remarks>
        /// <param name="labels">The list being built.</param>
        /// <param name="modelViewProjection">The composed matrix.</param>
        /// <param name="width">Viewport width in pixels.</param>
        /// <param name="height">Viewport height in pixels.</param>
        /// <param name="corner">The corner's world position.</param>
        /// <param name="vertexIndex">The corner's vertex index.</param>
        /// <param name="centre">The face centre's world position.</param>
        /// <param name="attachments">Existing attachments, or null.</param>
        private static void AddCorner(List<IndexLabel> labels, Matrix4x4 modelViewProjection, int width, int height,
            Vector3 corner, int vertexIndex, Vector3 centre, ModelAttachments? attachments)
        {
            if (!ViewportMath.TryProject(modelViewProjection, corner, width, height, out Vector2 pixel))
            {
                return;
            }

            if (ViewportMath.TryProject(modelViewProjection, centre, width, height, out Vector2 centrePixel))
            {
                Vector2 outwards = pixel - centrePixel;
                float length = outwards.Length();

                if (length > 0.001f)
                {
                    pixel += outwards / length * CornerNudgePixels;
                }
            }

            labels.Add(new IndexLabel(IndexLabelKind.Vertex, vertexIndex,
                "v" + vertexIndex + EffectorNote(attachments, vertexIndex), pixel));
        }

        /// <summary>The bracketed emitter note appended to a face label, or nothing.</summary>
        /// <param name="attachments">Existing attachments, or null.</param>
        /// <param name="faceIndex">The face index.</param>
        /// <returns>Text to append.</returns>
        private static string EmitterNote(ModelAttachments? attachments, int faceIndex)
        {
            IReadOnlyList<int> ids = attachments?.EmittersOnFace(faceIndex) ?? Array.Empty<int>();
            return ids.Count == 0 ? string.Empty : " [emitter " + string.Join(",", ids) + "]";
        }

        /// <summary>The bracketed effector note appended to a vertex label, or nothing.</summary>
        /// <param name="attachments">Existing attachments, or null.</param>
        /// <param name="vertexIndex">The vertex index.</param>
        /// <returns>Text to append.</returns>
        private static string EffectorNote(ModelAttachments? attachments, int vertexIndex)
        {
            IReadOnlyList<int> ids = attachments?.EffectorsOnVertex(vertexIndex) ?? Array.Empty<int>();
            return ids.Count == 0 ? string.Empty : " [effector " + string.Join(",", ids) + "]";
        }
    }
}
