using FlashEditor.Definitions.Sprites;
using Xunit;

namespace FlashEditor.Tests.Definitions
{
    /// <summary>
    ///     Known-answer vectors for the type 8 transfer curve.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Type 8 is the most common non-constant node in the cache, and it maps its input
    ///     through a 257-entry curve built once from the decoded markers. The expectations here
    ///     were produced by compiling <c>Node_Sub10_Sub9.method1031</c>, <c>method1034</c> and
    ///     <c>method1035</c> verbatim and running them over the marker sets this cache actually
    ///     contains, rather than by reimplementing them a second time.
    ///     </para>
    ///     <para>
    ///     Against those 1,715 real curves this implementation is byte-identical to the client
    ///     on all 440,755 entries, in all three interpolation modes. The vectors below are the
    ///     three real shapes plus synthetic cases covering behaviour that the real data happens
    ///     not to exercise.
    ///     </para>
    ///     <para>
    ///     The values that matter most are the ones outside 0..4096. A curve is <em>not</em>
    ///     clamped to its last marker: the segment search stops one short of the end, so a
    ///     position past the final marker keeps extrapolating with t above 4096. Only the final
    ///     result is bounded, to a signed short. An implementation that clamps to the endpoint
    ///     instead - as this one used to - agrees everywhere inside the marker range and is
    ///     wrong outside it.
    ///     </para>
    /// </remarks>
    public class TextureCurveTests
    {
        private static int[] BuildCurve(int mode, params int[] markerPairs)
        {
            var markers = new int[markerPairs.Length / 2][];
            for (int i = 0; i < markers.Length; i++)
                markers[i] = new[] { markerPairs[i * 2], markerPairs[i * 2 + 1] };

            var node = new TextureNode { Type = 8, GradientPreset = mode, GradientData = markers };
            Texture.InitCurveTransfer(node);
            return node.CurveLut;
        }

        /// <summary>Linear, the mode 1,627 of the cache's 1,715 curves use.</summary>
        [Fact]
        public void Linear_MatchesTheClient()
        {
            int[] lut = BuildCurve(0, 0, 0, 475, 3496, 4096, 4096);

            Assert.Equal(0, lut[0]);
            Assert.Equal(3586, lut[64]);
            Assert.Equal(3756, lut[128]);
            Assert.Equal(3926, lut[192]);
            Assert.Equal(4096, lut[256]);
        }

        /// <summary>Cosine, indexed off the client's 256-entry table rather than Math.Cos.</summary>
        [Fact]
        public void Cosine_MatchesTheClient()
        {
            int[] lut = BuildCurve(1,
                0, 0, 455, 4095, 806, 1, 1220, 4095, 1613, 1, 2027, 4095,
                2441, 1, 2813, 4095, 3227, 1, 3620, 4095, 4096, 1);

            Assert.Equal(0, lut[0]);
            Assert.Equal(2210, lut[64]);
            Assert.Equal(4073, lut[128]);
            Assert.Equal(1250, lut[192]);
            Assert.Equal(1, lut[256]);
        }

        /// <summary>
        ///     Cubic, which overshoots its markers in both directions.
        /// </summary>
        /// <remarks>
        ///     The negative entry is the point: the client uses Paul Bourke's cubic, whose knot
        ///     tangent is <c>p2 - p0</c>. The half-scaled Catmull-Rom tangent this used to use
        ///     passes through the same knots, so it looks right at every marker and is wrong
        ///     between them - and it cannot produce this undershoot at all.
        /// </remarks>
        [Fact]
        public void Cubic_OvershootsAndUndershootsLikeTheClient()
        {
            int[] lut = BuildCurve(2, 0, 0, 2006, 41, 2027, 3185, 4096, 4075);

            Assert.Equal(0, lut[0]);
            Assert.Equal(-377, lut[64]);   // undershoots below zero
            Assert.Equal(3225, lut[128]);
            Assert.Equal(4246, lut[192]);  // overshoots above 4096
            Assert.Equal(4075, lut[256]);
        }

        /// <summary>
        ///     A curve whose last marker sits below 4096 extrapolates rather than flattening.
        /// </summary>
        [Fact]
        public void PositionsPastTheLastMarker_Extrapolate()
        {
            int[] lut = BuildCurve(0, 0, 0, 2048, 2048);

            Assert.Equal(2048, lut[128]);  // the last marker
            Assert.Equal(3072, lut[192]);  // would be 2048 if clamped
            Assert.Equal(4096, lut[256]);
        }

        /// <summary>
        ///     Extrapolation is not bounded by the marker values either.
        /// </summary>
        [Fact]
        public void Extrapolation_RunsPastTheHighestMarkerValue()
        {
            int[] lut = BuildCurve(0, 0, 1000, 2048, 3000);

            Assert.Equal(3000, lut[128]);
            Assert.Equal(5000, lut[256]);  // above every marker in the curve
        }

        /// <summary>
        ///     Cubic extrapolation past the last marker, which runs away hard.
        /// </summary>
        /// <remarks>
        ///     Kept because it is the case where the reflected virtual markers from
        ///     <c>method1034</c> and the unbounded t compound. The client really does produce a
        ///     large negative here; only the signed-short bound stops it.
        /// </remarks>
        [Fact]
        public void CubicExtrapolation_MatchesTheClient()
        {
            int[] lut = BuildCurve(2, 0, 0, 1024, 2048, 2048, 1024, 3072, 4096);

            Assert.Equal(2048, lut[64]);
            Assert.Equal(1024, lut[128]);
            Assert.Equal(4096, lut[192]);
            Assert.Equal(-19456, lut[256]);
        }

        /// <summary>
        ///     A node with no curve data gets the identity ramp, as the client's method1001 does.
        /// </summary>
        [Fact]
        public void MissingCurveData_BecomesTheIdentityRamp()
        {
            var node = new TextureNode { Type = 8 };
            Texture.InitCurveTransfer(node);

            Assert.Equal(0, node.CurveLut[0]);
            Assert.Equal(2048, node.CurveLut[128]);
            Assert.Equal(4096, node.CurveLut[256]);
        }

        /// <summary>
        ///     A single marker cannot describe a curve, and must not take the texture down.
        /// </summary>
        /// <remarks>
        ///     The client throws "Curve operation requires at least two markers" here. This
        ///     leaves the curve null instead, which the evaluator reads as a passthrough, so a
        ///     malformed graph costs one node rather than the whole texture.
        /// </remarks>
        [Fact]
        public void SingleMarker_LeavesTheCurveUnbuilt()
        {
            var node = new TextureNode
            {
                Type = 8,
                GradientData = new[] { new[] { 0, 1234 } },
            };
            Texture.InitCurveTransfer(node);

            Assert.Null(node.CurveLut);
        }
    }
}
