using System.Numerics;
using System;
using OpenTK.Graphics.OpenGL;

namespace FlashEditor.Rendering
{
    /// <summary>
    ///     Owns the GL buffers for the three overlays and draws them after the model.
    /// </summary>
    /// <remarks>
    ///     Everything here is untestable: <c>ModelRenderer</c> and the shaders are OpenGL, so a defect
    ///     on this path passes every test in the suite and has to be checked by eye. That is the reason
    ///     this type contains no arithmetic at all. Every position, colour and index it uploads was
    ///     computed by <see cref="OverlayGeometry"/> or <see cref="ParticleBillboards"/>, which are
    ///     testable; what is left here is buffer management and draw-state changes, and those are the
    ///     kind of mistake that is at least visible the moment you look at the viewport.
    ///     <para>
    ///     All three overlays share the model shader and its twelve-float vertex layout, so one
    ///     attribute binding serves them and no program switch happens between them.
    ///     </para>
    /// </remarks>
    internal sealed class ViewportOverlayRenderer : IDisposable
    {
        /// <summary>One overlay's GL objects and how much they currently hold.</summary>
        /// <remarks>
        ///     The capacities are tracked so an upload can use <c>BufferSubData</c> when the existing
        ///     allocation is big enough and only reallocate when it grows. The particle buffer is
        ///     rewritten every frame, so reallocating each time would churn driver memory at the
        ///     redraw rate.
        /// </remarks>
        private sealed class Buffers
        {
            /// <summary>Vertex array object, or 0 before it is created.</summary>
            public int VAO;

            /// <summary>Vertex buffer.</summary>
            public int VBO;

            /// <summary>Element (index) buffer.</summary>
            public int EBO;

            /// <summary>How many floats the vertex buffer is currently allocated for.</summary>
            public int VertexCapacity;

            /// <summary>How many indices the element buffer is currently allocated for.</summary>
            public int IndexCapacity;

            /// <summary>How many indices to draw. Zero hides the overlay without freeing anything.</summary>
            public int IndexCount;

            /// <summary>Creates the GL objects on first use.</summary>
            /// <remarks>
            ///     Lazy because the constructor may run before a GL context exists - the renderer is a
            ///     field of a control, and a control is constructed before it is realised.
            /// </remarks>
            public void Ensure()
            {
                if (VAO == 0)
                {
                    VAO = GL.GenVertexArray();
                    VBO = GL.GenBuffer();
                    EBO = GL.GenBuffer();
                }
            }

            /// <summary>Deletes the GL objects and forgets the capacities.</summary>
            public void Release()
            {
                if (VAO == 0)
                {
                    return;
                }

                GL.DeleteVertexArray(VAO);
                GL.DeleteBuffer(VBO);
                GL.DeleteBuffer(EBO);

                VAO = 0;
                VBO = 0;
                EBO = 0;
                VertexCapacity = 0;
                IndexCapacity = 0;
                IndexCount = 0;
            }
        }

        /// <summary>Floats in one vertex, matching the shader's layout.</summary>
        private const int FloatsPerVertex = OverlayGeometry.FloatsPerVertex;

        /// <summary>Bytes in one vertex, which is the attribute stride.</summary>
        private const int VertexStrideBytes = FloatsPerVertex * sizeof(float);

        /// <summary>The wireframe's buffers.</summary>
        private readonly Buffers wireframe = new Buffers();

        /// <summary>The hover highlight's buffers.</summary>
        private readonly Buffers highlight = new Buffers();

        /// <summary>The particle billboards' buffers.</summary>
        private readonly Buffers particles = new Buffers();

        /// <summary>A one-pixel white texture, or 0 before it is created.</summary>
        /// <remarks>
        ///     The shader always samples a texture, and the overlays have none. Binding white rather
        ///     than branching in the shader keeps the program single and the sample a no-op.
        /// </remarks>
        private int whiteTexture;

        /// <summary>Reused staging buffer for the particle vertices.</summary>
        /// <remarks>
        ///     Grown to the system's whole cap the first time it is too small rather than to what this
        ///     frame needs, because the particle count rises and a per-frame regrowth would allocate
        ///     on most frames of a starting effect.
        /// </remarks>
        private float[] particleVertices = Array.Empty<float>();

        /// <summary>Whether to draw the wireframe.</summary>
        public bool ShowWireframe { get; set; }

        /// <summary>Whether to draw the particles.</summary>
        public bool ShowParticles { get; set; } = true;

        /// <summary>How many wireframe lines were last uploaded, for the status bar.</summary>
        public int WireframeLineCount { get; private set; }

        /// <summary>How many particle quads were last uploaded, for the status bar.</summary>
        public int ParticleQuadCount { get; private set; }

