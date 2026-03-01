using FlashEditor.Definitions;
using OpenTK.Graphics.OpenGL;
using FlashEditor.Utils;
using FlashEditor;
using System.Collections.Generic;

internal sealed class ModelRenderer
{
    private readonly List<Batch> _batches = new();

    private class Batch
    {
        public int VAO;
        public int VBO;
        public int EBO;
        public int IndexCount;
        public int Texture;
    }

    public void Load(ModelDefinition def, GLTextureCache textures)
    {
        Dispose();
        if (def.FaceTextureUCoordinates == null || def.FaceTextureVCoordinates == null)
            return;

        var groups = new Dictionary<int, List<float>>();
        var indices = new Dictionary<int, List<ushort>>();
        int vertIndex = 0;

        for (int i = 0; i < def.TriangleCount; i++)
        {
            int texId = def.FaceTextures == null ? -1 : def.FaceTextures[i];
            if (!groups.TryGetValue(texId, out var vList))
            {
                vList = new List<float>();
                groups[texId] = vList;
                indices[texId] = new List<ushort>();
            }

            int a = def.faceIndices1[i];
            int b = def.faceIndices2[i];
            int c = def.faceIndices3[i];

            float[] u = def.FaceTextureUCoordinates[i];
            float[] v = def.FaceTextureVCoordinates[i];

            AppendVertex(vList, def, a, u[0], v[0]);
            indices[texId].Add((ushort)vertIndex++);
            AppendVertex(vList, def, b, u[1], v[1]);
            indices[texId].Add((ushort)vertIndex++);
            AppendVertex(vList, def, c, u[2], v[2]);
            indices[texId].Add((ushort)vertIndex++);
        }

        foreach (var kvp in groups)
        {
            float[] verts = kvp.Value.ToArray();
            ushort[] idx = indices[kvp.Key].ToArray();

            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(ushort), idx, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);

            _batches.Add(new Batch
            {
                VAO = vao,
                VBO = vbo,
                EBO = ebo,
                IndexCount = idx.Length,
                Texture = kvp.Key == -1 ? 0 : textures.GetTexture(kvp.Key)
            });
        }
    }

    public void LoadSimple(float[] vertices, ushort[] indices, int texture)
    {
        Dispose();

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);
        GL.BindVertexArray(0);

        _batches.Add(new Batch
        {
            VAO = vao,
            VBO = vbo,
            EBO = ebo,
            IndexCount = indices.Length,
            Texture = texture
        });
    }

    private static void AppendVertex(List<float> list, ModelDefinition def, int vert, float u, float v)
    {
        list.Add(def.VertX[vert] / 128f);
        list.Add(def.VertY[vert] / 128f);
        list.Add(-def.VertZ[vert] / 128f);
        list.Add(u);
        list.Add(v);
    }

    public void Draw()
    {
        foreach (var b in _batches)
        {
            if (b.Texture != 0)
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, b.Texture);
            }
            GL.BindVertexArray(b.VAO);
            GL.DrawElements(PrimitiveType.Triangles, b.IndexCount, DrawElementsType.UnsignedShort, 0);
        }
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        foreach (var b in _batches)
        {
            GL.DeleteBuffer(b.VBO);
            GL.DeleteBuffer(b.EBO);
            GL.DeleteVertexArray(b.VAO);
        }
        _batches.Clear();
    }
}
