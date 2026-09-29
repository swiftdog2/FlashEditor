using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Models.Interchange {
    /// <summary>
    ///     Everything a model built from nothing states: its mesh, one colour per face, and the
    ///     labels and flags that decide how the client draws and animates it.
    /// </summary>
    /// <remarks>
    ///     No alphas, per-face priorities, particles or bonds: a model that needs one of those
    ///     starts from a model that has it, through <see cref="ModelObjImporter"/>.
    /// </remarks>
    public sealed class ModelBlueprint {
        /// <summary>The model id, which is its index-7 group id.</summary>
        public int ModelId { get; init; }

        /// <summary>
        ///     The format type. Below 13 the client draws the model four times larger
        ///     (<see cref="ModelFile.VertexShift"/>), so a mesh in world units wants 13 or more.
        /// </summary>
        public int FormatType { get; init; } = 15;

        /// <summary>The vertices and faces, in stored space.</summary>
        public ModelGeometry Geometry { get; init; } = null!;

        /// <summary>One packed HSL colour word per face, as the file stores it.</summary>
        public ushort[] FaceColours { get; init; } = null!;

        /// <summary>
        ///     One render type per face (0 Gouraud, 1 flat, 2 not drawn), or null for no block, which
        ///     the client reads as 0 on every face.
        /// </summary>
        public byte[]? FaceRenderTypes { get; init; }

        /// <summary>The render priority every face shares, 0 to 254. 255 is the per-face marker.</summary>
        public int GlobalPriority { get; init; }

        /// <summary>One face label (skin group) per face, 0 to 255, or null for none.</summary>
        public int[]? FaceSkins { get; init; }

        /// <summary>One vertex label (skin group) per vertex, 0 to 255, or null for none.</summary>
        public int[]? VertexSkins { get; init; }

        /// <summary>Per-face textures and the triangles that map them, or null for an untextured model.</summary>
        public ModelTexturing? Texturing { get; init; }
    }

    /// <summary>
    ///     A model's texture data, in the stored form <see cref="ModelFile"/> holds it.
    /// </summary>
    /// <remarks>
    ///     Carried verbatim rather than derived, because there is nothing to derive it from: the
    ///     textured triangles and their projection scalars are authored, and
    ///     <see cref="ScaleSlack"/> holds bytes the client never reads yet the shipped files do not
    ///     leave zero (model 6702 carries <c>0A 00 00 52 00 00 53 00</c> there).
    /// </remarks>
    public sealed class ModelTexturing {
        /// <summary>Raw per-face texture id, one per face: the index-9 id plus one, 0 for none.</summary>
        public ushort[] FaceTextureIds { get; init; } = Array.Empty<ushort>();

        /// <summary>
        ///     Which textured triangle each textured face maps through, one per face whose raw id is
        ///     non-zero, in face order. A byte each in the newer format. Empty when
        ///     <see cref="TextureTypes"/> is, because the client then reads none.
        /// </summary>
        public int[] TextureCoords { get; init; } = Array.Empty<int>();

        /// <summary>Type of each textured triangle, 0 to 3.</summary>
        public byte[] TextureTypes { get; init; } = Array.Empty<byte>();

        /// <summary>First reference vertex of each textured triangle.</summary>
        public ushort[] VertexA { get; init; } = Array.Empty<ushort>();

        /// <summary>Second reference vertex of each textured triangle.</summary>
        public ushort[] VertexB { get; init; } = Array.Empty<ushort>();

        /// <summary>Third reference vertex of each textured triangle.</summary>
        public ushort[] VertexC { get; init; } = Array.Empty<ushort>();

        /// <summary>First projection scalar per triangle; unused on type 0.</summary>
        public int[] ScaleP { get; init; } = Array.Empty<int>();

        /// <summary>Second projection scalar per triangle; unused on type 0.</summary>
        public int[] ScaleQ { get; init; } = Array.Empty<int>();

        /// <summary>Third projection scalar per triangle; unused on type 0.</summary>
        public int[] ScaleR { get; init; } = Array.Empty<int>();

        /// <summary>The <c>i_96_</c> byte per triangle; unused on type 0.</summary>
        public byte[] FieldA { get; init; } = Array.Empty<byte>();

        /// <summary>The <c>i_97_</c> byte per triangle; unused on type 0.</summary>
        public byte[] FieldB { get; init; } = Array.Empty<byte>();

        /// <summary>The <c>i_98_</c> byte per triangle; unused on type 0.</summary>
        public byte[] FieldC { get; init; } = Array.Empty<byte>();

        /// <summary>First extra byte per triangle; read on type 2 only.</summary>
        public byte[] Type2FieldA { get; init; } = Array.Empty<byte>();

        /// <summary>Second extra byte per triangle; read on type 2 only.</summary>
        public byte[] Type2FieldB { get; init; } = Array.Empty<byte>();

        /// <summary>
        ///     The unread remainder of the projection block, whose declared size is a per-entry
        ///     maximum the narrower entries do not fill. Its length is fixed by the triangle types and
        ///     the format type; see <see cref="ModelFile.SlackTextureScale"/>.
        /// </summary>
        public byte[] ScaleSlack { get; init; } = Array.Empty<byte>();
    }

    /// <summary>
    ///     Builds a newer-format <see cref="ModelFile"/> from a <see cref="ModelBlueprint"/>, to be
    ///     encoded by <see cref="ModelCodec"/> like any decoded model.
    /// </summary>
    /// <remarks>
    ///     The vertex and face blocks come from <see cref="ModelGeometryEncoder"/>, so a built model
    ///     and an OBJ-edited one spell the same mesh the same way: zero deltas carry no mask bit,
    ///     smarts take their narrowest width, and each face takes the rolling strip opcode that
    ///     reaches it. Model 6702 re-encodes to its stored bytes under these rules, which is what
    ///     <c>RealCacheNewModelTests</c> pins.
    /// </remarks>
    public static class ModelFileBuilder {
        /// <summary>Flags bit 0: every face carries a render type.</summary>
        private const byte FaceTypesFlag = 0x1;

        /// <summary>Flags bit 3: the format type is stored in the byte before the footer.</summary>
        private const byte EmbeddedFormatTypeFlag = 0x8;

        /// <summary>The format type a newer-format model has when it stores none.</summary>
        private const int DefaultFormatType = 12;

        /// <summary>Builds the stored form of a blueprint.</summary>
        /// <param name="blueprint">What the model states.</param>
        /// <returns>A model ready for <see cref="ModelFile.Encode"/>.</returns>
        /// <exception cref="ModelImportException">
        ///     The id is one the client decodes as new-protocol, an array disagrees with the count it
        ///     is per, a value does not fit its field, or the mesh does not fit the format.
        /// </exception>
        public static ModelFile Build(ModelBlueprint blueprint) {
            if (blueprint == null)
                throw new ArgumentNullException(nameof(blueprint));
            if (blueprint.Geometry == null)
                throw new ArgumentException("A blueprint needs a mesh.", nameof(blueprint));
            if (blueprint.FaceColours == null)
                throw new ArgumentException("A blueprint needs a colour per face.", nameof(blueprint));

            ModelGeometry geometry = blueprint.Geometry;
            int vertices = geometry.VertexCount;
            int faces = geometry.FaceCount;

            RefuseNewProtocolId(blueprint.ModelId);
            RefuseCount(vertices, "vertices");
            RefuseCount(faces, "faces");
            RefuseLength(blueprint.FaceColours.Length, faces, "face colours");
            RefuseLength(blueprint.FaceRenderTypes?.Length, faces, "face render types");
            RefuseLength(blueprint.FaceSkins?.Length, faces, "face labels");
            RefuseLength(blueprint.VertexSkins?.Length, vertices, "vertex labels");
            RefuseRange(blueprint.FormatType, 0, 255, "format type");
            RefuseRange(blueprint.GlobalPriority, 0, 254, "global priority");
            ModelGeometryEncoder.RefuseOutOfRangeIndices(geometry);

            ModelGeometryEncoder.EncodeVertices(geometry, out byte[] vertexFlags,
                out StoredSmart[] deltasX, out StoredSmart[] deltasY, out StoredSmart[] deltasZ,
                out int lengthX, out int lengthY, out int lengthZ);
            ModelGeometryEncoder.EncodeFaces(geometry, out byte[] opcodes,
                out StoredSmart[] faceDeltas, out int faceLength);

            byte flags = 0;
            if (blueprint.FaceRenderTypes != null)
                flags |= FaceTypesFlag;
            if (blueprint.FormatType != DefaultFormatType)
                flags |= EmbeddedFormatTypeFlag;

            var model = new ModelFile {
                Encoding = ModelEncoding.Newer,
                ModelId = blueprint.ModelId,
                VertexCount = vertices,
                FaceCount = faces,
                FormatType = blueprint.FormatType,
                Flags = flags,
                PriorityFlag = (byte) blueprint.GlobalPriority,
                FaceSkinFlag = (byte) (blueprint.FaceSkins != null ? 1 : 0),
                VertexSkinFlag = (byte) (blueprint.VertexSkins != null ? 1 : 0),
                VertexXLength = lengthX,
                VertexYLength = lengthY,
                VertexZLength = lengthZ,
                FaceIndexLength = faceLength,
                Sentinel = new byte[] { 0xFF, 0xFF },
                VertexFlags = vertexFlags,
                FaceTypeBytes = blueprint.FaceRenderTypes,
                FaceOpcodes = opcodes,
                FaceSkins = Labels(blueprint.FaceSkins, "face label"),
                VertexSkins = Labels(blueprint.VertexSkins, "vertex label"),
                FaceColours = blueprint.FaceColours,
                VertexDeltasX = deltasX,
                VertexDeltasY = deltasY,
                VertexDeltasZ = deltasZ,
                FaceIndexDeltas = faceDeltas,
                SlackVertexX = Array.Empty<byte>(),
                SlackVertexY = Array.Empty<byte>(),
                SlackVertexZ = Array.Empty<byte>(),
                SlackFaceIndex = Array.Empty<byte>(),
                SlackTextureCoord = Array.Empty<byte>(),
                Gap = Array.Empty<byte>()
            };

            ApplyTexturing(model, blueprint.Texturing ?? new ModelTexturing());
            return model;
        }

        /// <summary>Writes the texture blocks into a model being built, after checking they fit it.</summary>
        /// <param name="model">The model, with its counts already set.</param>
        /// <param name="texturing">The texture data, empty for an untextured model.</param>
        private static void ApplyTexturing(ModelFile model, ModelTexturing texturing) {
            int triangles = texturing.TextureTypes.Length;
            RefuseRange(triangles, 0, 255, "textured triangle count");
            RefuseLength(texturing.VertexA.Length, triangles, "first reference vertices");
            RefuseLength(texturing.VertexB.Length, triangles, "second reference vertices");
            RefuseLength(texturing.VertexC.Length, triangles, "third reference vertices");
            RefuseLength(texturing.ScaleP.Length, triangles, "first projection scalars");
            RefuseLength(texturing.ScaleQ.Length, triangles, "second projection scalars");
            RefuseLength(texturing.ScaleR.Length, triangles, "third projection scalars");
            RefuseLength(texturing.FieldA.Length, triangles, "i_96_ bytes");
            RefuseLength(texturing.FieldB.Length, triangles, "i_97_ bytes");
            RefuseLength(texturing.FieldC.Length, triangles, "i_98_ bytes");
            RefuseLength(texturing.Type2FieldA.Length, triangles, "first type-2 bytes");
            RefuseLength(texturing.Type2FieldB.Length, triangles, "second type-2 bytes");
            foreach (byte type in texturing.TextureTypes)
                RefuseRange(type, 0, 3, "textured triangle type");

            bool hasIds = texturing.FaceTextureIds.Length > 0;
            if (hasIds)
                RefuseLength(texturing.FaceTextureIds.Length, model.FaceCount, "face texture ids");

            //The client reads a coordinate only where both the ids and a textured triangle exist
            //(Model.java:539-541), one per face with a non-zero id.
            int expectedCoords = 0;
            if (hasIds && triangles > 0) {
                foreach (ushort id in texturing.FaceTextureIds) {
                    if (id != 0)
                        expectedCoords++;
                }
            }
            RefuseLength(texturing.TextureCoords.Length, expectedCoords, "texture coordinate indices");

            var coords = new StoredSmart[expectedCoords];
            for (int i = 0; i < coords.Length; i++) {
                RefuseRange(texturing.TextureCoords[i], 0, 255, "texture coordinate index at " + i);
                coords[i] = new StoredSmart(texturing.TextureCoords[i], JagStream.SmartWidth.OneByte);
            }

            model.TexturedFaceCount = triangles;
            model.FaceTextureFlag = (byte) (hasIds ? 1 : 0);
            model.FaceTextureIds = hasIds ? texturing.FaceTextureIds : null;
            model.TextureCoords = coords;
            model.TextureCoordLength = coords.Length;
            model.TextureTypes = texturing.TextureTypes;
            model.TextureVertexA = texturing.VertexA;
            model.TextureVertexB = texturing.VertexB;
            model.TextureVertexC = texturing.VertexC;
            model.TextureScaleP = texturing.ScaleP;
            model.TextureScaleQ = texturing.ScaleQ;
            model.TextureScaleR = texturing.ScaleR;
            model.TextureFieldA = texturing.FieldA;
            model.TextureFieldB = texturing.FieldB;
            model.TextureFieldC = texturing.FieldC;
            model.TextureType2FieldA = texturing.Type2FieldA;
            model.TextureType2FieldB = texturing.Type2FieldB;

            int consumed = 0;
            foreach (byte type in texturing.TextureTypes) {
                if (type < 1)
                    continue;
                ModelFile.ScaleWidths(model.FormatType, type, out int p, out int q, out int r);
                consumed += p + q + r;
            }
            int slack = model.Type1To3FaceCount * ModelFile.ScaleStride(model.FormatType) - consumed;
            RefuseLength(texturing.ScaleSlack.Length, slack, "unread projection-block bytes");
            model.SlackTextureScale = texturing.ScaleSlack;
        }

        /// <summary>
        ///     Refuses an id the client would decode with the new-protocol decoder.
        /// </summary>
        /// <remarks>
        ///     The client selects that decoder by id alone (Model.java:90-94), so a newer-format model
        ///     stored at one of those ids is read with the wrong layout.
        /// </remarks>
        /// <param name="modelId">The id to check.</param>
        /// <exception cref="ModelImportException">The id is in the range.</exception>
        public static void RefuseNewProtocolId(int modelId) {
            if (modelId < ModelCodec.FirstNewProtocolModelId || modelId > ModelCodec.LastNewProtocolModelId)
                return;

            throw new ModelImportException(
                $"Model id {modelId} is one of {ModelCodec.FirstNewProtocolModelId}-" +
                $"{ModelCodec.LastNewProtocolModelId}, which the 637 client hard-codes to its " +
                "new-protocol decoder (Model.java:90-94). A model built here is newer-format and " +
                "would be read with the wrong layout there. Index 7 can only append at the next id, " +
                "so no new model can be created in this cache until that range is behind it.");
        }

        private static void RefuseCount(int count, string what) {
            if (count <= ModelGeometryEncoder.MaxCount)
                return;
            throw new ModelImportException(
                $"The mesh has {count} {what}, and the footer stores the count in two bytes, so " +
                $"{ModelGeometryEncoder.MaxCount} is the most a model can hold.");
        }

        private static void RefuseLength(int? length, int expected, string what) {
            if (length == null || length.Value == expected)
                return;
            throw new ModelImportException(
                $"There are {length} {what} for {expected} entries; the file indexes them by position.");
        }

        private static void RefuseRange(int value, int low, int high, string what) {
            if (value >= low && value <= high)
                return;
            throw new ModelImportException($"The {what} is {value}; the field holds {low} to {high}.");
        }

        private static StoredSmart[]? Labels(int[]? labels, string what) {
            if (labels == null)
                return null;

            var stored = new StoredSmart[labels.Length];
            for (int i = 0; i < labels.Length; i++) {
                RefuseRange(labels[i], 0, 255, what + " at " + i);
                stored[i] = new StoredSmart(labels[i], JagStream.SmartWidth.OneByte);
            }
            return stored;
        }
    }
}
