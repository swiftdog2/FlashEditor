using FlashEditor.Definitions;
using FlashEditor.Definitions.Sprites;
using OpenTK.Graphics.OpenGL;
using FlashEditor.Utils;
using FlashEditor;
using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class ModelRenderer
{
    private readonly List<Batch> _opaqueBatches = new();
    private readonly List<Batch> _translucentBatches = new();
    private readonly List<int> _ownedTextures = new();
    private int _whiteTexture;

    private const int Stride = 12; // pos(3) + normal(3) + uv(2) + alpha(1) + colour(3)

    private class Batch
    {
        public int VAO;
        public int VBO;
        public int EBO;
        public int IndexCount;
        public int Texture;
        public TextureDefinition? TexDef;
    }

    /// <summary>
    /// Lazily creates or returns a shared 1x1 white texture for colour-only faces.
    /// </summary>
    private int GetWhiteTexture()
    {
        if (_whiteTexture != 0) return _whiteTexture;
        _whiteTexture = CreateSolidTexture(255, 255, 255);
        return _whiteTexture;
    }

    public void Load(ModelDefinition def, GLTextureCache textures)
    {
        Dispose();
        LoadInternal(new[] { def }, textures);
    }

    public void LoadMultiple(IList<ModelDefinition> defs, GLTextureCache textures)
    {
        Dispose();
        LoadInternal(defs, textures);
    }

    private void LoadInternal(IList<ModelDefinition> defs, GLTextureCache textures)
    {
        // Collect all faces with sort keys
        var allFaces = new List<(ModelDefinition def, int faceIdx, int[][] colours, float[][] normals)>();

        foreach (var def in defs)
        {
            int[][] vertColours = def.ComputeUnlitColours();
            float[][] vertNormals = def.ComputeFaceVertexNormals();
            for (int i = 0; i < def.TriangleCount; i++)
                allFaces.Add((def, i, vertColours, vertNormals));
        }

        // Sort faces: opaque first, then translucent; within each group, ascending priority
        allFaces.Sort((a, b) => {
            bool aTranslucent = IsTranslucent(a.def, a.faceIdx);
            bool bTranslucent = IsTranslucent(b.def, b.faceIdx);
            if (aTranslucent != bTranslucent)
                return aTranslucent ? 1 : -1;
            int aPri = a.def.GetFacePriority(a.faceIdx);
            int bPri = b.def.GetFacePriority(b.faceIdx);
            return aPri.CompareTo(bPri);
        });

        // Group sorted faces into batches by texture key
        // We use an ordered approach: accumulate faces into batches, starting a new batch
        // when the texture key changes or when we cross the opaque/translucent boundary.
        var groups = new List<(int key, bool translucent, List<float> verts, List<uint> indices, int vertCount)>();

        foreach (var (def, faceIdx, colours, normals) in allFaces)
        {
            int a = def.faceIndices1[faceIdx];
            int b = def.faceIndices2[faceIdx];
            int c = def.faceIndices3[faceIdx];

            if ((uint)a >= (uint)def.VertexCount ||
                (uint)b >= (uint)def.VertexCount ||
                (uint)c >= (uint)def.VertexCount)
                continue;

            float u0, u1, u2, v0, v1, v2;
            int key;

            bool hasUV = def.FaceTextureUCoordinates != null &&
                         def.FaceTextureVCoordinates != null &&
                         def.FaceTextureUCoordinates[faceIdx] != null &&
                         def.FaceTextureVCoordinates[faceIdx] != null;

            if (hasUV)
            {
                // hasUV already null-tested both arrays; the compiler cannot
                // carry that state through the bool local.
                float[] uArr = def.FaceTextureUCoordinates![faceIdx];
                float[] vArr = def.FaceTextureVCoordinates![faceIdx];
                u0 = uArr[0]; u1 = uArr[1]; u2 = uArr[2];
                v0 = vArr[0]; v1 = vArr[1]; v2 = vArr[2];
                int texId = def.FaceTextures == null ? -1 : def.FaceTextures[faceIdx];
                key = texId >= 0 ? texId : -1; // -1 = white texture (colour-only)
            }
            else
            {
                u0 = 0; u1 = 0; u2 = 0;
                v0 = 0; v1 = 0; v2 = 0;
                key = -1; // colour-only: use white texture
            }

            float alpha = 1.0f;
            if (def.FaceAlpha != null)
                alpha = (255 - (def.FaceAlpha[faceIdx] & 0xFF)) / 255f;

            bool translucent = IsTranslucent(def, faceIdx);

            // Get vertex colours from pre-computed array
            int rgb0 = colours[faceIdx][0];
            int rgb1 = colours[faceIdx][1];
            int rgb2 = colours[faceIdx][2];

            // Find or create the right group
            var group = groups.Count > 0 ? groups[^1] : default;
            if (groups.Count == 0 || group.key != key || group.translucent != translucent)
            {
                group = (key, translucent, new List<float>(), new List<uint>(), 0);
                groups.Add(group);
            }

            float[] fn = normals[faceIdx];
            int vi = group.vertCount;
            AppendVertex(group.verts, def, a, fn[0], fn[1], fn[2], u0, v0, alpha, rgb0);
            group.indices.Add((uint)vi++);
            AppendVertex(group.verts, def, b, fn[3], fn[4], fn[5], u1, v1, alpha, rgb1);
            group.indices.Add((uint)vi++);
            AppendVertex(group.verts, def, c, fn[6], fn[7], fn[8], u2, v2, alpha, rgb2);
            group.indices.Add((uint)vi++);

            // Update vertCount in the list (structs are value types)
            groups[^1] = (group.key, group.translucent, group.verts, group.indices, vi);
        }

        // Build GPU batches
        foreach (var (key, translucent, vertList, idxList, _) in groups)
        {
            float[] verts = vertList.ToArray();
            uint[] idx = idxList.ToArray();
            if (idx.Length == 0) continue;

            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.StaticDraw);

            // location 0: position (3 floats)
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 0);
            // location 1: normal (3 floats)
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 3 * sizeof(float));
            // location 2: UV (2 floats)
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Stride * sizeof(float), 6 * sizeof(float));
            // location 3: alpha (1 float)
            GL.EnableVertexAttribArray(3);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, Stride * sizeof(float), 8 * sizeof(float));
            // location 4: colour (3 floats)
            GL.EnableVertexAttribArray(4);
            GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 9 * sizeof(float));

            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * sizeof(uint), idx, BufferUsageHint.StaticDraw);
            GL.BindVertexArray(0);

            int texture;
            TextureDefinition? texDef = null;
            if (key >= 0)
            {
                texture = textures.GetTexture(key);
                if (texture == 0)
                    texture = GetWhiteTexture(); // definition missing — use white so vertex lighting is visible
                if (TextureManager.Textures.TryGetValue(key, out var td))
                    texDef = td;
            }
            else
            {
                texture = GetWhiteTexture();
            }

            var batch = new Batch
            {
                VAO = vao,
                VBO = vbo,
                EBO = ebo,
                IndexCount = idx.Length,
                Texture = texture,
                TexDef = texDef
            };

            if (translucent)
                _translucentBatches.Add(batch);
            else
                _opaqueBatches.Add(batch);
        }
    }

    private static bool IsTranslucent(ModelDefinition def, int faceIdx)
    {
        if (def.FaceAlpha == null) return false;
        int a = def.FaceAlpha[faceIdx] & 0xFF;
        return a > 0; // 0 = fully opaque, >0 = some transparency
    }

    private static int CreateSolidTexture(byte r, byte g, byte b)
    {
        int tex = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        byte[] pixel = { r, g, b, 255 };
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, 1, 1, 0,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        return tex;
    }

    public void LoadSimple(float[] vertices, uint[] indices, int texture)
    {
        Dispose();

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Stride * sizeof(float), 6 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, Stride * sizeof(float), 8 * sizeof(float));
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 9 * sizeof(float));

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);
        GL.BindVertexArray(0);

        _opaqueBatches.Add(new Batch
        {
            VAO = vao,
            VBO = vbo,
            EBO = ebo,
            IndexCount = indices.Length,
            Texture = texture
        });
    }

    private static void AppendVertex(List<float> list, ModelDefinition def, int vert,
        float nx, float ny, float nz,
        float u, float v, float alpha, int rgb)
    {
        list.Add(def.VertX[vert] / 128f);
        list.Add(-def.VertY[vert] / 128f);
        list.Add(-def.VertZ[vert] / 128f);
        list.Add(nx);
        list.Add(ny);
        list.Add(nz);
        list.Add(u);
        list.Add(v);
        list.Add(alpha);
        list.Add(((rgb >> 16) & 0xFF) / 255f);
        list.Add(((rgb >> 8) & 0xFF) / 255f);
        list.Add((rgb & 0xFF) / 255f);
    }

    /// <summary>
    /// Draws all batches with two-pass rendering: opaque first, then translucent
    /// with depth writes disabled to prevent depth-fighting.
    /// </summary>
    public void Draw(float elapsedSeconds = 0f, int uTexOffsetLoc = -1)
    {
        // Pass 1: opaque batches with full depth writes
        GL.DepthMask(true);
        foreach (var b in _opaqueBatches)
            DrawBatch(b, elapsedSeconds, uTexOffsetLoc);

        // Pass 2: translucent batches with depth writes disabled
        GL.DepthMask(false);
        foreach (var b in _translucentBatches)
            DrawBatch(b, elapsedSeconds, uTexOffsetLoc);

        GL.DepthMask(true);
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    private static void DrawBatch(Batch b, float elapsedSeconds, int uTexOffsetLoc)
    {
        // UV animation offset — Hydra columnar texture metadata doesn't
        // expose animation speed/direction directly; those live in the
        // per-texture operation graph (index 9).  Static for now.
        float uOff = 0f, vOff = 0f;

        if (uTexOffsetLoc >= 0)
            GL.Uniform2(uTexOffsetLoc, uOff, vOff);

        // Additive blend for textures with field1818 == 2
        bool additive = b.TexDef?.field1818 == 2;
        if (additive)
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, b.Texture);
        GL.BindVertexArray(b.VAO);
        GL.DrawElements(PrimitiveType.Triangles, b.IndexCount, DrawElementsType.UnsignedInt, 0);

        if (additive)
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
    }

    public void Dispose()
    {
        foreach (var b in _opaqueBatches)
        {
            GL.DeleteBuffer(b.VBO);
            GL.DeleteBuffer(b.EBO);
            GL.DeleteVertexArray(b.VAO);
        }
        foreach (var b in _translucentBatches)
        {
            GL.DeleteBuffer(b.VBO);
            GL.DeleteBuffer(b.EBO);
            GL.DeleteVertexArray(b.VAO);
        }
        foreach (int tex in _ownedTextures)
            GL.DeleteTexture(tex);
        _ownedTextures.Clear();
        _opaqueBatches.Clear();
        _translucentBatches.Clear();
        // Note: _whiteTexture is NOT disposed here — it's shared and reused
    }
}
