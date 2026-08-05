using System.Numerics;
using System;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Builds the viewport's overlay geometry - the wireframe and the hover highlight - in the
    ///     model shader's own vertex layout.
    /// </summary>
    /// <remarks>
    ///     The overlays share the model shader rather than having one of their own, which keeps the
    ///     renderer to a single program and a single vertex format. The price is that the shader
    ///     <i>lights</i> what it draws, and an overlay wants a flat known colour. The way to one is
    ///     arithmetic rather than a second shader: write the light direction itself as the vertex
    ///     normal, so <c>N dot L</c> is exactly 1 and the lighting term is exactly
    ///     <see cref="FullIncidenceLighting"/>, then pre-divide the colour by that. See
    ///     <see cref="Unlit"/>.
    ///     <para>
    ///     The constants below mirror <c>Shaders/texture.vert</c> and have to move with it. There is
    ///     no way to check that from inside the process - the shader is compiled by the driver - so
    ///     the tests assert the derived value and this remark is the link back to the source.
    ///     </para>
    /// </remarks>
    public static class OverlayGeometry
    {
        /// <summary>
        ///     Floats per vertex: position 3, normal 3, uv 2, alpha 1, colour 3.
        /// </summary>
        /// <remarks>
        ///     The layout the model shader declares at locations 0 to 4. Everything the overlay
        ///     renderer uploads is in it, including the particle billboards, which is why one vertex
        ///     array object and one attribute binding serve all three.
        /// </remarks>
        public const int FloatsPerVertex = 12;

        /// <summary>The shader's ambient term.</summary>
        /// <remarks><c>texture.vert</c>: <c>lighting = 1.2 * (0.3 + 0.7 * NdotL)</c>.</remarks>
        public const float ShaderAmbient = 0.3f;

        /// <summary>The shader's diffuse term, scaled by <c>N dot L</c>.</summary>
        public const float ShaderDiffuse = 0.7f;

        /// <summary>The shader's overall gain, applied after ambient and diffuse are summed.</summary>
        public const float ShaderGain = 1.2f;

        /// <summary>
        ///     What the shader multiplies a colour by when the normal faces the light exactly.
        /// </summary>
        /// <remarks>
        ///     <c>ShaderGain * (ShaderAmbient + ShaderDiffuse)</c>, which is 1.2 because the ambient
        ///     and diffuse terms sum to one. Kept as its own constant because it is the number the
        ///     overlay divides by, and deriving it inline would hide the assumption that the two terms
        ///     sum to one - if they ever stop doing so, this is the constant that has to change and
        ///     the others do not.
        /// </remarks>
        public const float FullIncidenceLighting = ShaderGain * (ShaderAmbient + ShaderDiffuse);

        /// <summary>
        ///     How far along the light direction the wireframe is pushed, in world units.
        /// </summary>
        /// <remarks>
        ///     A depth bias. The wireframe sits exactly on the surface it outlines, so without it the
        ///     two are coplanar and the depth test decides between them per fragment - the classic
        ///     stippled, crawling z-fight. Nudging along the light direction rather than along each
        ///     face's own normal is deliberate: the light follows the camera, so this pushes roughly
        ///     towards the viewer whatever angle the model is at, and one uniform offset serves every
        ///     triangle.
        /// </remarks>
        public const float WireframeDepthBiasWorldUnits = 0.008f;

        /// <summary>
        ///     How opaque the hover highlight is.
        /// </summary>
        /// <remarks>
        ///     Short of one on purpose. An opaque highlight erases the geometry it is meant to be
        ///     pointing at, and the thing being pointed at is a face someone is about to attach a
        ///     particle emitter to.
        /// </remarks>
        public const float HighlightOpacity = 0.55f;

        /// <summary>The wireframe's colour, as it should appear on screen after lighting.</summary>
        /// <remarks>A desaturated blue, so it reads against both the dark background and lit geometry.</remarks>
        public static Vector3 WireframeColour => new Vector3(0.45f, 0.6f, 0.85f);

        /// <summary>The hover highlight's colour, as it should appear on screen after lighting.</summary>
        /// <remarks>Amber, chosen to be the complement of the wireframe so the two never blend into one.</remarks>
        public static Vector3 HighlightColour => new Vector3(1f, 0.72f, 0.15f);

        /// <summary>
        ///     Pre-divides a wanted on-screen colour by the lighting the shader will apply to it.
        /// </summary>
        /// <remarks>
        ///     Only correct in combination with writing the light direction as the vertex normal, which
        ///     is what every builder here and in <see cref="ParticleBillboards"/> does. Used on its own
        ///     it would darken a colour by a fifth for no reason.
        /// </remarks>
        /// <param name="colour">The colour that should appear on screen.</param>
        /// <returns>The colour to store in the vertex buffer.</returns>
        public static Vector3 Unlit(Vector3 colour)
        {
            return colour / FullIncidenceLighting;
        }

        /// <summary>Builds a line-list wireframe over every pickable triangle.</summary>
        /// <remarks>
        ///     Three edges per triangle, with nothing shared between neighbours. Deduplicating shared
        ///     edges would roughly halve the index count and would need a per-model edge set built at
        ///     load and rebuilt whenever the pose moved; at the sizes this viewer opens, the copy is
        ///     cheaper than the bookkeeping. It also guarantees every face's outline is complete, which
        ///     is what the overlay is for.
        ///     <para>
        ///     Vertices are not shared between triangles either, because each carries the biased
        ///     position rather than the surface position.
        ///     </para>
        /// </remarks>
        /// <param name="mesh">The pick mesh, so the outline covers exactly what the cursor can reach.</param>
        /// <param name="lightDirection">The current light direction, written as every vertex normal.</param>
        /// <param name="indices">Line-list indices, two per edge.</param>
        /// <returns>The interleaved vertex buffer.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="mesh"/> is null.</exception>
        public static float[] BuildWireframe(PickMesh mesh, Vector3 lightDirection, out uint[] indices)
        {
            if (mesh == null)
            {
                throw new ArgumentNullException(nameof(mesh));
            }

            float[] buffer = new float[mesh.TriangleCount * 3 * FloatsPerVertex];
            indices = new uint[mesh.TriangleCount * 6];

            Vector3 normal = SafeNormal(lightDirection);
            Vector3 colour = Unlit(WireframeColour);
            Vector3 bias = normal * WireframeDepthBiasWorldUnits;

            for (int triangle = 0; triangle < mesh.TriangleCount; triangle++)
            {
                mesh.TriangleCorners(triangle, out Vector3 a, out Vector3 b, out Vector3 c);

                int firstVertex = triangle * 3;
                Write(buffer, firstVertex, a + bias, normal, colour, 1f);
                Write(buffer, firstVertex + 1, b + bias, normal, colour, 1f);
                Write(buffer, firstVertex + 2, c + bias, normal, colour, 1f);

                int firstIndex = triangle * 6;
                indices[firstIndex] = (uint)firstVertex;
                indices[firstIndex + 1] = (uint)(firstVertex + 1);
                indices[firstIndex + 2] = (uint)(firstVertex + 1);
                indices[firstIndex + 3] = (uint)(firstVertex + 2);
                indices[firstIndex + 4] = (uint)(firstVertex + 2);
                indices[firstIndex + 5] = (uint)firstVertex;
            }

            return buffer;
        }

        /// <summary>Builds the single translucent triangle that marks the hovered face.</summary>
        /// <remarks>
        ///     No depth bias here. The highlight is drawn with the depth test off entirely
        ///     (<see cref="ViewportOverlayRenderer.Draw"/>), because a face the cursor is over must be
        ///     visible even when it is behind something - the picker is two-sided, so it can be.
        /// </remarks>
        /// <param name="a">First corner, in world space.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        /// <param name="lightDirection">The current light direction, written as every vertex normal.</param>
        /// <returns>The interleaved vertex buffer, three vertices long.</returns>
        public static float[] BuildHighlight(Vector3 a, Vector3 b, Vector3 c, Vector3 lightDirection)
        {
            float[] buffer = new float[3 * FloatsPerVertex];

            Vector3 normal = SafeNormal(lightDirection);
            Vector3 colour = Unlit(HighlightColour);

            Write(buffer, 0, a, normal, colour, HighlightOpacity);
            Write(buffer, 1, b, normal, colour, HighlightOpacity);
            Write(buffer, 2, c, normal, colour, HighlightOpacity);

            return buffer;
        }

        /// <summary>Writes one vertex into an interleaved buffer in the shader's layout.</summary>
        /// <remarks>
        ///     The single place the field order is written down, so the overlay, the highlight and the
        ///     billboards cannot drift apart from one another. The uv pair is zeroed - the overlays
        ///     sample a one-pixel white texture, so any coordinate would do and zero is the one that
        ///     says nothing was intended. <see cref="ParticleBillboards"/> overwrites it afterwards.
        /// </remarks>
        /// <param name="buffer">The interleaved buffer.</param>
        /// <param name="vertex">Which vertex of it.</param>
        /// <param name="position">World-space position.</param>
        /// <param name="normal">Vertex normal. The light direction, for anything that wants a flat colour.</param>
        /// <param name="colour">Colour, already divided by <see cref="FullIncidenceLighting"/>.</param>
        /// <param name="opacity">Alpha, 0 to 1.</param>
        internal static void Write(float[] buffer, int vertex, Vector3 position, Vector3 normal, Vector3 colour,
            float opacity)
        {
            int offset = vertex * FloatsPerVertex;

            buffer[offset] = position.X;
            buffer[offset + 1] = position.Y;
            buffer[offset + 2] = position.Z;
            buffer[offset + 3] = normal.X;
            buffer[offset + 4] = normal.Y;
            buffer[offset + 5] = normal.Z;
            buffer[offset + 6] = 0f;
            buffer[offset + 7] = 0f;
            buffer[offset + 8] = opacity;
            buffer[offset + 9] = colour.X;
            buffer[offset + 10] = colour.Y;
            buffer[offset + 11] = colour.Z;
        }

        /// <summary>Normalises a direction, falling back to straight up when it has no length.</summary>
        /// <remarks>
        ///     The light direction comes from the camera and is momentarily degenerate while a view
        ///     matrix is being rebuilt. Normalising a zero vector gives NaN, and a NaN normal makes
        ///     every vertex of the overlay vanish rather than merely mislight - so the fallback is not
        ///     defensive padding, it is what keeps a transient state from clearing the screen.
        /// </remarks>
        /// <param name="direction">The direction.</param>
        /// <returns>A unit vector.</returns>
        private static Vector3 SafeNormal(Vector3 direction)
        {
            return direction.LengthSquared() > 1E-12f ? Vector3.Normalize(direction) : Vector3.UnitY;
        }
    }
}
