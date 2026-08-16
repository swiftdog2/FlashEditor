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
        public bool StretchToFootprint {
            get => _stretchToFootprint;

            /* The setter has to move the opcode as well as the field. Opcode 3 carries no
               payload, so its whole meaning is whether it is in the stream - assigning the
               field alone leaves the record re-encoding to the bytes it already held, which
               is an edit that vanishes with no error anywhere. */
            set {
                _stretchToFootprint = value;
                SetBareOpcode(3, value);
            }
        }

        private bool _stretchToFootprint;

        /// <summary>The opcodes seen at decode time, in order, so a round trip is byte-exact.</summary>
        public List<DecodedOpcode> DecodedOpcodes { get; } = new List<DecodedOpcode>();

        /* Payload-free opcodes an edit has turned off. Suppressed rather than removed from the
           stream above, so turning one back on puts it where the file had it - see
           SuppressedOpcodes for the defect that rule exists for. */
        private readonly SuppressedOpcodes suppressed = new SuppressedOpcodes();

        /// <summary>Turns a payload-free opcode on or off, keeping the position the file stored it at.</summary>
        /// <param name="opcode">The payload-free opcode.</param>
        /// <param name="present">Whether the record should emit it.</param>
        private void SetBareOpcode(int opcode, bool present) {
            suppressed.Set(DecodedOpcodes, opcode, present);
        }

        /// <summary>Whether the record will emit an opcode: stored, and not turned off by an edit.</summary>
        /// <param name="opcode">The opcode.</param>
        /// <returns>Whether it is emitted.</returns>
        private bool Emits(int opcode) => suppressed.Emits(DecodedOpcodes, opcode);


        /// <summary>Decodes one map scene icon definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public MapSceneIconDefinition Decode(JagStream stream) {
            DecodedOpcodes.Clear();
            suppressed.Clear();

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

                //Skipped where it stood rather than deleted from the list, so restoring it
                //puts it back in the same place.
                if (suppressed.Contains(opcode))
                    continue;

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

        /// <summary>
        ///     Sets the icon sprite, keeping whichever of the two "no icon" encodings the record was
        ///     stored in.
        /// </summary>
        /// <remarks>
        ///     <b>"No icon" has two encodings and they are not interchangeable on re-encode.</b>
        ///     Opcode 4 sets <see cref="SpriteGroupId"/> to -1 explicitly; the opcode being absent
        ///     leaves the client's <c>anInt114</c> at the same -1 (Class9.java:247-250). Seven of the
        ///     hundred records in both caches carry opcode 4 and 93 carry opcode 1, so both forms are
        ///     live and re-encoding one as the other rewrites a file nobody edited.
        ///     <para>
        ///     Assigning <see cref="SpriteGroupId"/> alone is therefore not enough, and worse, is
        ///     silent. A record carrying opcode 4 replays that opcode whatever the field says, so
        ///     setting a real sprite on one produces the same bytes and the edit vanishes; and a
        ///     record carrying opcode 1 set to -1 would write <c>0xFFFF</c> through opcode 1's
        ///     unsigned short, which decodes back as 65535 rather than as -1.
        ///     </para>
        ///     <para>
        ///     So the opcode is swapped <b>in place</b>, at the position the record already stored
        ///     it. In place matters: not one of this group's sibling groups is in ascending opcode
        ///     order, the encoder replays the stored sequence, and appending the replacement instead
        ///     would reorder a file that only had one field changed. Setting a value and setting it
        ///     back therefore lands on the bytes it started from.
        ///     </para>
        ///     <para>
        ///     A record carrying <i>neither</i> opcode keeps neither: the field is set and
        ///     <see cref="AddedOpcodes"/> chooses the form, which is the only case where this editor
        ///     picks one.
        ///     </para>
        /// </remarks>
        /// <param name="spriteGroupId">The index-8 sprite group, or -1 for no icon.</param>
        public void SetSpriteGroupId(int spriteGroupId) {
            SpriteGroupId = spriteGroupId;

            int wanted = spriteGroupId == -1 ? 4 : 1;
            int unwanted = spriteGroupId == -1 ? 1 : 4;

            for (int i = 0; i < DecodedOpcodes.Count; i++) {
                if (DecodedOpcodes[i].Opcode != unwanted)
                    continue;

                DecodedOpcodes[i] = new DecodedOpcode(wanted, CurrentValue(wanted));
            }
        }

        /// <summary>
        ///     Which of the two "no icon" encodings this record uses, or that it names an icon.
        /// </summary>
        /// <remarks>
        ///     Shown beside the sprite field because the two forms are indistinguishable from the
        ///     decoded value: both leave <see cref="SpriteGroupId"/> at -1, and only the opcode list
        ///     says which was stored.
        /// </remarks>
        /// <returns>The description.</returns>
        public string DescribeAbsentIconEncoding() {
            if (Emits(4))
                return "no icon, stored as opcode 4";
            if (Emits(1))
                return "names an icon through opcode 1";
            return "no icon, stored as the opcode being absent";
        }

        private IEnumerable<int> AddedOpcodes() {
            //Opcode 4 is the explicit "no sprite" form, so a -1 added by an edit uses it rather
            //than writing -1 through opcode 1's unsigned short.
            if (!Emits(1) && !Emits(4) && SpriteGroupId > 0) yield return 1;
            if (!Emits(4) && !Emits(1) && SpriteGroupId == -1) yield return 4;
            if (!Emits(2) && TintRgb != 0) yield return 2;
            if (!Emits(3) && StretchToFootprint) yield return 3;
        }
    }
}
