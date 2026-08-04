using System;
using System.Numerics;
using FlashEditor.Rendering;
using Xunit;

namespace FlashEditor.Tests.Rendering
{
    /// <summary>
    ///     Pins the cursor-to-world and world-to-pixel arithmetic the index overlay is built on.
    /// </summary>
    /// <remarks>
    ///     No capture on this machine can see the GL surface, so a picker that is subtly wrong is
    ///     invisible: it still returns a face, and the highlight still lands on a face, just not the
    ///     one under the cursor. Someone then attaches a particle emitter to it. That is the defect
    ///     these tests exist to catch, and every expected value here is worked out from the
    ///     convention rather than produced by running the code.
    ///     <para>
    ///     Nothing here builds a projection with a library helper. The whole point of
    ///     <see cref="ViewportMath"/> taking one composed matrix is that it makes no assumption about
    ///     who built it, so the tests supply matrices written out by hand.
    ///     </para>
    /// </remarks>
    public class ViewportMathTests
    {
        /// <summary>A viewport wide enough that a pixel error of one is visible in the assertion.</summary>
        private const int Width = 800;

        /// <summary>Deliberately different from the width, so a swapped axis cannot pass.</summary>
        private const int Height = 600;

        /// <summary>
        ///     Screen y runs downwards and clip y runs upwards, and the projection flips between them.
        /// </summary>
        /// <remarks>
        ///     Asserted off centre on purpose. The centre pixel is the one point a y-flip defect
        ///     cannot move, so a test that only checked it would pass with the flip missing.
        /// </remarks>
        [Fact]
        public void Project_PutsClipUpAtTheTopOfTheScreen()
        {
            Matrix4x4 identity = Matrix4x4.Identity;

            Assert.True(ViewportMath.TryProject(identity, Vector3.Zero, Width, Height, out Vector2 centre));
            Assert.Equal(Width / 2.0, centre.X, 3);
            Assert.Equal(Height / 2.0, centre.Y, 3);

            Assert.True(ViewportMath.TryProject(identity, new Vector3(0f, 1f, 0f), Width, Height,
                out Vector2 top));
            Assert.Equal(Width / 2.0, top.X, 3);
            Assert.Equal(0.0, top.Y, 3);

            Assert.True(ViewportMath.TryProject(identity, new Vector3(1f, 0f, 0f), Width, Height,
                out Vector2 right));
            Assert.Equal((double)Width, right.X, 3);
            Assert.Equal(Height / 2.0, right.Y, 3);
        }

        /// <summary>A position at or behind the eye has no pixel, rather than a mirrored one.</summary>
        [Fact]
        public void Project_RefusesAPositionBehindTheEye()
        {
            Matrix4x4 projection = Perspective(90f, Width / (float)Height, 1f, 100f);

            //Positive z is behind the eye for a right-handed camera looking down -z.
            Assert.False(ViewportMath.TryProject(projection, new Vector3(0f, 0f, 5f), Width, Height, out _));
            Assert.True(ViewportMath.TryProject(projection, new Vector3(0f, 0f, -5f), Width, Height, out _));
        }

        /// <summary>A viewport with no area produces neither a ray nor a pixel.</summary>
        [Fact]
        public void ZeroSizedViewport_ProducesNothing()
        {
            Assert.False(ViewportMath.TryProject(Matrix4x4.Identity, Vector3.Zero, 0, Height, out _));
            Assert.False(ViewportMath.TryBuildRay(Matrix4x4.Identity, 1f, 1f, Width, 0, out _));
        }

        /// <summary>
        ///     A ray built from a pixel projects back to that pixel, anywhere along its length.
        /// </summary>
        /// <remarks>
        ///     The strongest statement available without a second implementation to compare against:
        ///     unprojection and projection are inverses, at four corners of the viewport and at three
        ///     depths. A y flip, a transposed matrix or a depth range read as 0 to 1 all break it.
        /// </remarks>
        [Theory]
        [InlineData(0f, 0f)]
        [InlineData(799f, 0f)]
        [InlineData(0f, 599f)]
        [InlineData(640f, 150f)]
        public void RayAndProjection_AreInverses(float pixelX, float pixelY)
        {
            Matrix4x4 projection = Perspective(60f, Width / (float)Height, 1f, 200f);

            Assert.True(ViewportMath.TryBuildRay(projection, pixelX, pixelY, Width, Height, out PickRay ray));

            foreach (float along in new[] { 0.05f, 0.4f, 0.9f })
            {
                Vector3 point = ray.At(along);
                Assert.True(ViewportMath.TryProject(projection, point, Width, Height, out Vector2 pixel));
                Assert.Equal(pixelX, pixel.X, 1);
                Assert.Equal(pixelY, pixel.Y, 1);
            }
        }

        /// <summary>The ray starts on the near plane and points away from the eye.</summary>
        [Fact]
        public void Ray_StartsOnTheNearPlaneAndPointsForwards()
        {
            const float near = 2f;
            Matrix4x4 projection = Perspective(90f, 1f, near, 200f);

            Assert.True(ViewportMath.TryBuildRay(projection, Width / 2f, Height / 2f, Width, Height,
                out PickRay ray));

            Assert.Equal(-(double)near, ray.Origin.Z, 3);
            Assert.True(ray.Direction.Z < 0f, "The ray must point away from a camera looking down -z.");
        }

