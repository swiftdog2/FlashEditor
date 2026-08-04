namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     One arm of a CS2 switch: the value it matches and how far it jumps.
    /// </summary>
    /// <remarks>
    ///     Both fields are settled from what the interpreter does with them at
    ///     <c>Class247.java:7975-7980</c>, not from the field names the decompiler produced.
    ///     Opcode 51 takes the block named by its operand, looks the popped stack value up in it,
    ///     and on a hit does <c>current += node.value</c> - so the key is the value being switched
    ///     on and the payload is a delta applied to the program counter. Both are stored as signed
    ///     four byte integers and negative values are legal in both.
    /// </remarks>
    public readonly struct ClientScriptSwitchCase {
        /// <summary>The value popped off the integer stack that selects this arm.</summary>
        public int Value { get; }

        /// <summary>
        ///     How far the program counter moves when this arm is taken.
        /// </summary>
        /// <remarks>
        ///     Relative to the switch instruction, and applied after the counter has already been
        ///     advanced past it, so it is an offset in instructions rather than in bytes.
        /// </remarks>
        public int JumpOffset { get; }

        /// <summary>Binds a case value to its jump.</summary>
        /// <param name="value">The value this arm matches.</param>
        /// <param name="jumpOffset">The program-counter delta.</param>
        public ClientScriptSwitchCase(int value, int jumpOffset) {
            Value = value;
            JumpOffset = jumpOffset;
        }
    }
}
