using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A map scene icon: the small picture drawn over a bank, altar, staircase or furnace on the
    ///     minimap and the world map.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="cache.RSConstants.MAP_SCENE_GROUP"/>. The definition id is
    ///     the file id. Decoded by <c>Class9.method193</c> (Class9.java:233-258).
    ///
    ///     An object definition points at one of these through its <c>mapSceneId</c>, which is
    ///     opcode <b>102</b> and not opcode 68 - see <see cref="ObjectDefinition"/> for why the two
    ///     are easy to confuse.
    ///
    ///     Not to be confused with <c>FlashEditor.Map.MapScene</c>, which is a block of map squares.
    /// </remarks>
    public class MapSceneIconDefinition {
        /// <summary>Definition id, which is also its file id within the group.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     Opcode 1. The sprite group in JS5 index 8 holding this icon, or -1 for none.
        /// </summary>
        /// <remarks>
        ///     The icon is file 0 of that group (Class324.java:34-36). Opcode 4 sets this to -1
        ///     explicitly, and the client draws nothing at all when it is -1 (Class122.java:93).
        /// </remarks>
        public int SpriteGroupId { get; set; }

        /// <summary>Opcode 2. A flat tint applied over the sprite, or 0 to draw it untinted.</summary>
        public int TintRgb { get; set; }

        /// <summary>
        ///     Opcode 3. Whether to stretch the icon to the object's tile footprint.
        /// </summary>
        /// <remarks>
        ///     When false the client draws the sprite at its native pixel size, which is authored
        ///     for the minimap's 4 pixels per tile. When true it scales to
        ///     <c>4 * footprint</c> instead (Class122.java:114-117).
        /// </remarks>
        public bool StretchToFootprint { get; set; }

        /// <summary>The opcodes seen at decode time, in order, so a round trip is byte-exact.</summary>
        public List<DecodedOpcode> DecodedOpcodes { get; } = new List<DecodedOpcode>();

        /// <summary>Decodes one map scene icon definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public MapSceneIconDefinition Decode(JagStream stream) {
            DecodedOpcodes.Clear();

            while (true) {
                int opcode = stream.ReadUnsignedByte();
                if (opcode == 0)
                    break;

                switch (opcode) {
                    case 1:
                        SpriteGroupId = stream.ReadUnsignedShort();
                        break;
                    case 2:
                        TintRgb = stream.ReadMedium();
                        break;
                    case 3:
                        StretchToFootprint = true;
                        break;
                    case 4:
                        SpriteGroupId = -1;
                        break;
                    default:
                        //The client silently ignores an unknown opcode without skipping a payload,
                        //which desynchronises everything after it. Refusing is strictly better.
                        throw new System.IO.InvalidDataException(
                            "Unknown map scene opcode " + opcode + " in definition " + Id);
                }

                DecodedOpcodes.Add(new DecodedOpcode(opcode, CurrentValue(opcode)));
            }

            return this;
        }

        private int CurrentValue(int opcode) {
            switch (opcode) {
                case 1: return SpriteGroupId;
                case 2: return TintRgb;
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
                case 1: stream.WriteShort(value); break;
                case 2: stream.WriteMedium(value); break;
                //3 and 4 carry no payload.
            }
        }

        private IEnumerable<int> AddedOpcodes() {
            //Opcode 4 is the explicit "no sprite" form, so a -1 added by an edit uses it rather
            //than writing -1 through opcode 1's unsigned short.
            if (!DecodedOpcodes.Has(1) && !DecodedOpcodes.Has(4) && SpriteGroupId > 0) yield return 1;
            if (!DecodedOpcodes.Has(4) && !DecodedOpcodes.Has(1) && SpriteGroupId == -1) yield return 4;
            if (!DecodedOpcodes.Has(2) && TintRgb != 0) yield return 2;
            if (!DecodedOpcodes.Has(3) && StretchToFootprint) yield return 3;
        }
    }
}
