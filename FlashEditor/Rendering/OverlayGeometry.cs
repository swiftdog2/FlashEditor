using System.Numerics;
using System;

namespace FlashEditor.Rendering
{
    public static class OverlayGeometry
    {
        public const int FloatsPerVertex = 12;

        public const float ShaderAmbient = 0.3f;

        public const float ShaderDiffuse = 0.7f;

        public const float ShaderGain = 1.2f;

        public const float FullIncidenceLighting = 1.2f;

        public const float WireframeDepthBiasWorldUnits = 0.008f;

        public const float HighlightOpacity = 0.55f;

        public static Vector3 WireframeColour => new Vector3(0.45f, 0.6f, 0.85f);

        public static Vector3 HighlightColour => new Vector3(1f, 0.72f, 0.15f);

        public static Vector3 Unlit(Vector3 colour)
        {
            return colour / 1.2f;
        }

        public static float[] BuildWireframe(PickMesh mesh, Vector3 lightDirection, out uint[] indices)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException("mesh");
            }
            float[] array = new float[mesh.TriangleCount * 3 * 12];
            indices = new uint[mesh.TriangleCount * 6];
            Vector3 vector = SafeNormal(lightDirection);
            Vector3 colour = Unlit(WireframeColour);
            Vector3 vector2 = vector * 0.008f;
            for (int i = 0; i < mesh.TriangleCount; i++)
            {
                mesh.TriangleCorners(i, out var a, out var b, out var c);
                int num = i * 3;
                Write(array, num, a + vector2, vector, colour, 1f);
                Write(array, num + 1, b + vector2, vector, colour, 1f);
                Write(array, num + 2, c + vector2, vector, colour, 1f);
                int num2 = i * 6;
                indices[num2] = (uint)num;
                indices[num2 + 1] = (uint)(num + 1);
                indices[num2 + 2] = (uint)(num + 1);
                indices[num2 + 3] = (uint)(num + 2);
                indices[num2 + 4] = (uint)(num + 2);
                indices[num2 + 5] = (uint)num;
            }
            return array;
        }

        public static float[] BuildHighlight(Vector3 a, Vector3 b, Vector3 c, Vector3 lightDirection)
        {
            float[] array = new float[36];
            Vector3 normal = SafeNormal(lightDirection);
            Vector3 colour = Unlit(HighlightColour);
            Write(array, 0, a, normal, colour, 0.55f);
            Write(array, 1, b, normal, colour, 0.55f);
            Write(array, 2, c, normal, colour, 0.55f);
            return array;
        }

        internal static void Write(float[] buffer, int vertex, Vector3 position, Vector3 normal, Vector3 colour, float opacity)
        {
            int num = vertex * 12;
            buffer[num] = position.X;
            buffer[num + 1] = position.Y;
            buffer[num + 2] = position.Z;
            buffer[num + 3] = normal.X;
            buffer[num + 4] = normal.Y;
            buffer[num + 5] = normal.Z;
            buffer[num + 6] = 0f;
            buffer[num + 7] = 0f;
            buffer[num + 8] = opacity;
            buffer[num + 9] = colour.X;
            buffer[num + 10] = colour.Y;
            buffer[num + 11] = colour.Z;
        }

        private static Vector3 SafeNormal(Vector3 direction)
        {
            return (direction.LengthSquared() > 1E-12f) ? Vector3.Normalize(direction) : Vector3.UnitY;
        }
    }
}
