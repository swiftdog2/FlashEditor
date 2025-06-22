using FlashEditor.Definitions;
using OpenTK.Graphics.OpenGL;
using FlashEditor.Utils;

internal sealed class ModelRenderer {
    private int _vao, _vbo, _ibo, _indexCount;

    public void Load(ModelDefinition m) {
        DebugUtil.Debug($"[Load] Start loading ModelDefinition {m.ModelID}", DebugUtil.LOG_DETAIL.ADVANCED);

        // dispose previous
        if (_vao != 0) {
            DebugUtil.Debug($"[Load] Disposing previous VAO({_vao}), VBO({_vbo}), IBO({_ibo})", DebugUtil.LOG_DETAIL.ADVANCED);
            GL.DeleteBuffer(_vbo);
            GL.DeleteBuffer(_ibo);
            GL.DeleteVertexArray(_vao);
        }

        DebugUtil.Debug($"[Load] Allocating vertices array for {m.VertexCount} vertices", DebugUtil.LOG_DETAIL.ADVANCED);
        float[] vertices = new float[m.VertexCount * 3];
        for (int i = 0 ; i < m.VertexCount ; i++) {
            vertices[i * 3 + 0] = m.VertX[i] / 128f;
            vertices[i * 3 + 1] = m.VertY[i] / 128f;
            vertices[i * 3 + 2] = -m.VertZ[i] / 128f;
        }
        DebugUtil.Debug($"[Load] Finished filling vertices ({vertices.Length} floats)", DebugUtil.LOG_DETAIL.ADVANCED);

        DebugUtil.Debug($"[Load] Allocating indices array for {m.TriangleCount} triangles", DebugUtil.LOG_DETAIL.ADVANCED);
        ushort[] indices = new ushort[m.TriangleCount * 3];
        for (int i = 0 ; i < m.TriangleCount ; i++) {
            indices[i * 3 + 0] = (ushort) m.faceIndices1[i];
            indices[i * 3 + 1] = (ushort) m.faceIndices2[i];
            indices[i * 3 + 2] = (ushort) m.faceIndices3[i];
        }
        _indexCount = indices.Length;
        DebugUtil.Debug($"[Load] Finished filling indices (count={_indexCount})", DebugUtil.LOG_DETAIL.ADVANCED);

        DebugUtil.Debug("[Load] Generating VAO, VBO, and IBO", DebugUtil.LOG_DETAIL.ADVANCED);
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        _ibo = GL.GenBuffer();
        DebugUtil.Debug($"[Load] Generated VAO({_vao}), VBO({_vbo}), IBO({_ibo})", DebugUtil.LOG_DETAIL.ADVANCED);

        DebugUtil.Debug($"[Load] Binding VAO({_vao})", DebugUtil.LOG_DETAIL.ADVANCED);
        GL.BindVertexArray(_vao);

        DebugUtil.Debug($"[Load] Binding and uploading vertex buffer ({vertices.Length * sizeof(float)} bytes)", DebugUtil.LOG_DETAIL.ADVANCED);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableClientState(ArrayCap.VertexArray);
        GL.VertexPointer(3, VertexPointerType.Float, 0, 0);

        DebugUtil.Debug($"[Load] Binding and uploading index buffer ({indices.Length * sizeof(ushort)} bytes)", DebugUtil.LOG_DETAIL.ADVANCED);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ibo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(ushort), indices, BufferUsageHint.StaticDraw);

        GL.BindVertexArray(0);
        DebugUtil.Debug($"[Load] Unbound VAO, setup complete", DebugUtil.LOG_DETAIL.ADVANCED);

        DebugUtil.Debug($"Loaded ModelDefinition {m.ModelID} into OpenGL (VAO={_vao}, indices={_indexCount})", DebugUtil.LOG_DETAIL.ADVANCED);
    }

    public void Draw() {
        if (_vao == 0) {
            DebugUtil.Debug("[Draw] No VAO generated yet, skipping draw", DebugUtil.LOG_DETAIL.ADVANCED);
            return;
        }

        DebugUtil.Debug($"[Draw] Binding VAO({_vao}) and drawing {_indexCount} indices", DebugUtil.LOG_DETAIL.ADVANCED);
        GL.BindVertexArray(_vao);
        GL.DrawElements(BeginMode.Triangles, _indexCount, DrawElementsType.UnsignedShort, 0);
        GL.BindVertexArray(0);
        DebugUtil.Debug("[Draw] Unbound VAO after draw", DebugUtil.LOG_DETAIL.ADVANCED);
    }
}
