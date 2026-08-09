using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A floor underlay: the base ground colour of a tile.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group 1. 159 definitions in the shipped 639 cache, ids 0..158. The definition
    ///     id is the file id within the group.
    ///
    ///     The client decomposes the colour into four blend accumulators at decode time
    ///     (FloorUnderlay.method718) rather than keeping a packed HSL, because the terrain blender
    ///     has to area-average it. This type keeps the raw 24-bit RGB instead, which is what an
    ///     editor edits and what has to be written back; the accumulator derivation belongs with the
    ///     blender.
    ///
    ///     Opcode table verified against FloorUnderlay.java:21-48.
    ///     See <c>reference/hydra-637-maps/04-floor-definitions.md</c>.
    /// </remarks>
    public class FloorUnderlayDefinition {
        /// <summary>Definition id, which is also its file id within group 1.</summary>
        public int Id { get; set; } = -1;

        /// <summary>Opcode 1. The base colour as 24-bit RGB. Defaults to black.</summary>
        public int Rgb { get; set; }

        /// <summary>
        ///     Opcode 2. Texture id, or -1 for none.
        /// </summary>
        /// <remarks>
        ///     An unsigned <em>short</em> with 0xFFFF meaning -1 (FloorUnderlay.java:25-28), not the
        ///     unsigned byte the overlay uses for its opcode 2.
        /// </remarks>
        public int TextureId { get; set; } = -1;

        /// <summary>Opcode 3. Texture scale, read as an unsigned short shifted left 2.</summary>
        public int TextureScale { get; set; } = 512;

        /// <summary>Opcode 4. Whether this floor casts a shadow.</summary>
        public bool CastsShadow { get; set; } = true;

        /// <summary>Opcode 5. Whether this floor participates in occlusion.</summary>
        public bool Occludes { get; set; } = true;

        /// <summary>
        ///     The opcodes seen at decode time, in order.
        /// </summary>
        /// <remarks>
        ///     Replayed by <see cref="Encode"/> so an unedited definition re-encodes to the exact
        ///     bytes it was decoded from. The client tolerates any order, but a cache editor cannot
        ///     afford to rewrite bytes it did not mean to change: the archive CRC covers them.
        /// </remarks>
        public List<DecodedOpcode> DecodedOpcodes { get; } = new List<DecodedOpcode>();

        /// <summary>Decodes one underlay definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public FloorUnderlayDefinition Decode(JagStream stream) {
            DecodedOpcodes.Clear();

            while (true) {
                int opcode = stream.ReadUnsignedByte();
                if (opcode == 0)
                    break;

                switch (opcode) {
                    case 1:
                        Rgb = stream.ReadMedium();
                        break;
                    case 2:
                        TextureId = stream.ReadUnsignedShort();
                        if (TextureId == 0xFFFF)
                            TextureId = -1;
                        break;
                    case 3:
                        TextureScale = stream.ReadUnsignedShort() << 2;
                        break;
                    case 4:
                        CastsShadow = false;
                        break;
                    case 5:
                        Occludes = false;
                        break;
                    default:
                        //Every opcode the client handles is listed above, and all 159 shipped
                        //definitions decode with exactly these. An unknown opcode means the stream
                        //desynchronised, and continuing would read the rest as garbage.
                        throw new System.IO.InvalidDataException(
                            "Unknown floor underlay opcode " + opcode + " in definition " + Id);
                }

                DecodedOpcodes.Add(new DecodedOpcode(opcode, CurrentValue(opcode)));
            }

            return this;
        }

        /// <summary>The value an opcode would carry if written right now.</summary>
        private int CurrentValue(int opcode) {
            switch (opcode) {
                case 1: return Rgb;
                case 2: return TextureId;
                case 3: return TextureScale;
                default: return 0;
            }
        }

        /// <summary>Encodes this definition back to its file representation.</summary>
        /// <returns>The encoded bytes, positioned at 0.</returns>
        public JagStream Encode() {
            JagStream stream = new JagStream();

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
                case 2: stream.WriteShort(value == -1 ? 0xFFFF : value); break;
                case 3: stream.WriteShort(value >> 2); break;
                //4 and 5 carry no payload.
            }
        }

        /// <summary>Opcodes an edit has made necessary that the decoded file did not carry.</summary>
        private IEnumerable<int> AddedOpcodes() {
            if (!DecodedOpcodes.Has(1) && Rgb != 0) yield return 1;
            if (!DecodedOpcodes.Has(2) && TextureId != -1) yield return 2;
            if (!DecodedOpcodes.Has(3) && TextureScale != 512) yield return 3;
            if (!DecodedOpcodes.Has(4) && !CastsShadow) yield return 4;
            if (!DecodedOpcodes.Has(5) && !Occludes) yield return 5;
        }
    }
}
