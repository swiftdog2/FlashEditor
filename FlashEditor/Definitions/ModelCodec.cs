using System;
using System.IO;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     Reads and writes index 7's three model layouts, byte for byte.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A model's header sits at the <em>end</em> of the buffer and every block is located by an
    ///     offset accumulated from it, so nothing here is a sequential parse and the usual
    ///     exact-consumption test does not apply. Two things stand in for it. A block that reaches
    ///     outside the buffer, or a length that comes out negative because two blocks overlap, is
    ///     refused outright. Anything left between the end of the last block and the footer is
    ///     captured as <see cref="ModelFile.Gap"/> and written back rather than assumed away - it is
    ///     empty for every model in both caches, which is a claim for a sweep to make rather than
    ///     something to bake in here.
    ///     </para>
    ///     <para>
    ///     Ported from the 637 client, which is the authority for every field width and read order
    ///     here: <c>decoder_newer_format</c> (Model.java:381-807), <c>method2587</c>
    ///     (Model.java:1363-1620) and <c>decoder_newest_format</c> (Model.java:809-1330).
    ///     </para>
    /// </remarks>
    public static class ModelCodec {
        /// <summary>Lowest model id the client treats as new-protocol.</summary>
        /// <remarks>
        ///     The client hard-codes the range at Model.java:84 rather than reading a marker, so
        ///     these seven ids are the format selector. Only the repack holds them; the vanilla
        ///     b639 capture stops at 63606.
        /// </remarks>
        public const int FirstNewProtocolModelId = 63607;

        /// <summary>Highest model id the client treats as new-protocol.</summary>
        public const int LastNewProtocolModelId = 63613;

        /// <summary>Footer size of a newer-format model, including the two sentinel bytes.</summary>
        private const int NewerFooterSize = 23;

        /// <summary>Footer size of a legacy model.</summary>
        private const int LegacyFooterSize = 18;

        /// <summary>Footer size of a new-protocol model.</summary>
        private const int NewProtocolFooterSize = 26;

        /// <summary>Header size of a new-protocol model.</summary>
        private const int NewProtocolHeaderSize = 3;

        /// <summary>
        ///     Which layout a model uses, decided exactly as the client decides it.
        /// </summary>
        /// <remarks>
        ///     The id is tested first and unconditionally (Model.java:84-94), so a new-protocol
        ///     model is never examined for a sentinel. Everything else is newer if and only if it
        ///     ends <c>FF FF</c>; the client has no third sentinel, and neither cache holds a model
        ///     ending <c>FF FD</c>.
        /// </remarks>
        /// <param name="data">The stored model bytes.</param>
        /// <param name="modelId">The group id the model was read from.</param>
        /// <returns>The layout to decode with.</returns>
        public static ModelEncoding ClassifyEncoding(byte[] data, int modelId) {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (modelId >= FirstNewProtocolModelId && modelId <= LastNewProtocolModelId)
                return ModelEncoding.NewProtocol;
            if (data.Length < 2)
                throw new InvalidDataException("A model needs at least a sentinel; this one is " +
                                               data.Length + " bytes.");
            return data[data.Length - 1] == 0xFF && data[data.Length - 2] == 0xFF
                ? ModelEncoding.Newer
                : ModelEncoding.Legacy;
        }

        /// <summary>
        ///     Decodes a model into the form that can be written back unchanged.
        /// </summary>
        /// <param name="stream">The stored model bytes.</param>
        /// <param name="modelId">The group id the model was read from, which selects the layout.</param>
        /// <returns>The stored form.</returns>
        public static ModelFile Decode(JagStream stream, int modelId) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            return Decode(stream.ToArray(), modelId);
        }

        /// <summary>
        ///     Decodes a model into the form that can be written back unchanged.
        /// </summary>
        /// <param name="data">The stored model bytes.</param>
        /// <param name="modelId">The group id the model was read from, which selects the layout.</param>
        /// <returns>The stored form.</returns>
        /// <exception cref="InvalidDataException">
        ///     The declared block lengths do not account for the buffer, or a block reaches outside
        ///     it. Both mean the file was not read the way the client reads it, and a decoder that
        ///     carried on would silently produce geometry from the wrong bytes.
        /// </exception>
        public static ModelFile Decode(byte[] data, int modelId) {
            ModelEncoding encoding = ClassifyEncoding(data, modelId);
            switch (encoding) {
                case ModelEncoding.Legacy:
                    return DecodeLegacy(data, modelId);
                case ModelEncoding.Newer:
                    return DecodeNewer(data, modelId);
                default:
                    return DecodeNewProtocol(data, modelId);
            }
        }

        // ===================================================================
        //  Newer - decoder_newer_format, Model.java:381
        // ===================================================================

        private static ModelFile DecodeNewer(byte[] data, int modelId) {
            Require(data.Length >= NewerFooterSize, modelId, "shorter than its own footer");

            var model = new ModelFile { Encoding = ModelEncoding.Newer, ModelId = modelId };
            var footer = new Cursor(data, data.Length - NewerFooterSize, modelId);

            model.VertexCount = footer.U16();
            model.FaceCount = footer.U16();
            model.TexturedFaceCount = footer.U8();
            model.Flags = (byte) footer.U8();

            /* The client reaches the format type with caret -= 7 then caret += 6, which lands one
               byte before the footer rather than inside it (Model.java:401-405). An encoder that
               sizes the tail as the footer alone loses that byte on all 24,557 models that carry
               one in the vanilla capture. */
            model.FormatType = 12;
            if (model.HasEmbeddedFormatType) {
                Require(data.Length >= NewerFooterSize + 1, modelId, "declares an embedded format type but has no room for it");
                model.FormatType = data[data.Length - NewerFooterSize - 1];
            }

            model.PriorityFlag = (byte) footer.U8();
            model.AlphaFlag = (byte) footer.U8();
            model.FaceSkinFlag = (byte) footer.U8();
            model.FaceTextureFlag = (byte) footer.U8();
            model.VertexSkinFlag = (byte) footer.U8();
            model.VertexXLength = footer.U16();
            model.VertexYLength = footer.U16();
            model.VertexZLength = footer.U16();
            model.FaceIndexLength = footer.U16();
            model.TextureCoordLength = footer.U16();
            model.Sentinel = Slice(data, data.Length - 2, 2, modelId);

            int vc = model.VertexCount;
            int fc = model.FaceCount;
            int tfc = model.TexturedFaceCount;

            model.TextureTypes = Slice(data, 0, tfc, modelId);
            int type0 = model.Type0FaceCount;
            int type13 = model.Type1To3FaceCount;
            int type2 = model.Type2FaceCount;

            int at = tfc;
            int offVertexFlags = Advance(ref at, vc);
            int offFaceTypes = Advance(ref at, model.HasFaceTypes ? fc : 0);
            int offOpcodes = Advance(ref at, fc);
            int offPriorities = Advance(ref at, model.PriorityFlag == 255 ? fc : 0);
            int offFaceSkins = Advance(ref at, model.FaceSkinFlag == 1 ? fc : 0);
            int offVertexSkins = Advance(ref at, model.VertexSkinFlag == 1 ? vc : 0);
            int offAlphas = Advance(ref at, model.AlphaFlag == 1 ? fc : 0);
            int offFaceIndex = Advance(ref at, model.FaceIndexLength);
            int offTextureIds = Advance(ref at, model.FaceTextureFlag == 1 ? fc * 2 : 0);
            int offTextureCoords = Advance(ref at, model.TextureCoordLength);
            int offColours = Advance(ref at, fc * 2);
            int offX = Advance(ref at, model.VertexXLength);
            int offY = Advance(ref at, model.VertexYLength);
            int offZ = Advance(ref at, model.VertexZLength);
            int offType0 = Advance(ref at, type0 * 6);
            int offType13 = Advance(ref at, type13 * 6);
            int offScale = Advance(ref at, type13 * ModelFile.ScaleStride(model.FormatType));
            int offFieldA = Advance(ref at, type13);
            int offFieldB = Advance(ref at, type13);
            int offFieldC = Advance(ref at, type13 + type2 * 2);
            int offTail = at;

            model.VertexFlags = Slice(data, offVertexFlags, vc, modelId);
            model.FaceTypeBytes = model.HasFaceTypes ? Slice(data, offFaceTypes, fc, modelId) : null;
            model.FaceOpcodes = Slice(data, offOpcodes, fc, modelId);
            model.FacePriorities = model.PriorityFlag == 255 ? Slice(data, offPriorities, fc, modelId) : null;
            model.FaceAlphas = model.AlphaFlag == 1 ? Slice(data, offAlphas, fc, modelId) : null;
            model.FaceSkins = model.FaceSkinFlag == 1 ? ReadByteSkins(data, offFaceSkins, fc, modelId) : null;
            model.VertexSkins = model.VertexSkinFlag == 1 ? ReadByteSkins(data, offVertexSkins, vc, modelId) : null;
            model.FaceTextureIds = model.FaceTextureFlag == 1 ? ReadUShorts(data, offTextureIds, fc, modelId) : null;
            model.FaceColours = ReadUShorts(data, offColours, fc, modelId);

            ReadVertexDeltas(data, model, offX, offY, offZ, modelId);
            ReadFaceIndexDeltas(data, model, offFaceIndex, 0xFF, modelId);
            ReadTextureCoords(data, model, offTextureCoords, false, modelId);
            ReadTexturedFaces(data, model, offType0, offType13, offScale, offFieldA, offFieldB, offFieldC, modelId);

            int footerStart = data.Length - NewerFooterSize - (model.HasEmbeddedFormatType ? 1 : 0);
            ReadTail(data, model, offTail, footerStart, modelId);
            return model;
        }

        // ===================================================================
        //  Legacy - method2587, Model.java:1363
        // ===================================================================

        private static ModelFile DecodeLegacy(byte[] data, int modelId) {
            Require(data.Length >= LegacyFooterSize, modelId, "shorter than its own footer");

            var model = new ModelFile { Encoding = ModelEncoding.Legacy, ModelId = modelId, FormatType = 12 };
            var footer = new Cursor(data, data.Length - LegacyFooterSize, modelId);

            model.VertexCount = footer.U16();
            model.FaceCount = footer.U16();
            model.TexturedFaceCount = footer.U8();
            model.LegacyFaceMaskFlag = (byte) footer.U8();
            model.PriorityFlag = (byte) footer.U8();
            model.AlphaFlag = (byte) footer.U8();
            model.FaceSkinFlag = (byte) footer.U8();
            model.VertexSkinFlag = (byte) footer.U8();
            model.VertexXLength = footer.U16();
            model.VertexYLength = footer.U16();
            model.VertexZLength = footer.U16();
            model.FaceIndexLength = footer.U16();

            int vc = model.VertexCount;
            int fc = model.FaceCount;
            int tfc = model.TexturedFaceCount;

            /* The legacy block order is not the newer one with a block removed: the face skins,
               render mask and vertex skins sit in a different sequence (Model.java:1391-1409). */
            int at = 0;
            int offVertexFlags = Advance(ref at, vc);
            int offOpcodes = Advance(ref at, fc);
            int offPriorities = Advance(ref at, model.PriorityFlag == 255 ? fc : 0);
            int offFaceSkins = Advance(ref at, model.FaceSkinFlag == 1 ? fc : 0);
            int offFaceTypes = Advance(ref at, model.HasFaceTypes ? fc : 0);
            int offVertexSkins = Advance(ref at, model.VertexSkinFlag == 1 ? vc : 0);
            int offAlphas = Advance(ref at, model.AlphaFlag == 1 ? fc : 0);
            int offFaceIndex = Advance(ref at, model.FaceIndexLength);
            int offColours = Advance(ref at, fc * 2);
            int offTextured = Advance(ref at, tfc * 6);
            int offX = Advance(ref at, model.VertexXLength);
            int offY = Advance(ref at, model.VertexYLength);
            int offZ = Advance(ref at, model.VertexZLength);
            int dataEnd = at;

            model.VertexFlags = Slice(data, offVertexFlags, vc, modelId);
            model.FaceOpcodes = Slice(data, offOpcodes, fc, modelId);
            model.FacePriorities = model.PriorityFlag == 255 ? Slice(data, offPriorities, fc, modelId) : null;
            model.FaceSkins = model.FaceSkinFlag == 1 ? ReadByteSkins(data, offFaceSkins, fc, modelId) : null;
            model.FaceTypeBytes = model.HasFaceTypes ? Slice(data, offFaceTypes, fc, modelId) : null;
            model.VertexSkins = model.VertexSkinFlag == 1 ? ReadByteSkins(data, offVertexSkins, vc, modelId) : null;
            model.FaceAlphas = model.AlphaFlag == 1 ? Slice(data, offAlphas, fc, modelId) : null;
            model.FaceColours = ReadUShorts(data, offColours, fc, modelId);

            ReadVertexDeltas(data, model, offX, offY, offZ, modelId);
            ReadFaceIndexDeltas(data, model, offFaceIndex, 0xFF, modelId);

            model.TextureVertexA = new ushort[tfc];
            model.TextureVertexB = new ushort[tfc];
            model.TextureVertexC = new ushort[tfc];
            var textured = new Cursor(data, offTextured, modelId);
            for (int i = 0; i < tfc; i++) {
                model.TextureVertexA[i] = (ushort) textured.U16();
                model.TextureVertexB[i] = (ushort) textured.U16();
                model.TextureVertexC[i] = (ushort) textured.U16();
            }

            model.TextureCoords = Array.Empty<StoredSmart>();
            model.SlackTextureCoord = Array.Empty<byte>();
            model.SlackTextureScale = Array.Empty<byte>();
            model.Gap = Slice(data, dataEnd, data.Length - LegacyFooterSize - dataEnd, modelId);
            return model;
        }

        // ===================================================================
        //  New protocol - decoder_newest_format with newProtocol, Model.java:809
        // ===================================================================

        private static ModelFile DecodeNewProtocol(byte[] data, int modelId) {
            Require(data.Length >= NewProtocolHeaderSize + NewProtocolFooterSize, modelId,
                "shorter than its own header and footer");

            var model = new ModelFile { Encoding = ModelEncoding.NewProtocol, ModelId = modelId };
            model.Header = Slice(data, 0, NewProtocolHeaderSize, modelId);
            Require(model.Header[0] == 1, modelId, "is new-protocol but its version byte is not 1");
            model.FormatType = model.Header[2];

            var footer = new Cursor(data, data.Length - NewProtocolFooterSize, modelId);
            model.VertexCount = footer.U16();
            model.FaceCount = footer.U16();
            model.TexturedFaceCount = footer.U16();
            model.Flags = (byte) footer.U8();

            /* Bit 3 would send the client seven bytes back from here, which on a 26-byte footer
               lands inside the vertex count rather than before the footer. No new-protocol model
               in either cache sets it, so rather than reproduce a read that cannot be right, this
               refuses. */
            Require(!model.HasEmbeddedFormatType, modelId,
                "sets the embedded-format-type bit on a new-protocol footer, where the client's " +
                "backwards seek lands inside the footer itself");
            Require(!model.HasTrailingBlock, modelId,
                "sets flags bit 7, whose trailing block is unreachable in both caches and cannot " +
                "be located without guessing");

            model.PriorityFlag = (byte) footer.U8();
            model.AlphaFlag = (byte) footer.U8();
            model.FaceSkinFlag = (byte) footer.U8();
            model.FaceTextureFlag = (byte) footer.U8();
            model.VertexSkinFlag = (byte) footer.U8();
            model.VertexXLength = footer.U16();
            model.VertexYLength = footer.U16();
            model.VertexZLength = footer.U16();
            model.FaceIndexLength = footer.U16();
            model.TextureCoordLength = footer.U16();
            model.StoredVertexSkinLength = footer.U16();
            model.StoredFaceSkinLength = footer.U16();

            int vc = model.VertexCount;
            int fc = model.FaceCount;
            int tfc = model.TexturedFaceCount;

            /* The stored lengths are ignored unless bits 4 and 5 say so, and six of the seven
               new-protocol models store a figure the client then throws away. Both are written back
               as stored. */
            int vertexSkinLength = model.VertexSkinsAreSmart
                ? model.StoredVertexSkinLength
                : (model.VertexSkinFlag == 1 ? vc : 0);
            int faceSkinLength = model.FaceSkinsAreSmart
                ? model.StoredFaceSkinLength
                : (model.FaceSkinFlag == 1 ? fc : 0);

            model.TextureTypes = Slice(data, NewProtocolHeaderSize, tfc, modelId);
            int type0 = model.Type0FaceCount;
            int type13 = model.Type1To3FaceCount;
            int type2 = model.Type2FaceCount;

            int at = NewProtocolHeaderSize + tfc;
            int offVertexFlags = Advance(ref at, vc);
            int offFaceTypes = Advance(ref at, model.HasFaceTypes ? fc : 0);
            int offOpcodes = Advance(ref at, fc);
            int offPriorities = Advance(ref at, model.PriorityFlag == 255 ? fc : 0);
            int offFaceSkins = Advance(ref at, faceSkinLength);
            int offVertexSkins = Advance(ref at, vertexSkinLength);
            int offAlphas = Advance(ref at, model.AlphaFlag == 1 ? fc : 0);
            int offFaceIndex = Advance(ref at, model.FaceIndexLength);
            int offTextureIds = Advance(ref at, model.FaceTextureFlag == 1 ? fc * 2 : 0);
            int offTextureCoords = Advance(ref at, model.TextureCoordLength);
            int offColours = Advance(ref at, fc * 2);
            int offX = Advance(ref at, model.VertexXLength);
            int offY = Advance(ref at, model.VertexYLength);
            int offZ = Advance(ref at, model.VertexZLength);
            int offType0 = Advance(ref at, type0 * 6);
            int offType13 = Advance(ref at, type13 * 6);
            int offScale = Advance(ref at, type13 * ModelFile.ScaleStride(model.FormatType));
            int offFieldA = Advance(ref at, type13);
            int offFieldB = Advance(ref at, type13);
            int offFieldC = Advance(ref at, type13 + type2 * 2);
            int offTail = at;

            model.VertexFlags = Slice(data, offVertexFlags, vc, modelId);
            model.FaceTypeBytes = model.HasFaceTypes ? Slice(data, offFaceTypes, fc, modelId) : null;
            model.FaceOpcodes = Slice(data, offOpcodes, fc, modelId);
            model.FacePriorities = model.PriorityFlag == 255 ? Slice(data, offPriorities, fc, modelId) : null;
            model.FaceAlphas = model.AlphaFlag == 1 ? Slice(data, offAlphas, fc, modelId) : null;
            model.FaceTextureIds = model.FaceTextureFlag == 1 ? ReadUShorts(data, offTextureIds, fc, modelId) : null;
            model.FaceColours = ReadUShorts(data, offColours, fc, modelId);

            model.FaceSkins = ReadSkinBlock(data, offFaceSkins, faceSkinLength, fc,
                model.FaceSkinFlag == 1, model.FaceSkinsAreSmart, out byte[] faceSkinSlack, modelId);
            model.SlackFaceSkin = faceSkinSlack;
            model.VertexSkins = ReadSkinBlock(data, offVertexSkins, vertexSkinLength, vc,
                model.VertexSkinFlag == 1, model.VertexSkinsAreSmart, out byte[] vertexSkinSlack, modelId);
            model.SlackVertexSkin = vertexSkinSlack;

            ReadVertexDeltas(data, model, offX, offY, offZ, modelId);
            ReadFaceIndexDeltas(data, model, offFaceIndex, 0x7, modelId);
            ReadTextureCoords(data, model, offTextureCoords, model.FormatType >= 16, modelId);
            ReadTexturedFaces(data, model, offType0, offType13, offScale, offFieldA, offFieldB, offFieldC, modelId);

            ReadTail(data, model, offTail, data.Length - NewProtocolFooterSize, modelId);
            return model;
        }

        // ===================================================================
        //  Shared block readers
        // ===================================================================

        private static void ReadVertexDeltas(byte[] data, ModelFile model, int offX, int offY, int offZ, int modelId) {
            var x = new Cursor(data, offX, modelId);
            var y = new Cursor(data, offY, modelId);
            var z = new Cursor(data, offZ, modelId);

            int countX = 0, countY = 0, countZ = 0;
            foreach (byte mask in model.VertexFlags) {
                if ((mask & 1) != 0) countX++;
                if ((mask & 2) != 0) countY++;
                if ((mask & 4) != 0) countZ++;
            }

            var deltasX = new StoredSmart[countX];
            var deltasY = new StoredSmart[countY];
            var deltasZ = new StoredSmart[countZ];
            countX = countY = countZ = 0;
            foreach (byte mask in model.VertexFlags) {
                if ((mask & 1) != 0) deltasX[countX++] = x.Smart();
                if ((mask & 2) != 0) deltasY[countY++] = y.Smart();
                if ((mask & 4) != 0) deltasZ[countZ++] = z.Smart();
            }

            model.VertexDeltasX = deltasX;
            model.VertexDeltasY = deltasY;
            model.VertexDeltasZ = deltasZ;
            model.SlackVertexX = Slice(data, x.Position, offX + model.VertexXLength - x.Position, modelId);
            model.SlackVertexY = Slice(data, y.Position, offY + model.VertexYLength - y.Position, modelId);
            model.SlackVertexZ = Slice(data, z.Position, offZ + model.VertexZLength - z.Position, modelId);
        }

        private static void ReadFaceIndexDeltas(byte[] data, ModelFile model, int offset, int opcodeMask, int modelId) {
            var cursor = new Cursor(data, offset, modelId);
            int count = 0;
            foreach (byte raw in model.FaceOpcodes)
                count += DeltasFor(raw & opcodeMask);

            var deltas = new StoredSmart[count];
            count = 0;
            foreach (byte raw in model.FaceOpcodes) {
                int needed = DeltasFor(raw & opcodeMask);
                for (int i = 0; i < needed; i++)
                    deltas[count++] = cursor.Smart();
            }

            model.FaceIndexDeltas = deltas;
            model.SlackFaceIndex = Slice(data, cursor.Position,
                offset + model.FaceIndexLength - cursor.Position, modelId);
        }

        /// <summary>
        ///     How many deltas a strip opcode consumes.
        /// </summary>
        /// <remarks>
        ///     Opcode 1 restarts the strip with three fresh vertices; 2, 3 and 4 each roll one
        ///     vertex forward. Anything else reads nothing at all - the client's four <c>if</c>
        ///     blocks simply all fail - so it must consume nothing here either.
        /// </remarks>
        /// <param name="opcode">The strip opcode.</param>
        /// <returns>The number of smart deltas that follow.</returns>
        private static int DeltasFor(int opcode) {
            if (opcode == 1)
                return 3;
            return opcode >= 2 && opcode <= 4 ? 1 : 0;
        }

        private static void ReadTextureCoords(byte[] data, ModelFile model, int offset, bool smart, int modelId) {
            var cursor = new Cursor(data, offset, modelId);

            /* The client allocates the coordinate array only when both a per-face texture id and at
               least one textured face are present (Model.java:539-541), and reads an entry only for
               a face whose stored texture id is non-zero. 65 models in the vanilla capture carry
               texture ids with no textured faces at all, and read nothing here. */
            bool present = model.FaceTextureFlag == 1 && model.TexturedFaceCount > 0;
            int count = 0;
            if (present) {
                foreach (ushort id in model.FaceTextureIds!) {
                    if (id != 0)
                        count++;
                }
            }

            var coords = new StoredSmart[count];
            count = 0;
            if (present) {
                foreach (ushort id in model.FaceTextureIds!) {
                    if (id == 0)
                        continue;
                    coords[count++] = smart ? cursor.UnsignedSmart() : cursor.Byte();
                }
            }

            model.TextureCoords = coords;
            model.SlackTextureCoord = Slice(data, cursor.Position,
                offset + model.TextureCoordLength - cursor.Position, modelId);
        }

        private static void ReadTexturedFaces(byte[] data, ModelFile model, int offType0, int offType13,
            int offScale, int offFieldA, int offFieldB, int offFieldC, int modelId) {
            int tfc = model.TexturedFaceCount;
            model.TextureVertexA = new ushort[tfc];
            model.TextureVertexB = new ushort[tfc];
            model.TextureVertexC = new ushort[tfc];
            model.TextureScaleP = new int[tfc];
            model.TextureScaleQ = new int[tfc];
            model.TextureScaleR = new int[tfc];
            model.TextureFieldA = new byte[tfc];
            model.TextureFieldB = new byte[tfc];
            model.TextureFieldC = new byte[tfc];
            model.TextureType2FieldA = new byte[tfc];
            model.TextureType2FieldB = new byte[tfc];

            var type0 = new Cursor(data, offType0, modelId);
            var type13 = new Cursor(data, offType13, modelId);
            var scale = new Cursor(data, offScale, modelId);
            var fieldA = new Cursor(data, offFieldA, modelId);
            var fieldB = new Cursor(data, offFieldB, modelId);
            var fieldC = new Cursor(data, offFieldC, modelId);

            for (int i = 0; i < tfc; i++) {
                int type = model.TextureTypes![i];
                if (type == 0) {
                    model.TextureVertexA[i] = (ushort) type0.U16();
                    model.TextureVertexB[i] = (ushort) type0.U16();
                    model.TextureVertexC[i] = (ushort) type0.U16();
                    continue;
                }

                if (type < 1 || type > 3)
                    continue;

                model.TextureVertexA[i] = (ushort) type13.U16();
                model.TextureVertexB[i] = (ushort) type13.U16();
                model.TextureVertexC[i] = (ushort) type13.U16();

                ModelFile.ScaleWidths(model.FormatType, type, out int wp, out int wq, out int wr);
                model.TextureScaleP[i] = wp == 2 ? scale.U16() : scale.Medium();
                model.TextureScaleQ[i] = wq == 2 ? scale.U16() : scale.Medium();
                model.TextureScaleR[i] = wr == 2 ? scale.U16() : scale.Medium();

                model.TextureFieldA[i] = (byte) fieldA.U8();
                model.TextureFieldB[i] = (byte) fieldB.U8();
                model.TextureFieldC[i] = (byte) fieldC.U8();
                if (type == 2) {
                    model.TextureType2FieldA[i] = (byte) fieldC.U8();
                    model.TextureType2FieldB[i] = (byte) fieldC.U8();
                }
            }

            int declared = model.Type1To3FaceCount * ModelFile.ScaleStride(model.FormatType);
            model.SlackTextureScale = Slice(data, scale.Position, offScale + declared - scale.Position, modelId);
        }

        private static void ReadTail(byte[] data, ModelFile model, int offset, int footerStart, int modelId) {
            var cursor = new Cursor(data, offset, modelId);

            if ((model.Flags & 0x2) == 2) {
                int emitters = cursor.U8();
                model.Emitters = new ModelParticleEmitter[emitters];
                for (int i = 0; i < emitters; i++)
                    model.Emitters[i] = new ModelParticleEmitter(cursor.U16(), cursor.U16());

                int effectors = cursor.U8();
                model.Effectors = new ModelParticleEffector[effectors];
                for (int i = 0; i < effectors; i++)
                    model.Effectors[i] = new ModelParticleEffector(cursor.U16(), cursor.U16());
            }

            if ((model.Flags & 0x4) == 4) {
                int bonds = cursor.U8();
                model.Bonds = new ModelBond[bonds];
                for (int i = 0; i < bonds; i++) {
                    int billboard = cursor.U16();
                    int face = cursor.U16();
                    StoredSmart third = model.BondFieldIsSmart ? cursor.SpecialSmart() : cursor.Byte();
                    model.Bonds[i] = new ModelBond(billboard, face, third, (sbyte) cursor.U8());
                }
            }

            model.Gap = Slice(data, cursor.Position, footerStart - cursor.Position, modelId);
        }

        private static StoredSmart[] ReadByteSkins(byte[] data, int offset, int count, int modelId) {
            var cursor = new Cursor(data, offset, modelId);
            var skins = new StoredSmart[count];
            for (int i = 0; i < count; i++)
                skins[i] = cursor.Byte();
            return skins;
        }

        private static StoredSmart[]? ReadSkinBlock(byte[] data, int offset, int declared,
            int count, bool present, bool smart, out byte[] slack, int modelId) {
            var cursor = new Cursor(data, offset, modelId);
            StoredSmart[]? skins = null;

            if (present) {
                skins = new StoredSmart[count];
                for (int i = 0; i < count; i++)
                    skins[i] = smart ? cursor.SpecialSmart() : cursor.Byte();
            }

            slack = Slice(data, cursor.Position, offset + declared - cursor.Position, modelId);
            return skins;
        }

        private static ushort[] ReadUShorts(byte[] data, int offset, int count, int modelId) {
            var cursor = new Cursor(data, offset, modelId);
            var values = new ushort[count];
            for (int i = 0; i < count; i++)
                values[i] = (ushort) cursor.U16();
            return values;
        }

        // ===================================================================
        //  Encoding
        // ===================================================================

        /// <summary>
        ///     Writes a model back out in the layout it was read from.
        /// </summary>
        /// <remarks>
        ///     Every block is emitted in the offset order the decoder accumulated, so the offsets a
        ///     re-read would compute are the ones this wrote to. The declared lengths are replayed
        ///     rather than recomputed, and the unread remainder of each block goes back with it.
        /// </remarks>
        /// <param name="model">The stored form to write.</param>
        /// <returns>The bytes index 7 would hold for it.</returns>
        public static JagStream Encode(ModelFile model) {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var stream = new JagStream();
            switch (model.Encoding) {
                case ModelEncoding.Legacy:
                    EncodeLegacy(model, stream);
                    break;
                case ModelEncoding.Newer:
                    EncodeNewer(model, stream);
                    break;
                default:
                    EncodeNewProtocol(model, stream);
                    break;
            }
            return stream.Flip();
        }

        private static void EncodeNewer(ModelFile model, JagStream stream) {
            Raw(stream, model.TextureTypes);
            Raw(stream, model.VertexFlags);
            Raw(stream, model.FaceTypeBytes);
            Raw(stream, model.FaceOpcodes);
            Raw(stream, model.FacePriorities);
            WriteByteSkins(stream, model.FaceSkins);
            WriteByteSkins(stream, model.VertexSkins);
            Raw(stream, model.FaceAlphas);
            WriteSmarts(stream, model.FaceIndexDeltas);
            Raw(stream, model.SlackFaceIndex);
            WriteUShorts(stream, model.FaceTextureIds);
            WriteTextureCoords(stream, model, false);
            Raw(stream, model.SlackTextureCoord);
            WriteUShorts(stream, model.FaceColours);
            WriteSmarts(stream, model.VertexDeltasX);
            Raw(stream, model.SlackVertexX);
            WriteSmarts(stream, model.VertexDeltasY);
            Raw(stream, model.SlackVertexY);
            WriteSmarts(stream, model.VertexDeltasZ);
            Raw(stream, model.SlackVertexZ);
            WriteTexturedFaces(stream, model);
            WriteTail(stream, model);
            Raw(stream, model.Gap);

            if (model.HasEmbeddedFormatType)
                stream.WriteByte(model.FormatType);

            stream.WriteShort(model.VertexCount);
            stream.WriteShort(model.FaceCount);
            stream.WriteByte(model.TexturedFaceCount);
            stream.WriteByte(model.Flags);
            stream.WriteByte(model.PriorityFlag);
            stream.WriteByte(model.AlphaFlag);
            stream.WriteByte(model.FaceSkinFlag);
            stream.WriteByte(model.FaceTextureFlag);
            stream.WriteByte(model.VertexSkinFlag);
            stream.WriteShort(model.VertexXLength);
            stream.WriteShort(model.VertexYLength);
            stream.WriteShort(model.VertexZLength);
            stream.WriteShort(model.FaceIndexLength);
            stream.WriteShort(model.TextureCoordLength);
            Raw(stream, model.Sentinel);
        }

        private static void EncodeLegacy(ModelFile model, JagStream stream) {
            Raw(stream, model.VertexFlags);
            Raw(stream, model.FaceOpcodes);
            Raw(stream, model.FacePriorities);
            WriteByteSkins(stream, model.FaceSkins);
            Raw(stream, model.FaceTypeBytes);
            WriteByteSkins(stream, model.VertexSkins);
            Raw(stream, model.FaceAlphas);
            WriteSmarts(stream, model.FaceIndexDeltas);
            Raw(stream, model.SlackFaceIndex);
            WriteUShorts(stream, model.FaceColours);
            for (int i = 0; i < model.TexturedFaceCount; i++) {
                stream.WriteShort(model.TextureVertexA[i]);
                stream.WriteShort(model.TextureVertexB[i]);
                stream.WriteShort(model.TextureVertexC[i]);
            }
            WriteSmarts(stream, model.VertexDeltasX);
            Raw(stream, model.SlackVertexX);
            WriteSmarts(stream, model.VertexDeltasY);
            Raw(stream, model.SlackVertexY);
            WriteSmarts(stream, model.VertexDeltasZ);
            Raw(stream, model.SlackVertexZ);
            Raw(stream, model.Gap);

            stream.WriteShort(model.VertexCount);
            stream.WriteShort(model.FaceCount);
            stream.WriteByte(model.TexturedFaceCount);
            stream.WriteByte(model.LegacyFaceMaskFlag);
            stream.WriteByte(model.PriorityFlag);
            stream.WriteByte(model.AlphaFlag);
            stream.WriteByte(model.FaceSkinFlag);
            stream.WriteByte(model.VertexSkinFlag);
            stream.WriteShort(model.VertexXLength);
            stream.WriteShort(model.VertexYLength);
            stream.WriteShort(model.VertexZLength);
            stream.WriteShort(model.FaceIndexLength);
        }

        private static void EncodeNewProtocol(ModelFile model, JagStream stream) {
            Raw(stream, model.Header);
            Raw(stream, model.TextureTypes);
            Raw(stream, model.VertexFlags);
            Raw(stream, model.FaceTypeBytes);
            Raw(stream, model.FaceOpcodes);
            Raw(stream, model.FacePriorities);
            WriteSkinBlock(stream, model.FaceSkins, model.FaceSkinsAreSmart, model.SlackFaceSkin);
            WriteSkinBlock(stream, model.VertexSkins, model.VertexSkinsAreSmart, model.SlackVertexSkin);
            Raw(stream, model.FaceAlphas);
            WriteSmarts(stream, model.FaceIndexDeltas);
            Raw(stream, model.SlackFaceIndex);
            WriteUShorts(stream, model.FaceTextureIds);
            WriteTextureCoords(stream, model, model.FormatType >= 16);
            Raw(stream, model.SlackTextureCoord);
            WriteUShorts(stream, model.FaceColours);
            WriteSmarts(stream, model.VertexDeltasX);
            Raw(stream, model.SlackVertexX);
            WriteSmarts(stream, model.VertexDeltasY);
            Raw(stream, model.SlackVertexY);
            WriteSmarts(stream, model.VertexDeltasZ);
            Raw(stream, model.SlackVertexZ);
            WriteTexturedFaces(stream, model);
            WriteTail(stream, model);
            Raw(stream, model.Gap);

            stream.WriteShort(model.VertexCount);
            stream.WriteShort(model.FaceCount);
            stream.WriteShort(model.TexturedFaceCount);
            stream.WriteByte(model.Flags);
            stream.WriteByte(model.PriorityFlag);
            stream.WriteByte(model.AlphaFlag);
            stream.WriteByte(model.FaceSkinFlag);
            stream.WriteByte(model.FaceTextureFlag);
            stream.WriteByte(model.VertexSkinFlag);
            stream.WriteShort(model.VertexXLength);
            stream.WriteShort(model.VertexYLength);
            stream.WriteShort(model.VertexZLength);
            stream.WriteShort(model.FaceIndexLength);
            stream.WriteShort(model.TextureCoordLength);
            stream.WriteShort(model.StoredVertexSkinLength);
            stream.WriteShort(model.StoredFaceSkinLength);
        }

        private static void WriteTexturedFaces(JagStream stream, ModelFile model) {
            int tfc = model.TexturedFaceCount;

            for (int i = 0; i < tfc; i++) {
                if (model.TextureTypes![i] != 0)
                    continue;
                stream.WriteShort(model.TextureVertexA[i]);
                stream.WriteShort(model.TextureVertexB[i]);
                stream.WriteShort(model.TextureVertexC[i]);
            }

            for (int i = 0; i < tfc; i++) {
                int type = model.TextureTypes![i];
                if (type < 1 || type > 3)
                    continue;
                stream.WriteShort(model.TextureVertexA[i]);
                stream.WriteShort(model.TextureVertexB[i]);
                stream.WriteShort(model.TextureVertexC[i]);
            }

            for (int i = 0; i < tfc; i++) {
                int type = model.TextureTypes![i];
                if (type < 1 || type > 3)
                    continue;
                ModelFile.ScaleWidths(model.FormatType, type, out int wp, out int wq, out int wr);
                WriteWidth(stream, model.TextureScaleP[i], wp);
                WriteWidth(stream, model.TextureScaleQ[i], wq);
                WriteWidth(stream, model.TextureScaleR[i], wr);
            }
            Raw(stream, model.SlackTextureScale);

            WriteTypedBytes(stream, model, model.TextureFieldA);
            WriteTypedBytes(stream, model, model.TextureFieldB);

            for (int i = 0; i < tfc; i++) {
                int type = model.TextureTypes![i];
                if (type < 1 || type > 3)
                    continue;
                stream.WriteByte(model.TextureFieldC[i]);
                if (type == 2) {
                    stream.WriteByte(model.TextureType2FieldA[i]);
                    stream.WriteByte(model.TextureType2FieldB[i]);
                }
            }
        }

        private static void WriteTypedBytes(JagStream stream, ModelFile model, byte[] values) {
            for (int i = 0; i < model.TexturedFaceCount; i++) {
                int type = model.TextureTypes![i];
                if (type >= 1 && type <= 3)
                    stream.WriteByte(values[i]);
            }
        }

        private static void WriteTail(JagStream stream, ModelFile model) {
            if (model.Emitters != null) {
                stream.WriteByte(model.Emitters.Length);
                foreach (ModelParticleEmitter emitter in model.Emitters) {
                    stream.WriteShort(emitter.EmitterId);
                    stream.WriteShort(emitter.FaceIndex);
                }

                stream.WriteByte(model.Effectors!.Length);
                foreach (ModelParticleEffector effector in model.Effectors!) {
                    stream.WriteShort(effector.EffectorId);
                    stream.WriteShort(effector.VertexIndex);
                }
            }

            if (model.Bonds == null)
                return;

            stream.WriteByte(model.Bonds.Length);
            foreach (ModelBond bond in model.Bonds) {
                stream.WriteShort(bond.BillboardId);
                stream.WriteShort(bond.FaceIndex);
                if (model.BondFieldIsSmart)
                    WriteSpecialSmart(stream, bond.Third);
                else
                    stream.WriteByte(bond.Third.Value);
                stream.WriteByte((byte) bond.Fourth);
            }
        }

        private static void WriteTextureCoords(JagStream stream, ModelFile model, bool smart) {
            foreach (StoredSmart coord in model.TextureCoords) {
                if (smart)
                    WriteUnsignedSmart(stream, coord);
                else
                    stream.WriteByte(coord.Value);
            }
        }

        private static void WriteSkinBlock(JagStream stream, StoredSmart[]? skins, bool smart, byte[]? slack) {
            if (skins != null) {
                foreach (StoredSmart skin in skins) {
                    if (smart)
                        WriteSpecialSmart(stream, skin);
                    else
                        stream.WriteByte(skin.Value);
                }
            }
            Raw(stream, slack);
        }

        private static void WriteByteSkins(JagStream stream, StoredSmart[]? skins) {
            if (skins == null)
                return;
            foreach (StoredSmart skin in skins)
                stream.WriteByte(skin.Value);
        }

        private static void WriteSmarts(JagStream stream, StoredSmart[] values) {
            foreach (StoredSmart value in values)
                stream.WriteSmart(value.Value, value.Width);
        }

        private static void WriteUShorts(JagStream stream, ushort[]? values) {
            if (values == null)
                return;
            foreach (ushort value in values)
                stream.WriteShort(value);
        }

        private static void WriteWidth(JagStream stream, int value, int width) {
            if (width == 2)
                stream.WriteShort(value);
            else
                stream.WriteMedium(value);
        }

        /// <summary>
        ///     Writes the unsigned smart the client reads as <c>readSmart(454)</c>: a byte for
        ///     0-127, otherwise a short biased by 32768.
        /// </summary>
        /// <remarks>
        ///     Not the signed smart. The two differ by 64 on the one-byte branch, so reading a
        ///     texture-coordinate index with the wrong one yields a well-formed value 64 too low -
        ///     which is exactly the defect this replaced (Model.java:1115 against
        ///     RSBuffer.java:857).
        /// </remarks>
        private static void WriteUnsignedSmart(JagStream stream, StoredSmart value) {
            if (value.Width == JagStream.SmartWidth.OneByte)
                stream.WriteByte(value.Value);
            else
                stream.WriteShort(value.Value + 32768);
        }

        /// <summary>
        ///     Writes the smart the client reads as <c>readSmart2</c>: a byte biased by -1, or a
        ///     short biased by -32769.
        /// </summary>
        private static void WriteSpecialSmart(JagStream stream, StoredSmart value) {
            if (value.Width == JagStream.SmartWidth.OneByte)
                stream.WriteByte(value.Value + 1);
            else
                stream.WriteShort(value.Value + 32769);
        }

        private static void Raw(JagStream stream, byte[]? data) {
            if (data != null && data.Length > 0)
                stream.Write(data, 0, data.Length);
        }

        // ===================================================================
        //  Bounds
        // ===================================================================

        private static int Advance(ref int at, int size) {
            int start = at;
            at += size;
            return start;
        }

        private static byte[] Slice(byte[] data, int offset, int length, int modelId) {
            Require(length >= 0, modelId,
                "declares blocks that overlap - a length came out negative at offset " + offset);
            Require(offset >= 0 && offset + length <= data.Length, modelId,
                "declares a block at " + offset + " of " + length + " bytes, past its " +
                data.Length + " bytes");

            if (length == 0)
                return Array.Empty<byte>();

            var slice = new byte[length];
            Array.Copy(data, offset, slice, 0, length);
            return slice;
        }

        private static void Require(bool condition, int modelId, string complaint) {
            if (!condition)
                throw new InvalidDataException("Model " + modelId + " " + complaint + ".");
        }

        /// <summary>
        ///     A read head over the model bytes, one per block the client keeps a separate caret for.
        /// </summary>
        private struct Cursor {
            private readonly byte[] _data;
            private readonly int _modelId;

            /// <summary>Where the next read starts.</summary>
            public int Position;

            /// <summary>Opens a read head at an absolute offset.</summary>
            /// <param name="data">The model bytes.</param>
            /// <param name="at">The offset to start at.</param>
            /// <param name="modelId">The model id, for failure messages.</param>
            public Cursor(byte[] data, int at, int modelId) {
                _data = data;
                _modelId = modelId;
                Position = at;
                Require(at >= 0 && at <= data.Length, modelId,
                    "places a block at " + at + ", outside its " + data.Length + " bytes");
            }

            /// <summary>Reads an unsigned byte.</summary>
            /// <returns>0 to 255.</returns>
            public int U8() {
                Check(1);
                return _data[Position++];
            }

            /// <summary>Reads a big-endian unsigned short.</summary>
            /// <returns>0 to 65535.</returns>
            public int U16() {
                Check(2);
                int value = (_data[Position] << 8) | _data[Position + 1];
                Position += 2;
                return value;
            }

            /// <summary>Reads a big-endian three-byte unsigned integer.</summary>
            /// <returns>0 to 16777215.</returns>
            public int Medium() {
                Check(3);
                int value = (_data[Position] << 16) | (_data[Position + 1] << 8) | _data[Position + 2];
                Position += 3;
                return value;
            }

            /// <summary>Reads a plain byte as a one-byte-wide stored value.</summary>
            /// <returns>The byte, tagged with the width it occupied.</returns>
            public StoredSmart Byte() => new StoredSmart(U8(), JagStream.SmartWidth.OneByte);

            /// <summary>Reads the client's signed smart, <c>RSBuffer.method1239</c>.</summary>
            /// <returns>-16384 to 16383, with its width.</returns>
            public StoredSmart Smart() {
                Check(1);
                if (_data[Position] < 128)
                    return new StoredSmart(U8() - 64, JagStream.SmartWidth.OneByte);
                return new StoredSmart(U16() - 0xC000, JagStream.SmartWidth.TwoByte);
            }

            /// <summary>Reads the client's unsigned smart, <c>RSBuffer.readSmart</c>.</summary>
            /// <returns>0 to 32767, with its width.</returns>
            public StoredSmart UnsignedSmart() {
                Check(1);
                if (_data[Position] < 128)
                    return new StoredSmart(U8(), JagStream.SmartWidth.OneByte);
                return new StoredSmart(U16() - 32768, JagStream.SmartWidth.TwoByte);
            }

            /// <summary>Reads the client's biased smart, <c>RSBuffer.readSmart2</c>.</summary>
            /// <returns>-1 to 32766, with its width.</returns>
            public StoredSmart SpecialSmart() {
                Check(1);
                if (_data[Position] < 128)
                    return new StoredSmart(U8() - 1, JagStream.SmartWidth.OneByte);
                return new StoredSmart(U16() - 32769, JagStream.SmartWidth.TwoByte);
            }

            private void Check(int bytes) {
                Require(Position >= 0 && Position + bytes <= _data.Length, _modelId,
                    "runs off the end of its " + _data.Length + " bytes at offset " + Position);
            }
        }
    }
}
