using System.Collections.Generic;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A player variable (varp): one slot of the per-player variable array, and whether it
    ///     survives a logout.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.VarPlayer"/>. 2,002 files in the shipped 639
    ///     cache and only <b>9</b> of them carry an opcode at all; the rest are a bare terminator, so
    ///     the group is mostly an id space. Decoded by <c>Class167.method2527</c> (:104-125)
    ///     dispatching to <c>method2530</c> (:127-144); the provider is <c>Class139</c>, which names
    ///     the group at Class139.java:19.
    ///     <para>
    ///     Settled by usage: the group's <i>file count</i> sizes the client's player-variable array
    ///     (Class140.java:49-50), and <c>Class140.method2288</c> (:120-135) resets variable <i>i</i>
    ///     on logout only when this field is 0. The client never tests it against anything but 0, so
    ///     the specific stored values 1..10 are not settled by the client.
    ///     </para>
    ///     <para>
    ///     File ids are not contiguous - 49 of the 2,050 ids the group spans are missing - so
    ///     enumerate the reference table rather than counting.
    ///     </para>
    /// </remarks>
    public sealed class VarPlayerDefinition : ConfigDefinition {
        /// <summary>Opcode 5. Non-zero marks the variable as surviving a logout.</summary>
        /// <remarks>
        ///     Measured (file, value) pairs, the only nine in the cache: (166,1) (167,2) (168,3)
        ///     (169,4) (170,5) (171,6) (173,7) (304,9) (872,10).
        /// </remarks>
        public int PersistenceScope { get; set; }

        /// <summary>Whether the client clears this variable on logout.</summary>
        public bool ResetOnLogout => PersistenceScope == 0;

        /// <summary>Decodes one varplayer definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public VarPlayerDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 5: PersistenceScope = stream.ReadUnsignedShort(); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 5: stream.WriteShort(PersistenceScope); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(5) && PersistenceScope != 0) yield return 5;
        }
    }
}
