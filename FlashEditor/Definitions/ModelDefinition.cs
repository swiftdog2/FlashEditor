using FlashEditor.Utils;
using System;
using System.IO;
using FlashEditor.IO;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     A model from index 7, in the form the viewer wants it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the <b>derived</b> half of the model path: absolute vertex coordinates rather
    ///     than deltas, triangles rather than a strip-opcode stream, normals, UVs and animation
    ///     tables. It is projected from <see cref="Source"/>, which holds the file as stored, and
    ///     that split is what makes the index encodable at all - several fields here are computed
    ///     and cannot be turned back into bytes, and several things the file stores were never kept
    ///     here.
    ///     </para>
    ///     <para>
    ///     The most load-bearing example is the vertex shift. <c>decoder_newer_format</c> never
    ///     touches the coordinates; the client's callers shift them left by two afterwards through
    ///     <c>method2592</c> when the format type is below 13 (Class107.java:175, Class152.java:114,
    ///     Node_Sub10_Sub16.java:33, ItemDefinition.java:155). This type applies the shift so the
    ///     viewer sees what the client draws, and <see cref="VertexShift"/> says by how much, so
    ///     nothing has to infer it back out of the coordinates.
    ///     </para>
    /// </remarks>
    public class ModelDefinition : IDefinition {
        #region stored form

        /// <summary>
        ///     The file exactly as index 7 stores it, or null when this definition was not decoded.
        /// </summary>
        /// <remarks>
        ///     <see cref="Encode"/> writes this back, not the derived arrays below. Nothing edits a
        ///     mesh yet, so the two cannot disagree; the moment something does, the edit has to be
        ///     applied here rather than to the projection.
        /// </remarks>
        public ModelFile? Source { get; private set; }

        #endregion

        #region decoded fields

        /// <summary>Total number of vertices in the model.</summary>
        public int VertexCount { get; private set; }
        /// <summary>Total number of triangular faces in the model.</summary>
        public int TriangleCount { get; private set; }
        /// <summary>Number of texture-mapped triangles.</summary>
        public int TexturedTriangleCount { get; private set; }

        /// <summary>X coordinates for each vertex, shifted by <see cref="VertexShift"/>.</summary>
        public int[] VertX = Array.Empty<int>();
        /// <summary>Y coordinates for each vertex (vertical axis), shifted by <see cref="VertexShift"/>.</summary>
        public int[] VertY = Array.Empty<int>();
        /// <summary>Z coordinates for each vertex, shifted by <see cref="VertexShift"/>.</summary>
        public int[] VertZ = Array.Empty<int>();

        /// <summary>
        ///     Animation skin group per vertex, or null when the model has none.
        /// </summary>
        /// <remarks>
        ///     Consumed and cleared by <see cref="ComputeAnimationTables"/>, which turns it into
        ///     <see cref="VertexGroups"/>. Read <see cref="Source"/> for the stored values.
        /// </remarks>
        public int[]? VertSkins;

        /// <summary>First vertex index of each triangle face.</summary>
        public int[] faceIndices1 = Array.Empty<int>();
        /// <summary>Second vertex index of each triangle face.</summary>
        public int[] faceIndices2 = Array.Empty<int>();
        /// <summary>Third vertex index of each triangle face.</summary>
        public int[] faceIndices3 = Array.Empty<int>();

        /// <summary>Per-face HSL-565 colour values. Convert to RGB via <see cref="RawHslToRgb"/>.</summary>
        public short[] FaceColour = Array.Empty<short>();

        /// <summary>
        ///     Per-face render type, or null when the model carries none.
        /// </summary>
        /// <remarks>
        ///     0 Gouraud, 1 flat, and <b>2 not drawn at all</b> - both of the client's renderers gate
        ///     their draw list on it before anything else (Renderable_Sub2.java:397,
        ///     Renderable_Sub3.java:172). The legacy encoding derives it from one bit of a mask and
        ///     so can only ever produce 0 or 1.
        /// </remarks>
        public sbyte[]? FaceRenderType;

        /// <summary>Per-face render priority, or null when a global priority is used.</summary>
        public sbyte[]? FacePriority;

        /// <summary>Per-face alpha, or null when the model is fully opaque.</summary>
        public sbyte[]? FaceAlpha;

        /// <summary>
        ///     Per-face animation skin group, or null.
        /// </summary>
        /// <remarks>
        ///     Held as <c>int</c> because the stored field is an <em>unsigned</em> byte
        ///     (Model.java:596) and 8,639 models in the repack carry a value above 127. A signed
        ///     byte turned every one of those negative.
        /// </remarks>
        public int[]? FaceSkin;

        /// <summary>
        ///     Type of each textured face, 0 to 3, or null when the model has no textured faces.
        /// </summary>
        /// <remarks>
        ///     Type 0 is a plain triangle mapping. Types 1 to 3 additionally carry the projection
        ///     scalars in <see cref="TextureScaleP"/> and the three per-face byte fields; type 2
        ///     carries two bytes more than the others. The legacy encoding has no type block and its
        ///     textured faces are all type 0.
        /// </remarks>
        public int[]? TextureType;

        /// <summary>
        ///     Index into the textured-face arrays for each face, or -1 when the face has no mapping.
        /// </summary>
        /// <remarks>
        ///     Held as <c>int</c> rather than a signed byte: the newer encoding allows 255 textured
        ///     faces and the new-protocol one allows 65,535, so the index does not fit a byte and
        ///     truncating it pointed faces at the wrong mapping.
        /// </remarks>
        public int[]? TextureCoordinates;

        /// <summary>Texture id per face, or -1 for untextured. Null when the model stores none.</summary>
        public short[]? FaceTextures;

        /// <summary>First reference vertex of each textured face.</summary>
        public short[]? TexIndA;
        /// <summary>Second reference vertex of each textured face.</summary>
        public short[]? TexIndB;
        /// <summary>Third reference vertex of each textured face.</summary>
        public short[]? TexIndC;

        /// <summary>
        ///     First projection scalar of each type 1-3 textured face, or null.
        /// </summary>
        /// <remarks>
        ///     <c>anIntArray1389</c>. Shifted alongside the vertices by <c>method2592</c>
        ///     (Model.java:1694-1698), which is why the shift is recorded rather than assumed.
        /// </remarks>
        public int[]? TextureScaleP;

        /// <summary>Second projection scalar of each type 1-3 textured face, or null.</summary>
        public int[]? TextureScaleQ;

        /// <summary>Third projection scalar of each type 1-3 textured face, or null.</summary>
        public int[]? TextureScaleR;

        /// <summary>First per-face byte of a type 1-3 textured face, or null.</summary>
        public sbyte[]? TextureFieldA;

        /// <summary>Second per-face byte of a type 1-3 textured face, or null.</summary>
        public sbyte[]? TextureFieldB;

        /// <summary>Third per-face byte of a type 1-3 textured face, or null.</summary>
        public sbyte[]? TextureFieldC;

        /// <summary>First extra byte carried only by a type-2 textured face, or null.</summary>
        public sbyte[]? TextureType2FieldA;

        /// <summary>Second extra byte carried only by a type-2 textured face, or null.</summary>
        public sbyte[]? TextureType2FieldB;

        /// <summary>Particle emitters riding on this model's faces, or null when it has none.</summary>
        public ModelParticleEmitter[]? Emitters;

        /// <summary>Particle effectors riding on this model's vertices, or null when it has none.</summary>
        public ModelParticleEffector[]? Effectors;

        /// <summary>Billboard bonds attached to this model's faces, or null when it has none.</summary>
        public ModelBond[]? Bonds;

        /// <summary>
        ///     The first emitter's configuration id, or 0xFFFF when the model has no emitters.
        /// </summary>
        /// <remarks>
        ///     A convenience for the model tab's summary line. A model may carry several emitters -
        ///     read <see cref="Emitters"/> for all of them.
        /// </remarks>
        public ushort ParticleEffectId { get; private set; } = 0xFFFF;

        /// <summary>
        ///     Model format version, which decides several field widths and the vertex shift.
        /// </summary>
        public int FormatType { get; private set; } = 12;

        /// <summary>
        ///     How many bits <see cref="VertX"/> and its siblings have been shifted left by.
        /// </summary>
        /// <remarks>
        ///     Two for a model below format type 13, zero otherwise. Without this the stored deltas
        ///     could not be recovered from the coordinates, which is what made the old decoder
        ///     unencodable.
        /// </remarks>
        public int VertexShift { get; private set; }

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

        #region decoding entry points

        /// <summary>The group id this model was read from, which is also its model id.</summary>
        /// <remarks>Set before <see cref="Decode"/>: it is what selects the new-protocol layout.</remarks>
        public int ModelID { get; set; }

        /// <summary>
        ///     Decodes a model and projects it into the arrays the viewer reads.
        /// </summary>
        /// <param name="stream">The stored model bytes.</param>
        /// <param name="xteaKey">Unused. No group in index 7 is encrypted in either cache.</param>
        public void Decode(JagStream stream, int[] xteaKey = null) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            Source = ModelCodec.Decode(stream, ModelID);
            DebugUtil.Debug($"Decoding model {ModelID} ({Source.Encoding})");

            Project(Source);

            ComputeNormals();
            ComputeTextureUVCoordinates();
            ComputeAnimationTables();
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Writes back <see cref="Source"/>, so a model that was decoded re-encodes to the bytes
        ///     it came from. A definition assembled by hand has no stored form and cannot be written
        ///     at all rather than being written as a guess.
        /// </remarks>
        public JagStream Encode() {
            if (Source == null)
                throw new NotSupportedException(
                    "This model was not decoded from a cache file, so there is no stored form to " +
                    "write back. Build a ModelFile and encode that.");
            return Source.Encode();
        }

        /// <summary>
        ///     Which of index 7's three layouts a model uses.
        /// </summary>
        /// <remarks>
        ///     Kept as a distinct enum from <see cref="ModelEncoding"/> because it is what the model
        ///     tab and the existing sweep report. There is no fourth case: an <c>FF FD</c> tail is
        ///     legacy to the client (Model.java:96-101), and no model in either cache carries one.
        /// </remarks>
        public enum ModelFormat {
            /// <summary>The 18-byte-footer legacy layout.</summary>
            Old = 0,

            /// <summary>The <c>FF FF</c> sentinel layout that holds all but nine models.</summary>
            Newer = 1,

            /// <summary>The new-protocol layout, selected by model id 63607-63613.</summary>
            Newest = 2
        }

        /// <summary>
        ///     Classifies a model without decoding it.
        /// </summary>
        /// <param name="stream">The stored model bytes.</param>
        /// <param name="modelId">The group id the model was read from.</param>
        /// <returns>The layout the client would decode it with.</returns>
        public static ModelFormat GetModelFormat(JagStream stream, int modelId = -1) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            switch (ModelCodec.ClassifyEncoding(stream.ToArray(), modelId)) {
                case ModelEncoding.Legacy:
                    return ModelFormat.Old;
                case ModelEncoding.Newer:
                    return ModelFormat.Newer;
                default:
                    return ModelFormat.Newest;
            }
        }

        #endregion

        #region projection

        /// <summary>
        ///     Turns the stored form into the arrays the renderer and the tabs read.
        /// </summary>
        /// <param name="file">The model as stored.</param>
        private void Project(ModelFile file) {
            VertexCount = file.VertexCount;
            TriangleCount = file.FaceCount;
            TexturedTriangleCount = file.TexturedFaceCount;
            FormatType = file.FormatType;
            VertexShift = file.VertexShift;

            ProjectVertices(file);
            ProjectFaceIndices(file);
            ProjectFaceAttributes(file);
            ProjectTexturedFaces(file);
            ProjectAttachments(file);

            if (file.Encoding == ModelEncoding.Legacy)
                FinishLegacy(file);
        }

        private void ProjectVertices(ModelFile file) {
            int count = file.VertexCount;
            VertX = new int[count];
            VertY = new int[count];
            VertZ = new int[count];

            int x = 0, y = 0, z = 0;
            int nextX = 0, nextY = 0, nextZ = 0;
            for (int i = 0; i < count; i++) {
                int mask = file.VertexFlags[i];
                if ((mask & 1) != 0) x += file.VertexDeltasX[nextX++].Value;
                if ((mask & 2) != 0) y += file.VertexDeltasY[nextY++].Value;
                if ((mask & 4) != 0) z += file.VertexDeltasZ[nextZ++].Value;

                VertX[i] = x << VertexShift;
                VertY[i] = y << VertexShift;
                VertZ[i] = z << VertexShift;
            }

            if (file.VertexSkins == null)
                return;

            VertSkins = new int[count];
            for (int i = 0; i < count; i++)
                VertSkins[i] = SkinValue(file, file.VertexSkins![i], file.VertexSkinsAreSmart);
        }

        /// <summary>
        ///     Replays the strip opcodes against the delta stream to recover each face's three
        ///     vertices.
        /// </summary>
        /// <remarks>
        ///     The new-protocol decoder masks the opcode byte to three bits (Model.java:1071); the
        ///     other two use it whole. Bits above that carry the per-face flag the trailing block
        ///     would consume, which no model in either cache has.
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        private void ProjectFaceIndices(ModelFile file) {
            int count = file.FaceCount;
            faceIndices1 = new int[count];
            faceIndices2 = new int[count];
            faceIndices3 = new int[count];

            int opcodeMask = file.Encoding == ModelEncoding.NewProtocol ? 0x7 : 0xFF;
            int a = 0, b = 0, c = 0, offset = 0, next = 0;

            for (int i = 0; i < count; i++) {
                int opcode = file.FaceOpcodes[i] & opcodeMask;

                if (opcode == 1) {
                    a = offset + file.FaceIndexDeltas[next++].Value;
                    b = a + file.FaceIndexDeltas[next++].Value;
                    c = b + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 2) {
                    b = c;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 3) {
                    a = c;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }
                else if (opcode == 4) {
                    int swap = a;
                    a = b;
                    b = swap;
                    c = offset + file.FaceIndexDeltas[next++].Value;
                    offset = c;
                }

                faceIndices1[i] = a;
                faceIndices2[i] = b;
                faceIndices3[i] = c;
            }
        }

        private void ProjectFaceAttributes(ModelFile file) {
            int count = file.FaceCount;
            FaceColour = new short[count];
            for (int i = 0; i < count; i++)
                FaceColour[i] = (short) file.FaceColours[i];

            if (file.FacePriorities != null) {
                FacePriority = new sbyte[count];
                for (int i = 0; i < count; i++)
                    FacePriority[i] = (sbyte) file.FacePriorities[i];
            }
            else {
                _globalPriority = file.PriorityFlag;
            }

            if (file.FaceAlphas != null) {
                FaceAlpha = new sbyte[count];
                for (int i = 0; i < count; i++)
                    FaceAlpha[i] = (sbyte) file.FaceAlphas[i];
            }

            if (file.FaceSkins != null) {
                FaceSkin = new int[count];
                for (int i = 0; i < count; i++)
                    FaceSkin[i] = SkinValue(file, file.FaceSkins![i], file.FaceSkinsAreSmart);
            }

            if (file.FaceTextureIds != null) {
                FaceTextures = new short[count];
                for (int i = 0; i < count; i++)
                    FaceTextures[i] = (short) (file.FaceTextureIds[i] - 1);
            }

            if (file.Encoding == ModelEncoding.Legacy) {
                ProjectLegacyFaceMasks(file);
                return;
            }

            if (file.FaceTypeBytes != null) {
                FaceRenderType = new sbyte[count];
                for (int i = 0; i < count; i++)
                    FaceRenderType[i] = (sbyte) file.FaceTypeBytes[i];
            }

            //The client allocates the coordinate array only when both a texture id block and at
            //least one textured face are present, and consumes an entry only for a face that
            //actually carries a texture.
            if (file.FaceTextureFlag != 1 || file.TexturedFaceCount == 0)
                return;

            TextureCoordinates = new int[count];
            int next = 0;
            for (int i = 0; i < count; i++) {
                TextureCoordinates[i] = FaceTextures![i] == -1
                    ? -1
                    : file.TextureCoords[next++].Value - 1;
            }
        }

        /// <summary>
        ///     Unpacks the legacy encoding's packed per-face mask byte.
        /// </summary>
        /// <remarks>
        ///     Bit 0 is the render type, bit 1 says the face is textured, and the remaining six bits
        ///     are the texture-coordinate index. A textured face's colour word is the texture id, so
        ///     the colour is replaced by the neutral 127 (Model.java:1497-1505).
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        private void ProjectLegacyFaceMasks(ModelFile file) {
            if (file.FaceTypeBytes == null)
                return;

            int count = file.FaceCount;
            FaceRenderType = new sbyte[count];
            TextureCoordinates = new int[count];
            FaceTextures = new short[count];

            for (int i = 0; i < count; i++) {
                int mask = file.FaceTypeBytes[i];
                FaceRenderType[i] = (sbyte) (mask & 1);

                if ((mask & 2) == 2) {
                    TextureCoordinates[i] = mask >> 2;
                    FaceTextures[i] = FaceColour[i];
                    FaceColour[i] = 127;
                }
                else {
                    TextureCoordinates[i] = -1;
                    FaceTextures[i] = -1;
                }
            }
        }

        private void ProjectTexturedFaces(ModelFile file) {
            int count = file.TexturedFaceCount;
            if (count == 0)
                return;

            TextureType = new int[count];
            TexIndA = new short[count];
            TexIndB = new short[count];
            TexIndC = new short[count];

            for (int i = 0; i < count; i++) {
                TextureType[i] = file.TextureTypes == null ? 0 : file.TextureTypes[i];
                TexIndA[i] = (short) file.TextureVertexA[i];
                TexIndB[i] = (short) file.TextureVertexB[i];
                TexIndC[i] = (short) file.TextureVertexC[i];
            }

            if (file.Type1To3FaceCount == 0)
                return;

            TextureScaleP = new int[count];
            TextureScaleQ = new int[count];
            TextureScaleR = new int[count];
            TextureFieldA = new sbyte[count];
            TextureFieldB = new sbyte[count];
            TextureFieldC = new sbyte[count];
            TextureType2FieldA = new sbyte[count];
            TextureType2FieldB = new sbyte[count];

            for (int i = 0; i < count; i++) {
                //The scalars share the vertices' coordinate space, so they take the same shift.
                //method2592 leaves the third one alone for a type-1 face (Model.java:1697).
                int shift = VertexShift;
                TextureScaleP[i] = file.TextureScaleP[i] << shift;
                TextureScaleQ[i] = file.TextureScaleQ[i] << shift;
                TextureScaleR[i] = TextureType![i] == 1
                    ? file.TextureScaleR[i]
                    : file.TextureScaleR[i] << shift;

                TextureFieldA[i] = (sbyte) file.TextureFieldA[i];
                TextureFieldB[i] = (sbyte) file.TextureFieldB[i];
                TextureFieldC[i] = (sbyte) file.TextureFieldC[i];
                TextureType2FieldA[i] = (sbyte) file.TextureType2FieldA[i];
                TextureType2FieldB[i] = (sbyte) file.TextureType2FieldB[i];
            }
        }

        private void ProjectAttachments(ModelFile file) {
            Emitters = file.Emitters;
            Effectors = file.Effectors;
            Bonds = file.Bonds;
            ParticleEffectId = Emitters != null && Emitters.Length > 0
                ? (ushort) Emitters[0].EmitterId
                : (ushort) 0xFFFF;
        }

        /// <summary>
        ///     Applies the two passes the legacy decoder runs after everything else is read.
        /// </summary>
        /// <remarks>
        ///     An array whose flag was set but whose content is uniformly absent is dropped, so a
        ///     legacy model without textures reports no textures rather than an array of -1. The
        ///     coordinate pass additionally drops a mapping whose reference triangle is the face
        ///     itself, which is the identity mapping and needs no UV computation
        ///     (Model.java:1585-1604).
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        private void FinishLegacy(ModelFile file) {
            int count = file.FaceCount;

            if (FaceRenderType != null) {
                bool anyFlat = false;
                for (int i = 0; i < count && !anyFlat; i++)
                    anyFlat = FaceRenderType[i] == 1;
                if (!anyFlat)
                    FaceRenderType = null;
            }

            if (TextureCoordinates != null) {
                bool anyMapping = false;
                for (int i = 0; i < count; i++) {
                    int coordinate = TextureCoordinates[i];
                    if (coordinate == -1 || TexIndA == null || coordinate >= TexIndA.Length)
                        continue;

                    if ((TexIndA[coordinate] & 0xFFFF) != faceIndices1[i] ||
                        (TexIndB![coordinate] & 0xFFFF) != faceIndices2[i] ||
                        (TexIndC![coordinate] & 0xFFFF) != faceIndices3[i])
                        anyMapping = true;
                    else
                        TextureCoordinates[i] = -1;
                }

                if (!anyMapping)
                    TextureCoordinates = null;
            }

            if (FaceTextures == null)
                return;

            bool anyTexture = false;
            for (int i = 0; i < count && !anyTexture; i++)
                anyTexture = FaceTextures[i] != -1;
            if (!anyTexture)
                FaceTextures = null;
        }

        /// <summary>
        ///     The skin group a stored value stands for.
        /// </summary>
        /// <remarks>
        ///     The byte form uses 255 as "no group" on the new-protocol encoding only
        ///     (Model.java:1054-1058); the other two read a plain unsigned byte and 255 is a real
        ///     group there.
        /// </remarks>
        /// <param name="file">The model as stored.</param>
        /// <param name="stored">The stored value.</param>
        /// <param name="smart">Whether the block held smarts rather than bytes.</param>
        /// <returns>The group, or -1 for none.</returns>
        private static int SkinValue(ModelFile file, StoredSmart stored, bool smart) {
            if (smart)
                return stored.Value;
            if (file.Encoding == ModelEncoding.NewProtocol && stored.Value == 255)
                return -1;
            return stored.Value;
        }

        #endregion

        #region derived data

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
                        int textureRenderType = 0;
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
            /// <summary>Accumulated X component.</summary>
            public int x;
            /// <summary>Accumulated Y component.</summary>
            public int y;
            /// <summary>Accumulated Z component.</summary>
            public int z;
            /// <summary>How many face normals were folded in.</summary>
            public int magnitude;
        }

        /// <summary>Container for face normal vectors.</summary>
        public class FaceNormal {
            /// <summary>X component.</summary>
            public int x;
            /// <summary>Y component.</summary>
            public int y;
            /// <summary>Z component.</summary>
            public int z;
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
        /// <param name="i">The face index.</param>
        /// <returns>The priority.</returns>
        public int GetFacePriority(int i) => FacePriority != null ? FacePriority[i] : _globalPriority;

        #endregion

        #region helper methods

        /// <summary>
        /// Creates a shallow clone with deep-copied mutable arrays so that
        /// NPC/item recolour transforms don't corrupt the cached original.
        /// </summary>
        /// <returns>The clone.</returns>
        /// <remarks>
        ///     The clone shares <see cref="Source"/> with the original, so recolouring it does not
        ///     change what <see cref="Encode"/> would write - which is right today, because a
        ///     recolour is a rendering transform rather than an edit to the model.
        /// </remarks>
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
        /// </summary>
        /// <returns><c>int[TriangleCount][3]</c> of packed 0xRRGGBB per vertex of each face.</returns>
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
        /// </summary>
        /// <returns>
        ///     <c>int[TriangleCount][3]</c> of packed 0xRRGGBB at full brightness, suitable for
        ///     dynamic lighting in the GPU shader.
        /// </returns>
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
        /// </summary>
        /// <returns><c>float[TriangleCount][9]</c> - three (x,y,z) triples per face.</returns>
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

        /// <summary>Converts a packed HSV index to 24-bit sRGB via the precomputed palette.</summary>
        /// <param name="hsl">The packed palette index.</param>
        /// <returns>24-bit RGB.</returns>
        public static int HslToRgb(int hsl) => _hsl2Rgb[hsl & 0xFFFF];

        /// <summary>
        /// Converts a raw 16-bit HSL colour as stored in the cache straight to 24-bit RGB.
        /// </summary>
        /// <remarks>
        /// The two-step the client always performs together: <c>Class111_Sub2.method2117</c>
        /// redistributes saturation against lightness before the palette lookup, so indexing the
        /// palette with the raw value gives a visibly different colour. Callers outside the model
        /// decoder want this rather than <see cref="HslToRgb"/>.
        /// </remarks>
        /// <param name="rawHsl">The packed HSL as read from the cache.</param>
        /// <returns>24-bit RGB.</returns>
        public static int RawHslToRgb(int rawHsl) => HslToRgb(RepackHsl(rawHsl));

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
