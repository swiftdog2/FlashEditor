using FlashEditor.Definitions;
using OpenTK.Graphics.OpenGL;
using FlashEditor.Utils;

internal sealed class ModelRenderer {
    private int _vao, _vbo, _ebo, _indexCount;

    public void Load(ModelDefinition def) {
        DebugUtil.Debug($"[Load] Start loading ModelDefinition {def.ModelID}", DebugUtil.LOG_DETAIL.ADVANCED);

        float[] verts = new float[def.VertexCount * 3];
        for (int i = 0; i < def.VertexCount; i++) {
            verts[i * 3 + 0] = def.VertX[i] / 128f;
            verts[i * 3 + 1] = def.VertY[i] / 128f;
            verts[i * 3 + 2] = -def.VertZ[i] / 128f;
        }

        ushort[] idx = new ushort[def.TriangleCount * 3];
        for (int i = 0; i < def.TriangleCount; i++) {
            idx[i * 3 + 0] = (ushort)def.faceIndices1[i];
            idx[i * 3 + 1] = (ushort)def.faceIndices2[i];
            idx[i * 3 + 2] = (ushort)def.faceIndices3[i];
        }

        Load(verts, idx);

        DebugUtil.Debug($"Loaded ModelDefinition {def.ModelID} into OpenGL (VAO={_vao}, indices={_indexCount})", DebugUtil.LOG_DETAIL.ADVANCED);
    }

    public void Load(float[] vertices, ushort[] indices) {
        if (_vao != 0) {
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
            GL.DeleteVertexArray(_vao);
        }

        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ebo = GL.GenBuffer();

        GL.BindVertexArray(_vao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(0);
    }

    public void Draw() {
        if (_vao == 0)
            return;

        GL.BindVertexArray(_vao);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);
    }

    public void Dispose() {
        if (_vao != 0) {
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ebo);
            GL.DeleteVertexArray(_vao);
            _vao = _vbo = _ebo = 0;
        }
    }
}
