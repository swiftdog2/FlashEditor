using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A cursor: the sprite the platform draws as the mouse pointer, and the pixel within it that
    ///     is the hotspot.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.Cursor"/>. 175 files in the shipped 639 cache,
    ///     every one carrying opcodes 1 and 2 in that order. Decoded by <c>Class231.method2880</c>
    ///     (:160-176) dispatching to <c>method2879</c> (:137-158); the provider is <c>Class11</c>,
    ///     which names the group at Class11.java:33.
    ///     <para>
    ///     Settled by usage: <c>RSFont.java:82-95</c> loads the sprite from index 8 and passes it to
    ///     <c>Signlink.method872</c> with <c>new Point(anInt1738, anInt1736)</c>, which is the
    ///     platform custom-cursor call.
    ///     </para>
    ///     <para>
    ///     <b>Opcode 2 is two bytes, not four.</b> <c>method2879</c> reads the two hotspot bytes and
    ///     then falls into opcode 1's <c>readUnsignedShort</c> behind
    ///     <c>if(!client.aBoolean3553) break;</c>. That predicate is assigned true at exactly one
    ///     site, a shutdown path at client.java:2842, so it reads false during a decode - JODE merged
    ///     two opcode bodies that shared a tail in bytecode. The data settles it independently: all
    ///     175 records carry both opcodes and all 175 consume their buffer exactly under the
    ///     two-byte reading, where the four-byte reading would over-read 350 bytes.
    ///     </para>
    /// </remarks>
    public sealed class CursorDefinition : ConfigDefinition {
        /// <summary>Opcode 1. Sprite group in JS5 index 8 holding the pointer image.</summary>
        /// <remarks>Measured 168..4027 across 172 distinct ids.</remarks>
        public int SpriteId { get; set; }

        /// <summary>Opcode 2, first byte. Hotspot x within the sprite.</summary>
        public int HotspotX { get; set; }

        /// <summary>Opcode 2, second byte. Hotspot y within the sprite.</summary>
        /// <remarks>
        ///     Measured hotspots: (5,0) on 136 records, (0,0) on 37, and one each of (6,0) and
        ///     (11,4).
        /// </remarks>
        public int HotspotY { get; set; }

        /// <summary>Decodes one cursor definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public CursorDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1:
                    SpriteId = stream.ReadUnsignedShort();
                    break;
                case 2:
                    HotspotX = stream.ReadUnsignedByte();
                    HotspotY = stream.ReadUnsignedByte();
                    break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1:
                    stream.WriteShort(SpriteId);
                    break;
                case 2:
                    stream.WriteByte(HotspotX);
                    stream.WriteByte(HotspotY);
                    break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && SpriteId != 0) yield return 1;
            if (!Has(2) && (HotspotX != 0 || HotspotY != 0)) yield return 2;
        }
    }
}
