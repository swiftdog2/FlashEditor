using FlashEditor.Definitions;
using OpenTK.Graphics.OpenGL;

internal sealed class ModelRenderer
{
    private int _vao, _vbo, _ibo, _indexCount;

    public void Load(ModelDefinition m)
    {
        // dispose previous
        if (_vao != 0)
        {
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ibo);
            GL.DeleteVertexArray(_vao);
        }

        float[] vertices = new float[m.VertexCount * 3];
        for (int i = 0; i < m.VertexCount; i++)
        {
            vertices[i * 3 + 0] = m.VertX[i] / 128f;
            vertices[i * 3 + 1] = m.VertY[i] / 128f;
            vertices[i * 3 + 2] = -m.VertZ[i] / 128f;
        }

        ushort[] indices = new ushort[m.TriangleCount * 3];
        for (int i = 0; i < m.TriangleCount; i++)
        {
            indices[i * 3 + 0] = m.IndA[i];
            indices[i * 3 + 1] = m.IndB[i];
            indices[i * 3 + 2] = m.IndC[i];
        }

        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ibo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * 4, vertices, BufferUsageHint.StaticDraw);
        GL.EnableClientState(ArrayCap.VertexArray);
        GL.VertexPointer(3, VertexPointerType.Float, 0, 0);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * 2, indices, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(0);
    }

    public void Draw()
    {
        if (_vao == 0) return;
        GL.BindVertexArray(_vao);
        GL.DrawElements(BeginMode.Triangles, _indexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);
    }
}
