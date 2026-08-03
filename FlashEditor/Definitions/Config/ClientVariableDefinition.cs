using System.Collections.Generic;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A client variable: the type letter of one slot of the client-side variable store, and
    ///     whether the server is allowed to write it.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.ClientVariable"/>. 1,445 files in the shipped
    ///     639 cache, every one carrying opcode 1 and 19 of them opcode 2. Decoded by
    ///     <c>Class90.method885</c> (:95-111) dispatching to <c>method884</c> (:68-93); the provider
    ///     is <c>Class132</c>, which names the group at Class132.java:117.
    ///     <para>
    ///     Settled by usage: the file count sizes the client-variable stores
    ///     (InterfaceSettings.java:342,344), <c>:346-350</c> marks variable <i>i</i> settable when
    ///     opcode 2 is absent, and <c>Class31.java:26-32</c> refuses a server update unless that mark
    ///     is set - additionally clamping the value to -1..1 when the type letter is <c>'1'</c>.
    ///     </para>
    ///     <para>
    ///     <c>method884</c> reads opcode 1 and then falls into opcode 2's body behind
    ///     <c>if(!client.aBoolean3553) break;</c>. That field is assigned true at exactly one site, a
    ///     shutdown path at client.java:2842, so it reads false during a decode: the fallthrough is
    ///     JODE's tail merge of two bodies that shared a tail in bytecode, not a real one.
    ///     </para>
    /// </remarks>
    public sealed class ClientVariableDefinition : ConfigDefinition {
        /// <summary>Opcode 1. The type letter, as the single byte the file stores.</summary>
        /// <remarks>
        ///     Kept as the raw byte rather than as the character it decodes to. The client remaps
        ///     0x80-0x9F through modified cp1252 and falls back to '?' for the five unassigned slots,
        ///     so the character does not identify the byte - and one record in this cache stores
        ///     0x80. Measured letters: <c>i</c> 1287, <c>1</c> 58, <c>c</c> 41, <c>o</c> 28,
        ///     <c>J</c> 8, <c>K</c> 7, <c>m</c> 5, <c>I</c> 2, <c>O</c> 2, <c>e</c> 2, <c>g</c> 2,
        ///     <c>d</c> 1, <c>n</c> 1, and one 0x80.
        /// </remarks>
        public int TypeLetterByte { get; set; }

        /// <summary>The character <see cref="TypeLetterByte"/> names.</summary>
        public char TypeLetter => ConfigText.ToCharacter(TypeLetterByte);

        /// <summary>Opcode 2. Present means the server is allowed to set this variable.</summary>
        /// <remarks>
        ///     Carries no payload. The client's field defaults to 1 and this opcode sets it to 0;
        ///     InterfaceSettings.java:346-347 flags the variable settable on that 0, and
        ///     Class31.java:27-28 drops a server update for any variable that is not flagged. So the
        ///     permissive case is the one that costs a byte, which is why only 19 of the 1,445
        ///     records carry it.
        /// </remarks>
        public bool ServerWritable { get; set; }

        /// <summary>Decodes one client variable definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public ClientVariableDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: TypeLetterByte = stream.ReadUnsignedByte(); break;
                case 2: ServerWritable = true; break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteByte(TypeLetterByte); break;
                case 2: break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && TypeLetterByte != 0) yield return 1;
            if (!Has(2) && ServerWritable) yield return 2;
        }
    }
}
