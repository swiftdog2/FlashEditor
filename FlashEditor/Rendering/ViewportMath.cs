using System.Numerics;
using System;

namespace FlashEditor.Rendering
{
    /// <summary>A world-space ray, as built from a cursor position over the viewport.</summary>
    /// <remarks>
    ///     <see cref="Direction"/> is deliberately not normalised: it is the near-plane point subtracted
    ///     from the far-plane point, so a distance of 0 is the near plane and 1 is the far plane.
    ///     <see cref="PickMesh"/> compares those distances against each other to find the nearest hit,
    ///     and for that only their order matters. Normalising would cost a square root per pick and
    ///     buy nothing.
    /// </remarks>
    /// <param name="Origin">Where the ray enters the frustum, on the near plane.</param>
    /// <param name="Direction">Near plane to far plane, unnormalised.</param>
    public readonly record struct PickRay(Vector3 Origin, Vector3 Direction)
    {
        /// <summary>The point a given fraction of the way from the near plane to the far plane.</summary>
        /// <param name="distance">0 at the near plane, 1 at the far plane.</param>
        /// <returns>The world-space point.</returns>
        public Vector3 At(float distance)
        {
            return Origin + Direction * distance;
        }
    }

    /// <summary>
    ///     Cursor-to-world and world-to-pixel arithmetic for the model viewport.
    /// </summary>
    /// <remarks>
    ///     Everything here takes one composed model-view-projection matrix and makes no assumption
    ///     about who built it or with which library, which is why the tests can supply matrices
    ///     written out by hand from the <c>glFrustum</c> specification. The convention it does commit
    ///     to is OpenGL's: clip space runs -1 to +1 on all three axes, including depth.
    /// </remarks>
    public static class ViewportMath
    {
        /// <summary>Builds a matrix from sixteen floats laid out row by row.</summary>
        /// <remarks>
        ///     The entry point for a matrix read back out of GL, which hands over its own column-major
        ///     layout; the caller decides which reading it has and this one is named so the choice is
        ///     visible at the call site. A wrong count is refused rather than padded, because a
        ///     silently zero-filled row produces a matrix that inverts, projects, and puts everything
        ///     in the wrong place.
        /// </remarks>
        /// <param name="values">Exactly sixteen values, row 0 first.</param>
        /// <returns>The matrix.</returns>
        /// <exception cref="ArgumentException">The span does not hold exactly sixteen values.</exception>
        public static Matrix4x4 FromRowMajor(ReadOnlySpan<float> values)
        {
            if (values.Length != 16)
            {
                throw new ArgumentException(
                    "A 4x4 matrix needs exactly 16 values, not " + values.Length + ".", nameof(values));
            }

            return new Matrix4x4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }

        /// <summary>
        ///     Turns a pixel under the cursor into the world-space ray through it.
        /// </summary>
        /// <remarks>
        ///     Built by unprojecting the same pixel twice, at the near plane and at the far plane, and
        ///     subtracting. That is more work than deriving a direction from the camera basis, and it
        ///     is right for any projection the caller composed, including an orthographic one where
        ///     the rays are parallel and the eye position is not a point the ray passes through.
        ///     <para>
        ///     Every failure returns <c>false</c> rather than throwing. This runs on a mouse-move
        ///     handler, and a viewport that has been resized to zero or a matrix that is momentarily
        ///     singular are both ordinary transient states rather than faults.
        ///     </para>
        /// </remarks>
        /// <param name="modelViewProjection">The composed matrix the viewport drew with.</param>
        /// <param name="pixelX">Cursor x, in pixels from the left edge.</param>
        /// <param name="pixelY">Cursor y, in pixels from the <b>top</b> edge.</param>
        /// <param name="width">Viewport width in pixels.</param>
        /// <param name="height">Viewport height in pixels.</param>
        /// <param name="ray">The ray, when this returns <c>true</c>.</param>
        /// <returns><c>true</c> when a usable ray was built.</returns>
        public static bool TryBuildRay(Matrix4x4 modelViewProjection, float pixelX, float pixelY,
            int width, int height, out PickRay ray)
        {
            ray = default;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            if (!Matrix4x4.Invert(modelViewProjection, out Matrix4x4 inverse))
            {
                return false;
            }

            float ndcX = 2f * pixelX / width - 1f;

            //The same flip TryProject applies, in reverse: the cursor's y grows downwards and clip y
            //grows upwards. Getting this wrong here and there consistently would still fail, because
            //the two are not inverses of one another unless both flip.
            float ndcY = 1f - 2f * pixelY / height;

            if (!TryUnproject(inverse, ndcX, ndcY, -1f, out Vector3 onNearPlane))
            {
                return false;
            }

            if (!TryUnproject(inverse, ndcX, ndcY, 1f, out Vector3 onFarPlane))
            {
                return false;
            }

            ray = new PickRay(onNearPlane, onFarPlane - onNearPlane);

            //A zero-length direction means the near and far planes unprojected to the same point,
            //which no triangle test can do anything with.
            return ray.Direction.LengthSquared() > 0f;
        }

