using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     One switch table, addressed by the operand of an opcode 51 instruction.
    /// </summary>
    /// <remarks>
    ///     The blocks are stored as an array and selected by index -
    ///     <c>aRSArrayArray5956[is_265_[current]]</c> at <c>Class247.java:7975</c> - so their order
    ///     is load bearing and reordering them repoints every switch in the script.
    ///     <para>
    ///     The client loads each block into a hash map keyed on the case value, which means it
    ///     cannot represent two arms with the same value. Nothing enforces that here: no block in
    ///     either supported cache repeats a value, and rejecting one would be this codec inventing a
    ///     rule the file format does not state.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptSwitchBlock {
        /// <summary>Bytes one stored case occupies: the value and the jump, four each.</summary>
        public const int StoredCaseLength = 8;

        /// <summary>Bytes a block occupies before its cases: the 16-bit case count.</summary>
        public const int StoredHeaderLength = 2;

        /// <summary>Most cases a block's 16-bit count can express.</summary>
        public const int MaxCases = 0xFFFF;

        /// <summary>The arms of this switch, in stored order.</summary>
        public IList<ClientScriptSwitchCase> Cases { get; } = new List<ClientScriptSwitchCase>();

        /// <summary>How many bytes this block occupies, its count field included.</summary>
        public int StoredLength => StoredHeaderLength + (Cases.Count * StoredCaseLength);

        /// <summary>Reads one block, leaving the stream on the byte after its last case.</summary>
        /// <param name="stream">The script, positioned at the block's case count.</param>
        /// <returns>The decoded block.</returns>
        public static ClientScriptSwitchBlock Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var block = new ClientScriptSwitchBlock();
            int cases = stream.ReadUnsignedShort();

            for (int index = 0 ; index < cases ; index++) {
                int value = stream.ReadInt();
                int jump = stream.ReadInt();
                block.Cases.Add(new ClientScriptSwitchCase(value, jump));
            }

            return block;
        }

        /// <summary>Writes this block at the stream's current position.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <exception cref="InvalidOperationException">The block holds more cases than the count field can state.</exception>
        public void Encode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (Cases.Count > MaxCases)
                throw new InvalidOperationException(
                    "A switch block's case count is stored as an unsigned short, so " + Cases.Count +
                    " cases cannot be written.");

            stream.WriteShort(Cases.Count);

            foreach (ClientScriptSwitchCase arm in Cases) {
                stream.WriteInteger(arm.Value);
                stream.WriteInteger(arm.JumpOffset);
            }
        }
    }
}
