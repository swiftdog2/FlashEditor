namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     How wide an instruction's operand is stored, which the opcode alone decides.
    /// </summary>
    /// <remarks>
    ///     Nothing in the instruction stream delimits an instruction, so this classification is the
    ///     only thing keeping the reader in step. Getting one instruction wrong desynchronises every
    ///     instruction after it, and the stream carries no marker the reader could resynchronise on.
    ///     The rule is <c>Class22.java:62-68</c>.
    /// </remarks>
    public enum ClientScriptOperandKind {
        /// <summary>A four byte big-endian signed integer.</summary>
        Integer,

        /// <summary>A single unsigned byte.</summary>
        Byte,

        /// <summary>A NUL-terminated string in the client's modified cp1252.</summary>
        Text
    }
}