        /// <summary>
        ///     Turns a world-space position into the pixel it was drawn at, for the label overlay.
        /// </summary>
        /// <param name="modelViewProjection">The composed matrix the viewport drew with.</param>
        /// <param name="world">The world-space position.</param>
        /// <param name="width">Viewport width in pixels.</param>
        /// <param name="height">Viewport height in pixels.</param>
        /// <param name="pixel">The pixel, when this returns <c>true</c>.</param>
        /// <returns><c>false</c> when the position is at or behind the eye and has no pixel.</returns>
        public static bool TryProject(Matrix4x4 modelViewProjection, Vector3 world, int width, int height,
            out Vector2 pixel)
        {
            pixel = default;

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            Vector4 clip = Vector4.Transform(new Vector4(world, 1f), modelViewProjection);

            //Behind the eye, w is negative, and dividing by it mirrors the point through the centre
            //of the screen. A label would then appear on the opposite side of the model from the
            //vertex it names, which reads as a label on the wrong vertex rather than as a bug.
            if (clip.W <= 0f)
            {
                return false;
            }

            float ndcX = clip.X / clip.W;
            float ndcY = clip.Y / clip.W;

            //Clip y runs up and screen y runs down, so the vertical axis is flipped here and
            //nowhere else. Losing this puts every label and every pick on the mirrored row.
            //It cannot be caught at the centre pixel, which is the one point the flip leaves where
            //it was - which is why ViewportMathTests asserts off centre.
            pixel = new Vector2(
                (ndcX * 0.5f + 0.5f) * width,
                (1f - (ndcY * 0.5f + 0.5f)) * height);

            return true;
        }

        /// <summary>Takes one clip-space point back into world space.</summary>
        /// <param name="inverse">The inverted model-view-projection matrix.</param>
        /// <param name="ndcX">Clip x, -1 at the left edge and +1 at the right.</param>
        /// <param name="ndcY">Clip y, -1 at the bottom edge and +1 at the top.</param>
        /// <param name="ndcZ">Clip z, -1 at the near plane and +1 at the far plane.</param>
        /// <param name="world">The world-space position, when this returns <c>true</c>.</param>
        /// <returns><c>false</c> when the point is on the plane at infinity and cannot be divided out.</returns>
        private static bool TryUnproject(Matrix4x4 inverse, float ndcX, float ndcY, float ndcZ,
            out Vector3 world)
        {
            Vector4 unprojected = Vector4.Transform(new Vector4(ndcX, ndcY, ndcZ, 1f), inverse);

            if (unprojected.W == 0f)
            {
                world = default;
                return false;
            }

            world = new Vector3(unprojected.X, unprojected.Y, unprojected.Z) / unprojected.W;
            return true;
        }
    }

    /// <summary>
    ///     Moller-Trumbore ray-triangle intersection, two-sided.
    /// </summary>
    /// <remarks>
    ///     Two-sided on purpose. A model's face winding is whatever its strip opcodes produced, the
    ///     viewport draws with culling off, and a one-sided test would leave a scattering of visibly
    ///     drawn faces unselectable with nothing on screen to explain which ones.
    /// </remarks>
    public static class RayTriangle
    {
        /// <summary>
        ///     How near the ray-plane determinant may come to zero before the triangle is refused.
        /// </summary>
        /// <remarks>
        ///     It catches two cases at once: a ray parallel to the triangle's plane, and a degenerate
        ///     triangle whose corners do not span a plane at all. The second is not hypothetical -
        ///     index 7 holds faces whose three indices name one vertex, and the emitter path has its
        ///     own name for the same shape, stopping the emitter dead rather than spawning from a face
        ///     with no normal (<c>Particle_Sub9.java:367-375</c>).
        /// </remarks>
        public const float ParallelEpsilon = 1E-09f;

        /// <summary>Intersects a ray with a triangle, reporting the distance and barycentric weights.</summary>
        /// <param name="ray">The ray, whose direction need not be normalised.</param>
        /// <param name="a">First corner.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        /// <param name="distance">
        ///     Where along <paramref name="ray"/> the hit is, in the same units as its direction - so
        ///     0 is the near plane and 1 the far plane for a ray from
        ///     <see cref="ViewportMath.TryBuildRay"/>.
        /// </param>
        /// <param name="u">Weight of <paramref name="b"/>.</param>
        /// <param name="v">Weight of <paramref name="c"/>.</param>
        /// <returns><c>true</c> when the ray hits the triangle in front of its origin.</returns>
        public static bool Intersect(in PickRay ray, in Vector3 a, in Vector3 b, in Vector3 c,
            out float distance, out float u, out float v)
        {
            distance = 0f;
            u = 0f;
            v = 0f;

            Vector3 edgeAB = b - a;
            Vector3 edgeAC = c - a;
            Vector3 perpendicular = Vector3.Cross(ray.Direction, edgeAC);
            float determinant = Vector3.Dot(edgeAB, perpendicular);

            //Either sign is a hit; only a magnitude near zero is not. Testing for a positive
            //determinant here is what would make this one-sided.
            if (determinant > -ParallelEpsilon && determinant < ParallelEpsilon)
            {
                return false;
            }

            float inverseDeterminant = 1f / determinant;
            Vector3 originToA = ray.Origin - a;

            u = Vector3.Dot(originToA, perpendicular) * inverseDeterminant;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 crossAB = Vector3.Cross(originToA, edgeAB);

            v = Vector3.Dot(ray.Direction, crossAB) * inverseDeterminant;
            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            distance = Vector3.Dot(edgeAC, crossAB) * inverseDeterminant;

            //A negative distance is a triangle the ray has already passed. Returning it as a hit
            //lets the cursor pick geometry behind the eye.
            return distance >= 0f;
        }
    }
}
