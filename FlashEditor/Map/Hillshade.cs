using System;

namespace FlashEditor.Map {
    /// <summary>
    ///     Turns a vertex height grid into a per-tile brightness multiplier: a standard cartographic
    ///     hillshade.
    /// </summary>
    /// <remarks>
    ///     Deliberately not the client's lighting. The client's own 2D top-down view, the world map
    ///     (<c>Class278</c>), is flat and unlit on a hardcoded constant light of 96, which is the
    ///     strongest precedent available. Its two 3D paths disagree with each other - the software
    ///     renderer modulates the packed HSL lightness by <c>light/128</c> clamped to 2..126, the GPU
    ///     renderers multiply linear RGB by roughly 0.86 to 1.39 - and the software one is broken at
    ///     the real tile size: <c>s_Sub3.java:66</c> uses <c>512 * S</c> where it should be
    ///     <c>4 * S * S</c>, which is only equal when S is 128, so at 512 it clips flat ground to
    ///     lightness 126 and subtle relief disappears entirely.
    ///
    ///     What is taken from the client is the central-difference gradient stencil at
    ///     <c>s_Sub1.java:262-270</c>, which is correct and cheap, and the 512-unit tile size it
    ///     divides by. The light direction is not: 315 degrees azimuth at 45 degrees altitude is the
    ///     conventional north-west cartographic light, chosen over the client's
    ///     <c>(-200, -240, -200)</c> because an editor wants relief it can read rather than the
    ///     game's mood.
    ///
    ///     See <c>reference/hydra-637-maps/05-colour-and-rendering.md</c> section 5.
    /// </remarks>
    public static class Hillshade {
        /// <summary>Conventional north-west light, in degrees clockwise from north.</summary>
        public const double DefaultAzimuthDegrees = 315.0;

        /// <summary>Conventional sun elevation, in degrees above the horizon.</summary>
        public const double DefaultAltitudeDegrees = 45.0;

        /// <summary>
        ///     How much of the brightness the light direction controls, as opposed to ambient.
        /// </summary>
        /// <remarks>
        ///     The ambient term is derived from this and the altitude rather than fixed, so that
        ///     flat ground comes out at exactly 1.0. A fixed ambient would dim or brighten the whole
        ///     map whenever the altitude changed.
        /// </remarks>
        public const double Diffuse = 0.6;

        /// <summary>
        ///     Computes a brightness multiplier per tile from a vertex height grid.
        /// </summary>
        /// <param name="vertexHeights">
        ///     Heights indexed <c>[vertexX, vertexY]</c>, one larger than the tile grid on each axis.
        ///     Use <see cref="MapScene.HeightGrid"/>, which resolves shared vertices correctly.
        /// </param>
        /// <param name="azimuthDegrees">Light azimuth, clockwise from north.</param>
        /// <param name="altitudeDegrees">Light altitude above the horizon.</param>
        /// <param name="strength">
        ///     How far to move away from neutral, 0 to 1. Zero is an exact identity.
        /// </param>
        /// <returns>Multipliers indexed <c>[tileX, tileY]</c>, one smaller than the vertex grid.</returns>
        public static float[,] Build(int[,] vertexHeights, double azimuthDegrees, double altitudeDegrees, float strength) {
            if (vertexHeights == null) throw new ArgumentNullException(nameof(vertexHeights));

            int width = vertexHeights.GetLength(0) - 1;
            int height = vertexHeights.GetLength(1) - 1;

            var shade = new float[Math.Max(0, width), Math.Max(0, height)];

            double azimuth = azimuthDegrees * Math.PI / 180.0;
            double altitude = altitudeDegrees * Math.PI / 180.0;

            //Scene axes are X east, Y north, Z up. Azimuth is measured clockwise from north, so the
            //east component is the sine and the north component the cosine; 315 is north-west.
            double lightX = Math.Sin(azimuth) * Math.Cos(altitude);
            double lightY = Math.Cos(azimuth) * Math.Cos(altitude);
            double lightZ = Math.Sin(altitude);

            //Derived so that flat ground, whose dot product is exactly lightZ, lands on 1.0.
            double ambient = 1.0 - Diffuse * lightZ;

            //Both differences below span two tiles, matching the client's H[x+1] - H[x-1], so this
            //is twice the tile size with no further rescaling.
            const double Rise = 2.0 * TileShapes.TileSize;

            for (int x = 0; x < width; x++) {
                for (int y = 0; y < height; y++) {
                    /* Evaluated from the tile's own four corners rather than from a vertex-centred
                       stencil, so the shade lands on the tile it describes rather than half a tile
                       off. Over a constant slope this yields the same value as the client's
                       stencil, so the Rise term above is unchanged. */
                    int storedX = (vertexHeights[x + 1, y] + vertexHeights[x + 1, y + 1])
                                - (vertexHeights[x, y] + vertexHeights[x, y + 1]);
                    int storedY = (vertexHeights[x, y + 1] + vertexHeights[x + 1, y + 1])
                                - (vertexHeights[x, y] + vertexHeights[x + 1, y]);

                    /* Two sign conventions meet here and cancel exactly. Stored heights are negative
                       up, so the positive-up rise is the negation; and a surface normal is
                       (-de/dx, -de/dy, 1), which negates again. Both are written out rather than
                       collapsed, because the collapsed form reads like a missing minus sign and
                       "correcting" it turns every hill into a crater. */
                    double elevationX = -storedX;
                    double elevationY = -storedY;

                    double nx = -elevationX;
                    double ny = -elevationY;
                    double nz = Rise;

                    double length = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    double dot = (nx * lightX + ny * lightY + nz * lightZ) / length;

                    //A face turned away from the light gets ambient only. Cliffs reach this: 255
                    //steps over one tile is a rise of 8160 against a Rise of 1024, so the normal is
                    //very nearly horizontal and the dot goes strongly negative.
                    double lit = ambient + Diffuse * Math.Max(0.0, dot);

                    shade[x, y] = (float) (1.0 + strength * (lit - 1.0));
                }
            }

            return shade;
        }
    }
}
