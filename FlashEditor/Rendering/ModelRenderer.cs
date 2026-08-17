using FlashEditor.Definitions;
using FlashEditor.Definitions.Sprites;
using FlashEditor.Rendering;
using OpenTK.Graphics.OpenGL;
using FlashEditor.Utils;
using FlashEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using FlashEditor.Definitions.Models;

namespace FlashEditor.Rendering {
    internal sealed class ModelRenderer
    {
        private readonly List<Batch> _opaqueBatches = new();
        private readonly List<Batch> _translucentBatches = new();
        private readonly List<int> _ownedTextures = new();
        private int _whiteTexture;

        private const int Stride = 12; // pos(3) + normal(3) + uv(2) + alpha(1) + colour(3)

        // Offsets of the attributes ApplyPose rewrites, in floats from the start of a vertex.
        private const int PositionOffset = 0;
        private const int NormalOffset = 3;
        private const int AlphaOffset = 8;
        private const int ColourOffset = 9;

        // Model units per world unit. Read from RenderSpace rather than restated, because ApplyPose has
        // to divide by exactly what the initial upload divided by - and the cursor picker and the
        // particle billboards have to divide by the same thing again, or they land somewhere the model
        // is not. RenderSpace is the one statement of it.
        private const float ModelUnitsPerWorldUnit = RenderSpace.ModelUnitsPerWorldUnit;

        /// <summary>How many models the last Load or LoadMultiple was given.</summary>
        /// <remarks>
        ///     <see cref="ApplyPose"/> is indexed by the same position, so a caller can check the two
        ///     line up rather than discovering they do not as a model that refuses to move.
        /// </remarks>
        public int ModelCount { get; private set; }

        /// <summary>Whether the last <see cref="ApplyPose"/> shifted a face's alpha.</summary>
        /// <remarks>
        ///     A type-5 transform writes the alpha attribute but does <b>not</b> move the face between
        ///     the opaque and translucent draw passes, which are decided once at load. So a face that
        ///     fades in mid-animation is drawn with depth writes on and can sort wrongly against the
        ///     translucent pass. Surfaced rather than hidden: it is a visible artefact with an
        ///     unobvious cause, and this is the flag that names it.
        /// </remarks>
        public bool PoseChangedFaceAlpha { get; private set; }

        private class Batch
        {
            public int VAO;
            public int VBO;
            public int EBO;
            public int IndexCount;
            public int Texture;
            public TextureDefinition? TexDef;

            /// <summary>The interleaved vertex data as last uploaded, rewritten in place by a pose.</summary>
            public float[] Vertices = Array.Empty<float>();

            /// <summary>The same data as first built, so a pose can be undone without a reload.</summary>
            public float[] RestVertices = Array.Empty<float>();

            /// <summary>Which model of the loaded set each vertex slot came from.</summary>
            public int[] SourceModel = Array.Empty<int>();

            /// <summary>Which vertex of that model each slot came from.</summary>
            public int[] SourceVertex = Array.Empty<int>();

            /// <summary>Which face of that model each slot came from.</summary>
            public int[] SourceFace = Array.Empty<int>();

            /// <summary>Which corner of that face, 0 to 2, so a per-corner normal can be found.</summary>
            public byte[] SourceCorner = Array.Empty<byte>();
        }

        /// <summary>Accumulates one batch's geometry along with where every vertex came from.</summary>
        private sealed class BatchBuilder
        {
            public int Key;
            public bool Translucent;
            public readonly List<float> Vertices = new();
            public readonly List<uint> Indices = new();
            public readonly List<int> SourceModel = new();
            public readonly List<int> SourceVertex = new();
            public readonly List<int> SourceFace = new();
            public readonly List<byte> SourceCorner = new();
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
            ModelCount = defs.Count;

