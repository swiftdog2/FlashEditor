using FlashEditor;
using FlashEditor.utils;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FlashEditor.Definitions
{
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
    internal sealed class ModelDefinition : IDefinition
    {
        #region ≡ public decoded fields

        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int TexturedTriangleCount { get; private set; }

        public int[] VertX = Array.Empty<int>();
        public int[] VertY = Array.Empty<int>();
        public int[] VertZ = Array.Empty<int>();
        public byte[]? VertSkins;

        public ushort[] IndA = Array.Empty<ushort>();
        public ushort[] IndB = Array.Empty<ushort>();
        public ushort[] IndC = Array.Empty<ushort>();

        public ushort[] FaceColour = Array.Empty<ushort>();      // HSL‑555, convert via HslToRgb()
        public byte[]? FaceRenderType;    // 0 = flat, 1 = textured
        public byte[]? FacePriority;      // 0‑255, or null if global
        public byte[]? FaceAlpha;         // 0‑255
        public byte[]? FaceSkin;

        public byte[]? TextureType;       // per‑texturedFace flags (0,1,2,3)
        public ushort[]? TexIndA, TexIndB, TexIndC; // reference vertices for UV solve

        public ushort ParticleEffectId { get; private set; } = 0xFFFF; // 0xFFFF == none
        public ushort[]? ParticleAnchorVert; // optional vertex IDs

        #endregion

        #region ≡ decoding entry points

        /// <summary>
        /// Populates this definition by decoding the supplied stream (and optional XTEA key array).
        /// </summary>
        /// <param name="stream">JagStream containing full model+footer data.</param>
        /// <param name="xteaKey">Optional 4- or 10-int array for decryption.</param>
        public void Decode(JagStream stream, int[] xteaKey = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Copy raw bytes into a new JagStream so we can parse and rewind.
            byte[] rawBytes = stream.ToArray();
            JagStream rawStream = new JagStream(rawBytes);

            // Parse the last 2 bytes to get the model format
            ModelFormat modelFormat = GetModelFormat(rawStream);

            DebugUtil.Debug($"Decoding Model (Format: {modelFormat})");

            // Decode based on format
            switch (modelFormat)
            {
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
        public static ModelFormat GetModelFormat(JagStream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            long length = stream.Length;
            if (length < 2)
                throw new InvalidDataException("Stream too short to determine model format.");

            // Read without moving the stream’s Position
            byte last = stream.Get((int)(length - 1));
            byte penultimate = stream.Get((int)(length - 2));

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
        private void DecodeRS2(JagStream modelStream, int[] xteaKey)
        {
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
            byte priorityByte = (byte)var4.ReadUnsignedByte();
            bool hasPerFacePrio = priorityByte == 255;
            bool hasTransparency = var4.ReadUnsignedByte() == 1;
            bool hasTransGroup = var4.ReadUnsignedByte() == 1;
            bool hasVertGroup = var4.ReadUnsignedByte() == 1;
            bool hasAnimaya = var4.ReadUnsignedByte() == 1;

            // 5) Read the five explicit‐offset shorts
            int offVertexData = var4.ReadUnsignedShort();
            int offFaceColour = var4.ReadUnsignedShort();
            int offFaceData = var4.ReadUnsignedShort();
            int offFaceIndex = var4.ReadUnsignedShort();
            int offTextureData = var4.ReadUnsignedShort();

            // 6) Compute the intermediate offsets exactly as in Java
            int base0 = 0;
            int base1 = base0 + vertexCountFlag;
            int base2 = base1 + triangleCountFlag;
            if (priorityByte == 255) base1 += triangleCountFlag;
            int base3 = base1;
            if (hasTransGroup) base1 += triangleCountFlag;
            int base4 = base1;
            if (hasFaceRender) base1 += triangleCountFlag;
            int base5 = base1;
            base1 += offTextureData;
            int base6 = base1;
            if (hasTransparency) base1 += triangleCountFlag;
            int base7 = base1;
            base1 += offFaceIndex * 2;
            int base8 = base1;
            base1 += triangleCountFlag * 2;
            int base9 = base1;
            base1 += texturedCountFlag * 6;
            int base10 = base1;
            base1 += offVertexData;
            int base11 = base1;
            base1 += offFaceColour;

            // 7) Populate our public counts
            VertexCount = vertexCountFlag;
            TriangleCount = triangleCountFlag;
            TexturedTriangleCount = texturedCountFlag;

            // 8) Allocate geometry arrays
            VertX = new int[VertexCount];
            VertY = new int[VertexCount];
            VertZ = new int[VertexCount];
            if (hasVertGroup)
                VertSkins = new byte[VertexCount];

            IndA = new ushort[TriangleCount];
            IndB = new ushort[TriangleCount];
            IndC = new ushort[TriangleCount];

            FaceColour = new ushort[TriangleCount];
            if (hasFaceRender)
                FaceRenderType = new byte[TriangleCount];
            if (hasPerFacePrio)
                FacePriority = new byte[TriangleCount];
            if (hasTransparency)
                FaceAlpha = new byte[TriangleCount];
            if (hasTransGroup)
                FaceSkin = new byte[TriangleCount];

            if (TexturedTriangleCount > 0) {
                TextureType = new byte[TexturedTriangleCount];
                TexIndA = new ushort[TexturedTriangleCount];
                TexIndB = new ushort[TexturedTriangleCount];
                TexIndC = new ushort[TexturedTriangleCount];
            }

            // 9) Position each JagStream at its block start
            var4.Seek(base0);
            var5.Seek(base10);
            var6.Seek(base11);
            var7.Seek(base1);
            var8.Seek(base5);

            // 10) Decode vertex positions & optional vertex‐group
            int cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < VertexCount; i++)
            {
                int mask = var4.ReadUnsignedByte();
                int dx = (mask & 1) != 0 ? var5.ReadSignedSmart() : 0;
                int dy = (mask & 2) != 0 ? var6.ReadSignedSmart() : 0;
                int dz = (mask & 4) != 0 ? var7.ReadSignedSmart() : 0;
                cx += dx; cy += dy; cz += dz;
                VertX[i] = cx;
                VertY[i] = cy;
                VertZ[i] = cz;
                if (hasVertGroup)
                    VertSkins![i] = (byte)var8.ReadUnsignedByte();
            }

            // 11) Decode animaya (morph) groups if present
            // (skipped here—type 2 rarely uses them and no field was provided)

            // 12) Prepare face‐colour & flag streams
            var4.Seek(base8);
            var5.Seek(base4);
            var6.Seek(base2);
            var7.Seek(base6);
            var8.Seek(base3);

            // 13) Decode face colours, render‐type mask, priorities, alpha & trans‐groups
            bool anyTextured = false;
            bool anyRendered = false;
            for (int i = 0; i < TriangleCount; i++)
            {
                FaceColour[i] = (ushort)var4.ReadUnsignedShort();

                if (hasFaceRender)
                {
                    int faceMask = var5.ReadUnsignedByte();
                    FaceRenderType![i] = (byte)((faceMask & 1) != 0 ? 1 : 0);
                    if ((faceMask & 1) != 0) anyRendered = true;
                    TextureType![i] = (byte)((faceMask & 2) != 0
                        ? (faceMask >> 2)
                        : 255);
                    if ((faceMask & 2) != 0) anyTextured = true;
                }

                if (hasPerFacePrio)
                    FacePriority![i] = (byte)var6.ReadByte();

                if (hasTransparency)
                    FaceAlpha![i] = (byte)var7.ReadByte();

                if (hasTransGroup)
                    FaceSkin![i] = (byte)var8.ReadUnsignedByte();
            }

            // 14) Null‐out arrays that never saw a flag
            if (TextureType != null && !anyTextured) TextureType = null;
            if (FaceRenderType != null && !anyRendered) FaceRenderType = null;

            // 15) Decode triangle‐strip indices
            var4.Seek(base7);
            var5.Seek(base2);
            int a = 0, b = 0, c = 0, ptr = 0;
            for (int i = 0; i < TriangleCount; i++)
            {
                int op = var5.ReadUnsignedByte();
                if (op == 1)
                {
                    a = ptr + var4.ReadSignedSmart();
                    b = a + var4.ReadSignedSmart();
                    c = b + var4.ReadSignedSmart();
                    ptr = c;
                }
                else if (op == 2)
                {
                    c = ptr + var4.ReadSignedSmart();
                    ptr = c;
                }
                else if (op == 3)
                {
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
                IndA[i] = (ushort)a;
                IndB[i] = (ushort)b;
                IndC[i] = (ushort)c;
            }

            // 16) Decode textured‐face lookup tables
            var4.Seek(base9);
            for (int i = 0; i < TexturedTriangleCount; i++)
            {
                TextureType![i] = 0;
                TexIndA![i] = (ushort)var4.ReadUnsignedShort();
                TexIndB![i] = (ushort)var4.ReadUnsignedShort();
                TexIndC![i] = (ushort)var4.ReadUnsignedShort();
            }

            // 17) Done. ParticleEffectId and ParticleAnchorVert are not set in type 2.
        }


        private void DecodeOld(JagStream modelStream, int[] xteaKey)
        {
            throw new NotSupportedException("Old RS2 model format not supported.");
        }

        public int ModelID { get; set; }


        /// <inheritdoc />
        public JagStream Encode() => throw new NotSupportedException("Model re‑encoding is out of scope for viewer.");

        #endregion

        #region ≡ footer parsing

        public enum ModelFormat
        {
            Old = 0, // pre-sentinel style
            Newer = 1, // sentinel style without textures
            Newest = 2 // sentinel style with texture faces
        }

        public readonly struct Footer
        {
            public int VertexCount { get; }
            public int TriangleCount { get; }
            public int TexturedTriangleCount { get; }
            public ModelFormat Format { get; }
            public int FooterSize { get; }

            public Footer(int vertexCount, int triangleCount, int texturedTriangleCount, ModelFormat format, int footerSize)
            {
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
        private void DecodeRS3(JagStream full, int[] xteaKey)
        {
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
                if (hasVertGrp) VertSkins = new byte[vc];

                IndA = new ushort[fc];
                IndB = new ushort[fc];
                IndC = new ushort[fc];

                FaceColour = new ushort[fc];
                if (hasFaceType) FaceRenderType = new byte[fc];
                if (hasPriorities) FacePriority = new byte[fc];
                if (hasAlpha) FaceAlpha = new byte[fc];
                if (hasTransGrp) FaceSkin = new byte[fc];
                if (hasTextures)
                {
                    TextureType = new byte[tfc];
                    TexIndA = new ushort[tfc];
                    TexIndB = new ushort[tfc];
                    TexIndC = new ushort[tfc];
                }

                // 7) Decode vertex positions (flags + signedSmart deltas) :contentReference[oaicite:13]{index=13}
                int px = 0, py = 0, pz = 0;
                for (int i = 0; i < vc; i++)
                {
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
                for (int i = 0; i < fc; i++)
                {
                    FaceColour[i] = (ushort)st6.ReadUnsignedShort();
                    if (hasFaceType)
                        FaceRenderType[i] = (byte)st7.ReadUnsignedByte();
                    if (hasPriorities)
                        FacePriority[i] = (byte)st7.ReadUnsignedByte();
                    if (hasAlpha)
                        FaceAlpha[i] = (byte)st7.ReadUnsignedByte();
                    if (hasTransGrp)
                        FaceSkin[i] = (byte)st7.ReadUnsignedByte();  // short is fine
                    if (hasTextures)
                        TextureType[i] = (byte)st7.ReadUnsignedByte();

                }

                // 9) Decode the triangle indices (smart‐encoded) :contentReference[oaicite:15]{index=15}
                int a = 0, bPrev = 0, cPrev = 0, idxPtr = 0;
                for (int i = 0; i < fc; i++)
                {
                    int op = st8.ReadUnsignedByte();
                    if (op == 1)
                    {
                        a = st8.ReadSignedSmart() + idxPtr;
                        bPrev = st8.ReadSignedSmart() + a;
                        cPrev = st8.ReadSignedSmart() + bPrev;
                        idxPtr = cPrev;
                    }
                    else if (op == 2)
                    {
                        int tmp = cPrev;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                        bPrev = tmp;
                    }
                    else if (op == 3)
                    {
                        int tmp = a;
                        a = cPrev;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                        bPrev = tmp;
                    }
                    else if (op == 4)
                    {
                        int tmpA = a;
                        a = bPrev;
                        bPrev = tmpA;
                        cPrev = st8.ReadSignedSmart() + idxPtr;
                        idxPtr = cPrev;
                    }

                    IndA[i] = (ushort)a;
                    IndB[i] = (ushort)bPrev;
                    IndC[i] = (ushort)cPrev;
                }

                // 10) Decode textured‐face blocks :contentReference[oaicite:16]{index=16}
                if (tfc > 0)
                {
                    // Seek into the texture block
                    var stTex = new JagStream(b);
                    stTex.Seek(offFaceTextureData);
                    for (int i = 0; i < tfc; i++)
                    {
                        TextureType[i] = (byte)stTex.ReadByte();
                        TexIndA[i] = (ushort)(stTex.ReadUnsignedShort() - 1);
                        TexIndB[i] = (ushort)(stTex.ReadUnsignedShort() - 1);
                        TexIndC[i] = (ushort)(stTex.ReadUnsignedShort() - 1);
                    }

                    // Drop textureCoordinates if unused (Java does this check in decodeType3) :contentReference[oaicite:17]{index=17}
                    bool anyUV = false;
                    for (int i = 0; i < fc; i++)
                    {
                        int tcIdx = TextureType[i] & 0xFF;
                        if (tcIdx != 255)
                        {
                            if (IndA[i] != TexIndA[tcIdx] ||
                                IndB[i] != TexIndB[tcIdx] ||
                                IndC[i] != TexIndC[tcIdx])
                            {
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



        private byte _globalPriority;

        #endregion

        #region ≡ helper methods

        private static readonly int[] _hsl2Rgb = BuildHslLut();

        /// <summary>Converts RS 15‑bit HSL to 24‑bit sRGB.</summary>
        public static int HslToRgb(int hsl) => _hsl2Rgb[hsl & 0xFFFF];

        private static int[] BuildHslLut()
        {
            var lut = new int[65536];
            for (int h = 0; h < 512; h++)
            {
                int hue = (h >> 3) & 0x3F;          // 6‑bit
                int sat = h & 7;                  // 3‑bit
                for (int l = 0; l < 128; l++)
                {
                    int index = (h << 7) | l;
                    lut[index] = HslToRgbInternal(hue, sat, l);
                }
            }
            return lut;
        }

        private static int HslToRgbInternal(int hue6, int sat3, int lum7)
        {
            double h = hue6 / 64d;
            double s = sat3 / 8d;
            double l = lum7 / 128d;

            if (s == 0) { int v = (int)(l * 255); return (v << 16) | (v << 8) | v; }
            double q = l < .5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            double r = HueToRgb(p, q, h + 1 / 3d);
            double g = HueToRgb(p, q, h);
            double b = HueToRgb(p, q, h - 1 / 3d);
            return ((int)(r * 255) << 16) | ((int)(g * 255) << 8) | (int)(b * 255);
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1 / 6d) return p + (q - p) * 6 * t;
            if (t < 1 / 2d) return q;
            if (t < 2 / 3d) return p + (q - p) * (2 / 3d - t) * 6;
            return p;
        }

        #endregion
    }
}
