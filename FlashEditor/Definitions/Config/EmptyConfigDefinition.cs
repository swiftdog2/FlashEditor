using FlashEditor.IO;
namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     A config record that carries no opcodes at all, which is what twenty of index 2's groups
    ///     hold in this cache.
    /// </summary>
    /// <remarks>
    ///     Measured over every file of every group in index 2: <b>8,694 of the 16,981 files are a
    ///     single 0x00 byte</b>, and twenty groups are entirely made of them - group
    ///     <see cref="ConfigGroup.ClientString"/> plus the nineteen with no client provider at all
    ///     (2, 7, 18, 20, 21, 22, 23, 24, 25, 37, 38, 39, 40, 41, 42, 43, 44, 45, 48).
    ///     <para>
    ///     <b>There is nothing in them to reverse engineer.</b> A record is opcode-terminated and
    ///     these terminate immediately, so no opcode table can be recovered from 639 data and no
    ///     field in any of them can be named. They are almost certainly server-side config types
    ///     whose payloads are stripped from the client cache while the id space is kept, and the id
    ///     space is the only information they still carry.
    ///     </para>
    ///     <para>
    ///     Group 15 is the one of the twenty that <i>does</i> have a provider. <c>Class239</c>
    ///     (:70-79) stores the archive and the child count and stops - it has no getter and never
    ///     reads a file - and that count is used once, at InterfaceSettings.java:343, to size
    ///     <c>Class151_Sub1.aStringArray4967</c>. So group 15 exists purely as a length, which is
    ///     consistent with all 345 of its files being empty.
    ///     </para>
    ///     <para>
    ///     This is a real assertion rather than a placeholder. Because the loop throws on any opcode
    ///     it meets, a future cache that starts filling one of these groups fails loudly here instead
    ///     of being silently mis-read, and a byte-identity sweep over it states "this group is still
    ///     empty" in the only terms that survive the group filling up.
    ///     </para>
    /// </remarks>
    public sealed class EmptyConfigDefinition : ConfigDefinition {
        /// <summary>Decodes one empty config record.</summary>
        /// <param name="stream">The definition file.</param>
        /// <returns>This definition.</returns>
        public EmptyConfigDefinition DecodeFrom(JagStream stream) {
            Decode(stream);
            return this;
        }

        /// <inheritdoc/>
        protected override void ReadPayload(int opcode, JagStream stream) => throw Unknown(opcode);

        /// <inheritdoc/>
        protected override void WritePayload(int opcode, JagStream stream) => throw Unknown(opcode);
    }
}