            // Collect all faces with sort keys. The model index travels with the face because
            // ApplyPose needs to know which posed mesh a vertex belongs to, and a composite entity
            // is several models against one skeleton.
            var allFaces = new List<(ModelDefinition def, int modelIndex, int faceIdx, int[][] colours, float[][] normals)>();

            for (int modelIndex = 0; modelIndex < defs.Count; modelIndex++)
            {
                ModelDefinition def = defs[modelIndex];
                int[][] vertColours = def.ComputeUnlitColours();
                float[][] vertNormals = def.ComputeFaceVertexNormals();
                for (int i = 0; i < def.TriangleCount; i++)
                {
                    // Render type 2 means the face is not drawn. Both of the 637 client's
                    // renderers gate their draw list on it before anything else
                    // (Renderable_Sub2.java:397, Renderable_Sub3.java:172), so these faces
                    // never reach the rasteriser. They are stray geometry carrying face
                    // colour HSL 0, and drawing them puts black slivers on the 12,621
                    // models in this cache that contain one.
                    if (def.FaceRenderType != null && def.FaceRenderType[i] == 2)
                        continue;

                    allFaces.Add((def, modelIndex, i, vertColours, vertNormals));
                }
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
            var groups = new List<BatchBuilder>();

            foreach (var (def, modelIndex, faceIdx, colours, normals) in allFaces)
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
                BatchBuilder? group = groups.Count > 0 ? groups[^1] : null;
                if (group == null || group.Key != key || group.Translucent != translucent)
                {
                    group = new BatchBuilder { Key = key, Translucent = translucent };
                    groups.Add(group);
                }

                float[] fn = normals[faceIdx];
                int vi = group.SourceVertex.Count;
                AppendVertex(group, def, modelIndex, faceIdx, 0, a, fn[0], fn[1], fn[2], u0, v0, alpha, rgb0);
                group.Indices.Add((uint)vi++);
                AppendVertex(group, def, modelIndex, faceIdx, 1, b, fn[3], fn[4], fn[5], u1, v1, alpha, rgb1);
                group.Indices.Add((uint)vi++);
                AppendVertex(group, def, modelIndex, faceIdx, 2, c, fn[6], fn[7], fn[8], u2, v2, alpha, rgb2);
                group.Indices.Add((uint)vi);
            }

            // Build GPU batches
            foreach (BatchBuilder builder in groups)
            {
                float[] verts = builder.Vertices.ToArray();
                uint[] idx = builder.Indices.ToArray();
                if (idx.Length == 0) continue;

                int vao = GL.GenVertexArray();
                int vbo = GL.GenBuffer();
                int ebo = GL.GenBuffer();

                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                // DynamicDraw rather than StaticDraw: an animated model rewrites this buffer on every
                // frame the playhead crosses. The hint costs nothing for a model that never animates.
                GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);

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
                if (builder.Key >= 0)
                {
                    texture = textures.GetTexture(builder.Key);
                    if (texture == 0)
                        texture = GetWhiteTexture(); // definition missing — use white so vertex lighting is visible
                    if (TextureManager.Textures.TryGetValue(builder.Key, out var td))
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
                    TexDef = texDef,
                    Vertices = verts,
                    RestVertices = (float[])verts.Clone(),
                    SourceModel = builder.SourceModel.ToArray(),
                    SourceVertex = builder.SourceVertex.ToArray(),
                    SourceFace = builder.SourceFace.ToArray(),
                    SourceCorner = builder.SourceCorner.ToArray()
                };

                if (builder.Translucent)
                    _translucentBatches.Add(batch);
                else
                    _opaqueBatches.Add(batch);
            }
        }