        /// <summary>Whether a face is currently highlighted.</summary>
        public bool HasHighlight { get; private set; }

        /// <summary>Rebuilds and uploads the wireframe.</summary>
        /// <remarks>
        ///     A null or empty mesh zeroes the index count rather than releasing the buffers, so
        ///     switching models does not churn GL objects.
        /// </remarks>
        /// <param name="mesh">The pick mesh to outline, or null.</param>
        /// <param name="lightDirection">The current light direction.</param>
        public void SetWireframe(PickMesh? mesh, Vector3 lightDirection)
        {
            if (mesh == null || mesh.TriangleCount == 0)
            {
                WireframeLineCount = 0;
                wireframe.IndexCount = 0;
                return;
            }

            float[] vertices = OverlayGeometry.BuildWireframe(mesh, lightDirection, out uint[] indices);
            Upload(wireframe, vertices, indices);
            WireframeLineCount = indices.Length / 2;
        }

        /// <summary>Uploads the single triangle marking the hovered face.</summary>
        /// <param name="a">First corner, in world space.</param>
        /// <param name="b">Second corner.</param>
        /// <param name="c">Third corner.</param>
        /// <param name="lightDirection">The current light direction.</param>
        public void SetHighlight(Vector3 a, Vector3 b, Vector3 c, Vector3 lightDirection)
        {
            float[] vertices = OverlayGeometry.BuildHighlight(a, b, c, lightDirection);
            Upload(highlight, vertices, new uint[3] { 0u, 1u, 2u });
            HasHighlight = true;
        }

        /// <summary>Stops drawing the highlight.</summary>
        public void ClearHighlight()
        {
            HasHighlight = false;
            highlight.IndexCount = 0;
        }

        /// <summary>Rebuilds and uploads the particle billboards.</summary>
        /// <param name="system">The particle system, or null.</param>
        /// <param name="cameraRight">The camera's right axis, so the quads face the camera.</param>
        /// <param name="cameraUp">The camera's up axis.</param>
        /// <param name="lightDirection">The current light direction.</param>
        public void SetParticles(ParticleSystem? system, Vector3 cameraRight, Vector3 cameraUp, Vector3 lightDirection)
        {
            if (system == null || system.LiveParticleCount == 0)
            {
                ParticleQuadCount = 0;
                particles.IndexCount = 0;
                return;
            }

            int floatsNeeded = system.LiveParticleCount * ParticleBillboards.FloatsPerParticle;

            if (particleVertices.Length < floatsNeeded)
            {
                particleVertices = new float[system.MaximumParticles * ParticleBillboards.FloatsPerParticle];
            }

            int quads = ParticleBillboards.Build(system, cameraRight, cameraUp, lightDirection, particleVertices);
            uint[] indices = ParticleBillboards.BuildIndices(quads);

            //Only the used prefix of the staging buffer is uploaded; the rest is last frame's data.
            Upload(particles, particleVertices, indices, floatsNeeded);
            ParticleQuadCount = quads;
        }

        /// <summary>Draws whichever overlays are enabled and have anything in them.</summary>
        /// <remarks>
        ///     The order and the depth state are the whole of this method's content, and each choice
        ///     is deliberate.
        ///     <para>
        ///     <b>Wireframe with depth writes on</b>, so it occludes correctly against the model it is
        ///     drawn over - it is already biased towards the viewer by
        ///     <see cref="OverlayGeometry.WireframeDepthBiasWorldUnits"/>.
        ///     </para>
        ///     <para>
        ///     <b>Particles with depth writes off</b> but the test still on. They are translucent and
        ///     unsorted, so writing depth would make whichever quad happened to be drawn first hide
        ///     the ones behind it, and a dense effect would visibly punch holes in itself.
        ///     </para>
        ///     <para>
        ///     <b>Highlight with the depth test off entirely</b>, and drawn last. Picking is two-sided,
        ///     so the face under the cursor can be behind the rest of the model, and a highlight that
        ///     vanished on those faces would look like a picker that had failed rather than a face
        ///     that is round the back.
        ///     </para>
        ///     <para>
        ///     Every state change is undone before the method returns, so the next frame's model draw
        ///     starts where it expects to.
        ///     </para>
        /// </remarks>
        public void Draw()
        {
            EnsureWhiteTexture();

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, whiteTexture);

            if (ShowWireframe && wireframe.IndexCount > 0)
            {
                GL.DepthMask(flag: true);
                GL.BindVertexArray(wireframe.VAO);
                GL.DrawElements(PrimitiveType.Lines, wireframe.IndexCount, DrawElementsType.UnsignedInt, 0);
            }

