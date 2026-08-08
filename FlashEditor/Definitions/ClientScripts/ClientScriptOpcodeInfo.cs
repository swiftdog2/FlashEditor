namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     What is known about one CS2 opcode, and how much of it is proven.
    /// </summary>
    /// <remarks>
    ///     <see cref="Mnemonic"/> is deliberately nullable and that is the whole point of this type.
    ///     A name is carried only where the 637 client's own dispatch settles it; everything else
    ///     keeps the number and says which dispatcher would have handled it. A wrong mnemonic is
    ///     worse than no mnemonic, because a number is honestly unknown while a name is confidently
    ///     misleading - and this project has already been burnt once by a mapping that looked right
    ///     in aggregate and was not.
    /// </remarks>
    public sealed class ClientScriptOpcodeInfo {
        /// <summary>The stored opcode.</summary>
        public int Opcode { get; }

        /// <summary>The mnemonic, or <c>null</c> when the client's dispatch does not settle one.</summary>
        public string? Mnemonic { get; }

        /// <summary>
        ///     What the client's dispatch arm does with the stacks, in one line.
        /// </summary>
        /// <remarks>
        ///     Present for every opcode, named or not: for a named one it is the evidence behind the
        ///     name, and for an unnamed one it is at least the dispatcher and the calling convention
        ///     it reaches, which is more than the number alone says.
        /// </remarks>
        public string Summary { get; }

        /// <summary>Where in the 637 client the claim can be checked, as <c>file:line</c>.</summary>
        public string Citation { get; }

        /// <summary>Whether a mnemonic has been proven for this opcode.</summary>
        public bool IsNamed => Mnemonic != null;

        /// <summary>Binds one opcode's name, description and citation.</summary>
        /// <param name="opcode">The stored opcode.</param>
        /// <param name="mnemonic">The proven mnemonic, or null.</param>
        /// <param name="summary">What the dispatch does.</param>
        /// <param name="citation">The client line that proves it.</param>
        public ClientScriptOpcodeInfo(int opcode, string? mnemonic, string summary, string citation) {
            Opcode = opcode;
            Mnemonic = mnemonic;
            Summary = summary;
            Citation = citation;
        }
    }
}
