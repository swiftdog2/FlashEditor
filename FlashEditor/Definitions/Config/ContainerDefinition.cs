using System.Collections.Generic;

namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     An item container: how many slots one of the game's inventories, banks or shop stocks
    ///     holds.
    /// </summary>
    /// <remarks>
    ///     JS5 index 2, group <see cref="ConfigGroup.Container"/>. 609 files in the shipped 639
    ///     cache, every one of which carries opcode 2 and nothing else. Decoded by
    ///     <c>Node_Sub46_Sub18.method1628</c> (:31-47) dispatching to <c>method1627</c> (:14-29); the
    ///     provider is <c>Class8</c>, which names the group at Class8.java:163.
    ///     <para>
    ///     Settled by usage rather than by the field name: <c>Class156_Sub1.java:32,43</c> compares
    ///     this number against <c>Node_Sub3.itemIDS.length</c> as the container's slot count, and CS2
    ///     reads it at Class247.java:2115. Measured values 1..516.
    ///     </para>
    /// </remarks>
    public sealed class ContainerDefinition : ConfigDefinition {
        /// <summary>Opcode 2. How many item slots the container holds.</summary>
        public int Capacity { get; set; }

        /// <summary>Decodes one container definition.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public ContainerDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 2: Capacity = stream.ReadUnsignedShort(); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) {
            switch (opcode) {
                case 2: stream.WriteShort(Capacity); break;
                default: throw Unknown(opcode);
            }
        }

        /// <inheritdoc/>
        protected override IEnumerable<int> AddedOpcodes() {
            if (!Has(2) && Capacity != 0) yield return 2;
        }
    }
}