            if (ShowParticles && particles.IndexCount > 0)
            {
                GL.DepthMask(flag: false);
                GL.BindVertexArray(particles.VAO);
                GL.DrawElements(PrimitiveType.Triangles, particles.IndexCount, DrawElementsType.UnsignedInt, 0);
                GL.DepthMask(flag: true);
            }

            if (HasHighlight && highlight.IndexCount > 0)
            {
                GL.Disable(EnableCap.DepthTest);
                GL.DepthMask(flag: false);
                GL.BindVertexArray(highlight.VAO);
                GL.DrawElements(PrimitiveType.Triangles, highlight.IndexCount, DrawElementsType.UnsignedInt, 0);
                GL.DepthMask(flag: true);
                GL.Enable(EnableCap.DepthTest);
            }

            GL.BindVertexArray(0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        /// <summary>Releases every GL object this renderer owns.</summary>
        /// <remarks>
        ///     Must be called on the thread that holds the GL context, which is why it is
        ///     <see cref="IDisposable"/> and not a finaliser - a finaliser runs on the collector's
        ///     thread, where these handles mean nothing and deleting them is undefined.
        /// </remarks>
        public void Dispose()
        {
            wireframe.Release();
            highlight.Release();
            particles.Release();

            if (whiteTexture != 0)
            {
                GL.DeleteTexture(whiteTexture);
                whiteTexture = 0;
            }

            WireframeLineCount = 0;
            ParticleQuadCount = 0;
            HasHighlight = false;
        }

        /// <summary>Uploads vertices and indices, growing the buffers only when they are too small.</summary>
        /// <param name="target">Which overlay's buffers.</param>
        /// <param name="vertices">The staging buffer.</param>
        /// <param name="indices">The indices.</param>
        /// <param name="vertexFloats">
        ///     How much of <paramref name="vertices"/> is live, or -1 for all of it. The particle
        ///     staging buffer is sized for the cap and only partly filled.
        /// </param>
        private static void Upload(Buffers target, float[] vertices, uint[] indices, int vertexFloats = -1)
        {
            int floats = vertexFloats < 0 ? vertices.Length : vertexFloats;

            target.Ensure();
            GL.BindVertexArray(target.VAO);

            GL.BindBuffer(BufferTarget.ArrayBuffer, target.VBO);

            if (floats > target.VertexCapacity)
            {
                GL.BufferData(BufferTarget.ArrayBuffer, floats * sizeof(float), vertices,
                    BufferUsageHint.DynamicDraw);
                target.VertexCapacity = floats;
            }
            else
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, floats * sizeof(float), vertices);
            }

            //Inside the bound vertex array object, so the attribute layout is recorded against it and
            //Draw only has to bind the one object.
            BindAttributes();

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, target.EBO);

            if (indices.Length > target.IndexCapacity)
            {
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices,
                    BufferUsageHint.DynamicDraw);
                target.IndexCapacity = indices.Length;
            }
            else
            {
                GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indices.Length * sizeof(uint), indices);
            }

            target.IndexCount = indices.Length;
            GL.BindVertexArray(0);
        }

        /// <summary>Declares the model shader's vertex layout on the currently bound buffer.</summary>
        /// <remarks>
        ///     The offsets mirror <see cref="OverlayGeometry.Write"/> and the locations mirror
        ///     <c>Shaders/texture.vert</c>. Nothing can check that agreement at build time, and a
        ///     mismatch does not fail - it draws the colour as a position.
        /// </remarks>
        private static void BindAttributes()
        {
            const int PositionOffset = 0;
            const int NormalOffset = 3 * sizeof(float);
            const int TexCoordOffset = 6 * sizeof(float);
            const int AlphaOffset = 8 * sizeof(float);
            const int ColourOffset = 9 * sizeof(float);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, VertexStrideBytes,
                PositionOffset);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, normalized: false, VertexStrideBytes,
                NormalOffset);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, normalized: false, VertexStrideBytes,
                TexCoordOffset);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, normalized: false, VertexStrideBytes,
                AlphaOffset);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, normalized: false, VertexStrideBytes,
                ColourOffset);
        }

        /// <summary>Creates the one-pixel white texture on first use.</summary>
        /// <remarks>
        ///     Nearest filtering, because a one-pixel texture has nothing to interpolate and asking for
        ///     mipmaps on one without supplying them leaves it incomplete and sampling black.
        /// </remarks>
        private void EnsureWhiteTexture()
        {
            if (whiteTexture != 0)
            {
                return;
            }

            const int Nearest = 9728;

            whiteTexture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, whiteTexture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, Nearest);

            byte[] pixels = new byte[4] { 255, 255, 255, 255 };
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba,
                PixelType.UnsignedByte, pixels);

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }
    }
}
