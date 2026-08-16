using System.Collections.Generic;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     One opcode as it appeared in a definition file, with the value it carried.
    /// </summary>
    /// <remarks>
    ///     The weaker of the two occurrence records here, and only usable where every opcode's
    ///     payload is a single integer. Anything with a multi-field or variable-length payload -
    ///     which is most of the format - wants <see cref="OpcodeRecord"/> and
    ///     <see cref="OpcodeStreamDefinition"/> instead, which keep the payload bytes verbatim.
    ///     <para>
    ///     Recording the value per occurrence, rather than only the field it landed in, is what
    ///     makes a byte-exact round trip possible when a definition sets the same opcode twice.
    ///     Floor overlay 94 in the shipped 639 cache emits opcode 11 as 255 and then 127; a decoder
    ///     that keeps only the winning value re-encodes both as 127, producing a file of the right
    ///     length and the wrong contents. The archive CRC covers those bytes, so that is a silent
    ///     corruption rather than an error.
    ///     </para>
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

    /// <summary>
    ///     The payload-free opcodes an edit has turned off, kept apart from the recorded stream so
    ///     turning one back on puts it where the file had it.
    /// </summary>
    /// <remarks>
    ///     <b>A bare opcode's whole meaning is whether it is in the stream</b>, so a boolean spelled
    ///     by one cannot be written back by re-deriving a payload - there is no payload. Removing the
    ///     entry from the recorded stream instead is the obvious move and is the defect this project
    ///     has already paid for once, on the object and NPC codecs: removing it forgets <i>where</i>
    ///     the opcode was, so turning the flag back on re-emits it at the end of the record. That is
    ///     a record of the right length with a byte moved, which the commit path stages as a real
    ///     change - and an archive CRC covers the stored bytes, so it drags in the reference-table
    ///     entry of every archive packed alongside it, for an edit that netted nothing.
    ///     <para>
    ///     Shared by the three index 2 codecs that predate <c>ConfigDefinition</c> and keep a
    ///     <see cref="DecodedOpcode"/> list of their own; <c>ConfigDefinition</c> states the same
    ///     rule against its own richer stream.
    ///     </para>
    /// </remarks>
    internal sealed class SuppressedOpcodes {
        private readonly HashSet<int> suppressed = new HashSet<int>();

        /// <summary>Forgets every suppression, for a record about to be decoded again.</summary>
        public void Clear() => suppressed.Clear();

        /// <summary>Whether an opcode is currently turned off.</summary>
        /// <param name="opcode">The opcode.</param>
        /// <returns>Whether the encoder should skip it.</returns>
        public bool Contains(int opcode) => suppressed.Contains(opcode);

        /// <summary>
        ///     Turns a payload-free opcode on or off.
        /// </summary>
        /// <remarks>
        ///     An opcode the file never carried cannot be suppressed - there is nothing to skip -
        ///     and does not need to be: the codec's own added-opcode rule decides whether an edit
        ///     appends it, by comparing the field against its constructor default.
        /// </remarks>
        /// <param name="stored">The recorded stream.</param>
        /// <param name="opcode">The payload-free opcode.</param>
        /// <param name="present">Whether the record should emit it.</param>
        public void Set(List<DecodedOpcode> stored, int opcode, bool present) {
            if (present)
                suppressed.Remove(opcode);
            else if (stored.Has(opcode))
                suppressed.Add(opcode);
        }

        /// <summary>Whether the record will emit an opcode: stored, and not turned off.</summary>
        /// <param name="stored">The recorded stream.</param>
        /// <param name="opcode">The opcode.</param>
        /// <returns>Whether it is emitted.</returns>
        public bool Emits(List<DecodedOpcode> stored, int opcode) {
            return stored.Has(opcode) && !suppressed.Contains(opcode);
        }
    }
}
