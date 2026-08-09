using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     Which of index 7's three on-disk layouts a model uses.
    /// </summary>
    /// <remarks>
    ///     Selected exactly as <c>Model.&lt;init&gt;</c> selects it (Model.java:84-100): the model id
    ///     first, then the trailing sentinel. There is no fourth case - a <c>FF FD</c> tail is
    ///     legacy to the client, not a format of its own.
    /// </remarks>
    public enum ModelEncoding {
        /// <summary>
        ///     <c>method2587</c> (Model.java:1363): an 18-byte footer, no sentinel, no texture
        ///     types, and the face render type packed into a mask byte.
        /// </summary>
        Legacy,

        /// <summary>
        ///     <c>decoder_newer_format</c> (Model.java:381): a 21-byte footer behind an
        ///     <c>FF FF</c> sentinel. Everything in both shipped caches bar nine models.
        /// </summary>
        Newer,

        /// <summary>
        ///     <c>decoder_newest_format</c> with <c>newProtocol</c> set (Model.java:809), chosen by
        ///     model id 63607-63613 alone: a 3-byte header, a 26-byte footer, a 16-bit textured-face
        ///     count and two extra declared block lengths.
        /// </summary>
        NewProtocol
    }

    /// <summary>
    ///     A smart-encoded value together with the width it was stored in.
    /// </summary>
    /// <remarks>
    ///     Every smart form in this index has two encodings for part of its range, so the decoded
    ///     number alone does not determine the bytes. Neither shipped cache contains a widened
    ///     value - not one two-byte signed smart in index 7 holds something the one-byte form could
    ///     have carried - which is exactly why the width has to be recorded rather than recomputed:
    ///     the input that would tell a shortest-form encoder apart from a faithful one is absent
    ///     from the data, so no sweep over this cache can catch the difference.
    /// </remarks>
    public readonly struct StoredSmart {
        /// <summary>The decoded value.</summary>
        public int Value { get; }

        /// <summary>The width the value occupied on the wire.</summary>
        public JagStream.SmartWidth Width { get; }

        /// <summary>Binds a smart value to the width it was read in.</summary>
        /// <param name="value">The decoded value.</param>
        /// <param name="width">The width actually present on the wire.</param>
        public StoredSmart(int value, JagStream.SmartWidth width) {
            Value = value;
            Width = width;
        }
    }

    /// <summary>
    ///     One particle emitter attached to a model.
    /// </summary>
    /// <remarks>
    ///     Named from what the client does with the pair, not from the decompiled field names.
    ///     <c>Model.java:762-772</c> resolves the first value through <c>ParticleType.list</c> (index
    ///     27) and uses the second to index the three face-vertex arrays, so the emitter rides on a
    ///     face rather than on a vertex.
    /// </remarks>
    public readonly struct ModelParticleEmitter {
        /// <summary>Index-27 emitter configuration id.</summary>
        public int EmitterId { get; }

        /// <summary>Face the emitter is anchored to.</summary>
        public int FaceIndex { get; }

        /// <summary>Binds an emitter to the face it rides on.</summary>
        /// <param name="emitterId">Index-27 emitter configuration id.</param>
        /// <param name="faceIndex">Face the emitter is anchored to.</param>
        public ModelParticleEmitter(int emitterId, int faceIndex) {
            EmitterId = emitterId;
            FaceIndex = faceIndex;
        }
    }

    /// <summary>
    ///     One particle effector attached to a model.
    /// </summary>
    /// <remarks>
    ///     The second list in the same tail block, and anchored differently to an emitter:
    ///     <c>Renderable_Sub1.java:1461-1472</c> indexes the <em>vertex</em> coordinate arrays with
    ///     it, while an emitter indexes the face arrays.
    /// </remarks>
    public readonly struct ModelParticleEffector {
        /// <summary>Effector configuration id.</summary>
        public int EffectorId { get; }

        /// <summary>Vertex the effector is anchored to.</summary>
        public int VertexIndex { get; }

        /// <summary>Binds an effector to the vertex it rides on.</summary>
        /// <param name="effectorId">Effector configuration id.</param>
        /// <param name="vertexIndex">Vertex the effector is anchored to.</param>
        public ModelParticleEffector(int effectorId, int vertexIndex) {
            EffectorId = effectorId;
            VertexIndex = vertexIndex;
        }
    }

    /// <summary>
    ///     One billboard bond attached to a model.
    /// </summary>
    /// <remarks>
    ///     <c>Renderable_Sub1.java:159-166</c> resolves the first value through
    ///     <c>Class177.list</c>, which is the index-29 billboard loader, and uses the second to index
    ///     the face arrays. The remaining two fields are carried through to <c>Class170</c>
    ///     unexamined by anything that would name them, so they keep positional names.
    /// </remarks>
    public readonly struct ModelBond {
        /// <summary>Index-29 billboard configuration id.</summary>
        public int BillboardId { get; }

        /// <summary>Face the billboard is bonded to.</summary>
        public int FaceIndex { get; }

        /// <summary>
        ///     Third field, a plain byte unless the new-protocol flags byte sets bit 6.
        /// </summary>
        /// <remarks>
        ///     255 decodes to -1 in the byte form. No model in either cache sets bit 6, so the
        ///     two-byte form is unreachable here and is kept because the branch exists in the
        ///     client (Model.java:1245-1252).
        /// </remarks>
        public StoredSmart Third { get; }

        /// <summary>Fourth field, a signed byte.</summary>
        public sbyte Fourth { get; }

        /// <summary>Binds a bond record.</summary>
        /// <param name="billboardId">Index-29 billboard configuration id.</param>
        /// <param name="faceIndex">Face the billboard is bonded to.</param>
        /// <param name="third">Third field.</param>
        /// <param name="fourth">Fourth field.</param>
        public ModelBond(int billboardId, int faceIndex, StoredSmart third, sbyte fourth) {
            BillboardId = billboardId;
            FaceIndex = faceIndex;
            Third = third;
            Fourth = fourth;
        }
    }

    /// <summary>
    ///     A model exactly as index 7 stores it: every field the client reads, plus every choice the
    ///     format leaves open, so the bytes can be written back unchanged.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     This is the <b>stored</b> half of the model path. <see cref="ModelDefinition"/> is the
    ///     derived half - normals, UVs, animation tables, the shifted vertex coordinates the viewer
    ///     wants - and it cannot be encoded from, because several of the things it holds are
    ///     computed and several of the things the file holds it never keeps.
    ///     </para>
    ///     <para>
    ///     Four kinds of non-canonical choice are recorded here rather than recomputed, because
    ///     recomputing any of them changes a file nobody edited:
    ///     </para>
    ///     <list type="bullet">
    ///     <item><b>Strip opcodes.</b> Any face can be written as opcode 1 with three fresh deltas
    ///     instead of 2, 3 or 4, so the opcode bytes are kept verbatim and the delta stream is
    ///     replayed against them.</item>
    ///     <item><b>Smart widths.</b> Recorded per value - see <see cref="StoredSmart"/>.</item>
    ///     <item><b>Declared block lengths.</b> The footer states the vertex, face-index and
    ///     texture-coordinate block sizes; they are not derived from the content, and 13,787 models
    ///     in the vanilla capture declare a textured-face block larger than the client consumes.
    ///     The declared values are kept and the unconsumed remainder with them.</item>
    ///     <item><b>The format-type flag bit.</b> Bit 3 of the flags byte says the format type is
    ///     stored; it does not follow from the value. One model in the repack sets the bit and
    ///     stores 12, which is what bit 3 clear already means - and no model in the vanilla capture
    ///     does, so on the default cache nothing would catch an encoder that recomputed it.</item>
    ///     </list>
    /// </remarks>
    public sealed class ModelFile {
        /// <summary>Which on-disk layout this model uses.</summary>
        public ModelEncoding Encoding { get; internal set; }

        /// <summary>The group id the model was read from, which is also its model id.</summary>
        public int ModelId { get; internal set; }

        /// <summary>Vertex count declared by the footer.</summary>
        public int VertexCount { get; internal set; }

        /// <summary>Face count declared by the footer.</summary>
        public int FaceCount { get; internal set; }

        /// <summary>Textured-face count declared by the footer.</summary>
        public int TexturedFaceCount { get; internal set; }

        /// <summary>
        ///     The effective format type, which decides several field widths further down the file.
        /// </summary>
        /// <remarks>
        ///     12 unless the flags byte sets bit 3, in which case it is the byte immediately before
        ///     the footer, or unless this is a new-protocol model, whose header carries it.
        /// </remarks>
        public int FormatType { get; internal set; }

        /// <summary>
        ///     The three header bytes a new-protocol model opens with: version, an unused byte, and
        ///     the format type.
        /// </summary>
        /// <remarks>Null for the other two encodings, which have no header at all.</remarks>
        public byte[]? Header { get; internal set; }

        /// <summary>
        ///     The flags byte, verbatim.
        /// </summary>
        /// <remarks>
        ///     Bit 0 per-face render types, bit 1 a particle tail, bit 2 a bond tail, bit 3 an
        ///     embedded format type. Bits 4-7 are read only by the new-protocol decoder, and only
        ///     bit 4 occurs in either cache. Written back whole rather than rebuilt from the flags
        ///     that were understood.
        /// </remarks>
        public byte Flags { get; internal set; }

        /// <summary>
        ///     Legacy's equivalent of flags bit 0: 1 when each face carries a packed
        ///     render-type/texture mask byte.
        /// </summary>
        public byte LegacyFaceMaskFlag { get; internal set; }

        /// <summary>255 when each face carries its own render priority, otherwise the global one.</summary>
        public byte PriorityFlag { get; internal set; }

        /// <summary>1 when each face carries an alpha byte.</summary>
        public byte AlphaFlag { get; internal set; }

        /// <summary>1 when each face carries a skin group.</summary>
        public byte FaceSkinFlag { get; internal set; }

        /// <summary>1 when each face carries a texture id.</summary>
        public byte FaceTextureFlag { get; internal set; }

        /// <summary>1 when each vertex carries a skin group.</summary>
        public byte VertexSkinFlag { get; internal set; }

        /// <summary>Declared byte length of the X delta block.</summary>
        public int VertexXLength { get; internal set; }

        /// <summary>Declared byte length of the Y delta block.</summary>
        public int VertexYLength { get; internal set; }

        /// <summary>Declared byte length of the Z delta block.</summary>
        public int VertexZLength { get; internal set; }

        /// <summary>Declared byte length of the face-index delta block.</summary>
        public int FaceIndexLength { get; internal set; }

        /// <summary>
        ///     Declared byte length of the texture-coordinate block.
        /// </summary>
        /// <remarks>Not present in the legacy footer, where it is always zero.</remarks>
        public int TextureCoordLength { get; internal set; }

        /// <summary>
        ///     The vertex-skin block length as the new-protocol footer stores it.
        /// </summary>
        /// <remarks>
        ///     Kept separately from the length actually used, because when flags bit 4 is clear the
        ///     client ignores the stored value and derives the length from the vertex count instead
        ///     (Model.java:860-869). One new-protocol model stores 0 there and six store a real
        ///     figure, so the stored bytes cannot be reproduced from the derived length.
        /// </remarks>
        public int StoredVertexSkinLength { get; internal set; }

        /// <summary>
        ///     The face-skin block length as the new-protocol footer stores it, kept for the same
        ///     reason as <see cref="StoredVertexSkinLength"/>.
        /// </summary>
        public int StoredFaceSkinLength { get; internal set; }

        /// <summary>
        ///     The two trailing sentinel bytes of a newer-format model.
        /// </summary>
        /// <remarks>
        ///     Always <c>FF FF</c> in both caches - that is what selects the format - but stored
        ///     rather than emitted as a constant so the encoder writes back what it read.
        /// </remarks>
        public byte[]? Sentinel { get; internal set; }

        /// <summary>
        ///     One byte per textured face giving its type, 0 to 3.
        /// </summary>
        /// <remarks>
        ///     Null for the legacy encoding, which has no such block and whose textured faces are
        ///     all type 0.
        /// </remarks>
        public byte[]? TextureTypes { get; internal set; }

        /// <summary>One mask byte per vertex saying which of the three axes carries a delta.</summary>
        public byte[] VertexFlags { get; internal set; }

        /// <summary>
        ///     One byte per face: the render type on the newer encodings, the packed
        ///     render-type/texture mask on the legacy one. Null when the flag is clear.
        /// </summary>
        public byte[]? FaceTypeBytes { get; internal set; }

        /// <summary>One strip opcode per face, verbatim.</summary>
        public byte[] FaceOpcodes { get; internal set; }

        /// <summary>One priority byte per face, or null when a global priority is used.</summary>
        public byte[]? FacePriorities { get; internal set; }

        /// <summary>One alpha byte per face, or null.</summary>
        public byte[]? FaceAlphas { get; internal set; }

        /// <summary>
        ///     Face skin groups, one per face, or null.
        /// </summary>
        /// <remarks>
        ///     A plain unsigned byte on the legacy and newer encodings, so the range is 0-255 and
        ///     8,639 models in the repack carry a value above 127 - which is why this is not a
        ///     signed byte array. New-protocol models with flags bit 5 set store a smart instead,
        ///     which is why the width travels with the value.
        /// </remarks>
        public StoredSmart[]? FaceSkins { get; internal set; }

        /// <summary>Vertex skin groups, one per vertex, or null. Same widths as <see cref="FaceSkins"/>.</summary>
        public StoredSmart[]? VertexSkins { get; internal set; }

        /// <summary>
        ///     The raw per-face texture id, before the client's subtraction of one. Null when the
        ///     flag is clear.
        /// </summary>
        public ushort[]? FaceTextureIds { get; internal set; }

        /// <summary>The raw per-face HSL-565 colour word.</summary>
        public ushort[] FaceColours { get; internal set; }

        /// <summary>X deltas in stream order, one per vertex whose mask sets bit 0.</summary>
        public StoredSmart[] VertexDeltasX { get; internal set; }

        /// <summary>Y deltas in stream order, one per vertex whose mask sets bit 1.</summary>
        public StoredSmart[] VertexDeltasY { get; internal set; }

        /// <summary>Z deltas in stream order, one per vertex whose mask sets bit 2.</summary>
        public StoredSmart[] VertexDeltasZ { get; internal set; }

        /// <summary>
        ///     Face-index deltas in stream order: three for an opcode 1 face, one for any other.
        /// </summary>
        public StoredSmart[] FaceIndexDeltas { get; internal set; }

        /// <summary>
        ///     Texture-coordinate indices in stream order, one per face that carries a texture.
        /// </summary>
        public StoredSmart[] TextureCoords { get; internal set; }

        /// <summary>Bytes of the X delta block the client never reads.</summary>
        public byte[] SlackVertexX { get; internal set; }

        /// <summary>Bytes of the Y delta block the client never reads.</summary>
        public byte[] SlackVertexY { get; internal set; }

        /// <summary>Bytes of the Z delta block the client never reads.</summary>
        public byte[] SlackVertexZ { get; internal set; }

        /// <summary>Bytes of the face-index block the client never reads.</summary>
        public byte[] SlackFaceIndex { get; internal set; }

        /// <summary>Bytes of the texture-coordinate block the client never reads.</summary>
        public byte[] SlackTextureCoord { get; internal set; }

        /// <summary>
        ///     Bytes of the textured-face scale block the client never reads.
        /// </summary>
        /// <remarks>
        ///     The one slack region that is routinely non-empty. The block is sized
        ///     <c>types1to3 * width(formatType)</c>, but a type-2 face at format type 15 consumes 7
        ///     of those 9 bytes, so any model mixing type 2 with format 15 leaves a remainder.
        /// </remarks>
        public byte[] SlackTextureScale { get; internal set; }

        /// <summary>Bytes of the new-protocol vertex-skin block the client never reads.</summary>
        public byte[]? SlackVertexSkin { get; internal set; }

        /// <summary>Bytes of the new-protocol face-skin block the client never reads.</summary>
        public byte[]? SlackFaceSkin { get; internal set; }

        /// <summary>
        ///     Bytes between the end of the particle and bond tail and the start of the footer.
        /// </summary>
        /// <remarks>
        ///     Empty for every model in both caches. Captured anyway, because a decoder that
        ///     assumed it away would produce a shorter file for the first model that has one.
        /// </remarks>
        public byte[] Gap { get; internal set; }

        /// <summary>First reference vertex of each textured face.</summary>
        public ushort[] TextureVertexA { get; internal set; }

        /// <summary>Second reference vertex of each textured face.</summary>
        public ushort[] TextureVertexB { get; internal set; }

        /// <summary>Third reference vertex of each textured face.</summary>
        public ushort[] TextureVertexC { get; internal set; }

        /// <summary>
        ///     First of the three projection scalars a type 1-3 textured face carries.
        /// </summary>
        /// <remarks>
        ///     Two or three bytes wide depending on the format type and the face type; the widths
        ///     follow from those two and are not stored. Entries for type-0 faces are unused.
        /// </remarks>
        public int[] TextureScaleP { get; internal set; }

        /// <summary>Second projection scalar of a type 1-3 textured face.</summary>
        public int[] TextureScaleQ { get; internal set; }

        /// <summary>Third projection scalar of a type 1-3 textured face.</summary>
        public int[] TextureScaleR { get; internal set; }

        /// <summary>One byte per type 1-3 textured face, from the block at <c>i_96_</c>.</summary>
        public byte[] TextureFieldA { get; internal set; }

        /// <summary>One byte per type 1-3 textured face, from the block at <c>i_97_</c>.</summary>
        public byte[] TextureFieldB { get; internal set; }

        /// <summary>One byte per type 1-3 textured face, from the block at <c>i_98_</c>.</summary>
        public byte[] TextureFieldC { get; internal set; }

        /// <summary>First of the two extra bytes a type-2 textured face carries in that same block.</summary>
        public byte[] TextureType2FieldA { get; internal set; }

        /// <summary>Second of the two extra bytes a type-2 textured face carries.</summary>
        public byte[] TextureType2FieldB { get; internal set; }

        /// <summary>Particle emitters, or null when flags bit 1 is clear.</summary>
        public ModelParticleEmitter[]? Emitters { get; internal set; }

        /// <summary>Particle effectors, or null when flags bit 1 is clear.</summary>
        public ModelParticleEffector[]? Effectors { get; internal set; }

        /// <summary>Billboard bonds, or null when flags bit 2 is clear.</summary>
        public ModelBond[]? Bonds { get; internal set; }

        /// <summary>Whether each face carries its own render type (or, on legacy, a mask byte).</summary>
        public bool HasFaceTypes =>
            Encoding == ModelEncoding.Legacy ? LegacyFaceMaskFlag == 1 : (Flags & 0x1) == 1;

        /// <summary>Whether the format type is stored in the byte before the footer.</summary>
        public bool HasEmbeddedFormatType => Encoding != ModelEncoding.Legacy && (Flags & 0x8) == 8;

        /// <summary>Whether the new-protocol vertex-skin block holds smarts rather than bytes.</summary>
        public bool VertexSkinsAreSmart => Encoding == ModelEncoding.NewProtocol && (Flags & 0x10) != 0;

        /// <summary>Whether the new-protocol face-skin block holds smarts rather than bytes.</summary>
        public bool FaceSkinsAreSmart => Encoding == ModelEncoding.NewProtocol && (Flags & 0x20) != 0;

        /// <summary>Whether a new-protocol bond's third field is a smart rather than a byte.</summary>
        public bool BondFieldIsSmart => Encoding == ModelEncoding.NewProtocol && (Flags & 0x40) != 0;

        /// <summary>
        ///     Whether the new-protocol footer is followed by the extra block flags bit 7 declares.
        /// </summary>
        /// <remarks>
        ///     No model in either cache sets it. <see cref="ModelCodec"/> refuses such a model
        ///     rather than guessing, because the block's own length is read from a byte the client
        ///     locates by a backwards seek nothing here can check.
        /// </remarks>
        public bool HasTrailingBlock => Encoding == ModelEncoding.NewProtocol && (Flags & 0x80) != 0;

        /// <summary>
        ///     How many bits the client's callers shift this model's vertices left by after loading.
        /// </summary>
        /// <remarks>
        ///     Two when the format type is below 13, zero otherwise. The shift is not part of the
        ///     decode: <c>decoder_newer_format</c> never touches the coordinates, and the callers do
        ///     it afterwards through <c>method2592</c> (Model.java:1682-1700) - Class107.java:175,
        ///     Class152.java:114, Node_Sub10_Sub16.java:33, ItemDefinition.java:155. Recording it
        ///     here is what lets the viewer apply it while the stored deltas stay stored.
        /// </remarks>
        public int VertexShift => FormatType < 13 ? 2 : 0;

        /// <summary>Textured faces of type 0, which carry three reference vertices and nothing else.</summary>
        public int Type0FaceCount => CountTypes(0, 0);

        /// <summary>Textured faces of type 1 to 3, which carry the projection and layer blocks.</summary>
        public int Type1To3FaceCount => CountTypes(1, 3);

        /// <summary>Textured faces of type 2, the only ones carrying two extra bytes.</summary>
        public int Type2FaceCount => CountTypes(2, 2);

        /// <summary>
        ///     Byte widths of the three projection scalars a textured face of the given type carries.
        /// </summary>
        /// <remarks>
        ///     Type 2 disagrees with types 1 and 3 above format 14, which is what makes the block's
        ///     declared size an overestimate rather than a total (Model.java:695-745).
        /// </remarks>
        /// <param name="formatType">The model's format type.</param>
        /// <param name="textureType">The textured face's type, 1 to 3.</param>
        /// <param name="first">Width of the first scalar.</param>
        /// <param name="second">Width of the second scalar.</param>
        /// <param name="third">Width of the third scalar.</param>
        public static void ScaleWidths(int formatType, int textureType,
            out int first, out int second, out int third) {
            if (textureType == 2) {
                if (formatType >= 16) {
                    first = second = third = 3;
                    return;
                }
                first = 2;
                second = formatType < 14 ? 2 : 3;
                third = 2;
                return;
            }

            if (formatType < 15) {
                first = 2;
                second = formatType < 14 ? 2 : 3;
                third = 2;
                return;
            }

            first = second = third = 3;
        }

        /// <summary>
        ///     The declared stride of the projection block, which is a per-entry maximum rather than
        ///     the exact size of every entry.
        /// </summary>
        /// <remarks>
        ///     <c>i_94_</c> at Model.java:475-482: 7 at format type 14, 9 at 15 and above, 6 below.
        /// </remarks>
        /// <param name="formatType">The model's format type.</param>
        /// <returns>The stride in bytes.</returns>
        public static int ScaleStride(int formatType) {
            if (formatType == 14)
                return 7;
            return formatType >= 15 ? 9 : 6;
        }

        private int CountTypes(int low, int high) {
            if (TextureTypes == null)
                return Encoding == ModelEncoding.Legacy && low == 0 ? TexturedFaceCount : 0;

            int count = 0;
            foreach (byte type in TextureTypes) {
                if (type >= low && type <= high)
                    count++;
            }
            return count;
        }

        /// <summary>Re-encodes this model to the bytes index 7 would store for it.</summary>
        /// <returns>The stored form.</returns>
        public JagStream Encode() => ModelCodec.Encode(this);
    }
}