        /// <summary>Sixteen floats become the matrix laid out row by row.</summary>
        [Fact]
        public void FromRowMajor_TakesTheRowsInOrder()
        {
            var values = new float[16];
            for (int i = 0; i < 16; i++)
                values[i] = i + 1;

            Matrix4x4 matrix = ViewportMath.FromRowMajor(values);

            Assert.Equal(1f, matrix.M11);
            Assert.Equal(4f, matrix.M14);
            Assert.Equal(5f, matrix.M21);
            Assert.Equal(16f, matrix.M44);
        }

        /// <summary>A wrong count is refused rather than read past or padded.</summary>
        [Fact]
        public void FromRowMajor_RefusesTheWrongCount()
        {
            Assert.Throws<ArgumentException>(() => ViewportMath.FromRowMajor(new float[15]));
        }

        /// <summary>A ray through the middle of a triangle hits it, with the barycentric weights.</summary>
        [Fact]
        public void RayTriangle_ReportsTheBarycentricWeights()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(1f, 0f, 0f);
            var c = new Vector3(0f, 1f, 0f);

            //Straight down -z at the point one quarter along ab and one quarter along ac.
            var ray = new PickRay(new Vector3(0.25f, 0.25f, 5f), new Vector3(0f, 0f, -10f));

            Assert.True(RayTriangle.Intersect(ray, a, b, c, out float distance, out float u, out float v));
            Assert.Equal(0.25f, u, 4);
            Assert.Equal(0.25f, v, 4);
            Assert.Equal(0.5f, distance, 4);
        }

        /// <summary>A ray outside the triangle misses, including just outside the hypotenuse.</summary>
        [Fact]
        public void RayTriangle_MissesOutsideTheTriangle()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(1f, 0f, 0f);
            var c = new Vector3(0f, 1f, 0f);
            var direction = new Vector3(0f, 0f, -10f);

            Assert.False(RayTriangle.Intersect(new PickRay(new Vector3(-0.1f, 0.5f, 5f), direction),
                a, b, c, out _, out _, out _));
            Assert.False(RayTriangle.Intersect(new PickRay(new Vector3(0.7f, 0.7f, 5f), direction),
                a, b, c, out _, out _, out _));
        }

        /// <summary>
        ///     The back of a triangle is hit as readily as the front.
        /// </summary>
        /// <remarks>
        ///     Two-sided on purpose - the format's face winding is whatever the strip opcodes
        ///     produced, and the viewport draws with culling off. A one-sided picker would leave a
        ///     scattering of visible faces unselectable with no visible reason.
        /// </remarks>
        [Fact]
        public void RayTriangle_HitsFromBehind()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(1f, 0f, 0f);
            var c = new Vector3(0f, 1f, 0f);

            var fromBehind = new PickRay(new Vector3(0.25f, 0.25f, -5f), new Vector3(0f, 0f, 10f));

            Assert.True(RayTriangle.Intersect(fromBehind, a, b, c, out float distance, out _, out _));
            Assert.Equal(0.5f, distance, 4);
        }

        /// <summary>A triangle behind the ray's origin is not a hit.</summary>
        [Fact]
        public void RayTriangle_IgnoresWhatIsBehindTheOrigin()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(1f, 0f, 0f);
            var c = new Vector3(0f, 1f, 0f);

            var pastIt = new PickRay(new Vector3(0.25f, 0.25f, -5f), new Vector3(0f, 0f, -10f));

            Assert.False(RayTriangle.Intersect(pastIt, a, b, c, out _, out _, out _));
        }

        /// <summary>
        ///     A face whose three indices name one vertex is refused rather than dividing by zero.
        /// </summary>
        /// <remarks>
        ///     Not hypothetical. Index 7 holds faces whose corners collapse, and the emitter path has
        ///     its own name for the same shape - <c>Particle_Sub9.java:367</c> flags a degenerate
        ///     attachment face and stops the emitter dead.
        /// </remarks>
        [Fact]
        public void RayTriangle_RefusesADegenerateTriangle()
        {
            var point = new Vector3(1f, 2f, 3f);
            var ray = new PickRay(new Vector3(1f, 2f, 10f), new Vector3(0f, 0f, -20f));

            Assert.False(RayTriangle.Intersect(ray, point, point, point, out _, out _, out _));
        }

        /// <summary>
        ///     A right-handed OpenGL perspective matrix, row-major, written out rather than derived.
        /// </summary>
        /// <remarks>
        ///     Transcribed from the specification's <c>glFrustum</c> so the tests do not inherit any
        ///     library's opinion about the depth range. Clip space here is -1 at the near plane and
        ///     +1 at the far one, which is the convention <see cref="ViewportMath"/> documents.
        /// </remarks>
        /// <param name="fovDegrees">Vertical field of view.</param>
        /// <param name="aspect">Width over height.</param>
        /// <param name="near">Near plane distance, positive.</param>
        /// <param name="far">Far plane distance, positive.</param>
        /// <returns>The matrix.</returns>
        private static Matrix4x4 Perspective(float fovDegrees, float aspect, float near, float far)
        {
            float focal = 1f / MathF.Tan(fovDegrees * MathF.PI / 360f);

            return new Matrix4x4(
                focal / aspect, 0f, 0f, 0f,
                0f, focal, 0f, 0f,
                0f, 0f, (far + near) / (near - far), -1f,
                0f, 0f, 2f * far * near / (near - far), 0f);
        }
    }
}
