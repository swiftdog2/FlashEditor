using System.Numerics;
using System;
using System.Collections.Generic;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     A consecutive span of quads that share one material, and so one bound texture.
    /// </summary>
    /// <remarks>
    ///     The client batches particles exactly this way. <c>Class360.java:411-424</c> walks the
    ///     particle list and breaks the batch the moment <c>anInt6180</c>, the particle's material
    ///     id, differs from the one the batch opened with; <c>:440-443</c> then binds that material
    ///     through <c>RenderType_Sub1.method1834</c> and draws. It does <b>not</b> sort by material,
    ///     and neither does this - the quads are translucent and drawn with depth writes off, so
    ///     reordering them reorders the blend and changes the picture. More draw calls is the price
    ///     of keeping the order the simulation produced.
    /// </remarks>
    /// <param name="MaterialId">
    ///     The material every quad in the run names, or <see cref="ParticleMaterialRun.NoMaterial"/>
    ///     when the emitter declared none.
    /// </param>
    /// <param name="FirstQuad">Index of the run's first quad within the frame's buffer.</param>
    /// <param name="QuadCount">How many quads the run holds.</param>
    public readonly record struct ParticleMaterialRun(int MaterialId, int FirstQuad, int QuadCount)
    {
        /// <summary>The material id an emitter with no opcode 15 leaves on its particles.</summary>
        /// <remarks>
        ///     Matches <c>ParticleEmitterDefinition.NoMaterial</c>. A run carrying it has no texture
        ///     to bind and falls back to the flat white one, which is the pre-existing appearance
        ///     rather than a new failure mode.
        /// </remarks>
        public const int NoMaterial = -1;
    }

    /// <summary>
    ///     Turns live particles into camera-facing quads in the model shader's vertex layout.
    /// </summary>
    /// <remarks>
    ///     A transcription of what the client's GL renderer does at <c>Class360.java:105-177</c>. It
    ///     reads back the modelview matrix (<c>:109</c>, <c>glGetFloatv(GL_MODELVIEW_MATRIX)</c>),
    ///     takes its first two rows as the camera's right and up axes (<c>:110-115</c>), and builds
    ///     the four corner offsets by adding and subtracting them (<c>:116-125</c>). That is why no
    ///     particle carries a rotation: the quad is built in camera axes, so it faces the camera at
    ///     every angle by construction.
    ///     <para>
    ///     This takes the two axes as arguments rather than reading GL itself, which keeps the whole
    ///     of it testable and lets the caller supply the axes it already has.
    ///     </para>
    /// </remarks>
    public static class ParticleBillboards
    {
        /// <summary>Corners per particle.</summary>
        public const int VerticesPerParticle = 4;

        /// <summary>Indices per particle: two triangles sharing the diagonal.</summary>
        public const int IndicesPerParticle = 6;

        /// <summary>Floats one particle occupies in the vertex buffer.</summary>
        public const int FloatsPerParticle = VerticesPerParticle * OverlayGeometry.FloatsPerVertex;

        /// <summary>
        ///     Builds the index buffer for a given number of particles.
        /// </summary>
        /// <remarks>
        ///     Depends only on the count, never on the particles, so a caller can build it once for
        ///     the cap and reuse it. Winding is 0-1-2 then 0-2-3 - bottom left, bottom right, top
        ///     right, top left - which matches the order <see cref="Build"/> writes the corners in.
        /// </remarks>
        /// <param name="capacity">How many particles to index.</param>
        /// <returns>Triangle-list indices.</returns>
        public static uint[] BuildIndices(int capacity)
        {
            uint[] indices = new uint[capacity * IndicesPerParticle];

            for (int particle = 0; particle < capacity; particle++)
            {
                uint firstVertex = (uint)(particle * VerticesPerParticle);
                int offset = particle * IndicesPerParticle;

                indices[offset] = firstVertex;
                indices[offset + 1] = firstVertex + 1;
                indices[offset + 2] = firstVertex + 2;
                indices[offset + 3] = firstVertex;
                indices[offset + 4] = firstVertex + 2;
                indices[offset + 5] = firstVertex + 3;
            }

            return indices;
        }

        /// <summary>Writes every live particle of a system into a vertex buffer as a quad.</summary>
        /// <param name="system">The system to read live particles from.</param>
        /// <param name="cameraRight">The camera's right axis, unit length, in world space.</param>
        /// <param name="cameraUp">The camera's up axis, unit length, in world space.</param>
        /// <param name="lightDirection">
        ///     The current light direction, written as every corner's normal so the shader's lighting
        ///     term is a known constant and the spawn colour survives to the screen. See
        ///     <see cref="OverlayGeometry.Unlit"/>.
        /// </param>
        /// <param name="buffer">
        ///     The destination, at least <see cref="FloatsPerParticle"/> per live particle. Supplied by
        ///     the caller and reused, because this runs every frame and the cap is 2047 quads.
        /// </param>
        /// <param name="runs">
        ///     Cleared and refilled with one entry per consecutive span of quads sharing a material,
        ///     in draw order. Null when the caller does not batch, in which case every quad is still
        ///     written and only the grouping is skipped.
        /// </param>
        /// <returns>How many particles were written.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="system"/> or <paramref name="buffer"/> is null.</exception>
        /// <exception cref="ArgumentException">The buffer is too small.</exception>
        public static int Build(ParticleSystem system, Vector3 cameraRight, Vector3 cameraUp, Vector3 lightDirection,
            float[] buffer, List<ParticleMaterialRun>? runs = null)
        {
            if (system == null)
            {
                throw new ArgumentNullException(nameof(system));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            int liveParticleCount = system.LiveParticleCount;

            //Refused rather than truncated. A short buffer means the caller sized it from a stale
            //count, and drawing the first n particles would hide that.
            if (buffer.Length < liveParticleCount * FloatsPerParticle)
            {
                throw new ArgumentException(
                    "The buffer holds " + buffer.Length / FloatsPerParticle + " particles and "
                    + liveParticleCount + " are alive.", nameof(buffer));
            }

            Vector3 normal = lightDirection.LengthSquared() > 1E-12f
                ? Vector3.Normalize(lightDirection)
                : Vector3.UnitY;

            runs?.Clear();
            int runMaterial = 0;
            int runStart = 0;

            for (int index = 0; index < liveParticleCount; index++)
            {
                Particle particle = system.ParticleAt(index);

                if (runs != null)
                {
                    if (index == 0)
                    {
                        runMaterial = particle.MaterialId;
                    }
                    else if (particle.MaterialId != runMaterial)
                    {
                        runs.Add(new ParticleMaterialRun(runMaterial, runStart, index - runStart));
                        runMaterial = particle.MaterialId;
                        runStart = index;
                    }
                }

                //Positions are stored in twelfths of a model unit, so they shift down before the
                //conversion into world space.
                Vector3 centre = RenderSpace.ToWorld(
                    particle.X >> ParticleUnits.PositionFractionBits,
                    particle.Y >> ParticleUnits.PositionFractionBits,
                    particle.Z >> ParticleUnits.PositionFractionBits);

                //Size is the quad's half extent, not its width - the corners are the centre plus and
                //minus it on both axes. Stored shifted up 14 at load and down 12 here, which leaves a
                //net factor of four (Class360.java:141 takes the same shift).
                float halfExtent = (particle.Size >> ParticleUnits.SizeFractionBits)
                    / RenderSpace.ModelUnitsPerWorldUnit;

                Vector3 right = cameraRight * halfExtent;
                Vector3 up = cameraUp * halfExtent;

                Vector3 colour = OverlayGeometry.Unlit(new Vector3(
                    particle.Red / 255f,
                    particle.Green / 255f,
                    particle.Blue / 255f));

                float opacity = particle.Alpha / 255f;

                int firstVertex = index * VerticesPerParticle;
                WriteCorner(buffer, firstVertex, centre - right - up, normal, colour, opacity, 0f, 0f);
                WriteCorner(buffer, firstVertex + 1, centre + right - up, normal, colour, opacity, 1f, 0f);
                WriteCorner(buffer, firstVertex + 2, centre + right + up, normal, colour, opacity, 1f, 1f);
                WriteCorner(buffer, firstVertex + 3, centre - right + up, normal, colour, opacity, 0f, 1f);
            }

            //The last run is closed here rather than in the loop, because nothing inside the loop
            //can see that it has ended. Omitting this drops the final material's quads from the
            //draw entirely, and with one emitter alive that is every quad on screen.
            if (runs != null && liveParticleCount > 0)
                runs.Add(new ParticleMaterialRun(runMaterial, runStart, liveParticleCount - runStart));

            return liveParticleCount;
        }

        /// <summary>Writes one quad corner, with its texture coordinate.</summary>
        /// <remarks>
        ///     Delegates to <see cref="OverlayGeometry.Write"/> and then overwrites the uv pair it
        ///     zeroed, rather than duplicating the layout here. The particles are the only thing in
        ///     this file that has a real texture coordinate; the wireframe and the highlight sample a
        ///     single white pixel.
        /// </remarks>
        /// <param name="buffer">The interleaved buffer.</param>
        /// <param name="vertex">Which vertex of it.</param>
        /// <param name="position">World-space corner position.</param>
        /// <param name="normal">The light direction.</param>
        /// <param name="colour">Colour, already divided by the shader's lighting.</param>
        /// <param name="opacity">Alpha, 0 to 1.</param>
        /// <param name="u">Texture u.</param>
        /// <param name="v">Texture v.</param>
        private static void WriteCorner(float[] buffer, int vertex, Vector3 position, Vector3 normal, Vector3 colour,
            float opacity, float u, float v)
        {
            OverlayGeometry.Write(buffer, vertex, position, normal, colour, opacity);

            int offset = vertex * OverlayGeometry.FloatsPerVertex;
            buffer[offset + 6] = u;
            buffer[offset + 7] = v;
        }
    }
}
