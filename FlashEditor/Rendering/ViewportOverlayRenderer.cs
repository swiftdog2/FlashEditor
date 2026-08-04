using System.Numerics;
using System;
using OpenTK.Graphics.OpenGL;

namespace FlashEditor.Rendering
{
    internal sealed class ViewportOverlayRenderer : IDisposable
    {
        private sealed class Buffers
        {
            public int VAO;

            public int VBO;

            public int EBO;

            public int VertexCapacity;

            public int IndexCapacity;

            public int IndexCount;

            public void Ensure()
            {
                if (VAO == 0)
                {
                    VAO = GL.GenVertexArray();
                    VBO = GL.GenBuffer();
                    EBO = GL.GenBuffer();
                }
            }

            public void Release()
            {
                if (VAO != 0)
                {
                    GL.DeleteVertexArray(VAO);
                    GL.DeleteBuffer(VBO);
                    GL.DeleteBuffer(EBO);
                    VAO = (VBO = (EBO = 0));
                    VertexCapacity = (IndexCapacity = (IndexCount = 0));
                }
            }
        }

        private readonly Buffers wireframe = new Buffers();

        private readonly Buffers highlight = new Buffers();

        private readonly Buffers particles = new Buffers();

        private int whiteTexture;

        private float[] particleVertices = Array.Empty<float>();

        public bool ShowWireframe { get; set; }

        public bool ShowParticles { get; set; } = true;


        public int WireframeLineCount { get; private set; }

        public int ParticleQuadCount { get; private set; }

        public bool HasHighlight { get; private set; }

        public void SetWireframe(PickMesh? mesh, Vector3 lightDirection)
        {
            if (mesh == null || mesh.TriangleCount == 0)
            {
                WireframeLineCount = 0;
                wireframe.IndexCount = 0;
            }
            else
            {
                uint[] indices;
                float[] vertices = OverlayGeometry.BuildWireframe(mesh, lightDirection, out indices);
                Upload(wireframe, vertices, indices);
                WireframeLineCount = indices.Length / 2;
            }
        }

        public void SetHighlight(Vector3 a, Vector3 b, Vector3 c, Vector3 lightDirection)
        {
            float[] vertices = OverlayGeometry.BuildHighlight(a, b, c, lightDirection);
            Upload(highlight, vertices, new uint[3] { 0u, 1u, 2u });
            HasHighlight = true;
        }

        public void ClearHighlight()
        {
            HasHighlight = false;
            highlight.IndexCount = 0;
        }

        public void SetParticles(ParticleSystem? system, Vector3 cameraRight, Vector3 cameraUp, Vector3 lightDirection)
        {
            if (system == null || system.LiveParticleCount == 0)
            {
                ParticleQuadCount = 0;
                particles.IndexCount = 0;
                return;
            }
            int num = system.LiveParticleCount * 48;
            if (particleVertices.Length < num)
            {
                particleVertices = new float[system.MaximumParticles * 48];
            }
            int num2 = ParticleBillboards.Build(system, cameraRight, cameraUp, lightDirection, particleVertices);
            uint[] indices = ParticleBillboards.BuildIndices(num2);
            Upload(particles, particleVertices, indices, num);
            ParticleQuadCount = num2;
        }

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

        private static void Upload(Buffers target, float[] vertices, uint[] indices, int vertexFloats = -1)
        {
            int num = ((vertexFloats < 0) ? vertices.Length : vertexFloats);
            target.Ensure();
            GL.BindVertexArray(target.VAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, target.VBO);
            if (num > target.VertexCapacity)
            {
                GL.BufferData(BufferTarget.ArrayBuffer, num * 4, vertices, BufferUsageHint.DynamicDraw);
                target.VertexCapacity = num;
            }
            else
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, num * 4, vertices);
            }
            BindAttributes();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, target.EBO);
            if (indices.Length > target.IndexCapacity)
            {
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * 4, indices, BufferUsageHint.DynamicDraw);
                target.IndexCapacity = indices.Length;
            }
            else
            {
                GL.BufferSubData(BufferTarget.ElementArrayBuffer, IntPtr.Zero, indices.Length * 4, indices);
            }
            target.IndexCount = indices.Length;
            GL.BindVertexArray(0);
        }

        private static void BindAttributes()
        {
            int stride = 48;
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, normalized: false, stride, 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, normalized: false, stride, 12);
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, normalized: false, stride, 24);
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, normalized: false, stride, 32);
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, normalized: false, stride, 36);
        }

        private void EnsureWhiteTexture()
        {
            if (whiteTexture == 0)
            {
                whiteTexture = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, whiteTexture);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9728);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9728);
                byte[] pixels = new byte[4] { 255, 255, 255, 255 };
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                GL.BindTexture(TextureTarget.Texture2D, 0);
            }
        }
    }
}
