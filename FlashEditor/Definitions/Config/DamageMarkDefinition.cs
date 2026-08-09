using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A damage mark: the sprites, font, colour and lifetime of one hit splat drawn over a mobile.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.DamageMark"/>. 28 files in the shipped 639
    ///     cache. Decoded by <c>Class86.method851</c> (:312-333) dispatching to <c>method841</c>
    ///     (:143-192); the provider is <c>Class121</c>, which names the group at Class121.java:102.
    ///     <para>
    ///     Settled by usage: <c>IntegerNode.java:344,355</c> fetches one per hit on a mobile and
    ///     subtracts <see cref="LifetimeMillis"/> from the mark's deadline, and
    ///     <c>Class86.method848</c> (:254-273) substitutes the damage number into every <c>"%1"</c>
    ///     of <see cref="NumberTemplate"/>. 27 of the 28 records carry opcode 8; 26 store the bare
    ///     <c>"%1"</c> and file 22 stores the empty string, so a mark that draws no number at all is
    ///     a shipped case rather than a hypothetical one.
    ///     </para>
    ///     <para>
    ///     <b>Not one of the 28 files is in ascending opcode order</b>, across 6 distinct orders, so
    ///     the stored order is the model. Opcodes 2, 7, 11, 12 and 13 occur in no file here.
    ///     </para>
    /// </remarks>
    public sealed class DamageMarkDefinition : ConfigDefinition {
        /// <summary>Opcode 1. Font id for the damage number, or -1 for the default font.</summary>
        /// <remarks>
        ///     <c>anInt655</c>, resolved to an <c>RSFont</c> by <c>Node_Sub1.method945</c>
        ///     (IntegerNode.java:540-548), which falls back to the default when the lookup fails.
        /// </remarks>
        public int FontId { get; set; } = -1;

        /// <summary>Opcode 2. Colour of the damage number, 24-bit RGB.</summary>
        /// <remarks>
        ///     OR'd with an alpha byte before drawing (IntegerNode.java:743,770). Occurs in no file
        ///     of this cache, so every mark uses the constructor's white.
        /// </remarks>
        public int TextRgb { get; set; } = 0xFFFFFF;

        /// <summary>Opcode 3. First sprite layer, a group in JS5 index 8, or -1.</summary>
        /// <remarks>
        ///     Loaded through <c>Class86.method847</c> (:215-252) from
        ///     <c>aClass121_644.aJS5Archive_1005</c>, which InterfaceSettings.java:265-266 gives as
        ///     index 8. The three layers are drawn one over the next in the order 3, 4, 6
        ///     (IntegerNode.java:403, 438, 455).
        /// </remarks>
        public int SpriteLayer1Id { get; set; } = -1;

        /// <summary>Opcode 4. Second sprite layer, drawn over the first.</summary>
        public int SpriteLayer2Id { get; set; } = -1;

        /// <summary>Opcode 5. A fourth sprite, preloaded but drawn by nothing in this client.</summary>
        /// <remarks>
        ///     <c>anInt652</c>. <c>method847</c> loads it alongside the three layers and
        ///     <c>method852</c> (:335-356) hands it back, but that accessor has no caller anywhere in
        ///     the 637 source. Recorded rather than named.
        /// </remarks>
        public int PreloadedSpriteId { get; set; } = -1;

        /// <summary>Opcode 6. Third sprite layer, drawn over the second.</summary>
        public int SpriteLayer3Id { get; set; } = -1;

        /// <summary>Opcode 7. Horizontal drift, in pixels, decaying to zero over the lifetime.</summary>
        /// <remarks>
        ///     <c>anInt653</c>. IntegerNode.java:671-672 computes
        ///     <c>anInt653 - anInt653 * elapsed / anInt651</c>, so the mark starts this far across and
        ///     slides back. Signed. Occurs in no file of this cache.
        /// </remarks>
        public int DriftX { get; set; }

        /// <summary>Opcode 8. The damage number's format string, with <c>"%1"</c> as the number.</summary>
        /// <remarks>
        ///     Stored as a <c>gjstr2</c>, so the leading zero version byte is part of the payload.
        ///     Every occurrence in this cache is the bare <c>"%1"</c>.
        /// </remarks>
        public string NumberTemplate { get; set; } = "";

        /// <summary>Opcode 9. How long the mark stays on screen, in milliseconds.</summary>
        /// <remarks>
        ///     <c>anInt651</c>, subtracted from the mark's deadline at IntegerNode.java:346,357 and
        ///     the denominator of every drift and fade ramp. Defaults to 70.
        /// </remarks>
        public int LifetimeMillis { get; set; } = 70;

        /// <summary>Opcode 10. Vertical drift, in pixels, decaying to zero over the lifetime.</summary>
        /// <remarks><c>anInt650</c>, the y counterpart of <see cref="DriftX"/>. Signed.</remarks>
        public int DriftY { get; set; }

        /// <summary>Opcodes 11 and 14. When the mark starts fading, in milliseconds, or -1.</summary>
        /// <remarks>
        ///     <c>anInt645</c>. IntegerNode.java:711-712 ramps the alpha over
        ///     <c>LifetimeMillis - this</c> once it is non-negative. <b>Two opcodes write it</b>:
        ///     opcode 11 sets it to 0 with no payload and opcode 14 stores an unsigned short, so a
        ///     zero has two encodings and which one was stored is only recoverable from
        ///     <see cref="ConfigDefinition.DecodedOpcodes"/>. Opcode 11 occurs in no file of this
        ///     cache; opcode 14 occurs in all 28.
        /// </remarks>
        public int FadeStartMillis { get; set; } = -1;

        /// <summary>Opcode 12. Selects how stacked marks are laid out.</summary>
        /// <remarks>
        ///     <c>anInt642</c>, branched on at Particle_Sub3_Sub4_Sub2.java:719-735 when more than
        ///     one mark is live. The individual values are not settled here and it occurs in no file
        ///     of this cache.
        /// </remarks>
        public int Unknown12 { get; set; } = -1;

        /// <summary>Opcode 13. A fixed vertical offset, in pixels, added to the draw position.</summary>
        /// <remarks><c>anInt646</c> (IntegerNode.java:679). Signed. Occurs in no file of this cache.</remarks>
        public int OffsetY { get; set; }

        /// <summary>Decodes one damage mark definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public DamageMarkDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: FontId = stream.ReadUnsignedShort(); break;
                case 2: TextRgb = stream.ReadMedium(); break;
                case 3: SpriteLayer1Id = stream.ReadUnsignedShort(); break;
                case 4: SpriteLayer2Id = stream.ReadUnsignedShort(); break;
                case 5: PreloadedSpriteId = stream.ReadUnsignedShort(); break;
                case 6: SpriteLayer3Id = stream.ReadUnsignedShort(); break;
                case 7: DriftX = stream.ReadShort(); break;
                case 8: NumberTemplate = ConfigText.ReadVersionedString(stream); break;
                case 9: LifetimeMillis = stream.ReadUnsignedShort(); break;
                case 10: DriftY = stream.ReadShort(); break;
                case 11: FadeStartMillis = 0; break;
                case 12: Unknown12 = stream.ReadUnsignedByte(); break;
                case 13: OffsetY = stream.ReadShort(); break;
                case 14: FadeStartMillis = stream.ReadUnsignedShort(); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteShort(FontId); break;
                case 2: stream.WriteMedium(TextRgb); break;
                case 3: stream.WriteShort(SpriteLayer1Id); break;
                case 4: stream.WriteShort(SpriteLayer2Id); break;
                case 5: stream.WriteShort(PreloadedSpriteId); break;
                case 6: stream.WriteShort(SpriteLayer3Id); break;
                case 7: stream.WriteShort(DriftX); break;
                case 8: ConfigText.WriteVersionedString(stream, NumberTemplate); break;
                case 9: stream.WriteShort(LifetimeMillis); break;
                case 10: stream.WriteShort(DriftY); break;
                case 11: break;
                case 12: stream.WriteByte(Unknown12); break;
                case 13: stream.WriteShort(OffsetY); break;
                case 14: stream.WriteShort(FadeStartMillis); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && FontId != -1) yield return 1;
            if (!Has(2) && TextRgb != 0xFFFFFF) yield return 2;
            if (!Has(3) && SpriteLayer1Id != -1) yield return 3;
            if (!Has(4) && SpriteLayer2Id != -1) yield return 4;
            if (!Has(5) && PreloadedSpriteId != -1) yield return 5;
            if (!Has(6) && SpriteLayer3Id != -1) yield return 6;
            if (!Has(7) && DriftX != 0) yield return 7;
            if (!Has(8) && NumberTemplate != "") yield return 8;
            if (!Has(9) && LifetimeMillis != 70) yield return 9;
            if (!Has(10) && DriftY != 0) yield return 10;
            //Opcode 14 is the general form; opcode 11 only expresses a zero, and an edit that has
            //not carried either opcode has nothing that says which of the two was intended.
            if (!Has(11) && !Has(14) && FadeStartMillis != -1) yield return 14;
            if (!Has(12) && Unknown12 != -1) yield return 12;
            if (!Has(13) && OffsetY != 0) yield return 13;
        }
    }
}
