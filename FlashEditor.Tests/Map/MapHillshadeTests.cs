using System;
using FlashEditor.Map;
using Xunit;

namespace FlashEditor.Tests.Map
{
    /// <summary>
    ///     Pins the relief shading maths.
    /// </summary>
    /// <remarks>
    ///     Shading fails silently: a dropped negation or a mis-signed gradient still produces a
    ///     plausible-looking picture, just with every hill inverted into a crater. These tests
    ///     assert the properties that distinguish right from plausible.
    /// </remarks>
    public sealed class MapHillshadeTests
    {
        private const double Azimuth = Hillshade.DefaultAzimuthDegrees;
        private const double Altitude = Hillshade.DefaultAltitudeDegrees;

        /// <summary>
        ///     Flat ground is exactly neutral, at any base height.
        /// </summary>
        /// <remarks>
        ///     Pins the derived ambient. Fixing ambient as a constant instead, with a clamp to 1,
        ///     would dim the whole map because flat ground's dot product is <c>sin(altitude)</c>
        ///     rather than zero. Also pins that only differences matter, not absolute height.
        /// </remarks>
        [Theory]
        [InlineData(0)]
        [InlineData(-320)]
        [InlineData(-1920)]
        [InlineData(100)]
        public void FlatGroundIsExactlyNeutral(int baseHeight)
        {
            float[,] shade = Hillshade.Build(Uniform(9, 9, baseHeight), Azimuth, Altitude, 1f);

            foreach (float value in shade)
                Assert.Equal(1.0f, value, 5);
        }

        /// <summary>
        ///     A surface facing the light is brighter, and one facing away is darker.
        /// </summary>
        /// <remarks>
        ///     The direction a slope <em>faces</em> is its downhill direction, which is the opposite
        ///     of the direction it rises in. So with a north-west light it is the ground rising
        ///     toward the <em>east</em> that is lit: that is the western face of a hill peaking to
        ///     the east. Getting this backwards is the easiest mistake to make here, which is why
        ///     the assertion messages spell out the reasoning.
        ///
        ///     Heights are negative up, so rising means the stored value decreases.
        /// </remarks>
        [Fact]
        public void SurfacesFacingTheLightAreBrighterAndSurfacesAwayAreDarker()
        {
            //Rises toward the east, so the surface faces west, toward the light.
            int[,] facingWest = Grid(9, 9, (x, y) => -x * 64);

            //Rises toward the west, so the surface faces east, away from the light.
            int[,] facingEast = Grid(9, 9, (x, y) => -(8 - x) * 64);

            float lit = Hillshade.Build(facingWest, Azimuth, Altitude, 1f)[4, 4];
            float unlit = Hillshade.Build(facingEast, Azimuth, Altitude, 1f)[4, 4];

            Assert.True(lit > 1.0f,
                $"a west-facing surface should catch a north-west light, got {lit}");
            Assert.True(unlit < 1.0f,
                $"an east-facing surface should fall into shade, got {unlit}");
        }

        /// <summary>
        ///     A hill is not a crater.
        /// </summary>
        /// <remarks>
        ///     The decisive sign test. A single monotone ramp passes even when both the gradient and
        ///     the light are flipped, because two errors cancel. Inverting the terrain and requiring
        ///     the relation to reverse does not.
        /// </remarks>
        [Fact]
        public void AHillIsNotACrater()
        {
            //A cone, highest at the centre. Negative is up.
            int[,] hill = Grid(11, 11, (x, y) => -(10 - Math.Abs(x - 5) - Math.Abs(y - 5)) * 64);
            int[,] pit = Grid(11, 11, (x, y) => (10 - Math.Abs(x - 5) - Math.Abs(y - 5)) * 64);

            float[,] hillShade = Hillshade.Build(hill, Azimuth, Altitude, 1f);
            float[,] pitShade = Hillshade.Build(pit, Azimuth, Altitude, 1f);

            //On a hill the north-west flank catches a north-west light and the south-east flank
            //falls away. A pit reverses both.
            Assert.True(hillShade[3, 6] > hillShade[6, 3],
                "a hill should be lit on its north-west flank");
            Assert.True(pitShade[3, 6] < pitShade[6, 3],
                "a pit should be shadowed where the hill was lit");
        }

        /// <summary>Zero strength is an exact identity, not an approximation.</summary>
        /// <remarks>
        ///     This is the contract behind the strength slider's left stop and the layer checkbox:
        ///     turning relief off must give back exactly the picture that existed before it.
        /// </remarks>
        [Fact]
        public void ZeroStrengthIsAnExactIdentity()
        {
            float[,] shade = Hillshade.Build(Varied(17, 17), Azimuth, Altitude, 0f);

            foreach (float value in shade)
                Assert.Equal(1.0f, value);
        }

        /// <summary>Strength scales the deviation from neutral linearly.</summary>
        [Fact]
        public void StrengthScalesTheDeviationFromNeutral()
        {
            int[,] heights = Varied(17, 17);

            float[,] full = Hillshade.Build(heights, Azimuth, Altitude, 1f);
            float[,] half = Hillshade.Build(heights, Azimuth, Altitude, 0.5f);

            for (int x = 0; x < full.GetLength(0); x++)
                for (int y = 0; y < full.GetLength(1); y++)
                    Assert.Equal(0.5f * (full[x, y] - 1f), half[x, y] - 1f, 5);
        }

        /// <summary>
        ///     The steepest encodable cliff stays inside the shade band.
        /// </summary>
        /// <remarks>
        ///     255 storable steps over one tile is a rise of 8160 world units against a rise term of
        ///     1024, so the normal is nearly horizontal. Guards the downstream channel multiply
        ///     against ever seeing a negative factor.
        /// </remarks>
        [Fact]
        public void ACliffStaysWithinTheShadeBand()
        {
            int[,] cliff = Grid(9, 9, (x, y) => x < 4 ? 0 : -255 * 32);

            double ambient = 1.0 - Hillshade.Diffuse * Math.Sin(Altitude * Math.PI / 180.0);

            foreach (float value in Hillshade.Build(cliff, Azimuth, Altitude, 1f))
            {
                Assert.True(value > 0f, $"shade went non-positive at {value}");
                Assert.InRange(value, (float) ambient - 0.001f, (float) (ambient + Hillshade.Diffuse) + 0.001f);
            }
        }

        /// <summary>A grid with no tiles in it yields no shades rather than throwing.</summary>
        [Fact]
        public void ADegenerateGridIsEmpty()
        {
            Assert.Empty(Hillshade.Build(new int[1, 1], Azimuth, Altitude, 1f));
        }

        [Fact]
        public void NullHeightsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => Hillshade.Build(null, Azimuth, Altitude, 1f));
        }

        private static int[,] Uniform(int w, int h, int value) => Grid(w, h, (_, _) => value);

        /// <summary>A deterministic, non-monotone height field.</summary>
        private static int[,] Varied(int w, int h) =>
            Grid(w, h, (x, y) => -((x * 37 + y * 61) % 23) * 32);

        private static int[,] Grid(int w, int h, Func<int, int, int> f)
        {
            var grid = new int[w, h];
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    grid[x, y] = f(x, y);
            return grid;
        }
    }
}