        /// <summary>
        /// Rewrites the loaded geometry from a skeletal pose and re-uploads it.
        /// </summary>
        /// <remarks>
        ///     This is the whole of "the renderer animates": the client applies frame transforms to
        ///     vertex arrays on the CPU, so the shader is untouched and the vertex buffer is respecified
        ///     each time the playhead crosses into a new frame. Call it only when the pose has changed -
        ///     <c>SkeletalAnimator.Advance</c> already reports that - because at 30fps against 20ms
        ///     cycles most redraws land inside a frame's duration and have nothing to upload.
        ///     <para>
        ///     Positions and normals are always rewritten. Alpha and colour are rewritten only when a
        ///     type 5 or type 7 transform actually moved them, which is rare, and the opaque and
        ///     translucent bucketing is <b>not</b> recomputed - see <see cref="PoseChangedFaceAlpha"/>.
        ///     </para>
        /// </remarks>
        /// <param name="poses">
        ///     One posed mesh per model, in the order the models were passed to
        ///     <see cref="Load"/> or <see cref="LoadMultiple"/>. A shorter list leaves the models past
        ///     its end at rest rather than throwing.
        /// </param>
        public void ApplyPose(IReadOnlyList<PosedMesh>? poses)
        {
            if (poses == null || poses.Count == 0)
                return;

            // One normal pass per model per pose, not per batch: a model's faces are scattered across
            // every batch its textures put them in, and recomputing per batch would repeat the work.
            var normals = new float[poses.Count][][];
            var colours = new int[poses.Count][];
            PoseChangedFaceAlpha = false;

            for (int m = 0; m < poses.Count; m++)
            {
                normals[m] = PosedNormals.ComputeFaceVertexNormals(poses[m]);
                // Empty rather than null for a model whose colours a type-7 transform never touched:
                // the emptiness is the "nothing to rewrite" signal, and it needs no null analysis.
                colours[m] = poses[m].FaceColourChanged ? PosedFaceColours(poses[m]) : Array.Empty<int>();
                if (poses[m].FaceAlphaChanged)
                    PoseChangedFaceAlpha = true;
            }

            foreach (var batch in _opaqueBatches)
                ApplyPoseToBatch(batch, poses, normals, colours);
            foreach (var batch in _translucentBatches)
                ApplyPoseToBatch(batch, poses, normals, colours);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        /// <summary>
        /// Puts the loaded geometry back to the rest mesh it was built from.
        /// </summary>
        /// <remarks>
        ///     Restores the bytes rather than re-deriving them, so stopping playback cannot drift away
        ///     from what a never-animated model looks like.
        /// </remarks>
        public void ResetPose()
        {
            PoseChangedFaceAlpha = false;
            foreach (var batch in _opaqueBatches)
                RestoreBatch(batch);
            foreach (var batch in _translucentBatches)
                RestoreBatch(batch);
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        private static void RestoreBatch(Batch batch)
        {
            if (batch.RestVertices.Length == 0 || batch.RestVertices.Length != batch.Vertices.Length)
                return;

            Array.Copy(batch.RestVertices, batch.Vertices, batch.Vertices.Length);
            Upload(batch);
        }

        private static void ApplyPoseToBatch(Batch batch, IReadOnlyList<PosedMesh> poses,
            float[][][] normals, int[][] colours)
        {
            if (batch.SourceVertex.Length == 0)
                return;

            bool touched = false;

            for (int slot = 0; slot < batch.SourceVertex.Length; slot++)
            {
                int model = batch.SourceModel[slot];
                if ((uint)model >= (uint)poses.Count)
                    continue;

                PosedMesh mesh = poses[model];
                int vertex = batch.SourceVertex[slot];
                if ((uint)vertex >= (uint)mesh.VertexX.Length)
                    continue;

                int offset = slot * Stride;
                batch.Vertices[offset + PositionOffset + 0] = mesh.VertexX[vertex] / ModelUnitsPerWorldUnit;
                batch.Vertices[offset + PositionOffset + 1] = -mesh.VertexY[vertex] / ModelUnitsPerWorldUnit;
                batch.Vertices[offset + PositionOffset + 2] = -mesh.VertexZ[vertex] / ModelUnitsPerWorldUnit;

                int face = batch.SourceFace[slot];
                int corner = batch.SourceCorner[slot];
                float[][] modelNormals = normals[model];

                if ((uint)face < (uint)modelNormals.Length)
                {
                    float[] triple = modelNormals[face];
                    batch.Vertices[offset + NormalOffset + 0] = triple[corner * 3 + 0];
                    batch.Vertices[offset + NormalOffset + 1] = triple[corner * 3 + 1];
                    batch.Vertices[offset + NormalOffset + 2] = triple[corner * 3 + 2];

                    if (mesh.FaceAlphaChanged)
                        batch.Vertices[offset + AlphaOffset] = (255 - (mesh.FaceAlpha[face] & 0xFF)) / 255f;

                    int[] modelColours = colours[model];
                    if ((uint)face < (uint)modelColours.Length)
                    {
                        int rgb = modelColours[face];
                        batch.Vertices[offset + ColourOffset + 0] = ((rgb >> 16) & 0xFF) / 255f;
                        batch.Vertices[offset + ColourOffset + 1] = ((rgb >> 8) & 0xFF) / 255f;
                        batch.Vertices[offset + ColourOffset + 2] = (rgb & 0xFF) / 255f;
                    }
                }

                touched = true;
            }

            if (touched)
                Upload(batch);
        }

        private static void Upload(Batch batch)
        {
            GL.BindBuffer(BufferTarget.ArrayBuffer, batch.VBO);
            GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero,
                batch.Vertices.Length * sizeof(float), batch.Vertices);
        }

        /// <summary>
        /// Converts a posed mesh's transformed HSL face colours to the packed RGB the shader takes.
        /// </summary>
        /// <remarks>
        ///     The same two-step <c>ModelDefinition.ComputeUnlitColours</c> uses, so a type-7 transform
        ///     of zero produces exactly the colours the untransformed model was uploaded with.
        /// </remarks>
        private static int[] PosedFaceColours(PosedMesh mesh)
        {
            var result = new int[mesh.FaceColour.Length];
            for (int face = 0; face < result.Length; face++)
                result[face] = ModelDefinition.RawHslToRgb(mesh.FaceColour[face] & 0xFFFF);
            return result;
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

            // No source arrays: LoadSimple has no model behind it, so ApplyPose leaves it alone.
            _opaqueBatches.Add(new Batch
            {
                VAO = vao,
                VBO = vbo,
                EBO = ebo,
                IndexCount = indices.Length,
                Texture = texture
            });
        }

        private static void AppendVertex(BatchBuilder group, ModelDefinition def,
            int modelIndex, int faceIndex, int corner, int vert,
            float nx, float ny, float nz,
            float u, float v, float alpha, int rgb)
        {
            List<float> list = group.Vertices;
            list.Add(def.VertX[vert] / ModelUnitsPerWorldUnit);
            list.Add(-def.VertY[vert] / ModelUnitsPerWorldUnit);
            list.Add(-def.VertZ[vert] / ModelUnitsPerWorldUnit);
            list.Add(nx);
            list.Add(ny);
            list.Add(nz);
            list.Add(u);
            list.Add(v);
            list.Add(alpha);
            list.Add(((rgb >> 16) & 0xFF) / 255f);
            list.Add(((rgb >> 8) & 0xFF) / 255f);
            list.Add((rgb & 0xFF) / 255f);

            group.SourceModel.Add(modelIndex);
            group.SourceVertex.Add(vert);
            group.SourceFace.Add(faceIndex);
            group.SourceCorner.Add((byte)corner);
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

            // Mode 2 is the only alphaMode whose texels carry an alpha channel of their own
            // (SoftwareRasterizer.java:583-588), and the client sorts those faces into its
            // translucent pass. This viewer approximates that pass with an additive blend.
            bool additive = b.TexDef?.alphaMode == 2;
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
            ModelCount = 0;
            PoseChangedFaceAlpha = false;
            // Note: _whiteTexture is NOT disposed here — it's shared and reused
        }
    }
}
