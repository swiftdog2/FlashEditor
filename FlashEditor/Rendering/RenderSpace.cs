using System.Numerics;
using FlashEditor.Definitions;

namespace FlashEditor.Rendering
{
    public static class RenderSpace
    {
        public const float ModelUnitsPerWorldUnit = 128f;

        public static Vector3 ToWorld(int x, int y, int z)
        {
            return new Vector3((float)x / 128f, (float)(-y) / 128f, (float)(-z) / 128f);
        }

        public static Vector3 ToWorld(float x, float y, float z)
        {
            return new Vector3(x / 128f, (0f - y) / 128f, (0f - z) / 128f);
        }

        public static Vector3 ToWorld(ModelDefinition model, int vertex)
        {
            return ToWorld(model.VertX[vertex], model.VertY[vertex], model.VertZ[vertex]);
        }

        public static Vector3 ToWorld(PosedMesh mesh, int vertex)
        {
            return ToWorld(mesh.VertexX[vertex], mesh.VertexY[vertex], mesh.VertexZ[vertex]);
        }
    }
}
