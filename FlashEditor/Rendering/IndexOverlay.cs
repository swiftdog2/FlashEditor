using System.Collections.Generic;
using System.Numerics;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public enum IndexLabelKind
    {
        Face,
        Vertex
    }

    public readonly record struct IndexLabel(IndexLabelKind Kind, int Value, string Text, Vector2 Pixel);

    public sealed class ModelAttachments
    {
        private readonly Dictionary<int, List<int>> emittersByFace = new Dictionary<int, List<int>>();

        private readonly Dictionary<int, List<int>> effectorsByVertex = new Dictionary<int, List<int>>();

        public int FacesWithEmitters => emittersByFace.Count;

        public int VerticesWithEffectors => effectorsByVertex.Count;

        public ModelAttachments(ModelDefinition? model)
        {
            if (model?.Emitters != null)
            {
                ModelParticleEmitter[] emitters = model.Emitters;
                for (int i = 0; i < emitters.Length; i++)
                {
                    ModelParticleEmitter modelParticleEmitter = emitters[i];
                    Add(emittersByFace, modelParticleEmitter.FaceIndex, modelParticleEmitter.EmitterId);
                }
            }
            if (model?.Effectors != null)
            {
                ModelParticleEffector[] effectors = model.Effectors;
                for (int j = 0; j < effectors.Length; j++)
                {
                    ModelParticleEffector modelParticleEffector = effectors[j];
                    Add(effectorsByVertex, modelParticleEffector.VertexIndex, modelParticleEffector.EffectorId);
                }
            }
        }

        public IReadOnlyList<int> EmittersOnFace(int faceIndex)
        {
            IReadOnlyList<int> result;
            if (!emittersByFace.TryGetValue(faceIndex, out List<int>? value))
            {
                IReadOnlyList<int> readOnlyList = Array.Empty<int>();
                result = readOnlyList;
            }
            else
            {
                IReadOnlyList<int> readOnlyList = value;
                result = readOnlyList;
            }
            return result;
        }

        public IReadOnlyList<int> EffectorsOnVertex(int vertexIndex)
        {
            IReadOnlyList<int> result;
            if (!effectorsByVertex.TryGetValue(vertexIndex, out List<int>? value))
            {
                IReadOnlyList<int> readOnlyList = Array.Empty<int>();
                result = readOnlyList;
            }
            else
            {
                IReadOnlyList<int> readOnlyList = value;
                result = readOnlyList;
            }
            return result;
        }

        private static void Add(Dictionary<int, List<int>> map, int key, int id)
        {
            if (!map.TryGetValue(key, out List<int>? value))
            {
                value = (map[key] = new List<int>());
            }
            value.Add(id);
        }
    }

    public static class FaceLabelLayout
    {
        public const float CornerNudgePixels = 14f;

        public static IReadOnlyList<IndexLabel> Build(PickMesh mesh, in FaceHit hit, Matrix4x4 modelViewProjection, int width, int height, ModelAttachments? attachments = null)
        {
            List<IndexLabel> list = new List<IndexLabel>(4);
            if (mesh == null || !hit.Found)
            {
                return list;
            }
            if (!mesh.TryFaceCorners(hit.ModelIndex, hit.FaceIndex, out var a, out var b, out var c))
            {
                return list;
            }
            Vector3 vector = (a + b + c) / 3f;
            if (ViewportMath.TryProject(modelViewProjection, vector, width, height, out var pixel))
            {
                list.Add(new IndexLabel(IndexLabelKind.Face, hit.FaceIndex, "face " + hit.FaceIndex + EmitterNote(attachments, hit.FaceIndex), pixel));
            }
            AddCorner(list, modelViewProjection, width, height, a, hit.VertexA, vector, attachments);
            AddCorner(list, modelViewProjection, width, height, b, hit.VertexB, vector, attachments);
            AddCorner(list, modelViewProjection, width, height, c, hit.VertexC, vector, attachments);
            return list;
        }

        private static void AddCorner(List<IndexLabel> labels, Matrix4x4 modelViewProjection, int width, int height, Vector3 corner, int vertexIndex, Vector3 centre, ModelAttachments? attachments)
        {
            if (!ViewportMath.TryProject(modelViewProjection, corner, width, height, out var pixel))
            {
                return;
            }
            if (ViewportMath.TryProject(modelViewProjection, centre, width, height, out var pixel2))
            {
                Vector2 vector = pixel - pixel2;
                float num = vector.Length();
                if (num > 0.001f)
                {
                    pixel += vector / num * 14f;
                }
            }
            labels.Add(new IndexLabel(IndexLabelKind.Vertex, vertexIndex, "v" + vertexIndex + EffectorNote(attachments, vertexIndex), pixel));
        }

        private static string EmitterNote(ModelAttachments? attachments, int faceIndex)
        {
            IReadOnlyList<int> readOnlyList = attachments?.EmittersOnFace(faceIndex) ?? Array.Empty<int>();
            return (readOnlyList.Count == 0) ? string.Empty : (" [emitter " + string.Join(",", readOnlyList) + "]");
        }

        private static string EffectorNote(ModelAttachments? attachments, int vertexIndex)
        {
            IReadOnlyList<int> readOnlyList = attachments?.EffectorsOnVertex(vertexIndex) ?? Array.Empty<int>();
            return (readOnlyList.Count == 0) ? string.Empty : (" [effector " + string.Join(",", readOnlyList) + "]");
        }
    }
}
