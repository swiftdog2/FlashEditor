using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A struct: a bag of parameters addressed by id, with no fields of its own.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.Struct"/>. Decoded by
    ///     <c>InterfaceConfig.method1588</c> (:82-101) dispatching to <c>method1589</c> (:103-136);
    ///     the provider is <c>Class264</c>, which names the group at Class264.java:67.
    ///     <para>
    ///     <b>Opcode 249 is the whole format.</b> The record class has exactly one field, the
    ///     parameter array, and CS2 is the only reader: opcode 4500 (Class247.java:3784-3799) pops a
    ///     struct id and a parameter key, looks the key up in the group 11 parameter type table, and
    ///     uses <c>Class149.isString</c> to decide whether to pull a string or an integer out of this
    ///     record - falling back to that type's own default when the struct does not carry the key.
    ///     So a struct is a keyed property bag whose schema lives in another group entirely.
    ///     </para>
    ///     <para>
    ///     Measured over both caches: 1,182 of the records carry the block and the rest are bare
    ///     terminators; the blocks hold 12,269 entries under 232 distinct keys, every one of them a
    ///     live file id in group 11, and the per-entry string flag agrees with that record's type
    ///     letter on all 12,269. That join proves itself rather than merely correlating.
    ///     </para>
    ///     <para>
    ///     <b>Six records repeat a key.</b> Files 951, 973, 1330, 1337, 1342 and 1450 each carry one
    ///     or two keys twice, so <see cref="ConfigParameters"/>' ordered list is load-bearing on real
    ///     data here rather than only on a hand-built case - the client's own store keeps the
    ///     <i>first</i> occurrence (InterfaceConfig.java:125), and folding the block into a map would
    ///     drop the loser and reorder the survivors.
    ///     </para>
    /// </remarks>
    public sealed class StructDefinition : ConfigDefinition {
        /// <summary>Opcode 249. The parameter block, in stored order.</summary>
        public List<ConfigParameter> Parameters { get; } = new List<ConfigParameter>();

        /// <summary>Decodes one struct definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public StructDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            //The client's dispatcher tests 249 and falls through for anything else, consuming
            //nothing, which desynchronises the rest of the record. Refusing is strictly better.
            if (opcode != 249)
                throw Unknown(opcode);

            ConfigParameters.Read(stream, Parameters);
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            if (opcode != 249)
                throw Unknown(opcode);

            ConfigParameters.Write(stream, Parameters);
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(249) && Parameters.Count > 0)
                yield return 249;
        }
    }
}
