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

        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int TexturedTriangleCount { get; private set; }

        public int[] VertX = Array.Empty<int>();
        public int[] VertY = Array.Empty<int>();
        public int[] VertZ = Array.Empty<int>();
        public int[]? VertSkins;

        public int[] faceIndices1 = Array.Empty<int>();
        public int[] faceIndices2 = Array.Empty<int>();
        public int[] faceIndices3 = Array.Empty<int>();

        public short[] FaceColour = Array.Empty<short>();      // HSL‑555, convert via HslToRgb()
        public sbyte[]? FaceRenderType;    // 0 = flat, 1 = textured
        public sbyte[]? FacePriority;      // 0‑255, or null if global
        public sbyte[]? FaceAlpha;         // 0‑255
        public sbyte[]? FaceSkin;

        public sbyte[]? TextureType;       // per‑texturedFace flags (0,1,2,3)
        public sbyte[] TextureCoordinates;
        public short[] FaceTextures;
        public short[]? TexIndA, TexIndB, TexIndC; // reference vertices for UV solve

        public int[][] AnimayaGroups { get; private set; }
        public int[][] AnimayaScales { get; private set; }

        public ushort ParticleEffectId { get; private set; } = 0xFFFF; // 0xFFFF == none
        public ushort[]? ParticleAnchorVert; // optional vertex IDs

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

            // Parse the last 2 bytes to get the model format
            ModelFormat modelFormat = GetModelFormat(rawStream);

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
                    //DecodeRS3(stream, xteaKey);
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
        public static ModelFormat GetModelFormat(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            long length = stream.Length;
            if (length < 2)
                throw new InvalidDataException("Stream too short to determine model format.");

            // Read without moving the stream’s Position
            byte last = stream.Get((int) (length - 1));
            byte penultimate = stream.Get((int) (length - 2));

            // 0xFF FD => newest (type3)
            if (penultimate == 0xFF && last == 0xFD)
                return ModelFormat.Newest;

            // 0xFF FE => newer (type2)
            if (penultimate == 0xFF && last == 0xFE)
                return ModelFormat.Newer;

            // 0xFF FF => type1 (also handled as 'newer')
            if (penultimate == 0xFF && last == 0xFF)
                return ModelFormat.Newer;

            // anything else => old RS2-legacy format
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
        private void DecodeRS2(JagStream modelStream, int[] xteaKey) {
            // 1) Read all bytes so we can fork multiple JagStreams
            byte[] data = modelStream.ToArray();

            DebugUtil.PrintByteArray(data);

            // 2) Create five JagStreams to mirror the Java InputStreams (var4…var8)
            var var4 = new JagStream(data);
            var var5 = new JagStream(data);
            var var6 = new JagStream(data);
            var var7 = new JagStream(data);
            var var8 = new JagStream(data);

            // 3) Seek var4 to the start of the 23-byte footer
            var4.Seek(data.Length - 23);

            // 4) Read counts and flags
            int vertexCountFlag = var4.ReadUnsignedShort();
            int triangleCountFlag = var4.ReadUnsignedShort();
            int texturedCountFlag = var4.ReadUnsignedByte();

            bool hasFaceRender = var4.ReadUnsignedByte() == 1;
            int renderPriorities = (byte) var4.ReadUnsignedByte();
            bool hasPerFacePrio = renderPriorities == 255;
            bool hasTransparency = var4.ReadUnsignedByte() == 1;
            bool hasTransGroup = var4.ReadUnsignedByte() == 1;
            bool hasVertGroup = var4.ReadUnsignedByte() == 1;
            bool hasAnimaya = var4.ReadUnsignedByte() == 1;

            DebugUtil.Debug(
    $"Flags: vertexCount={vertexCountFlag}, triCount={triangleCountFlag}, texCount={texturedCountFlag}, " +
    $"hasFaceRender={hasFaceRender}, hasPerFacePrio={hasPerFacePrio}, hasTransparency={hasTransparency}, " +
    $"hasTransGroup={hasTransGroup}, hasVertGroup={hasVertGroup}, hasAnimaya={hasAnimaya}",
    DebugUtil.LOG_DETAIL.INSANE
);


            // 5) Read the five explicit‐offset shorts
            int offVertexData = var4.ReadUnsignedShort();
            int offFaceColour = var4.ReadUnsignedShort();
            int offFaceData = var4.ReadUnsignedShort();
            int offFaceIndex = var4.ReadUnsignedShort();
            int offTextureData = var4.ReadUnsignedShort();

            DebugUtil.Debug(
    $"Footer offsets: offVertexData={offVertexData}, offFaceColour={offFaceColour}, offFaceData={offFaceData}, " +
    $"offFaceIndex={offFaceIndex}, offTextureData={offTextureData}",
    DebugUtil.LOG_DETAIL.INSANE
);

            // 6) Compute the intermediate offsets exactly as in Java
            byte offset = 0;
            int verticesOffset = offset + vertexCountFlag;
            int indices1Offset = verticesOffset;

            verticesOffset += triangleCountFlag;

            int var26 = verticesOffset;

            if (renderPriorities == 255)
                verticesOffset += triangleCountFlag;

            int base3 = verticesOffset;

            if (hasTransGroup)
                verticesOffset += triangleCountFlag;

            int base4 = verticesOffset;

            if (hasFaceRender)
                verticesOffset += triangleCountFlag;

            int base5 = verticesOffset;
            verticesOffset += offTextureData;

            int base6 = verticesOffset;

            if (hasTransparency)
                verticesOffset += triangleCountFlag;

            int base7 = verticesOffset;
            verticesOffset += offFaceIndex;
            int base8 = verticesOffset;
            verticesOffset += triangleCountFlag * 2;
            int base9 = verticesOffset;
            verticesOffset += texturedCountFlag * 6;
            int base10 = verticesOffset;
            verticesOffset += offVertexData;
            int base11 = verticesOffset;
            verticesOffset += offFaceColour;

            DebugUtil.Debug(
    $"Computed bases: var26(z‐deltas)={var26}, base3(animaya)={base3}, base4(faceRender)={base4}, " +
    $"base5(vertGroup)={base5}, base6(transparency)={base6}, base7(faceIndex)={base7}, base8(faceData)={base8}, " +
    $"base9(texture)={base9}, base10(x‐deltas)={base10}, base11(y‐deltas)={base11}",
    DebugUtil.LOG_DETAIL.INSANE
);

            // 7) Populate our public counts
            VertexCount = vertexCountFlag;
            TriangleCount = triangleCountFlag;
            TexturedTriangleCount = texturedCountFlag;

            // 8) Allocate geometry arrays
            VertX = new int[VertexCount];
            VertY = new int[VertexCount];
            VertZ = new int[VertexCount];

            faceIndices1 = new int[TriangleCount];
            faceIndices2 = new int[TriangleCount];
            faceIndices3 = new int[TriangleCount];

            if (TexturedTriangleCount > 0) {
                TextureType = new sbyte[TexturedTriangleCount];
                TexIndA = new short[TexturedTriangleCount];
                TexIndB = new short[TexturedTriangleCount];
                TexIndC = new short[TexturedTriangleCount];
            }

            if (hasVertGroup) {
                VertSkins = new int[VertexCount];
            }

            if (hasFaceRender) {
                FaceRenderType = new sbyte[TriangleCount];
                TextureCoordinates = new sbyte[TriangleCount];
                FaceTextures = new short[TriangleCount];
            }

            if (hasPerFacePrio)
                FacePriority = new sbyte[TriangleCount];
            else
                _globalPriority = (byte) renderPriorities;


            if (hasTransparency)
                FaceAlpha = new sbyte[TriangleCount];

            if (hasTransGroup)
                FaceSkin = new sbyte[TriangleCount];

            if (hasAnimaya) {
                AnimayaGroups = new int[VertexCount][];
                AnimayaScales = new int[VertexCount][];
            }

            FaceColour = new short[TriangleCount];


            DebugUtil.Debug($"Data.Length        = {data.Length}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"VertexCount        = {VertexCount}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"triangleCountFlag  = {TriangleCount}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"texturedCountFlag  = {TexturedTriangleCount}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"offset (flags)     = {0}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"base10 (X deltas)  = {base10}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"base11 (Y deltas)  = {base11}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"z-deltas (var26)    = {var26}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"vertexGroup (base5)= {base5}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"… faceData (base8) = {base8}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"… texture start (base9)= {base9}", DebugUtil.LOG_DETAIL.INSANE);

            // 9) Position each JagStream at its block start
            var4.Seek(offset); //flags
            var5.Seek(base10); //X deltas
            var6.Seek(base11); //Y deltas
            var7.Seek(var26);
            var8.Seek(base5);

            DebugUtil.Debug($"var4.Position = {var4.Position}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"var5.Position = {var5.Position}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"var6.Position = {var6.Position}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"var7.Position = {var7.Position}", DebugUtil.LOG_DETAIL.INSANE);
            DebugUtil.Debug($"var8.Position = {var8.Position}", DebugUtil.LOG_DETAIL.INSANE);


            // 10) Decode vertex positions & optional vertex‐group
            int cx = 0, cy = 0, cz = 0;
            for (int i = 0 ; i < VertexCount ; i++) {
                int mask = var4.ReadUnsignedByte();
                int dx = (mask & 1) != 0 ? var5.ReadShortSmart() : 0;
                int dy = (mask & 2) != 0 ? var6.ReadShortSmart() : 0;
                int dz = (mask & 4) != 0 ? var7.ReadShortSmart() : 0;
                cx += dx; cy += dy; cz += dz;
                VertX[i] = cx;
                VertY[i] = cy;
                VertZ[i] = cz;
                DebugUtil.Debug($"v[{i}]: mask=0x{mask:X2}, dx={dx}, dy={dy}, dz={dz}, pos=({cx},{cy},{cz})", DebugUtil.LOG_DETAIL.INSANE);

                if (hasVertGroup) {
                    VertSkins![i] = (byte) var8.ReadUnsignedByte();
                    DebugUtil.Debug($"  vertGroup[{i}] = {VertSkins![i]}", DebugUtil.LOG_DETAIL.INSANE);
                }
            }

            // 11) Decode animaya (morph) groups if present
            if (hasAnimaya) {
                DebugUtil.Debug($"Decoding animaya for {VertexCount} vertices…", DebugUtil.LOG_DETAIL.INSANE);

                for (int i = 0 ; i < VertexCount ; i++) {
                    int mask = var8.ReadUnsignedByte();
                    //DebugUtil.Debug($" animaya[{i}]: count={mask}", DebugUtil.LOG_DETAIL.INSANE);

                    AnimayaGroups[i] = new int[mask];
                    AnimayaScales[i] = new int[mask];

                    for (int j = 0 ; j < mask ; j++) {
                        AnimayaGroups[i][j] = var8.ReadUnsignedByte();
                        AnimayaScales[i][j] = var8.ReadUnsignedByte();

                        //DebugUtil.Debug($"   animaya[{i}][{j}] = grp={AnimayaGroups[i][j]}, scale={AnimayaScales[i][j]}", DebugUtil.LOG_DETAIL.INSANE);

                    }
                }
            }

            // 12) Prepare face‐colour & flag streams
            var4.Seek(base8);
            var5.Seek(base4);
            var6.Seek(indices1Offset);
            var7.Seek(base6);
            var8.Seek(base3);

            // 13) Decode face colours, render‐type mask, priorities, alpha & trans‐groups
            bool anyTextured = false;
            bool anyRendered = false;

            DebugUtil.Debug($"Decoding {TriangleCount} faces…", DebugUtil.LOG_DETAIL.INSANE);

            for (int i = 0 ; i < TriangleCount ; i++) {
                FaceColour[i] = (short) var4.ReadUnsignedShort();

                DebugUtil.Debug($"f[{i}]: colour=0x{FaceColour[i]:X4}", DebugUtil.LOG_DETAIL.INSANE);

                if (hasFaceRender) {
                    int mask = var5.ReadUnsignedByte();
                    DebugUtil.Debug($" f[{i}] mask=0x{mask:X2}", DebugUtil.LOG_DETAIL.INSANE);

                    if ((mask & 1) != 0) {
                        FaceRenderType![i] = 1;
                        anyRendered = true;
                    }
                    else {
                        FaceRenderType![i] = 0;
                    }

                    if ((mask & 2) == 2) {
                        TextureCoordinates[i] = (sbyte) (mask >> 2);
                        FaceTextures[i] = FaceColour[i];
                        FaceColour[i] = 127;

                        if (FaceTextures[i] != -1)
                            anyRendered = true;
                    }
                    else {
                        TextureCoordinates[i] = -1;
                        FaceTextures[i] = -1;
                    }
                }

                if (hasPerFacePrio)
                    FacePriority![i] = var6.ReadSignedByte();

                if (hasTransparency)
                    FaceAlpha![i] = var7.ReadSignedByte();

                if (hasTransGroup)
                    FaceSkin![i] = var8.ReadSignedByte();
            }

            var4.Seek(base7);
            var5.Seek(indices1Offset);

            // 14) Null‐out arrays that never saw a flag
            if (TextureType != null && !anyTextured)
                TextureType = null;

            if (FaceRenderType != null && !anyRendered)
                FaceRenderType = null;

            // 15) Decode triangle‐strip indices
            int a = 0, b = 0, c = 0, ptr = 0;
            for (int i = 0 ; i < TriangleCount ; i++) {
                int op = var5.ReadUnsignedByte();
                DebugUtil.Debug($" strip[{i}]: op={op}", DebugUtil.LOG_DETAIL.INSANE);

                if (op == 1) {
                    a = ptr + var4.ReadSignedSmart();
                    b = a + var4.ReadSignedSmart();
                    c = b + var4.ReadSignedSmart();
                    ptr = c;
                }
                else if (op == 2) {
                    c = ptr + var4.ReadSignedSmart();
                    ptr = c;
                }
                else if (op == 3) {
                    int tmp = a;
                    a = c;
                    c = ptr + var4.ReadSignedSmart();
                    ptr = c;
                    b = tmp;
                }
                else // op == 4
                {
                    int tmp = a;
                    a = b;
                    b = tmp;
                    c = ptr + var4.ReadSignedSmart();
                    ptr = c;
                }

                DebugUtil.Debug($"  => a={a}, b={b}, c={c}", DebugUtil.LOG_DETAIL.INSANE);

                faceIndices1[i] = (ushort) a;
                faceIndices2[i] = (ushort) b;
                faceIndices3[i] = (ushort) c;
            }

            // 16) Decode textured‐face lookup tables
            DebugUtil.Debug($"Decoding textured faces (count={TexturedTriangleCount})…", DebugUtil.LOG_DETAIL.INSANE);

            var4.Seek(base9);
            if (TextureType != null) {
                for (int i = 0 ; i < TexturedTriangleCount ; i++) {
                    TextureType![i] = 0;
                    TexIndA![i] = (short) var4.ReadUnsignedShort();
                    TexIndB![i] = (short) var4.ReadUnsignedShort();
                    TexIndC![i] = (short) var4.ReadUnsignedShort();
                    DebugUtil.Debug($" tex[{i}] = ({TexIndA![i]},{TexIndB![i]},{TexIndC![i]})", DebugUtil.LOG_DETAIL.INSANE);
                }
            }

            // 17) Done. ParticleEffectId and ParticleAnchorVert are not set in type 2.
        }


        private void DecodeOld(JagStream modelStream, int[] xteaKey) {

            DebugUtil.Debug("Old RS2 model format not supported.");
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
        /// Decode the “newest” RS2 (602–647) model layout (no footer bytes remain in `s`).
        /// Footer has been stripped off upstream, so here we work directly on the full byte[].
        /// </summary>
        /*
        private void DecodeRS3(JagStream full, int[] xteaKey) {
            {
                // 1) Grab the raw buffer
                byte[] b = full.ToArray();

                // 2) Mirror Java's eight InputStreams
                var st2 = new JagStream(b);
                var st3 = new JagStream(b);
                var st4 = new JagStream(b);
                var st5 = new JagStream(b);
                var st6 = new JagStream(b);
                var st7 = new JagStream(b);
                var st8 = new JagStream(b);

                // 3) Seek to the 26‐byte footer at the end
                st2.Seek(b.Length - 26);

                // 4) Read counts & flags exactly as Java did :contentReference[oaicite:12]{index=12}
                int vc = st2.ReadUnsignedShort(); // vertexCount
                int fc = st2.ReadUnsignedShort(); // triangleCount
                int tfc = st2.ReadUnsignedByte();  // numTextureFaces

                bool hasFaceType = st2.ReadUnsignedByte() == 1;
                bool hasTextures = st2.ReadUnsignedByte() == 1; // textureCoordinates & faceTextures
                bool hasPriorities = st2.ReadUnsignedByte() == 255; // 255 means per-face
                bool hasAlpha = st2.ReadUnsignedByte() == 1;
                bool hasTransGrp = st2.ReadUnsignedByte() == 1;
                bool hasVertGrp = st2.ReadUnsignedByte() == 1;
                bool hasAnimaya = st2.ReadUnsignedByte() == 1;

                // Read all of the Jagex‐computed offsets from the footer
                int offVertexFlags = st2.ReadUnsignedShort();
                int offVertexX = st2.ReadUnsignedShort();
                int offVertexY = st2.ReadUnsignedShort();
                int offVertexZ = st2.ReadUnsignedShort();
                int offFaceColours = st2.ReadUnsignedShort();
                int offFaceData = st2.ReadUnsignedShort();
                int offFaceIndexData = st2.ReadUnsignedShort();
                int offFaceTextureData = st2.ReadUnsignedShort();

                // 5) Now set each stream to its start‐of‐block
                st2.Seek(offVertexFlags);
                st3.Seek(offVertexX);
                st4.Seek(offVertexY);
                st5.Seek(offVertexZ);
                st6.Seek(offFaceColours);
                st7.Seek(offFaceData);
                st8.Seek(offFaceIndexData);

                // 6) Allocate your arrays on the C# side
                VertexCount = vc;
                TriangleCount = fc;
                TexturedTriangleCount = tfc;

                VertX = new int[vc];
                VertY = new int[vc];
                VertZ = new int[vc];
                if (hasVertGrp)
                    VertSkins = new int[vc];

                faceIndices1 = new int[fc];
                faceIndices2 = new int[fc];
                faceIndices3 = new int[fc];

                FaceColour = new short[fc];
                if (hasFaceType) FaceRenderType = new sbyte[fc];
                if (hasPriorities) FacePriority = new sbyte[fc];
                if (hasAlpha) FaceAlpha = new sbyte[fc];
                if (hasTransGrp) FaceSkin = new sbyte[fc];
                if (hasTextures) {
                    TextureType = new sbyte[tfc];
                    TexIndA = new short[tfc];
                    TexIndB = new short[tfc];
                    TexIndC = new short[tfc];
                }

                // 7) Decode vertex positions (flags + signedSmart deltas) :contentReference[oaicite:13]{index=13}
                int px = 0, py = 0, pz = 0;
                for (int i = 0 ; i < vc ; i++) {
                    int f = st2.ReadUnsignedByte();
                    if ((f & 1) != 0) px += st3.ReadSignedSmart();
                    if ((f & 2) != 0) py += st4.ReadSignedSmart();
                    if ((f & 4) != 0) pz += st5.ReadSignedSmart();
                    VertX[i] = px;
                    VertY[i] = py;
                    VertZ[i] = pz;
                    if (hasVertGrp)
                        VertSkins[i] = st6.ReadUnsignedByte();
                }

                // 8) Decode face‐colour + face‐flags :contentReference[oaicite:14]{index=14}
                for (int i = 0 ; i < fc ; i++) {
                    FaceColour[i] = (short) st6.ReadUnsignedShort();
                    if (hasFaceType)
                        FaceRenderType[i] = (byte) st7.ReadUnsignedByte();
                    if (hasPriorities)
                        FacePriority[i] = (byte) st7.ReadUnsignedByte();
                    if (hasAlpha)
                        FaceAlpha[i] = (byte) st7.ReadUnsignedByte();
                    if (hasTransGrp)
                        FaceSkin[i] = (byte) st7.ReadUnsignedByte();  // short is fine
                    if (hasTextures)
                        TextureType[i] = (byte) st7.ReadUnsignedByte();

                }

                // 9) Decode the triangle indices (smart‐encoded) :contentReference[oaicite:15]{index=15}
                int a = 0, bPrev = 0, cPrev = 0, idxPtr = 0;
                for (int i = 0 ; i < fc ; i++) {
                    int op = st8.ReadUnsignedByte();
                    if (op == 1) {
                        a = st8.ReadSignedSmart() + idxPtr;
                        bPrev = st8.ReadSignedSmart() + a;
                        cPrev = st8.ReadSignedSmart() + bPrev;
                        idxPtr = cPrev;
                    }
                    else if (op == 2) {
                        int tmp = cPrev;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                        bPrev = tmp;
                    }
                    else if (op == 3) {
                        int tmp = a;
                        a = cPrev;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                        bPrev = tmp;
                    }
                    else if (op == 4) {
                        int tmpA = a;
                        a = bPrev;
                        bPrev = tmpA;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                    }

                    faceIndices1[i] = (ushort) a;
                    faceIndices2[i] = (ushort) bPrev;
                    faceIndices3[i] = (ushort) cPrev;
                }

                // 10) Decode textured‐face blocks :contentReference[oaicite:16]{index=16}
                if (tfc > 0) {
                    // Seek into the texture block
                    var stTex = new JagStream(b);
                    stTex.Seek(offFaceTextureData);
                    for (int i = 0 ; i < tfc ; i++) {
                        TextureType[i] = (byte) stTex.ReadByte();
                        TexIndA[i] = (short) (stTex.ReadUnsignedShort() - 1);
                        TexIndB[i] = (short) (stTex.ReadUnsignedShort() - 1);
                        TexIndC[i] = (short) (stTex.ReadUnsignedShort() - 1);
                    }

                    // Drop textureCoordinates if unused (Java does this check in decodeType3) :contentReference[oaicite:17]{index=17}
                    bool anyUV = false;
                    for (int i = 0 ; i < fc ; i++) {
                        int tcIdx = TextureType[i] & 0xFF;
                        if (tcIdx != 255) {
                            if (faceIndices1[i] != TexIndA[tcIdx] ||
                                faceIndices2[i] != TexIndB[tcIdx] ||
                                faceIndices3[i] != TexIndC[tcIdx]) {
                                anyUV = true;
                                break;
                            }
                        }
                    }
                    if (!anyUV)
                        TextureType = null;
                }

                // 11) Finally compute normals, UV‐coordinates & animation tables just like ModelLoader does
                //ComputeNormals();
                //ComputeTextureUVCoordinates();
                //ComputeAnimationTables();
            }
        }
        */

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
                int textureIdx = FaceTextures == null ? -1 : (FaceTextures[i] & 0xFFFF);

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
                        if (TextureType != null)
                            textureRenderType = TextureType[textureCoordinate];

                        if (textureRenderType == 0) {
                            int faceVertexIdx1 = faceIndices1[i];
                            int faceVertexIdx2 = faceIndices2[i];
                            int faceVertexIdx3 = faceIndices3[i];

                            short triangleVertexIdx1 = TexIndA![textureCoordinate];
                            short triangleVertexIdx2 = TexIndB![textureCoordinate];
                            short triangleVertexIdx3 = TexIndC![textureCoordinate];

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
                            float f903 = 1.0f / (f900 * f882 + f901 * f883 + f902 * f884);

                            u[0] = (f900 * f888 + f901 * f889 + f902 * f890) * f903;
                            u[1] = (f900 * f891 + f901 * f892 + f902 * f893) * f903;
                            u[2] = (f900 * f894 + f901 * f895 + f902 * f896) * f903;

                            f900 = f883 * f899 - f884 * f898;
                            f901 = f884 * f897 - f882 * f899;
                            f902 = f882 * f898 - f883 * f897;
                            f903 = 1.0f / (f900 * f885 + f901 * f886 + f902 * f887);

                            v[0] = (f900 * f888 + f901 * f889 + f902 * f890) * f903;
                            v[1] = (f900 * f891 + f901 * f892 + f902 * f893) * f903;
                            v[2] = (f900 * f894 + f901 * f895 + f902 * f896) * f903;
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
                int[] groupCounts = new int[256];
                int numGroups = 0;

                for (int i = 0 ; i < VertexCount ; ++i) {
                    int group = VertSkins[i];
                    groupCounts[group]++;
                    if (group > numGroups)
                        numGroups = group;
                }

                VertexGroups = new int[numGroups + 1][];

                for (int i = 0 ; i <= numGroups ; ++i) {
                    VertexGroups[i] = new int[groupCounts[i]];
                    groupCounts[i] = 0;
                }

                for (int i = 0 ; i < VertexCount ; i++) {
                    int g = VertSkins[i];
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

        #endregion

        #region ≡ helper methods

        private static readonly int[] _hsl2Rgb = BuildHslLut();

        /// <summary>Converts RS 15‑bit HSL to 24‑bit sRGB.</summary>
        public static int HslToRgb(int hsl) => _hsl2Rgb[hsl & 0xFFFF];

        private static int[] BuildHslLut() {
            var lut = new int[65536];
            for (int h = 0 ; h < 512 ; h++) {
                int hue = (h >> 3) & 0x3F;          // 6‑bit
                int sat = h & 7;                  // 3‑bit
                for (int l = 0 ; l < 128 ; l++) {
                    int index = (h << 7) | l;
                    lut[index] = HslToRgbInternal(hue, sat, l);
                }
            }
            return lut;
        }

        private static int HslToRgbInternal(int hue6, int sat3, int lum7) {
            double h = hue6 / 64d;
            double s = sat3 / 8d;
            double l = lum7 / 128d;

            if (s == 0) { int v = (int) (l * 255); return (v << 16) | (v << 8) | v; }
            double q = l < .5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double r = HueToRgb(p, q, h + 1 / 3d);
            double g = HueToRgb(p, q, h);
            double b = HueToRgb(p, q, h - 1 / 3d);
            return ((int) (r * 255) << 16) | ((int) (g * 255) << 8) | (int) (b * 255);
        }

        private static double HueToRgb(double p, double q, double t) {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6d) return p + (q - p) * 6 * t;
            if (t < 1 / 2d) return q;
            if (t < 2 / 3d) return p + (q - p) * (2 / 3d - t) * 6;
            return p;
        }

        #endregion
    }
}
