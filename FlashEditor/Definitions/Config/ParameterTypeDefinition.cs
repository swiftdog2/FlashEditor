using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A parameter type: what an opcode 249 parameter key means, and what its default is when a
    ///     record does not carry it.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.ParameterType"/>. 1,330 files in the shipped 639
    ///     cache, 851 of them empty. Decoded by <c>Class149.unpackConfig</c> (:128-145) dispatching
    ///     to <c>method2434</c> (:104-126); the provider is <c>Class365</c>, which names the group at
    ///     Class365.java:102.
    ///     <para>
    ///     <b>This is the table every param block keys off, and the join proves itself.</b>
    ///     <c>Class149.isString</c> (:92-102) returns whether the type letter is <c>'s'</c>, and CS2
    ///     opcode 6804 (Class247.java:7304-7320) looks a record up here by the same integer key a
    ///     param block stores, uses <c>isString()</c> to decide whether to pull a string or an int
    ///     out of that block, and falls back to opcode 5 or opcode 2 as the default. Measured across
    ///     the whole cache: group 26's 12,269 param entries use 232 distinct keys, every one a live
    ///     file id here, and the per-entry string flag agrees with the keyed record's type letter on
    ///     all 12,269 entries.
    ///     </para>
    ///     <para>File id 371 is missing from the group, so enumerate the reference table.</para>
    /// </remarks>
    public sealed class ParameterTypeDefinition : ConfigDefinition {
        /// <summary>Opcode 1. The type letter, as the single byte the file stores.</summary>
        /// <remarks>
        ///     Kept raw for the reason
        ///     <see cref="ClientVariableDefinition.TypeLetterByte"/> is: the client's cp1252 remap
        ///     collapses five bytes onto '?', and one record here stores 0x80. Measured letters:
        ///     <c>i</c> 228, <c>s</c> 59, <c>S</c> 27, <c>d</c> 19, <c>o</c> 18, <c>J</c> 15,
        ///     <c>c</c> 15, <c>g</c> 12, <c>A</c> 8, <c>I</c> 8, <c>K</c> 8, <c>m</c> 6, <c>1</c> 5,
        ///     <c>n</c> 3, <c>O</c> 3, <c>l</c> 2, and one each of <c>@ P t v y</c> and 0x80.
        /// </remarks>
        public int TypeLetterByte { get; set; }

        /// <summary>The character <see cref="TypeLetterByte"/> names.</summary>
        public char TypeLetter => ConfigText.ToCharacter(TypeLetterByte);

        /// <summary>Whether a param block stores this key's value as a string.</summary>
        /// <remarks><c>Class149.isString</c> (:92-102) is exactly <c>aChar1201 == 's'</c>.</remarks>
        public bool IsString => TypeLetter == 's';

        /// <summary>Opcode 2. The default value when the type is not a string.</summary>
        public int DefaultInt { get; set; }

        /// <summary>Opcode 4. Present clears a flag the client sets by default.</summary>
        /// <remarks>
        ///     <c>aBoolean1204</c>, which defaults to true. Nothing in the 637 client reads it back,
        ///     so it is not named further here. 81 records carry it.
        /// </remarks>
        public bool Unknown4 { get; set; } = true;

        /// <summary>Opcode 5. The default value when the type is a string.</summary>
        public string? DefaultString { get; set; }

        /// <summary>Decodes one parameter type definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public ParameterTypeDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: TypeLetterByte = stream.ReadUnsignedByte(); break;
                case 2: DefaultInt = stream.ReadInt(); break;
                case 4: Unknown4 = false; break;
                case 5: DefaultString = stream.ReadJagexString(); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 1: stream.WriteByte(TypeLetterByte); break;
                case 2: stream.WriteInteger(DefaultInt); break;
                case 4: break;
                case 5: stream.WriteJagexString(DefaultString ?? ""); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(1) && TypeLetterByte != 0) yield return 1;
            if (!Has(2) && DefaultInt != 0) yield return 2;
            if (!Has(4) && !Unknown4) yield return 4;
            if (!Has(5) && DefaultString != null) yield return 5;
        }
    }
}
