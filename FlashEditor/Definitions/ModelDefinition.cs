using System;
using System.Buffers.Binary;
using System.IO;
using FlashEditor;

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

        /// <inheritdoc/>
        public void Decode(JagStream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            var data = stream.ToArray();
            var footer = ParseFooter(data);

            switch (footer.Format)
            {
                case ModelFormat.Newest:
                    DecodeNewest(data, footer);
                    break;
                default:
                    throw new NotSupportedException($"Model footer format {footer.Format} not supported for production viewer.");
            }
        }
        public int ModelID { get; set; }


        /// <inheritdoc />
        public JagStream Encode() => throw new NotSupportedException("Model re‑encoding is out of scope for viewer.");

        #endregion

        #region ≡ footer parsing

        private enum ModelFormat { Old = 0, Newer = 1, Newest = 2 }

        private readonly struct Footer
        {
            public readonly ModelFormat Format;
            public readonly int VertexCount;
            public readonly int TriangleCount;
            public readonly int TexTriCount;
            public readonly byte FaceFlags;
            public readonly byte VertFlags;
            public readonly int FooterSize;
            public Footer(ModelFormat fmt, int v, int t, int tex, byte ff, byte vf, int sz)
            { Format = fmt; VertexCount = v; TriangleCount = t; TexTriCount = tex; FaceFlags = ff; VertFlags = vf; FooterSize = sz; }
        }

        /// <summary>Detects footer flavour and extracts counts & flags.</summary>
        private static Footer ParseFooter(ReadOnlySpan<byte> data)
        {
            // Newer/newest versions always end in FF FF
            if (data[^2] == 0xFF && data[^1] == 0xFF)
            {
                // Newest has 23 B footer by default (26 B if particle block present). We read the short <vertexCount>
                // two bytes earlier to guess size: VertexCount <= 65535 so byte‑19 must be 0..<256.
                int tentative = BinaryPrimitives.ReadUInt16BigEndian(data[^23..^21]);
                int footerSize = tentative <= 65535 ? 23 : 18; // rough but works for 602+ vs 474+.
                var f = data[^footerSize..];
                int verts = BinaryPrimitives.ReadUInt16BigEndian(f);
                int tris = BinaryPrimitives.ReadUInt16BigEndian(f[2..]);
                int tex = f[4];
                byte faceFlags = f[5];
                byte vertFlags = f[6];
                var fmt = footerSize == 23 ? ModelFormat.Newest : ModelFormat.Newer;
                return new Footer(fmt, verts, tris, tex, faceFlags, vertFlags, footerSize);
            }
            else // very old ≤474 not needed in rev 639 cache
            {
                throw new InvalidDataException("Unsupported pre‑474 model detected.");
            }
        }

        #endregion

        #region ≡ newest‑footer decoder (602‑647)

        private void DecodeNewest(ReadOnlySpan<byte> blob, Footer f)
        {
            VertexCount = f.VertexCount;
            TriangleCount = f.TriangleCount;
            TexturedTriangleCount = f.TexTriCount;

            // Allocate mandatory arrays.
            VertX = new int[VertexCount];
            VertY = new int[VertexCount];
            VertZ = new int[VertexCount];

            IndA = new ushort[TriangleCount];
            IndB = new ushort[TriangleCount];
            IndC = new ushort[TriangleCount];
            FaceColour = new ushort[TriangleCount];

            if ((f.VertFlags & 1) != 0) VertSkins = new byte[VertexCount];
            if ((f.FaceFlags & 0x01) != 0) FaceRenderType = new byte[TriangleCount];
            if ((f.FaceFlags & 0x02) != 0) FacePriority = new byte[TriangleCount];
            else /* global priority */    _globalPriority = (byte)0;
            if ((f.FaceFlags & 0x04) != 0) FaceSkin = new byte[TriangleCount];
            if ((f.FaceFlags & 0x08) != 0) FaceAlpha = new byte[TriangleCount];
            if (TexturedTriangleCount > 0) { TextureType = new byte[TexturedTriangleCount]; TexIndA = new ushort[TexturedTriangleCount]; TexIndB = new ushort[TexturedTriangleCount]; TexIndC = new ushort[TexturedTriangleCount]; }

            // To mimic Jagex we create multiple JagStreams pointing at same byte[] and move their caret’s independently.
            var src = blob.ToArray(); // required because JagStream works on byte[]
            var s1 = new JagStream(src); // vertex flags & type arrays
            var s2 = new JagStream(src);
            var s3 = new JagStream(src);
            var s4 = new JagStream(src);
            var s5 = new JagStream(src);
            var s6 = new JagStream(src);
            var s7 = new JagStream(src);

            // Seek each stream to its segment start (mirrors Jagex logic exactly).
            int basePos = 0;
            s1.Position = basePos;
            basePos += VertexCount;
            s2.Position = basePos;
            basePos += VertexCount;
            s3.Position = basePos;
            basePos += VertexCount;
            s4.Position = basePos;
            basePos += (f.VertFlags & 1) != 0 ? VertexCount : 0;
            s5.Position = basePos;
            basePos += TriangleCount * 2; // colour shorts
            s6.Position = basePos;
            basePos += (f.FaceFlags & 1) != 0 ? TriangleCount : 0; // renderType
            s7.Position = basePos;
            basePos += (f.FaceFlags & 0x02) != 0 ? TriangleCount : 0; // facePriority
            int alphaOffset = basePos;
            basePos += (f.FaceFlags & 0x04) != 0 ? TriangleCount : 0; // faceSkin
            int skinOffset = basePos;
            basePos += (f.FaceFlags & 0x08) != 0 ? TriangleCount : 0; // alpha
            int triangleTypeOffset = basePos;
            basePos += TriangleCount * 2; // indices (a)
            int triangleIndexBOffset = basePos;
            basePos += TriangleCount * 2; // indices (b)
            int triangleIndexCOffset = basePos;
            basePos += TriangleCount * 2; // indices (c)
            int textureInfoOffset = basePos; // remainder for textured triangles, if present

            var sColour = new JagStream(src); sColour.Position = s5.Position;
            var sFaceRenderType = new JagStream(src); sFaceRenderType.Position = s6.Position;
            var sFacePriority = new JagStream(src); sFacePriority.Position = s7.Position;
            var sFaceSkin = new JagStream(src); sFaceSkin.Position = skinOffset;
            var sFaceAlpha = new JagStream(src); sFaceAlpha.Position = alphaOffset;
            var sIndA = new JagStream(src); sIndA.Position = triangleTypeOffset;
            var sIndB = new JagStream(src); sIndB.Position = triangleIndexBOffset;
            var sIndC = new JagStream(src); sIndC.Position = triangleIndexCOffset;
            var sTex = new JagStream(src); sTex.Position = textureInfoOffset;

            // === decode vertices (delta‑encoded, signed smart) ===
            int vx = 0, vy = 0, vz = 0;
            for (int i = 0; i < VertexCount; i++)
            {
                int flags = s1.ReadUnsignedByte();
                int dx = (flags & 1) != 0 ? s3.ReadSignedSmart() : 0;
                int dy = (flags & 2) != 0 ? s3.ReadSignedSmart() : 0;
                int dz = (flags & 4) != 0 ? s3.ReadSignedSmart() : 0;
                vx += dx; vy += dy; vz += dz;
                VertX[i] = vx; VertY[i] = vy; VertZ[i] = vz;
                if ((f.VertFlags & 1) != 0) VertSkins![i] = s4.ReadUnsignedByte();
            }

            // === decode faces ===
            int lastIndex = 0, lastA = 0, lastB = 0, lastC = 0;
            byte globalPriority = 0;
            if ((f.FaceFlags & 0x02) == 0) globalPriority = sFacePriority.ReadUnsignedByte();
            _globalPriority = globalPriority;

            for (int i = 0; i < TriangleCount; i++)
            {
                if (FaceRenderType != null) FaceRenderType[i] = sFaceRenderType.ReadUnsignedByte();
                FaceColour[i] = (ushort)sColour.ReadUnsignedShort();
                if (FacePriority != null) FacePriority[i] = sFacePriority.ReadUnsignedByte();
                else /* global */ FacePriority = null;
                if (FaceSkin != null) FaceSkin[i] = sFaceSkin.ReadUnsignedByte();
                if (FaceAlpha != null) FaceAlpha[i] = sFaceAlpha.ReadUnsignedByte();
            }

            for (int i = 0; i < TriangleCount; i++)
            {
                int opcode = sIndA.ReadUnsignedByte();
                if (opcode == 1)
                {
                    lastA = sIndB.ReadUnsignedSmart();
                    lastIndex += lastA;
                    IndA[i] = (ushort)lastIndex;
                    lastB = sIndB.ReadUnsignedSmart();
                    lastIndex += lastB;
                    IndB[i] = (ushort)lastIndex;
                    lastC = sIndB.ReadUnsignedSmart();
                    lastIndex += lastC;
                    IndC[i] = (ushort)lastIndex;
                }
                else if (opcode == 2)
                {
                    IndA[i] = IndA[i - 1];
                    IndB[i] = IndC[i - 1];
                    lastC = sIndB.ReadUnsignedSmart();
                    lastIndex += lastC;
                    IndC[i] = (ushort)lastIndex;
                }
                else if (opcode == 3)
                {
                    IndA[i] = IndC[i - 1];
                    IndB[i] = IndB[i - 1];
                    lastC = sIndB.ReadUnsignedSmart();
                    lastIndex += lastC;
                    IndC[i] = (ushort)lastIndex;
                }
                else if (opcode == 4)
                {
                    IndA[i] = IndB[i - 1];
                    IndB[i] = IndA[i - 1];
                    lastC = sIndB.ReadUnsignedSmart();
                    lastIndex += lastC;
                    IndC[i] = (ushort)lastIndex;
                }
            }

            // === textured triangle info (types + mapping verts) ===
            for (int i = 0; i < TexturedTriangleCount; i++)
                TextureType![i] = sTex.ReadUnsignedByte();
            for (int i = 0; i < TexturedTriangleCount; i++)
            {
                TexIndA![i] = (ushort)sTex.ReadUnsignedShort();
                TexIndB![i] = (ushort)sTex.ReadUnsignedShort();
                TexIndC![i] = (ushort)sTex.ReadUnsignedShort();
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
