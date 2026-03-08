using FlashEditor;
using FlashEditor.Utils;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     Decodes RuneScape <c>model.dat</c> files from cache revisions 602‑647 (RuneTek 5, incl. rev 639).
    ///     <para>
    ///         The format stores its <em>header at the end</em>. We therefore:
    ///         <ol>
    ///             <li>Seek to <c>length ‑ footerSize</c> and read counts &amp; flags.</li>
    ///             <li>Compute absolute offsets for every variable‑length block.</li>
    ///             <li>Rewind to byte 0 and stream each block into strongly‑typed arrays.</li>
    ///         </ol>
    ///     </para>
    ///     <para>
    ///         Three footer flavours exist; this resolver fully supports the newest (23‑/26‑byte) flavour which every
    ///         639 model uses.  Older flavours are parsed just enough to avoid crashes but may omit HD‑only fields.
    ///     </para>
    /// </summary>
    public class ModelDefinition : IDefinition {
        #region ≡ public decoded fields

        /// <summary>Total number of vertices in the model.</summary>
        public int VertexCount { get; private set; }
        /// <summary>Total number of triangular faces in the model.</summary>
        public int TriangleCount { get; private set; }
        /// <summary>Number of texture-mapped triangles.</summary>
        public int TexturedTriangleCount { get; private set; }

        /// <summary>X coordinates for each vertex.</summary>
        public int[] VertX = Array.Empty<int>();
        /// <summary>Y coordinates for each vertex (vertical axis).</summary>
        public int[] VertY = Array.Empty<int>();
        /// <summary>Z coordinates for each vertex.</summary>
        public int[] VertZ = Array.Empty<int>();
        /// <summary>Animation skin group assignment per vertex, or null if ungrouped.</summary>
        public int[]? VertSkins;

        /// <summary>First vertex index of each triangle face.</summary>
        public int[] faceIndices1 = Array.Empty<int>();
        /// <summary>Second vertex index of each triangle face.</summary>
        public int[] faceIndices2 = Array.Empty<int>();
        /// <summary>Third vertex index of each triangle face.</summary>
        public int[] faceIndices3 = Array.Empty<int>();

        /// <summary>Per-face HSL-555 colour values. Convert to RGB via <see cref="HslToRgb"/>.</summary>
        public short[] FaceColour = Array.Empty<short>();
        /// <summary>Per-face render type (0 = flat shaded, 1 = textured), or null.</summary>
        public sbyte[]? FaceRenderType;
        /// <summary>Per-face render priority (0-255), or null when a global priority is used.</summary>
        public sbyte[]? FacePriority;
        /// <summary>Per-face alpha transparency (0-255), or null if fully opaque.</summary>
        public sbyte[]? FaceAlpha;
        /// <summary>Per-face animation skin group, or null.</summary>
        public sbyte[]? FaceSkin;

        /// <summary>Per-textured-face type flags (0, 1, 2, or 3), or null.</summary>
        public sbyte[]? TextureType;
        /// <summary>Texture coordinate index per face, mapping into TexInd arrays.</summary>
        public sbyte[] TextureCoordinates;
        /// <summary>Texture id per face, or -1 for untextured.</summary>
        public short[] FaceTextures;
        /// <summary>First, second, and third reference vertex indices for UV coordinate computation.</summary>
        public short[]? TexIndA, TexIndB, TexIndC;

        /// <summary>Animaya (skeletal morph) group ids per vertex.</summary>
        public int[][] AnimayaGroups { get; private set; }
        /// <summary>Animaya (skeletal morph) weight scales per vertex.</summary>
        public int[][] AnimayaScales { get; private set; }

        /// <summary>Particle effect id attached to this model (0xFFFF = none).</summary>
        public ushort ParticleEffectId { get; private set; } = 0xFFFF;
        /// <summary>Vertex ids to which particle effects are anchored, or null.</summary>
        public ushort[]? ParticleAnchorVert;

        /// <summary>
        /// Model format version. Old-format models have implicit type 12;
        /// newer formats default to 13+. When FormatType &lt; 13 the client
        /// left-shifts all vertex coordinates by 2 bits.
        /// </summary>
        public int FormatType { get; private set; } = 12;

        /// <summary>Per-vertex surface normals computed from triangle data.</summary>
        public VertexNormal[]? VertexNormals;

        /// <summary>Per-face surface normals used when faces are drawn flat.</summary>
        public FaceNormal[]? FaceNormals;

        /// <summary>Per-face texture U coordinates (three floats per triangle).</summary>
        public float[][]? FaceTextureUCoordinates;

        /// <summary>Per-face texture V coordinates (three floats per triangle).</summary>
        public float[][]? FaceTextureVCoordinates;

        /// <summary>Lists of vertex indices keyed by animation group.</summary>
        public int[][]? VertexGroups;

        #endregion

        #region ≡ decoding entry points

        /// <summary>
        /// Populates this definition by decoding the supplied stream (and optional XTEA key array).
        /// </summary>
        /// <param name="stream">JagStream containing full model+footer data.</param>
        /// <param name="xteaKey">Optional 4- or 10-int array for decryption.</param>
        public void Decode(JagStream stream, int[] xteaKey = null) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Copy raw bytes into a new JagStream so we can parse and rewind.
            byte[] rawBytes = stream.ToArray();
            JagStream rawStream = new JagStream(rawBytes);

            // Parse the last 2 bytes to get the model format (or use model ID for newProtocol models)
            ModelFormat modelFormat = GetModelFormat(rawStream, ModelID);

            DebugUtil.Debug($"Decoding Model (Format: {modelFormat})");

            // Decode based on format
            switch (modelFormat) {
                case ModelFormat.Old:
                    DecodeOld(stream, xteaKey);
                    break;
                case ModelFormat.Newer:
                    DecodeRS2(stream, xteaKey);
                    break;
                case ModelFormat.Newest:
                    DecodeRS3(stream, xteaKey);
                    break;

                default:
                    throw new NotSupportedException($"Unknown model format: {modelFormat}");
            }

            DebugUtil.Debug("Finished decoding");

            // Always compute derived data so downstream consumers like
            // the OpenGL renderer have the arrays they expect.
            // The helpers themselves guard against double work when
            // the values are already present.
            ComputeNormals();
            ComputeTextureUVCoordinates();
            ComputeAnimationTables();

        }

        /// <summary>
        /// Peeks at the last two bytes of the given JagStream (without altering its Position)
        /// and returns the appropriate ModelFormat enum.
        /// </summary>
        /// <param name="stream">JagStream containing raw model+footer bytes.</param>
        /// <returns>
        /// ModelFormat.Newest for 0xFF FD,
        /// ModelFormat.Newer  for 0xFF FE or 0xFF FF,
        /// ModelFormat.Old    otherwise.
        /// </returns>
        public static ModelFormat GetModelFormat(JagStream stream, int modelId = -1) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Models 63607-63613 use the newProtocol variant of the Newest format,
            // identified by model ID rather than a sentinel (matches Java client).
            if (modelId >= 63607 && modelId <= 63613)
                return ModelFormat.Newest;

            long length = stream.Length;
            if (length < 2)
                throw new InvalidDataException("Stream too short to determine model format.");

            // Read without moving the stream's Position
            byte last = stream.Get((int) (length - 1));
            byte penultimate = stream.Get((int) (length - 2));

            // 0xFF FF = Newer format (23-byte footer, no per-face textures in flags)
            if (penultimate == 0xFF && last == 0xFF)
                return ModelFormat.Newer;

            // 0xFF FD = Newest format (23-byte footer, with per-face texture data)
            if (penultimate == 0xFF && last == 0xFD)
                return ModelFormat.Newest;

            // anything else => old RS2-legacy format (18-byte footer)
            return ModelFormat.Old;
        }

        /// <summary>
        /// Decodes an RS2 “type 2” model (explicit‐length 23-byte footer, revisions ~602–618)
        /// into this <see cref="ModelDefinition"/>.
        /// </summary>
        /// <param name="modelStream">
        /// A <see cref="JagStream"/> positioned over the entire model + footer bytes.
        /// </param>
        /// <param name="xteaKey">Optional XTEA key (ignored by type 2 models).</param>
        /// <param name="footer">Footer info (counts are re‐read from the stream, so this may be unused).</param>
        /// <summary>
        /// Decodes the "newer" RS2 model format (0xFF 0xFF sentinel, 23-byte footer).
        /// Ported from Java's <c>decoder_newer_format()</c>.
        /// </summary>
        private void DecodeRS2(JagStream modelStream, int[] xteaKey) {
            byte[] data = modelStream.ToArray();

            // 7 streams to match Java's RSBuffer through RSBuffer_57_
            var st1 = new JagStream(data);
            var st2 = new JagStream(data);
            var st3 = new JagStream(data);
            var st4 = new JagStream(data);
            var st5 = new JagStream(data);
            var st6 = new JagStream(data);
            var st7 = new JagStream(data);

            // ── 1) Read 23-byte footer ───────────────────────────────────────────
            st1.Seek(data.Length - 23);

            int vc  = st1.ReadUnsignedShort();       // vertex count
            int fc  = st1.ReadUnsignedShort();       // face (triangle) count
            int tfc = st1.ReadUnsignedByte();        // textured triangle count

            // Packed flags byte — must extract bits, NOT compare == 1
            int flagsByte    = st1.ReadUnsignedByte();
            bool hasFaceType = (flagsByte & 0x1) == 1;   // face render type present

            // ── 1b) Determine format type ────────────────────────────────────
            // Hydra client (line 401-405): immediately after flags byte, caret
            // is at length-17.  caret -= 7 reads from length-24, then caret += 6
            // restores to length-17 so the rest of the footer reads normally.
            if ((flagsByte & 0x8) == 8) {
                long saved = st1.Position;           // data.Length - 17
                st1.Seek(saved - 7);                 // data.Length - 24
                FormatType = st1.ReadUnsignedByte();
                st1.Seek(saved);                     // restore to data.Length - 17
            }

            int priorityFlag  = st1.ReadUnsignedByte();  // 255 = per-face priority
            int alphaFlag     = st1.ReadUnsignedByte();   // 1 = per-face alpha
            int faceSkinFlag  = st1.ReadUnsignedByte();   // 1 = per-face skin group
            int faceTexFlag   = st1.ReadUnsignedByte();   // 1 = per-face texture id
            int vertSkinFlag  = st1.ReadUnsignedByte();   // 1 = per-vertex skin group

            int vertexXLen   = st1.ReadUnsignedShort();   // length of X-delta block
            int vertexYLen   = st1.ReadUnsignedShort();   // length of Y-delta block
            int vertexZLen   = st1.ReadUnsignedShort();   // length of Z-delta block
            int faceIndexLen = st1.ReadUnsignedShort();   // length of face-index smart block
            int texCoordLen  = st1.ReadUnsignedShort();   // length of texture-coord block

            // ── 2) Read texture face types from byte 0 ──────────────────────────
            int texType0Count = 0;
            if (tfc > 0) {
                TextureType = new sbyte[tfc];
                st1.Seek(0);
                for (int i = 0; i < tfc; i++) {
                    sbyte t = st1.ReadSignedByte();
                    TextureType[i] = t;
                    if (t == 0) texType0Count++;
                }
            }

            // ── 3) Compute block offsets (matching Java decoder_newer_format) ────
            //  Data starts with tfc bytes of texture-type, then all geometry blocks.
            int pos = tfc;

            int vertexFlagsOff = pos;
            pos += vc;

            int faceTypeOff = pos;
            if (hasFaceType) pos += fc;

            int faceOpcodeOff = pos;
            pos += fc;

            int facePriorityOff = pos;
            if (priorityFlag == 255) pos += fc;

            int faceSkinOff = pos;
            if (faceSkinFlag == 1) pos += fc;

            int vertSkinOff = pos;
            if (vertSkinFlag == 1) pos += vc;

            int faceAlphaOff = pos;
            if (alphaFlag == 1) pos += fc;

            int faceIndexSmartOff = pos;
            pos += faceIndexLen;

            int faceTextureOff = pos;
            if (faceTexFlag == 1) pos += fc * 2;

            int texCoordOff = pos;
            pos += texCoordLen;

            int faceColourOff = pos;
            pos += fc * 2;

            int vertexXOff = pos;
            pos += vertexXLen;

            int vertexYOff = pos;
            pos += vertexYLen;

            int vertexZOff = pos;
            pos += vertexZLen;

            int texFaceGeomOff = pos;   // type-0 texture geometry (6 bytes each)

            // ── 4) Populate counts ──────────────────────────────────────────────
            VertexCount = vc;
            TriangleCount = fc;
            TexturedTriangleCount = tfc;

            // ── 5) Allocate arrays ──────────────────────────────────────────────
            VertX = new int[vc];
            VertY = new int[vc];
            VertZ = new int[vc];

            faceIndices1 = new int[fc];
            faceIndices2 = new int[fc];
            faceIndices3 = new int[fc];

            FaceColour = new short[fc];

            if (hasFaceType)
                FaceRenderType = new sbyte[fc];

            if (priorityFlag == 255)
                FacePriority = new sbyte[fc];
            else
                _globalPriority = (byte) priorityFlag;

            if (alphaFlag == 1)
                FaceAlpha = new sbyte[fc];

            if (faceSkinFlag == 1)
                FaceSkin = new sbyte[fc];

            if (vertSkinFlag == 1)
                VertSkins = new int[vc];

            if (faceTexFlag == 1)
                FaceTextures = new short[fc];

            if (faceTexFlag == 1 && tfc > 0)
                TextureCoordinates = new sbyte[fc];

            if (tfc > 0) {
                TexIndA = new short[tfc];
                TexIndB = new short[tfc];
                TexIndC = new short[tfc];
            }

            // ── 6) Decode vertex positions ──────────────────────────────────────
            st1.Seek(vertexFlagsOff);
            st2.Seek(vertexXOff);
            st3.Seek(vertexYOff);
            st4.Seek(vertexZOff);
            st5.Seek(vertSkinOff);

            int cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < vc; i++) {
                int mask = st1.ReadUnsignedByte();
                int dx = (mask & 1) != 0 ? st2.ReadShortSmart() : 0;
                int dy = (mask & 2) != 0 ? st3.ReadShortSmart() : 0;
                int dz = (mask & 4) != 0 ? st4.ReadShortSmart() : 0;
                cx += dx; cy += dy; cz += dz;
                VertX[i] = cx;
                VertY[i] = cy;
                VertZ[i] = cz;

                if (vertSkinFlag == 1)
                    VertSkins![i] = st5.ReadUnsignedByte();
            }

            // ── 6b) Scale vertices for old-format models ────────────────────────
            if (FormatType < 13) {
                for (int i = 0; i < vc; i++) {
                    VertX[i] <<= 2;
                    VertY[i] <<= 2;
                    VertZ[i] <<= 2;
                }
            }

            // ── 7) Decode face data (each field from its own stream) ────────────
            st1.Seek(faceColourOff);
            st2.Seek(faceTypeOff);
            st3.Seek(facePriorityOff);
            st4.Seek(faceAlphaOff);
            st5.Seek(faceSkinOff);
            st6.Seek(faceTextureOff);
            st7.Seek(texCoordOff);

            for (int i = 0; i < fc; i++) {
                FaceColour[i] = (short) st1.ReadUnsignedShort();

                if (hasFaceType)
                    FaceRenderType![i] = st2.ReadSignedByte();

                if (priorityFlag == 255)
                    FacePriority![i] = st3.ReadSignedByte();

                if (alphaFlag == 1)
                    FaceAlpha![i] = st4.ReadSignedByte();

                if (faceSkinFlag == 1)
                    FaceSkin![i] = (sbyte) st5.ReadUnsignedByte();

                if (faceTexFlag == 1)
                    FaceTextures![i] = (short) (st6.ReadUnsignedShort() - 1);

                if (TextureCoordinates != null) {
                    if (FaceTextures![i] == -1)
                        TextureCoordinates[i] = -1;
                    else
                        TextureCoordinates[i] = (sbyte) (st7.ReadUnsignedByte() - 1);
                }
            }

            // ── 8) Decode triangle-strip indices ────────────────────────────────
            st1.Seek(faceIndexSmartOff);
            st2.Seek(faceOpcodeOff);

            int a = 0, b = 0, c = 0, ptr = 0;
            for (int i = 0; i < fc; i++) {
                int op = st2.ReadUnsignedByte();

                if (op == 1) {
                    a = ptr + st1.ReadShortSmart();
                    b = a + st1.ReadShortSmart();
                    c = b + st1.ReadShortSmart();
                    ptr = c;
                } else if (op == 2) {
                    b = c;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                } else if (op == 3) {
                    a = c;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                } else { // op == 4
                    int tmp = a;
                    a = b;
                    b = tmp;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                }

                faceIndices1[i] = a;
                faceIndices2[i] = b;
                faceIndices3[i] = c;
            }

            // ── 9) Decode textured-face lookup tables (type 0 only) ─────────────
            st1.Seek(texFaceGeomOff);
            if (TexIndA != null) {
                for (int i = 0; i < tfc; i++) {
                    int type = TextureType != null ? (TextureType[i] & 0xFF) : 0;
                    if (type == 0) {
                        TexIndA[i] = (short) st1.ReadUnsignedShort();
                        TexIndB![i] = (short) st1.ReadUnsignedShort();
                        TexIndC![i] = (short) st1.ReadUnsignedShort();
                    }
                    // Types 1-3 have more complex geometry data; skipped for basic rendering
                }
            }
        }


        /// <summary>
        /// Decodes an RS2 "old" legacy model (18-byte footer, no animaya).
        /// Ported from Hydra's <c>method2587()</c>.
        /// </summary>
        private void DecodeOld(JagStream modelStream, int[] xteaKey) {
            byte[] data = modelStream.ToArray();

            var st1 = new JagStream(data);
            var st2 = new JagStream(data);
            var st3 = new JagStream(data);
            var st4 = new JagStream(data);
            var st5 = new JagStream(data);

            // 1) Read the 18-byte footer
            st1.Seek(data.Length - 18);

            int vertexCountFlag = st1.ReadUnsignedShort();
            int triangleCountFlag = st1.ReadUnsignedShort();
            int texturedCountFlag = st1.ReadUnsignedByte();

            int hasFaceRenderFlag = st1.ReadUnsignedByte();   // 1 = yes
            int renderPriorities = st1.ReadUnsignedByte();     // 255 = per-face
            int hasFaceAlphaFlag = st1.ReadUnsignedByte();     // 1 = yes
            int hasFaceSkinFlag = st1.ReadUnsignedByte();      // 1 = yes
            int hasVertSkinFlag = st1.ReadUnsignedByte();      // 1 = yes

            int vertexXDataLen = st1.ReadUnsignedShort();
            int vertexYDataLen = st1.ReadUnsignedShort();
            int vertexZDataLen = st1.ReadUnsignedShort();
            int faceIndexDataLen = st1.ReadUnsignedShort();

            // 2) Compute block offsets (cumulative from byte 0)
            int offset = 0;

            int vertexFlagsOff = offset;
            offset += vertexCountFlag;

            int faceIndexOpcodesOff = offset;
            offset += triangleCountFlag;

            int facePriorityOff = offset;
            if (renderPriorities == 255)
                offset += triangleCountFlag;

            int faceSkinOff = offset;
            if (hasFaceSkinFlag == 1)
                offset += triangleCountFlag;

            int faceRenderTypeOff = offset;
            if (hasFaceRenderFlag == 1)
                offset += triangleCountFlag;

            int vertSkinOff = offset;
            if (hasVertSkinFlag == 1)
                offset += vertexCountFlag;

            int faceAlphaOff = offset;
            if (hasFaceAlphaFlag == 1)
                offset += triangleCountFlag;

            int faceIndexSmartOff = offset;
            offset += faceIndexDataLen;

            int faceColourOff = offset;
            offset += triangleCountFlag * 2;

            int texturedFaceOff = offset;
            offset += texturedCountFlag * 6;

            int vertexXOff = offset;
            offset += vertexXDataLen;

            int vertexYOff = offset;
            offset += vertexYDataLen;

            int vertexZOff = offset;

            // 3) Populate counts
            VertexCount = vertexCountFlag;
            TriangleCount = triangleCountFlag;
            TexturedTriangleCount = texturedCountFlag;

            // 4) Allocate arrays
            VertX = new int[VertexCount];
            VertY = new int[VertexCount];
            VertZ = new int[VertexCount];

            faceIndices1 = new int[TriangleCount];
            faceIndices2 = new int[TriangleCount];
            faceIndices3 = new int[TriangleCount];

            FaceColour = new short[TriangleCount];

            if (texturedCountFlag > 0) {
                TextureType = new sbyte[texturedCountFlag];
                TexIndA = new short[texturedCountFlag];
                TexIndB = new short[texturedCountFlag];
                TexIndC = new short[texturedCountFlag];
            }

            if (hasFaceRenderFlag == 1) {
                FaceRenderType = new sbyte[TriangleCount];
                TextureCoordinates = new sbyte[TriangleCount];
                FaceTextures = new short[TriangleCount];
            }

            if (renderPriorities == 255)
                FacePriority = new sbyte[TriangleCount];
            else
                _globalPriority = (byte) renderPriorities;

            if (hasFaceAlphaFlag == 1)
                FaceAlpha = new sbyte[TriangleCount];

            if (hasFaceSkinFlag == 1)
                FaceSkin = new sbyte[TriangleCount];

            if (hasVertSkinFlag == 1)
                VertSkins = new int[VertexCount];

            // 5) Decode vertices
            st1.Seek(vertexFlagsOff);
            st2.Seek(vertexXOff);
            st3.Seek(vertexYOff);
            st4.Seek(vertexZOff);
            st5.Seek(vertSkinOff);

            int cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < VertexCount; i++) {
                int mask = st1.ReadUnsignedByte();
                int dx = (mask & 1) != 0 ? st2.ReadShortSmart() : 0;
                int dy = (mask & 2) != 0 ? st3.ReadShortSmart() : 0;
                int dz = (mask & 4) != 0 ? st4.ReadShortSmart() : 0;
                cx += dx; cy += dy; cz += dz;
                VertX[i] = cx;
                VertY[i] = cy;
                VertZ[i] = cz;

                if (hasVertSkinFlag == 1)
                    VertSkins![i] = st5.ReadUnsignedByte();
            }

            // 5b) Old-format models have implicit formatType 12 (< 13) — scale vertices
            for (int i = 0; i < VertexCount; i++) {
                VertX[i] <<= 2;
                VertY[i] <<= 2;
                VertZ[i] <<= 2;
            }

            // 6) Decode face colours, render types, priorities, alpha, skins
            bool anyTextured = false;
            bool anyRendered = false;

            st1.Seek(faceColourOff);
            st2.Seek(faceRenderTypeOff);
            st3.Seek(facePriorityOff);
            st4.Seek(faceAlphaOff);
            st5.Seek(faceSkinOff);

            for (int i = 0; i < TriangleCount; i++) {
                FaceColour[i] = (short) st1.ReadUnsignedShort();

                if (hasFaceRenderFlag == 1) {
                    int mask = st2.ReadUnsignedByte();

                    if ((mask & 1) != 0) {
                        FaceRenderType![i] = 1;
                        anyRendered = true;
                    } else {
                        FaceRenderType![i] = 0;
                    }

                    if ((mask & 2) == 2) {
                        TextureCoordinates[i] = (sbyte) (mask >> 2);
                        FaceTextures[i] = FaceColour[i];
                        FaceColour[i] = 127;
                        if (FaceTextures[i] != -1)
                            anyTextured = true;
                    } else {
                        TextureCoordinates[i] = -1;
                        FaceTextures[i] = -1;
                    }
                }

                if (renderPriorities == 255)
                    FacePriority![i] = st3.ReadSignedByte();

                if (hasFaceAlphaFlag == 1)
                    FaceAlpha![i] = st4.ReadSignedByte();

                if (hasFaceSkinFlag == 1)
                    FaceSkin![i] = st5.ReadSignedByte();
            }

            // 7) Null-out arrays that never saw a flag
            if (FaceRenderType != null && !anyRendered)
                FaceRenderType = null;

            if (FaceTextures != null && !anyTextured)
                FaceTextures = null;

            // 8) Decode triangle-strip indices
            st1.Seek(faceIndexSmartOff);
            st2.Seek(faceIndexOpcodesOff);

            int a = 0, b = 0, c = 0, ptr = 0;
            for (int i = 0; i < TriangleCount; i++) {
                int op = st2.ReadUnsignedByte();

                if (op == 1) {
                    a = ptr + st1.ReadShortSmart();
                    b = a + st1.ReadShortSmart();
                    c = b + st1.ReadShortSmart();
                    ptr = c;
                } else if (op == 2) {
                    b = c;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                } else if (op == 3) {
                    a = c;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                } else { // op == 4
                    int tmp = a;
                    a = b;
                    b = tmp;
                    c = ptr + st1.ReadShortSmart();
                    ptr = c;
                }

                faceIndices1[i] = a;
                faceIndices2[i] = b;
                faceIndices3[i] = c;
            }

            // 9) Decode textured-face lookup tables
            st1.Seek(texturedFaceOff);
            if (TexIndA != null) {
                for (int i = 0; i < TexturedTriangleCount; i++) {
                    if (TextureType != null)
                        TextureType[i] = 0;
                    TexIndA[i] = (short) st1.ReadUnsignedShort();
                    TexIndB![i] = (short) st1.ReadUnsignedShort();
                    TexIndC![i] = (short) st1.ReadUnsignedShort();
                }
            }

            // 10) Check if texture coordinates are needed
            if (TextureCoordinates != null) {
                bool anyUV = false;
                for (int i = 0; i < TriangleCount; i++) {
                    int tcIdx = TextureCoordinates[i] & 0xFF;
                    if (tcIdx != 255 && TexIndA != null && tcIdx < TexIndA.Length) {
                        if (faceIndices1[i] != (TexIndA[tcIdx] & 0xFFFF) ||
                            faceIndices2[i] != (TexIndB![tcIdx] & 0xFFFF) ||
                            faceIndices3[i] != (TexIndC![tcIdx] & 0xFFFF)) {
                            anyUV = true;
                            break;
                        }
                    }
                }
                if (!anyUV)
                    TextureCoordinates = null;
            }

            // 11) Compute derived data
            ComputeNormals();
            ComputeTextureUVCoordinates();
            ComputeAnimationTables();
        }

        public int ModelID { get; set; }


        /// <inheritdoc />
        public JagStream Encode() => throw new NotSupportedException("Model re‑encoding is out of scope for viewer.");

        #endregion

        #region ≡ footer parsing

        public enum ModelFormat {
            Old = 0, // pre-sentinel style
            Newer = 1, // sentinel style without textures
            Newest = 2 // sentinel style with texture faces
        }

        public readonly struct Footer {
            public int VertexCount { get; }
            public int TriangleCount { get; }
            public int TexturedTriangleCount { get; }
            public ModelFormat Format { get; }
            public int FooterSize { get; }

            public Footer(int vertexCount, int triangleCount, int texturedTriangleCount, ModelFormat format, int footerSize) {
                VertexCount = vertexCount;
                TriangleCount = triangleCount;
                TexturedTriangleCount = texturedTriangleCount;
                Format = format;
                FooterSize = footerSize;
            }
        }


        #endregion

        #region ≡ newest‑footer decoder (602‑647)

        /// <summary>
        /// Decode the "newest" RS2 (602–647) model layout.
        /// Uses a 26-byte footer with explicit block offsets.
        /// Ported from Hydra's <c>decoder_newest_format()</c>.
        /// </summary>
        private void DecodeRS3(JagStream full, int[] xteaKey) {
            byte[] b = full.ToArray();

            // Mirror Java's streams — we need enough to avoid caret collisions
            var st1 = new JagStream(b);
            var st2 = new JagStream(b);
            var st3 = new JagStream(b);
            var st4 = new JagStream(b);
            var st5 = new JagStream(b);
            var st6 = new JagStream(b);
            var st7 = new JagStream(b);

            // Models 63607-63613 use the newProtocol variant (3-byte header, 26-byte footer)
            bool newProtocol = (ModelID >= 63607 && ModelID <= 63613);

            // 1) Read header (newProtocol only) and seek to footer
            if (newProtocol) {
                int version = st1.ReadUnsignedByte();
                if (version != 1)
                    throw new InvalidDataException($"newProtocol model {ModelID}: expected version 1, got {version}");
                st1.ReadUnsignedByte();                     // unused
                FormatType = st1.ReadUnsignedByte();        // format type from header
                st1.Seek(b.Length - 26);
            } else {
                st1.Seek(b.Length - 23);
            }

            // 2) Read counts & flags
            int vc = st1.ReadUnsignedShort();
            int fc = st1.ReadUnsignedShort();
            int tfc = newProtocol ? st1.ReadUnsignedShort() : st1.ReadUnsignedByte();

            // Packed flags byte — newProtocol uses all 8 bits
            int flagsByte = st1.ReadUnsignedByte();
            bool hasFaceType = (flagsByte & 0x1) == 1;
            bool hasTextures = (flagsByte & 0x2) == 2;
            bool hasFormatInFooter = (flagsByte & 0x8) == 8;   // bit 3
            int explicitVertSkin = (flagsByte & 0x10) != 0 ? 1 : 0;  // bit 4 (i1)
            int explicitFaceSkin = (flagsByte & 0x20) != 0 ? 1 : 0;  // bit 5 (i2)

            // If bit 3 is set, formatType lives 7 bytes before current position
            if (hasFormatInFooter) {
                long saved = st1.Position;
                st1.Seek(saved - 7);
                FormatType = st1.ReadUnsignedByte();
                st1.Seek(saved);
            } else if (!newProtocol) {
                FormatType = 13;  // default for non-newProtocol newest
            }

            int i5 = st1.ReadUnsignedByte();  // priority: 255 = per-face
            int i6 = st1.ReadUnsignedByte();  // alpha: 1 = yes
            int i7 = st1.ReadUnsignedByte();  // face skins: 1 = yes
            int i8 = st1.ReadUnsignedByte();  // face textures: 1 = yes
            int i9 = st1.ReadUnsignedByte();  // vertex skins: 1 = yes

            // Explicit block-length offsets from footer
            int i10 = st1.ReadUnsignedShort();   // vertex data len
            int i11 = st1.ReadUnsignedShort();   // face colour len
            int i12 = st1.ReadUnsignedShort();   // face data len
            int i13 = st1.ReadUnsignedShort();   // face index len
            int i14 = st1.ReadUnsignedShort();   // texture data len

            // Vertex skin / face skin lengths (i15/i16) — logic differs by protocol
            int i15, i16;
            if (newProtocol) {
                i15 = st1.ReadUnsignedShort();
                i16 = st1.ReadUnsignedShort();
                if (explicitVertSkin == 0) {
                    i15 = (i9 == 1) ? vc : 0;
                }
                if (explicitFaceSkin == 0) {
                    i16 = (i7 == 1) ? fc : 0;
                }
            } else {
                if (explicitVertSkin != 0)
                    i15 = st1.ReadUnsignedShort();
                else
                    i15 = (i9 == 1) ? vc : 0;

                if (explicitFaceSkin != 0)
                    i16 = st1.ReadUnsignedShort();
                else
                    i16 = (i7 == 1) ? fc : 0;
            }

            // 3) Compute block offsets
            int dataStart = newProtocol ? tfc + 3 : tfc;   // newProtocol has 3-byte header

            int vertexFlagsOff = dataStart;
            int faceTypeOff = vertexFlagsOff + vc;

            int faceIndexOpcodesOff = faceTypeOff;
            if (hasFaceType)
                faceIndexOpcodesOff = faceTypeOff + fc;

            int offset = faceIndexOpcodesOff + fc;

            int facePriorityOff = offset;
            if (i5 == 255)
                offset += fc;

            int faceSkinOff = offset;
            offset += i16;

            int vertSkinOff = offset;
            offset += i15;

            int faceAlphaOff = offset;
            if (i6 == 1)
                offset += fc;

            int faceIndexSmartOff = offset;
            offset += i13;

            int faceTextureOff = offset;
            if (i8 == 1)
                offset += fc * 2;

            int faceTexCoordOff = offset;
            offset += i14;

            int faceColourOff = offset;
            offset += fc * 2;

            int vertexXOff = offset;
            offset += i10;

            int vertexYOff = offset;
            offset += i11;

            int vertexZOff = offset;
            offset += i12;

            // 4) Populate counts
            VertexCount = vc;
            TriangleCount = fc;
            TexturedTriangleCount = tfc;

            // 5) Allocate arrays
            VertX = new int[vc];
            VertY = new int[vc];
            VertZ = new int[vc];

            faceIndices1 = new int[fc];
            faceIndices2 = new int[fc];
            faceIndices3 = new int[fc];

            FaceColour = new short[fc];

            if (hasFaceType)
                FaceRenderType = new sbyte[fc];

            if (i5 == 255)
                FacePriority = new sbyte[fc];
            else
                _globalPriority = (byte) i5;

            if (i6 == 1)
                FaceAlpha = new sbyte[fc];

            if (i7 == 1)
                FaceSkin = new sbyte[fc];

            if (i9 == 1)
                VertSkins = new int[vc];

            if (i8 == 1) {
                FaceTextures = new short[fc];
            }

            if (i8 == 1 && tfc > 0) {
                TextureCoordinates = new sbyte[fc];
            }

            if (tfc > 0) {
                TextureType = new sbyte[tfc];
                TexIndA = new short[tfc];
                TexIndB = new short[tfc];
                TexIndC = new short[tfc];
            }

            // 6) Decode textured face types (at the start of data section)
            if (tfc > 0) {
                st1.Seek(newProtocol ? 3 : 0);
                for (int i = 0; i < tfc; i++)
                    TextureType![i] = st1.ReadSignedByte();
            }

            // 7) Decode vertex positions
            st1.Seek(vertexFlagsOff);
            st2.Seek(vertexXOff);
            st3.Seek(vertexYOff);
            st4.Seek(vertexZOff);
            st5.Seek(vertSkinOff);

            int px = 0, py = 0, pz = 0;
            for (int i = 0; i < vc; i++) {
                int f = st1.ReadUnsignedByte();
                if ((f & 1) != 0) px += st2.ReadShortSmart();
                if ((f & 2) != 0) py += st3.ReadShortSmart();
                if ((f & 4) != 0) pz += st4.ReadShortSmart();
                VertX[i] = px;
                VertY[i] = py;
                VertZ[i] = pz;
                if (i9 == 1) {
                    if (explicitVertSkin != 0)
                        VertSkins![i] = st5.ReadSpecialSmart();
                    else {
                        int id = st5.ReadUnsignedByte();
                        VertSkins![i] = (id == 255) ? -1 : id;
                    }
                }
            }

            // 7b) Upscale old-format models to match the coordinate space.
            // Hydra client (Class141:1170) only shifts for formatType < 13;
            // newer models (formatType >= 13) already use full-scale coordinates.
            if (FormatType < 13) {
                for (int i = 0; i < vc; i++) {
                    VertX[i] <<= 2;
                    VertY[i] <<= 2;
                    VertZ[i] <<= 2;
                }
            }

            // 8) Decode face data — each field from its own stream to avoid caret collision
            st1.Seek(faceColourOff);     // face colours
            st2.Seek(faceTypeOff);       // face render type (if hasFaceType)
            st3.Seek(facePriorityOff);   // priorities (if i5==255)
            st4.Seek(faceAlphaOff);      // alpha (if i6==1)
            st5.Seek(faceSkinOff);       // face skins (if i7==1)
            st6.Seek(faceTextureOff);    // face textures (if i8==1)
            st7.Seek(faceTexCoordOff);   // texture coords (if i8==1 && tfc>0)

            for (int i = 0; i < fc; i++) {
                FaceColour[i] = (short) st1.ReadUnsignedShort();

                if (hasFaceType)
                    FaceRenderType![i] = st2.ReadSignedByte();

                if (i5 == 255)
                    FacePriority![i] = st3.ReadSignedByte();

                if (i6 == 1)
                    FaceAlpha![i] = st4.ReadSignedByte();

                if (i7 == 1) {
                    if (explicitFaceSkin != 0)
                        FaceSkin![i] = (sbyte) st5.ReadSpecialSmart();
                    else {
                        int id = st5.ReadUnsignedByte();
                        FaceSkin![i] = (id == 255) ? (sbyte) -1 : (sbyte) id;
                    }
                }

                if (i8 == 1) {
                    FaceTextures![i] = (short) (st6.ReadUnsignedShort() - 1);
                }

                if (TextureCoordinates != null) {
                    if (FaceTextures![i] == -1)
                        TextureCoordinates[i] = -1;
                    else if (FormatType >= 16)
                        TextureCoordinates[i] = (sbyte) (st7.ReadShortSmart() - 1);
                    else
                        TextureCoordinates[i] = (sbyte) (st7.ReadUnsignedByte() - 1);
                }
            }

            // 9) Decode triangle indices
            st1.Seek(faceIndexSmartOff);
            st2.Seek(faceIndexOpcodesOff);

            int a = 0, bPrev = 0, cPrev = 0, idxPtr = 0;
            for (int i = 0; i < fc; i++) {
                int raw = st2.ReadUnsignedByte();
                int op = newProtocol ? (raw & 0x7) : raw;  // newProtocol masks to 3 bits
                if (op == 1) {
                    a = st1.ReadShortSmart() + idxPtr;
                    bPrev = st1.ReadShortSmart() + a;
                    cPrev = st1.ReadShortSmart() + bPrev;
                    idxPtr = cPrev;
                } else if (op == 2) {
                    bPrev = cPrev;
                    cPrev = st1.ReadShortSmart() + idxPtr;
                    idxPtr = cPrev;
                } else if (op == 3) {
                    a = cPrev;
                    cPrev = st1.ReadShortSmart() + idxPtr;
                    idxPtr = cPrev;
                } else if (op == 4) {
                    int tmpA = a;
                    a = bPrev;
                    bPrev = tmpA;
                    cPrev = st1.ReadShortSmart() + idxPtr;
                    idxPtr = cPrev;
                }

                faceIndices1[i] = a;
                faceIndices2[i] = bPrev;
                faceIndices3[i] = cPrev;
            }

            // 10) Decode textured face references (type 0 = simple triangles)
            if (tfc > 0) {
                // Seek past the complex texture geometry to the type-0 block
                // The offset layout was already computed above
                st1.Seek(offset);  // vertexZOff + i12 = end of vertex Z data = start of tex geometry
                for (int i = 0; i < tfc; i++) {
                    int type = TextureType != null ? (TextureType[i] & 0xFF) : 0;
                    if (type == 0) {
                        TexIndA![i] = (short) st1.ReadUnsignedShort();
                        TexIndB![i] = (short) st1.ReadUnsignedShort();
                        TexIndC![i] = (short) st1.ReadUnsignedShort();
                    }
                }
            }
        }

        /// <summary>
        /// Computes per-vertex and per-face normals for lighting calculations.
        /// </summary>
        private void ComputeNormals() {
            if (VertexNormals != null)
                return;

            DebugUtil.Debug("[ComputeNormals] Generating normals", DebugUtil.LOG_DETAIL.ADVANCED);

            VertexNormals = new VertexNormal[VertexCount];
            for (int i = 0 ; i < VertexCount ; ++i)
                VertexNormals[i] = new VertexNormal();

            for (int i = 0 ; i < TriangleCount ; ++i) {
                int vertexA = faceIndices1[i];
                int vertexB = faceIndices2[i];
                int vertexC = faceIndices3[i];

                if ((uint)vertexA >= (uint)VertexCount ||
                    (uint)vertexB >= (uint)VertexCount ||
                    (uint)vertexC >= (uint)VertexCount)
                    continue;

                int xA = VertX[vertexB] - VertX[vertexA];
                int yA = VertY[vertexB] - VertY[vertexA];
                int zA = VertZ[vertexB] - VertZ[vertexA];

                int xB = VertX[vertexC] - VertX[vertexA];
                int yB = VertY[vertexC] - VertY[vertexA];
                int zB = VertZ[vertexC] - VertZ[vertexA];

                int nx = yA * zB - yB * zA;
                int ny = zA * xB - zB * xA;
                int nz = xA * yB - xB * yA;

                while (nx > 8192 || ny > 8192 || nz > 8192 || nx < -8192 || ny < -8192 || nz < -8192) {
                    nx >>= 1;
                    ny >>= 1;
                    nz >>= 1;
                }

                int length = (int) Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length <= 0)
                    length = 1;

                nx = nx * 256 / length;
                ny = ny * 256 / length;
                nz = nz * 256 / length;

                sbyte renderType = FaceRenderType == null ? (sbyte) 0 : FaceRenderType[i];

                if (renderType == 0) {
                    VertexNormal vn = VertexNormals[vertexA];
                    vn.x += nx;
                    vn.y += ny;
                    vn.z += nz;
                    vn.magnitude++;

                    vn = VertexNormals[vertexB];
                    vn.x += nx;
                    vn.y += ny;
                    vn.z += nz;
                    vn.magnitude++;

                    vn = VertexNormals[vertexC];
                    vn.x += nx;
                    vn.y += ny;
                    vn.z += nz;
                    vn.magnitude++;
                }
                else if (renderType == 1) {
                    if (FaceNormals == null)
                        FaceNormals = new FaceNormal[TriangleCount];

                    FaceNormal fn = FaceNormals[i] = new FaceNormal();
                    fn.x = nx;
                    fn.y = ny;
                    fn.z = nz;
                }
            }
        }

        /// <summary>
        /// Computes UV coordinates for textured triangles.
        /// </summary>
        private void ComputeTextureUVCoordinates() {
            FaceTextureUCoordinates = new float[TriangleCount][];
            FaceTextureVCoordinates = new float[TriangleCount][];

            for (int i = 0 ; i < TriangleCount ; i++) {
                int textureCoordinate = TextureCoordinates == null ? -1 : TextureCoordinates[i];
                int textureIdx = FaceTextures == null ? -1 : FaceTextures[i];

                if (textureIdx != -1) {
                    float[] u = new float[3];
                    float[] v = new float[3];

                    if (textureCoordinate == -1) {
                        u[0] = 0f; v[0] = 1f;
                        u[1] = 1f; v[1] = 1f;
                        u[2] = 0f; v[2] = 0f;
                    }
                    else {
                        textureCoordinate &= 0xFF;

                        sbyte textureRenderType = 0;
                        if (TextureType != null && textureCoordinate < TextureType.Length)
                            textureRenderType = TextureType[textureCoordinate];

                        if (textureRenderType == 0) {
                            int faceVertexIdx1 = faceIndices1[i];
                            int faceVertexIdx2 = faceIndices2[i];
                            int faceVertexIdx3 = faceIndices3[i];

                            if ((uint)faceVertexIdx1 >= (uint)VertexCount ||
                                (uint)faceVertexIdx2 >= (uint)VertexCount ||
                                (uint)faceVertexIdx3 >= (uint)VertexCount)
                                continue;

                            if (TexIndA == null || textureCoordinate >= TexIndA.Length)
                                continue;

                            short triangleVertexIdx1 = TexIndA[textureCoordinate];
                            short triangleVertexIdx2 = TexIndB![textureCoordinate];
                            short triangleVertexIdx3 = TexIndC![textureCoordinate];

                            if ((uint)triangleVertexIdx1 >= (uint)VertexCount ||
                                (uint)triangleVertexIdx2 >= (uint)VertexCount ||
                                (uint)triangleVertexIdx3 >= (uint)VertexCount)
                                continue;

                            float triangleX = VertX[triangleVertexIdx1];
                            float triangleY = VertY[triangleVertexIdx1];
                            float triangleZ = VertZ[triangleVertexIdx1];

                            float f882 = VertX[triangleVertexIdx2] - triangleX;
                            float f883 = VertY[triangleVertexIdx2] - triangleY;
                            float f884 = VertZ[triangleVertexIdx2] - triangleZ;
                            float f885 = VertX[triangleVertexIdx3] - triangleX;
                            float f886 = VertY[triangleVertexIdx3] - triangleY;
                            float f887 = VertZ[triangleVertexIdx3] - triangleZ;
                            float f888 = VertX[faceVertexIdx1] - triangleX;
                            float f889 = VertY[faceVertexIdx1] - triangleY;
                            float f890 = VertZ[faceVertexIdx1] - triangleZ;
                            float f891 = VertX[faceVertexIdx2] - triangleX;
                            float f892 = VertY[faceVertexIdx2] - triangleY;
                            float f893 = VertZ[faceVertexIdx2] - triangleZ;
                            float f894 = VertX[faceVertexIdx3] - triangleX;
                            float f895 = VertY[faceVertexIdx3] - triangleY;
                            float f896 = VertZ[faceVertexIdx3] - triangleZ;

                            float f897 = f883 * f887 - f884 * f886;
                            float f898 = f884 * f885 - f882 * f887;
                            float f899 = f882 * f886 - f883 * f885;
                            float f900 = f886 * f899 - f887 * f898;
                            float f901 = f887 * f897 - f885 * f899;
                            float f902 = f885 * f898 - f886 * f897;
                            float denom1 = f900 * f882 + f901 * f883 + f902 * f884;
                            if (MathF.Abs(denom1) < 1e-6f)
                            {
                                u[0] = 0f; u[1] = 1f; u[2] = 0f;
                                v[0] = 1f; v[1] = 1f; v[2] = 0f;
                            }
                            else
                            {
                                float f903 = 1.0f / denom1;

                                u[0] = (f900 * f888 + f901 * f889 + f902 * f890) * f903;
                                u[1] = (f900 * f891 + f901 * f892 + f902 * f893) * f903;
                                u[2] = (f900 * f894 + f901 * f895 + f902 * f896) * f903;

                                f900 = f883 * f899 - f884 * f898;
                                f901 = f884 * f897 - f882 * f899;
                                f902 = f882 * f898 - f883 * f897;
                                float denom2 = f900 * f885 + f901 * f886 + f902 * f887;
                                if (MathF.Abs(denom2) < 1e-6f)
                                {
                                    v[0] = 1f; v[1] = 1f; v[2] = 0f;
                                }
                                else
                                {
                                    f903 = 1.0f / denom2;
                                    v[0] = (f900 * f888 + f901 * f889 + f902 * f890) * f903;
                                    v[1] = (f900 * f891 + f901 * f892 + f902 * f893) * f903;
                                    v[2] = (f900 * f894 + f901 * f895 + f902 * f896) * f903;
                                }
                            }
                        }
                    }

                    FaceTextureUCoordinates[i] = u;
                    FaceTextureVCoordinates[i] = v;
                }
            }
        }

        /// <summary>
        /// Builds vertex animation lookup tables from packed vertex groups.
        /// </summary>
        private void ComputeAnimationTables() {
            if (VertSkins != null) {
                // First pass: find max group, skipping negative sentinels (-1 = no group)
                int numGroups = 0;
                for (int i = 0; i < VertexCount; ++i) {
                    int group = VertSkins[i];
                    if (group > numGroups)
                        numGroups = group;
                }

                int[] groupCounts = new int[numGroups + 1];
                for (int i = 0; i < VertexCount; ++i) {
                    int group = VertSkins[i];
                    if (group >= 0)
                        groupCounts[group]++;
                }

                VertexGroups = new int[numGroups + 1][];
                for (int i = 0; i <= numGroups; ++i) {
                    VertexGroups[i] = new int[groupCounts[i]];
                    groupCounts[i] = 0;
                }

                for (int i = 0; i < VertexCount; i++) {
                    int g = VertSkins[i];
                    if (g >= 0)
                        VertexGroups[g][groupCounts[g]++] = i;
                }

                VertSkins = null;
            }
        }

        /// <summary>Simple container for accumulated vertex normals.</summary>
        public class VertexNormal {
            public int x, y, z, magnitude;
        }

        /// <summary>Container for face normal vectors.</summary>
        public class FaceNormal {
            public int x, y, z;
        }

        /// <summary>
        /// Global render priority used when <see cref="FacePriority"/> is null.
        /// </summary>
        private byte _globalPriority;

        /// <summary>Gets the global render priority.</summary>
        public byte GlobalPriority => _globalPriority;

        /// <summary>
        /// Returns the effective render priority for face <paramref name="i"/>.
        /// Uses per-face array when available, otherwise the global value.
        /// </summary>
        public int GetFacePriority(int i) => FacePriority != null ? FacePriority[i] : _globalPriority;

        #endregion

        #region ≡ helper methods

        /// <summary>
        /// Creates a shallow clone with deep-copied mutable arrays so that
        /// NPC/item recolour transforms don't corrupt the cached original.
        /// </summary>
        public ModelDefinition CloneForRendering() {
            var clone = (ModelDefinition)MemberwiseClone();
            if (FaceColour != null) clone.FaceColour = (short[])FaceColour.Clone();
            if (FaceTextures != null) clone.FaceTextures = (short[])FaceTextures.Clone();
            if (VertX != null) clone.VertX = (int[])VertX.Clone();
            if (VertY != null) clone.VertY = (int[])VertY.Clone();
            if (VertZ != null) clone.VertZ = (int[])VertZ.Clone();
            return clone;
        }

        /// <summary>
        /// Computes per-vertex lit colours using Gouraud shading (smooth) or
        /// flat shading, matching the RS client light pipeline.
        /// Returns <c>int[TriangleCount][3]</c> of packed 0xRRGGBB per vertex of each face.
        /// </summary>
        public int[][] ComputeVertexColours() {
            if (VertexNormals == null)
                ComputeNormals();

            double lx = -50, ly = -10, lz = -50;
            double lLen = Math.Sqrt(lx * lx + ly * ly + lz * lz);
            lx /= lLen; ly /= lLen; lz /= lLen;

            const int ambient = 64;
            const int contrast = 768;

            var result = new int[TriangleCount][];

            for (int i = 0; i < TriangleCount; i++) {
                int a = faceIndices1[i];
                int b = faceIndices2[i];
                int c = faceIndices3[i];

                if ((uint)a >= (uint)VertexCount ||
                    (uint)b >= (uint)VertexCount ||
                    (uint)c >= (uint)VertexCount) {
                    result[i] = new int[] { 0x808080, 0x808080, 0x808080 };
                    continue;
                }

                int baseHsl = FaceColour != null ? (FaceColour[i] & 0xFFFF) : 0;
                sbyte renderType = FaceRenderType != null ? FaceRenderType[i] : (sbyte)0;

                // Repack the BASE colour once per face — this converts the raw HSL
                // into the palette's (hue | satRatio | chroma) space.  Lighting then
                // scales only the chroma, preserving hue and saturation.
                //
                // Textured faces also use the HSL colour path (not greyscale) so that
                // when a sprite texture fails to load and the white fallback is used,
                // the face still shows its base colour instead of appearing white/grey.
                // For old-format models FaceColour is 127 (neutral) so this produces
                // near-white which is correct.  For RS2 models FaceColour retains
                // the original HSL value, giving a reasonable colour approximation.
                int repacked = RepackHsl(baseHsl);

                if (renderType == 1 && FaceNormals != null && FaceNormals[i] != null) {
                    // Flat shading: use face normal for all 3 vertices
                    FaceNormal fn = FaceNormals[i];
                    double mag = Math.Sqrt((double)fn.x * fn.x + (double)fn.y * fn.y + (double)fn.z * fn.z);
                    if (mag < 1) mag = 1;
                    double dot = (fn.x * lx + fn.y * ly + fn.z * lz) / mag;
                    int lighting = (int)(ambient + contrast * dot);
                    lighting = Math.Clamp(lighting, 0, 127);

                    int litChroma = (repacked & 0x7F) * lighting >> 7;
                    litChroma = Math.Clamp(litChroma, 2, 126);
                    int rgb = HslToRgb((repacked & 0xFF80) | litChroma);
                    result[i] = new int[] { rgb, rgb, rgb };
                } else {
                    // Gouraud shading: per-vertex normals
                    int[] verts = { a, b, c };
                    var colours = new int[3];
                    for (int vi = 0; vi < 3; vi++) {
                        VertexNormal vn = VertexNormals![verts[vi]];
                        double mag = Math.Sqrt((double)vn.x * vn.x + (double)vn.y * vn.y + (double)vn.z * vn.z);
                        if (mag < 1) mag = 1;
                        double dot = (vn.x * lx + vn.y * ly + vn.z * lz) / mag;
                        int lighting = (int)(ambient + contrast * dot);
                        lighting = Math.Clamp(lighting, 0, 127);

                        int litChroma = (repacked & 0x7F) * lighting >> 7;
                        litChroma = Math.Clamp(litChroma, 2, 126);
                        colours[vi] = HslToRgb((repacked & 0xFF80) | litChroma);
                    }
                    result[i] = colours;
                }
            }

            return result;
        }

        /// <summary>
        /// Computes per-face vertex base colours WITHOUT directional lighting applied.
        /// Returns <c>int[TriangleCount][3]</c> of packed 0xRRGGBB at full brightness,
        /// suitable for dynamic lighting in the GPU shader.
        /// </summary>
        public int[][] ComputeUnlitColours() {
            var result = new int[TriangleCount][];

            for (int i = 0; i < TriangleCount; i++) {
                int a = faceIndices1[i];
                int b = faceIndices2[i];
                int c = faceIndices3[i];

                if ((uint)a >= (uint)VertexCount ||
                    (uint)b >= (uint)VertexCount ||
                    (uint)c >= (uint)VertexCount) {
                    result[i] = new int[] { 0x808080, 0x808080, 0x808080 };
                    continue;
                }

                int baseHsl = FaceColour != null ? (FaceColour[i] & 0xFFFF) : 0;
                int repacked = RepackHsl(baseHsl);
                int rgb = HslToRgb(repacked);
                result[i] = new int[] { rgb, rgb, rgb };
            }

            return result;
        }

        /// <summary>
        /// Returns normalised normal vectors for each vertex of each face.
        /// For flat-shaded faces (renderType==1), all three vertices share the face normal.
        /// For Gouraud-shaded faces, each vertex uses its accumulated vertex normal.
        /// Components are transformed to match the rendering coordinate system (Y and Z negated).
        /// Returns <c>float[TriangleCount][9]</c> — three (x,y,z) triples per face.
        /// </summary>
        public float[][] ComputeFaceVertexNormals() {
            if (VertexNormals == null)
                ComputeNormals();

            var result = new float[TriangleCount][];

            for (int i = 0; i < TriangleCount; i++) {
                int a = faceIndices1[i], b = faceIndices2[i], c = faceIndices3[i];

                if ((uint)a >= (uint)VertexCount ||
                    (uint)b >= (uint)VertexCount ||
                    (uint)c >= (uint)VertexCount) {
                    result[i] = new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 };
                    continue;
                }

                sbyte renderType = FaceRenderType != null ? FaceRenderType[i] : (sbyte)0;
                result[i] = new float[9];

                if (renderType == 1 && FaceNormals != null && FaceNormals[i] != null) {
                    FaceNormal fn = FaceNormals[i];
                    float mag = MathF.Sqrt(fn.x * fn.x + fn.y * fn.y + fn.z * fn.z);
                    if (mag < 1) mag = 1;
                    float nx = fn.x / mag, ny = -fn.y / mag, nz = -fn.z / mag;
                    result[i] = new float[] { nx, ny, nz, nx, ny, nz, nx, ny, nz };
                } else {
                    int[] verts = { a, b, c };
                    for (int vi = 0; vi < 3; vi++) {
                        VertexNormal vn = VertexNormals![verts[vi]];
                        float mag = MathF.Sqrt(vn.x * vn.x + vn.y * vn.y + vn.z * vn.z);
                        if (mag < 1) mag = 1;
                        result[i][vi * 3 + 0] = vn.x / mag;
                        result[i][vi * 3 + 1] = -vn.y / mag;
                        result[i][vi * 3 + 2] = -vn.z / mag;
                    }
                }
            }

            return result;
        }

        private static readonly int[] _hsl2Rgb = BuildHslLut();

        /// <summary>Converts a packed HSV index to 24‑bit sRGB via the precomputed palette.</summary>
        public static int HslToRgb(int hsl) => _hsl2Rgb[hsl & 0xFFFF];

        /// <summary>
        /// Builds the HSV→RGB lookup table matching Hydra's <c>Class122.method2199()</c>.
        /// Packed format: <c>(hue6 &lt;&lt; 10) | (sat3 &lt;&lt; 7) | value7</c>.
        /// Uses HSV sector decomposition (not HSL) so high-value colours stay saturated.
        /// RGB 0x000000 is remapped to 0x000001 (black = transparent in the engine).
        /// </summary>
        private static int[] BuildHslLut(double brightness = 0.7) {
            var lut = new int[65536];
            int idx = 0;
            for (int hueSat = 0; hueSat < 512; hueSat++) {
                double hue = ((hueSat >> 3) / 64.0 + 0.0078125) * 360.0;
                double sat = (hueSat & 7) / 8.0 + 0.0625;

                for (int vi = 0; vi < 128; vi++) {
                    double val = vi / 128.0;

                    // HSV→RGB standard 6-sector decomposition
                    double hSector = hue / 60.0;
                    int sector = (int)hSector % 6;
                    double frac = hSector - (int)hSector;
                    double p = val * (1.0 - sat);
                    double q = val * (1.0 - sat * frac);
                    double t = val * (1.0 - sat * (1.0 - frac));

                    double r, g, b;
                    switch (sector) {
                        case 0: r = val; g = t;   b = p;   break;
                        case 1: r = q;   g = val; b = p;   break;
                        case 2: r = p;   g = val; b = t;   break;
                        case 3: r = p;   g = q;   b = val; break;
                        case 4: r = t;   g = p;   b = val; break;
                        case 5: r = val; g = p;   b = q;   break;
                        default: r = g = b = 0; break;
                    }

                    int ri = (int)(Math.Pow(r, brightness) * 256.0);
                    int gi = (int)(Math.Pow(g, brightness) * 256.0);
                    int bi = (int)(Math.Pow(b, brightness) * 256.0);
                    if (ri > 255) ri = 255;
                    if (gi > 255) gi = 255;
                    if (bi > 255) bi = 255;

                    int rgb = (ri << 16) | (gi << 8) | bi;
                    if (rgb == 0) rgb = 1; // engine treats 0x000000 as transparent
                    lut[idx++] = rgb;
                }
            }
            return lut;
        }

        /// <summary>
        /// Repacks a raw 16-bit HSL value for palette lookup.
        /// Matches Hydra's <c>Class111_Sub2.method2117()</c>.
        /// </summary>
        private static int RepackHsl(int raw) {
            int hue = (raw >> 10) & 0x3F;
            int sat = (raw >> 3) & 0x70;
            int lum = raw & 0x7F;

            sat = lum >= 65 ? (127 - lum) * sat >> 7 : lum * sat >> 7;

            int chroma = lum + sat;
            int satRatio = chroma != 0 ? (sat << 8) / chroma : sat << 1;

            return (hue << 10) | ((satRatio >> 4) << 7) | chroma;
        }

        #endregion
    }
}
