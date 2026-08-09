using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     A floor overlay: the shaped patch drawn over a tile's underlay, such as a path, a rug or
    ///     water.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group 4. 235 definitions in the shipped 639 cache, ids 0..234. The definition
    ///     id is the file id within the group.
    ///
    ///     Opcode table verified against FloorOverlayConfig.java:122-168. Opcodes 4, 6 and 15 do not
    ///     exist and would desynchronise the stream.
    ///
    ///     Colours are kept as raw 24-bit RGB rather than the packed HSL the client converts them to
    ///     at decode time, so they survive a round trip. The conversion belongs with the renderer.
    ///     See <c>reference/hydra-637-maps/04-floor-definitions.md</c>.
    /// </remarks>
    public class FloorOverlayDefinition {
        /// <summary>
        ///     The RGB value that means "no colour, let the underlay show through".
        /// </summary>
        /// <remarks>Mapped to a packed HSL of -1 by the client's conversion.</remarks>
        public const int TransparentRgb = 0xFF00FF;

        /// <summary>Definition id, which is also its file id within group 4.</summary>
        public int Id { get; set; } = -1;

        /// <summary>Opcode 1. Primary colour as 24-bit RGB. Defaults to black, not transparent.</summary>
        public int PrimaryRgb { get; set; }

        /// <summary>Whether opcode 1 was present. Absent is not the same as an explicit black.</summary>
        public bool HasPrimaryRgb { get; set; }

        /// <summary>Opcodes 2 and 3. Texture id, or -1 for none.</summary>
        public int TextureId { get; set; } = -1;

        /// <summary>
        ///     Whether the texture id was read in the opcode-3 short form rather than the opcode-2
        ///     byte form.
        /// </summary>
        /// <remarks>
        ///     Both write the same field, so the form has to be remembered separately to re-encode
        ///     the same bytes. Opcode 2 has zero occurrences in the shipped cache.
        /// </remarks>
        public bool TextureIdIsShortForm { get; set; }

        /// <summary>Opcode 5. Whether this overlay participates in flat-ground occluders.</summary>
        public bool FlatGroundOccluder { get; set; } = true;

        /// <summary>Opcode 7. Secondary colour as 24-bit RGB, or -1 when absent.</summary>
        /// <remarks>
        ///     Takes precedence over <see cref="PrimaryRgb"/> when resolving a tile's colour, and is
        ///     the flat-colour override used by the low-detail renderer and the world map.
        /// </remarks>
        public int SecondaryRgb { get; set; } = -1;

        /// <summary>Opcode 8. Marks this definition as the world map's background overlay.</summary>
        /// <remarks>Carries no payload. Exactly one definition, id 5, uses it in the shipped cache.</remarks>
        public bool IsWorldMapBackground { get; set; }

        /// <summary>Opcode 9. Texture scale, read as an unsigned short shifted left 2.</summary>
        public int TextureScale { get; set; } = 512;

        /// <summary>Opcode 10. Whether this overlay casts a shadow.</summary>
        public bool CastsShadow { get; set; } = true;

        /// <summary>
        ///     Opcode 11. Blend priority. Note this is rewritten after decode.
        /// </summary>
        /// <seealso cref="ApplyPriorityComposite"/>
        public int Priority { get; set; } = 8;

        /// <summary>Opcode 12. Whether this overlay blends across tile edges with its neighbours.</summary>
        public bool BlendWithNeighbours { get; set; }

        /// <summary>Opcode 13. Water submersion tint as raw 24-bit RGB, not HSL.</summary>
        public int WaterTintRgb { get; set; } = 0x122F3D;

        /// <summary>Opcode 14. Depth over which the water tint saturates, read as a byte shifted left 2.</summary>
        public int WaterDepth { get; set; } = 64;

        /// <summary>Opcode 16. Opacity of the water tint at the surface.</summary>
        public int WaterAlpha { get; set; } = 127;

        /// <summary>The opcodes seen at decode time, in order, so a round trip is byte-exact.</summary>
        public List<DecodedOpcode> DecodedOpcodes { get; } = new List<DecodedOpcode>();

        /// <summary>Decodes one overlay definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public FloorOverlayDefinition Decode(JagStream stream) {
            DecodedOpcodes.Clear();

            while (true) {
                int opcode = stream.ReadUnsignedByte();
                if (opcode == 0)
                    break;

                switch (opcode) {
                    case 1:
                        PrimaryRgb = stream.ReadMedium();
                        HasPrimaryRgb = true;
                        break;
                    case 2:
                        TextureId = stream.ReadUnsignedByte();
                        TextureIdIsShortForm = false;
                        break;
                    case 3:
                        TextureId = stream.ReadUnsignedShort();
                        if (TextureId == 0xFFFF)
                            TextureId = -1;
                        TextureIdIsShortForm = true;
                        break;
                    case 5:
                        FlatGroundOccluder = false;
                        break;
                    case 7:
                        SecondaryRgb = stream.ReadMedium();
                        break;
                    case 8:
                        IsWorldMapBackground = true;
                        break;
                    case 9:
                        TextureScale = stream.ReadUnsignedShort() << 2;
                        break;
                    case 10:
                        CastsShadow = false;
                        break;
                    case 11:
                        //Overlay 94 genuinely emits this twice, 255 then 127, so the loop must be
                        //last-write-wins and each occurrence has to remember its own value.
                        Priority = stream.ReadUnsignedByte();
                        break;
                    case 12:
                        BlendWithNeighbours = true;
                        break;
                    case 13:
                        WaterTintRgb = stream.ReadMedium();
                        break;
                    case 14:
                        WaterDepth = stream.ReadUnsignedByte() << 2;
                        break;
                    case 16:
                        WaterAlpha = stream.ReadUnsignedByte();
                        break;
                    default:
                        //Opcodes 4, 6 and 15 fall through in the client, consuming nothing, which
                        //silently desynchronises everything after them. Refusing is strictly better.
                        throw new System.IO.InvalidDataException(
                            "Unknown floor overlay opcode " + opcode + " in definition " + Id);
                }

                DecodedOpcodes.Add(new DecodedOpcode(opcode, CurrentValue(opcode)));
            }

            return this;
        }

        /// <summary>The value an opcode would carry if written right now.</summary>
        private int CurrentValue(int opcode) {
            switch (opcode) {
                case 1: return PrimaryRgb;
                case 2:
                case 3: return TextureId;
                case 7: return SecondaryRgb;
                case 9: return TextureScale;
                case 11: return Priority;
                case 13: return WaterTintRgb;
                case 14: return WaterDepth;
                case 16: return WaterAlpha;
                default: return 0;
            }
        }

        /// <summary>
        ///     Folds the definition id into the priority, as the loader does unconditionally after
        ///     every decode.
        /// </summary>
        /// <remarks>
        ///     FloorOverlayConfig.method2691 (FloorOverlayConfig.java:169-179), reached from
        ///     Class32.java:137 for every definition including absent ones. Anything comparing
        ///     overlay priorities must compare this composite, not the raw opcode-11 byte, or the
        ///     neighbour-blend tie-breaking is wrong.
        ///
        ///     Deliberately not called from <see cref="Decode"/>: it is not part of the file format,
        ///     and applying it there would make <see cref="Encode"/> write the composite back.
        /// </remarks>
        /// <returns>The composite priority.</returns>
        public int ApplyPriorityComposite() => (Priority << 8) | (Id & 0xFF);

        /// <summary>Encodes this definition back to its file representation.</summary>
        /// <returns>The encoded bytes, positioned at 0.</returns>
        public JagStream Encode() {
            JagStream stream = new JagStream();

            //Replay what was decoded, in order. Only the last occurrence of an opcode picks up an
            //edit; earlier ones keep the value they carried, which is what makes a definition that
            //sets the same opcode twice come back byte-identical.
            for (int i = 0; i < DecodedOpcodes.Count; i++) {
                int opcode = DecodedOpcodes[i].Opcode;
                int value = DecodedOpcodes.IsLastOccurrence(i) ? CurrentValue(opcode) : DecodedOpcodes[i].Value;
                Emit(stream, opcode, value);
            }

            foreach (int opcode in AddedOpcodes())
                Emit(stream, opcode, CurrentValue(opcode));

            stream.WriteByte(0);
            return stream.Flip();
        }

        private static void Emit(JagStream stream, int opcode, int value) {
            stream.WriteByte((byte) opcode);
            switch (opcode) {
                case 1: stream.WriteMedium(value); break;
                case 2: stream.WriteByte(value); break;
                case 3: stream.WriteShort(value == -1 ? 0xFFFF : value); break;
                case 7: stream.WriteMedium(value); break;
                case 9: stream.WriteShort(value >> 2); break;
                case 11: stream.WriteByte(value); break;
                case 13: stream.WriteMedium(value); break;
                case 14: stream.WriteByte(value >> 2); break;
                case 16: stream.WriteByte(value); break;
                //5, 8, 10 and 12 carry no payload.
            }
        }

        /// <summary>Opcodes an edit has made necessary that the decoded file did not carry.</summary>
        private IEnumerable<int> AddedOpcodes() {
            if (!DecodedOpcodes.Has(1) && HasPrimaryRgb) yield return 1;
            if (!DecodedOpcodes.Has(2) && !DecodedOpcodes.Has(3) && TextureId != -1)
                yield return TextureIdIsShortForm ? 3 : 2;
            if (!DecodedOpcodes.Has(5) && !FlatGroundOccluder) yield return 5;
            if (!DecodedOpcodes.Has(7) && SecondaryRgb != -1) yield return 7;
            if (!DecodedOpcodes.Has(8) && IsWorldMapBackground) yield return 8;
            if (!DecodedOpcodes.Has(9) && TextureScale != 512) yield return 9;
            if (!DecodedOpcodes.Has(10) && !CastsShadow) yield return 10;
            if (!DecodedOpcodes.Has(11) && Priority != 8) yield return 11;
            if (!DecodedOpcodes.Has(12) && BlendWithNeighbours) yield return 12;
            if (!DecodedOpcodes.Has(13) && WaterTintRgb != 0x122F3D) yield return 13;
            if (!DecodedOpcodes.Has(14) && WaterDepth != 64) yield return 14;
            if (!DecodedOpcodes.Has(16) && WaterAlpha != 127) yield return 16;
        }
    }
}
