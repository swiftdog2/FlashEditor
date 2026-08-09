using System;
using FlashEditor.Definitions;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Recomputes lighting normals for a posed mesh, the way the client does when it rebuilds one.
    /// </summary>
    /// <remarks>
    ///     A transcription of <c>Renderable_Sub2.java:591-650</c>. It exists because a pose moves
    ///     vertices and the normals uploaded with the rest model are then wrong: a limb that has
    ///     rotated 90 degrees is still lit as though it had not, which reads as a flat, plastic
    ///     patch on an otherwise shaded model rather than as an obvious defect.
    ///     <para>
    ///     The output is per <b>face-vertex</b> rather than per vertex - nine floats for each face,
    ///     three corners of three components. That is deliberate: a flat-shaded face needs one normal
    ///     repeated across its corners while a smooth-shaded one needs each corner's averaged normal,
    ///     and a face's neighbours may be shaded the other way. Splitting the vertex is the standard
    ///     answer and it is what lets one buffer carry both kinds.
    ///     </para>
    /// </remarks>
    public static class PosedNormals
    {
        /// <summary>
        ///     Largest magnitude a raw cross-product component may reach before it is halved.
        /// </summary>
        /// <remarks>
        ///     <c>Renderable_Sub2.java:605-610</c>. The cross product of two edge vectors of a large
        ///     face overflows a 32-bit int when it is then squared for the length, so the client
        ///     halves all three components together until each fits. Halving them together is what
        ///     keeps the direction; scaling them independently would not.
        /// </remarks>
        private const int ComponentLimit = 8192;

        /// <summary>Length the client normalises a face normal to, before this converts to a unit float.</summary>
        private const int NormalLength = 256;

        /// <summary>Face render type 1: flat shaded, one normal for the whole face.</summary>
        private const int FlatRenderType = 1;

        /// <summary>Face render type 0: Gouraud shaded, normals averaged at the shared vertices.</summary>
        private const int SmoothRenderType = 0;

        /// <summary>
        ///     Computes a normal for each corner of each face of a posed mesh.
        /// </summary>
        /// <remarks>
        ///     Render type 2 contributes nothing - not to its own face and not to the vertex averages.
        ///     That falls out of the client's structure (<c>:622-649</c> handles 1 and 0 and lets
        ///     everything else through), and it is right: type 2 faces are stray geometry the client
        ///     never draws, frequently unattached to the mesh, and letting one into a vertex average
        ///     would tilt the shading of the real faces around it.
        /// </remarks>
        /// <param name="mesh">The posed mesh. Its vertex arrays must be in model units, so after
        ///     <see cref="PosedMesh.Finish"/>.</param>
        /// <returns>
        ///     One array of nine floats per face - corner A xyz, corner B xyz, corner C xyz - in the
        ///     viewport's world orientation, so already y- and z-flipped.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
        public static float[][] ComputeFaceVertexNormals(PosedMesh mesh)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            ModelDefinition model = mesh.Skin.Model;
            int faceCount = model.faceIndices1.Length;
            int vertexCount = mesh.VertexX.Length;

            //Running sums per vertex, for the smooth-shaded faces. Kept as integers because the
            //client does and because the per-face normals being summed are already quantised to a
            //length of 256, so there is no precision to be had from floats here.
            int[] smoothSumX = new int[vertexCount];
            int[] smoothSumY = new int[vertexCount];
            int[] smoothSumZ = new int[vertexCount];

            //One normal per flat-shaded face, kept apart from the vertex sums so that a flat face
            //neither contributes to nor reads from its neighbours.
            int[] flatNormalX = new int[faceCount];
            int[] flatNormalY = new int[faceCount];
            int[] flatNormalZ = new int[faceCount];
            bool[] isFlat = new bool[faceCount];

            for (int face = 0; face < faceCount; face++)
            {
                int cornerA = model.faceIndices1[face];
                int cornerB = model.faceIndices2[face];
                int cornerC = model.faceIndices3[face];

                //A face naming a vertex the model does not have contributes nothing. Index 7 holds
                //them, and reading past the array to light one is not worth doing.
                if ((uint)cornerA >= (uint)vertexCount
                    || (uint)cornerB >= (uint)vertexCount
                    || (uint)cornerC >= (uint)vertexCount)
                {
                    continue;
                }

                int abX = mesh.VertexX[cornerB] - mesh.VertexX[cornerA];
                int abY = mesh.VertexY[cornerB] - mesh.VertexY[cornerA];
                int abZ = mesh.VertexZ[cornerB] - mesh.VertexZ[cornerA];
                int acX = mesh.VertexX[cornerC] - mesh.VertexX[cornerA];
                int acY = mesh.VertexY[cornerC] - mesh.VertexY[cornerA];
                int acZ = mesh.VertexZ[cornerC] - mesh.VertexZ[cornerA];

                int normalX = abY * acZ - acY * abZ;
                int normalY = abZ * acX - acZ * abX;
                int normalZ = abX * acY - acX * abY;

                //Halve all three together until each fits, so the squares below cannot overflow.
                while (normalX > ComponentLimit || normalY > ComponentLimit || normalZ > ComponentLimit
                    || normalX < -ComponentLimit || normalY < -ComponentLimit || normalZ < -ComponentLimit)
                {
                    normalX >>= 1;
                    normalY >>= 1;
                    normalZ >>= 1;
                }

                //Computed in double precision because the components are still up to 8192 and the
                //sum of their squares is beyond an int.
                int length = (int)Math.Sqrt(
                    (double)normalX * normalX + (double)normalY * normalY + (double)normalZ * normalZ);

                //A degenerate face has no direction. One rather than a skip, so the arithmetic below
                //produces a zero vector instead of dividing by zero.
                if (length <= 0)
                {
                    length = 1;
                }

                normalX = normalX * NormalLength / length;
                normalY = normalY * NormalLength / length;
                normalZ = normalZ * NormalLength / length;

                //A missing render-type array means every face is type 0 - the legacy model format
                //cannot express anything else, and 10.6 million faces in this cache are in it.
                int renderType = model.FaceRenderType != null && face < model.FaceRenderType.Length
                    ? model.FaceRenderType[face]
                    : SmoothRenderType;

                switch (renderType)
                {
                    case FlatRenderType:
                        isFlat[face] = true;
                        flatNormalX[face] = normalX;
                        flatNormalY[face] = normalY;
                        flatNormalZ[face] = normalZ;
                        break;

                    case SmoothRenderType:
                        smoothSumX[cornerA] += normalX;
                        smoothSumY[cornerA] += normalY;
                        smoothSumZ[cornerA] += normalZ;
                        smoothSumX[cornerB] += normalX;
                        smoothSumY[cornerB] += normalY;
                        smoothSumZ[cornerB] += normalZ;
                        smoothSumX[cornerC] += normalX;
                        smoothSumY[cornerC] += normalY;
                        smoothSumZ[cornerC] += normalZ;
                        break;

                    //Type 2 is not drawn, so it neither carries a normal nor tilts its neighbours'.
                }
            }

            float[][] perFace = new float[faceCount][];
            Span<int> corners = stackalloc int[3];

            for (int face = 0; face < faceCount; face++)
            {
                int cornerA = model.faceIndices1[face];
                int cornerB = model.faceIndices2[face];
                int cornerC = model.faceIndices3[face];

                if ((uint)cornerA >= (uint)vertexCount
                    || (uint)cornerB >= (uint)vertexCount
                    || (uint)cornerC >= (uint)vertexCount)
                {
                    //Straight up, so a malformed face is lit consistently rather than left with a
                    //zero normal the shader would divide by.
                    perFace[face] = new float[9] { 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f };
                    continue;
                }

                if (isFlat[face])
                {
                    (float x, float y, float z) = Normalise(flatNormalX[face], flatNormalY[face], flatNormalZ[face]);
                    perFace[face] = new float[9] { x, y, z, x, y, z, x, y, z };
                    continue;
                }

                corners[0] = cornerA;
                corners[1] = cornerB;
                corners[2] = cornerC;

                float[] smooth = new float[9];
                for (int corner = 0; corner < 3; corner++)
                {
                    int vertex = corners[corner];
                    (float x, float y, float z) = Normalise(smoothSumX[vertex], smoothSumY[vertex], smoothSumZ[vertex]);
                    smooth[corner * 3] = x;
                    smooth[corner * 3 + 1] = y;
                    smooth[corner * 3 + 2] = z;
                }

                perFace[face] = smooth;
            }

            return perFace;
        }

        /// <summary>Scales an integer normal to unit length and into the viewport's orientation.</summary>
        /// <remarks>
        ///     The y and z negations are <see cref="RenderSpace"/>'s, applied here rather than by
        ///     calling it because a normal is a direction and must not be divided by
        ///     <see cref="RenderSpace.ModelUnitsPerWorldUnit"/> - a scale would come straight back out
        ///     in the normalisation, but routing a direction through a position conversion is the kind
        ///     of thing that stops being harmless the moment the conversion gains a translation.
        ///     <para>
        ///     A vertex that only ever belonged to type-1 or type-2 faces has a zero sum. Clamping the
        ///     length to one leaves it as a zero vector, which the shader treats as unlit, rather than
        ///     producing an infinity that turns the face black.
        ///     </para>
        /// </remarks>
        /// <param name="x">Accumulated x.</param>
        /// <param name="y">Accumulated y.</param>
        /// <param name="z">Accumulated z.</param>
        /// <returns>The unit-length normal in world orientation.</returns>
        private static (float, float, float) Normalise(int x, int y, int z)
        {
            float length = MathF.Sqrt((float)x * x + (float)y * y + (float)z * z);

            if (length < 1f)
            {
                length = 1f;
            }

            return (x / length, -y / length, -z / length);
        }
    }
}
