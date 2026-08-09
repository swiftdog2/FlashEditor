using System;
using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.VarBits {
    /// <summary>
    ///     One varbit from JS5 index 22: a bit range carved out of a player variable.
    /// </summary>
    /// <remarks>
    ///     A varbit id splits into <c>group = id &gt;&gt;&gt; 10</c> and <c>file = id &amp; 0x3FF</c>
    ///     (Class198.java:92-93, through Class234.java:31 and Class32.java:61), so a group holds 1024
    ///     varbits and a file is one record.
    ///     <para>
    ///     The record format is <c>VarBit.method3945</c>/<c>method3946</c> (VarBit.java:47-80): an
    ///     opcode loop with exactly one opcode. What the three fields mean is settled by
    ///     <c>Class140.method2289</c> (Class140.java:140-149), which indexes the varp array with
    ///     <see cref="VarpId"/>, masks with <c>anIntArray6070[toBit - fromBit]</c> and shifts left by
    ///     <see cref="FromBit"/> - and <c>anIntArray6070[n]</c> is <c>2^(n+1) - 1</c>
    ///     (Node_Sub46_Sub20.java:7,14). So <see cref="FromBit"/> is the least significant bit of the
    ///     range and <see cref="ToBit"/> the most significant.
    ///     </para>
    /// </remarks>
    public sealed class VarBitDefinition : OpcodeStreamDefinition {
        /// <summary>
        ///     How many bit positions the client's mask table can express.
        /// </summary>
        /// <remarks>
        ///     <c>anIntArray6070</c> has 32 entries, so a range wider than this indexes past the end
        ///     of it and the client throws while loading. No shipped record violates it; an editor
        ///     that lets the range be typed has to.
        /// </remarks>
        public const int ClientMaskTableSize = 32;

        /// <summary>The varbit id, which is <c>(group &lt;&lt; 10) | file</c>.</summary>
        public int Id { get; set; } = -1;

        /// <summary>Opcode 1, first field. Which player variable the bits are taken from.</summary>
        public int VarpId { get; set; }

        /// <summary>Opcode 1, second field. The least significant bit of the range.</summary>
        public int FromBit { get; set; }

        /// <summary>Opcode 1, third field. The most significant bit of the range.</summary>
        public int ToBit { get; set; }

        /// <summary>
        ///     Whether the file stored a record at all, rather than a bare terminator.
        /// </summary>
        /// <remarks>
        ///     Three on-disk states decode to the same all-zero varbit and only this tells them
        ///     apart: a file id the group does not hold at all, a one-byte file holding only the
        ///     terminator, and a six-byte record whose fields happen to be zero. A quarter of the
        ///     files in this index are bare terminators, so an encoder that wrote six bytes for a
        ///     default-valued varbit would rewrite every one of them on the first save.
        /// </remarks>
        public bool IsStored => Opcodes.Has(1);

        /// <summary>How many bits wide the range is.</summary>
        public int BitWidth => ToBit - FromBit + 1;

        /// <summary>The mask the client applies after shifting the varp right by <see cref="FromBit"/>.</summary>
        /// <remarks>
        ///     Computed the way <c>Class140.method2289</c> does rather than stored, so it cannot
        ///     disagree with the two bit positions it is derived from.
        /// </remarks>
        public int Mask => ToBit < FromBit ? 0 : (int) ((1L << BitWidth) - 1);

        /// <summary>Whether the client could load this record without indexing past its mask table.</summary>
        public bool FitsTheClientMaskTable =>
            FromBit >= 0 && ToBit >= FromBit && ToBit - FromBit < ClientMaskTableSize;

        /// <summary>Extracts this varbit's value out of the player variable it sits in.</summary>
        /// <param name="varpValue">The whole player variable.</param>
        /// <returns>The bits this varbit names, shifted down to zero.</returns>
        public int Extract(int varpValue) => (varpValue >> FromBit) & Mask;

        /// <summary>Reads one varbit record from its file.</summary>
        /// <param name="stream">The file, positioned at its first opcode.</param>
        /// <returns>This definition.</returns>
        public VarBitDefinition Decode(JagStream stream) {
            Opcodes.Clear();
            DecodeOpcodeStream(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override bool DecodeOpcode(JagStream stream, int opcode) {
            if (opcode != 1)
                return false;

            VarpId = stream.ReadUnsignedShort();
            FromBit = stream.ReadUnsignedByte();
            ToBit = stream.ReadUnsignedByte();
            return true;
        }

        /// <summary>Writes this varbit back to the file representation.</summary>
        /// <remarks>
        ///     A record that arrived as a bare terminator stays one unless a field was actually
        ///     changed, which is the whole of the absent-versus-default problem on this index.
        /// </remarks>
        /// <returns>The encoded file, positioned at 0.</returns>
        public JagStream Encode() {
            var records = new List<KeyValuePair<int, byte[]>>();

            if (Opcodes.Has(1) || VarpId != 0 || FromBit != 0 || ToBit != 0) {
                var payload = new JagStream();
                payload.WriteShort(VarpId);
                payload.WriteByte((byte) FromBit);
                payload.WriteByte((byte) ToBit);
                records.Add(new KeyValuePair<int, byte[]>(1, payload.Flip().ToArray()));
            }

            return Opcodes.Replay(records);
        }

        /// <summary>The record in words, for logs and list rows.</summary>
        /// <returns>The varp and bit range, or a note that nothing was stored.</returns>
        public override string ToString() {
            if (!IsStored)
                return "varbit " + Id + ": not stored";
            return "varbit " + Id + ": varp " + VarpId + " bits " + FromBit + ".." + ToBit;
        }
    }
}
