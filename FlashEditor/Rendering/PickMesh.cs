using System.Collections.Generic;
using System.Numerics;
using System;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public readonly struct FaceHit
    {
        public bool Found { get; init; }

        public int ModelIndex { get; init; }

        public int FaceIndex { get; init; }

        public int VertexA { get; init; }

        public int VertexB { get; init; }

        public int VertexC { get; init; }

        public float Distance { get; init; }

        public Vector3 Position { get; init; }

        public static FaceHit None => default(FaceHit);
    }

    public sealed class PickMesh
    {
        public const int UndrawnRenderType = 2;

        private readonly ModelDefinition[] models;

        private readonly int[] triangleModel;

        private readonly int[] triangleFace;

        private readonly Vector3[] cornerA;

        private readonly Vector3[] cornerB;

        private readonly Vector3[] cornerC;

        private readonly Vector3[][] restVertices;

        private readonly Vector3[][] vertices;

        public int ModelCount => models.Length;

        public int TriangleCount => triangleModel.Length;

        public bool IncludesUndrawnFaces { get; }

        public int UndrawnFaceCount { get; }

        public int MalformedFaceCount { get; }

        public bool IsPosed { get; private set; }

        public PickMesh(IReadOnlyList<ModelDefinition> models, bool includeUndrawnFaces = false)
        {
            if (models == null)
            {
                throw new ArgumentNullException("models");
            }
            this.models = new ModelDefinition[models.Count];
            restVertices = new Vector3[models.Count][];
            vertices = new Vector3[models.Count][];
            IncludesUndrawnFaces = includeUndrawnFaces;
            List<int> list = new List<int>();
            List<int> list2 = new List<int>();
            for (int i = 0; i < models.Count; i++)
            {
                ModelDefinition modelDefinition = models[i];
                this.models[i] = modelDefinition;
                Vector3[] array = new Vector3[modelDefinition.VertX.Length];
                for (int j = 0; j < array.Length; j++)
                {
                    array[j] = RenderSpace.ToWorld(modelDefinition, j);
                }
                restVertices[i] = array;
                vertices[i] = (Vector3[])array.Clone();
                for (int k = 0; k < modelDefinition.faceIndices1.Length; k++)
                {
                    if (modelDefinition.FaceRenderType != null && k < modelDefinition.FaceRenderType.Length && modelDefinition.FaceRenderType[k] == 2)
                    {
                        int undrawnFaceCount = UndrawnFaceCount;
                        UndrawnFaceCount = undrawnFaceCount + 1;
                        if (!includeUndrawnFaces)
                        {
                            continue;
                        }
                    }
                    if (!IndicesInRange(modelDefinition, k))
                    {
                        int undrawnFaceCount = MalformedFaceCount;
                        MalformedFaceCount = undrawnFaceCount + 1;
                    }
                    else
                    {
                        list.Add(i);
                        list2.Add(k);
                    }
                }
            }
            triangleModel = list.ToArray();
            triangleFace = list2.ToArray();
            cornerA = new Vector3[triangleModel.Length];
            cornerB = new Vector3[triangleModel.Length];
            cornerC = new Vector3[triangleModel.Length];
            RefreshCorners();
        }

        public void ApplyPose(IReadOnlyList<PosedMesh>? poses)
        {
            if (poses == null || poses.Count == 0)
            {
                ResetPose();
                return;
            }
            for (int i = 0; i < models.Length; i++)
            {
                Vector3[] array = vertices[i];
                if (i >= poses.Count)
                {
                    Array.Copy(restVertices[i], array, array.Length);
                    continue;
                }
                PosedMesh posedMesh = poses[i];
                int num = Math.Min(array.Length, posedMesh.VertexX.Length);
                for (int j = 0; j < num; j++)
                {
                    array[j] = RenderSpace.ToWorld(posedMesh, j);
                }
                for (int k = num; k < array.Length; k++)
                {
                    array[k] = restVertices[i][k];
                }
            }
            IsPosed = true;
            RefreshCorners();
        }

        public void ResetPose()
        {
            for (int i = 0; i < models.Length; i++)
            {
                Array.Copy(restVertices[i], vertices[i], vertices[i].Length);
            }
            IsPosed = false;
            RefreshCorners();
        }

        public bool TryPick(in PickRay ray, out FaceHit hit)
        {
            hit = FaceHit.None;
            float num = float.MaxValue;
            int num2 = -1;
            for (int i = 0; i < triangleModel.Length; i++)
            {
                if (RayTriangle.Intersect(in ray, in cornerA[i], in cornerB[i], in cornerC[i], out var distance, out var _, out var _) && !(distance >= num))
                {
                    num = distance;
                    num2 = i;
                }
            }
            if (num2 < 0)
            {
                return false;
            }
            int num3 = triangleModel[num2];
            int num4 = triangleFace[num2];
            ModelDefinition modelDefinition = models[num3];
            hit = new FaceHit
            {
                Found = true,
                ModelIndex = num3,
                FaceIndex = num4,
                VertexA = modelDefinition.faceIndices1[num4],
                VertexB = modelDefinition.faceIndices2[num4],
                VertexC = modelDefinition.faceIndices3[num4],
                Distance = num,
                Position = ray.At(num)
            };
            return true;
        }

        public bool TryFaceCorners(int modelIndex, int faceIndex, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = (b = (c = default(Vector3)));
            if ((uint)modelIndex >= (uint)models.Length)
            {
                return false;
            }
            ModelDefinition modelDefinition = models[modelIndex];
            if ((uint)faceIndex >= (uint)modelDefinition.faceIndices1.Length || !IndicesInRange(modelDefinition, faceIndex))
            {
                return false;
            }
            Vector3[] array = vertices[modelIndex];
            a = array[modelDefinition.faceIndices1[faceIndex]];
            b = array[modelDefinition.faceIndices2[faceIndex]];
            c = array[modelDefinition.faceIndices3[faceIndex]];
            return true;
        }

        public bool TryVertex(int modelIndex, int vertexIndex, out Vector3 position)
        {
            position = default(Vector3);
            if ((uint)modelIndex >= (uint)models.Length)
            {
                return false;
            }
            Vector3[] array = vertices[modelIndex];
            if ((uint)vertexIndex >= (uint)array.Length)
            {
                return false;
            }
            position = array[vertexIndex];
            return true;
        }

        public void TriangleCorners(int triangle, out Vector3 a, out Vector3 b, out Vector3 c)
        {
            a = cornerA[triangle];
            b = cornerB[triangle];
            c = cornerC[triangle];
        }

        public void TriangleSource(int triangle, out int modelIndex, out int faceIndex)
        {
            modelIndex = triangleModel[triangle];
            faceIndex = triangleFace[triangle];
        }

        private void RefreshCorners()
        {
            for (int i = 0; i < triangleModel.Length; i++)
            {
                ModelDefinition modelDefinition = models[triangleModel[i]];
                Vector3[] array = vertices[triangleModel[i]];
                int num = triangleFace[i];
                cornerA[i] = array[modelDefinition.faceIndices1[num]];
                cornerB[i] = array[modelDefinition.faceIndices2[num]];
                cornerC[i] = array[modelDefinition.faceIndices3[num]];
            }
        }

        private static bool IndicesInRange(ModelDefinition model, int face)
        {
            return (uint)model.faceIndices1[face] < (uint)model.VertX.Length && (uint)model.faceIndices2[face] < (uint)model.VertX.Length && (uint)model.faceIndices3[face] < (uint)model.VertX.Length;
        }
    }
}
