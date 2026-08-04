using System.Numerics;
using System;

namespace FlashEditor.Rendering
{
    public readonly record struct PickRay(Vector3 Origin, Vector3 Direction)
    {
        public Vector3 At(float distance)
        {
            return Origin + Direction * distance;
        }
    }

    public static class ViewportMath
    {
        public static Matrix4x4 FromRowMajor(ReadOnlySpan<float> values)
        {
            if (values.Length != 16)
            {
                throw new ArgumentException("A 4x4 matrix needs exactly 16 values, not " + values.Length + ".", "values");
            }
            return new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
        }

        public static bool TryBuildRay(Matrix4x4 modelViewProjection, float pixelX, float pixelY, int width, int height, out PickRay ray)
        {
            ray = default(PickRay);
            if (width <= 0 || height <= 0)
            {
                return false;
            }
            if (!Matrix4x4.Invert(modelViewProjection, out var result))
            {
                return false;
            }
            float ndcX = 2f * pixelX / (float)width - 1f;
            float ndcY = 1f - 2f * pixelY / (float)height;
            if (!TryUnproject(result, ndcX, ndcY, -1f, out var world))
            {
                return false;
            }
            if (!TryUnproject(result, ndcX, ndcY, 1f, out var world2))
            {
                return false;
            }
            ray = new PickRay(world, world2 - world);
            return ray.Direction.LengthSquared() > 0f;
        }

        public static bool TryProject(Matrix4x4 modelViewProjection, Vector3 world, int width, int height, out Vector2 pixel)
        {
            pixel = default(Vector2);
            if (width <= 0 || height <= 0)
            {
                return false;
            }
            Vector4 vector = Vector4.Transform(new Vector4(world, 1f), modelViewProjection);
            if (vector.W <= 0f)
            {
                return false;
            }
            float num = vector.X / vector.W;
            float num2 = vector.Y / vector.W;
            //Clip y runs up and screen y runs down, so the vertical axis is flipped here and
            //nowhere else. Losing this puts every label and every pick on the mirrored row.
            pixel = new Vector2((num * 0.5f + 0.5f) * (float)width, (1f - (num2 * 0.5f + 0.5f)) * (float)height);
            return true;
        }

        private static bool TryUnproject(Matrix4x4 inverse, float ndcX, float ndcY, float ndcZ, out Vector3 world)
        {
            Vector4 vector = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), inverse);
            if (vector.W == 0f)
            {
                world = default(Vector3);
                return false;
            }
            world = new Vector3(vector.X, vector.Y, vector.Z) / vector.W;
            return true;
        }
    }

    public static class RayTriangle
    {
        public const float ParallelEpsilon = 1E-09f;

        public static bool Intersect(in PickRay ray, in Vector3 a, in Vector3 b, in Vector3 c, out float distance, out float u, out float v)
        {
            distance = 0f;
            u = 0f;
            v = 0f;
            Vector3 vector = b - a;
            Vector3 vector2 = c - a;
            Vector3 vector3 = Vector3.Cross(ray.Direction, vector2);
            float num = Vector3.Dot(vector, vector3);
            if (num > -1E-09f && num < 1E-09f)
            {
                return false;
            }
            float num2 = 1f / num;
            Vector3 vector4 = ray.Origin - a;
            u = Vector3.Dot(vector4, vector3) * num2;
            if (u < 0f || u > 1f)
            {
                return false;
            }
            Vector3 vector5 = Vector3.Cross(vector4, vector);
            v = Vector3.Dot(ray.Direction, vector5) * num2;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }
            distance = Vector3.Dot(vector2, vector5) * num2;

            //A negative distance is a triangle the ray has already passed. Returning it as a hit
            //lets the cursor pick geometry behind the eye.
            return distance >= 0f;
        }
    }
}
