using System.Collections.Generic;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     One opcode as it appeared in a definition file, with the value it carried.
    /// </summary>
    /// <remarks>
    ///     Recording the value per occurrence, rather than only the field it landed in, is what
    ///     makes a byte-exact round trip possible when a definition sets the same opcode twice.
    ///     Floor overlay 94 in the shipped 639 cache emits opcode 11 as 255 and then 127; a decoder
    ///     that keeps only the winning value re-encodes both as 127, producing a file of the right
    ///     length and the wrong contents. The archive CRC covers those bytes, so that is a silent
    ///     corruption rather than an error.
    /// </remarks>
    public readonly struct DecodedOpcode {
        /// <summary>The opcode.</summary>
        public int Opcode { get; }

        /// <summary>
        ///     The field value this occurrence produced, after any decode-time transform.
        /// </summary>
        /// <remarks>
        ///     Post-transform rather than raw, so encoding applies the inverse and round-trips.
        ///     Zero for opcodes that carry no payload.
        /// </remarks>
        public int Value { get; }

        /// <summary>Creates a record of one decoded opcode.</summary>
        public DecodedOpcode(int opcode, int value = 0) {
            Opcode = opcode;
            Value = value;
        }
    }

    /// <summary>Helpers for replaying a recorded opcode sequence.</summary>
    internal static class DecodedOpcodeExtensions {
        /// <summary>
        ///     Whether the occurrence at <paramref name="index"/> is the last of its opcode.
        /// </summary>
        /// <remarks>
        ///     The last occurrence is the one that decided the field, so it is the one an edit has
        ///     to be written back through. Earlier occurrences replay their recorded value, which
        ///     preserves both the byte count and the client's last-write-wins behaviour.
        /// </remarks>
        public static bool IsLastOccurrence(this List<DecodedOpcode> opcodes, int index) {
            int opcode = opcodes[index].Opcode;
            for (int i = index + 1; i < opcodes.Count; i++)
                if (opcodes[i].Opcode == opcode)
                    return false;
            return true;
        }

        /// <summary>Whether the sequence contains an occurrence of <paramref name="opcode"/>.</summary>
        public static bool Has(this List<DecodedOpcode> opcodes, int opcode) {
            foreach (DecodedOpcode entry in opcodes)
                if (entry.Opcode == opcode)
                    return true;
            return false;
        }
    }
}
