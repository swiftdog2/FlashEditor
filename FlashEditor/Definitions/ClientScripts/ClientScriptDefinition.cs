using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     One compiled CS2 client script from JS5 index 12.
    /// </summary>
    /// <remarks>
    ///     The whole index is one script per group and one file per group, so a script id is a group
    ///     id. Two client readers agree on that: <c>Node_Sub46_Sub13_Sub2.getScript</c> takes
    ///     <c>getChildFromFolder(id, 0)</c> at <c>:18</c>, and <c>Class213.method2779</c> resolves an
    ///     interface hook through the reference table's identifier map and lands on the same
    ///     single-file accessor.
    ///     <para>
    ///     The layout is <c>Class22.unpack</c> (<c>Class22.java:11-78</c>) and it is addressed from
    ///     both ends, which is what makes the format unusual here. The last two bytes state how long
    ///     the switch section is; that fixes where the twelve byte footer starts; and the footer's
    ///     own offset is where the instruction stream stops. So a record cannot be read forwards
    ///     alone, and it cannot be read at all without knowing its exact length - which is why this
    ///     decoder takes the whole stream as one script rather than reading from the current
    ///     position.
    ///     </para>
    ///     <para>
    ///     This is a codec, not a disassembler. Instructions keep their numeric opcodes: naming them
    ///     needs a table over the roughly 580 distinct opcodes this cache uses, spread across three
    ///     dispatchers in <c>Class247</c>, and that is a much larger job than reading and writing
    ///     the bytes without losing any.
    ///     </para>
    /// </remarks>
    public sealed class ClientScriptDefinition {
        /// <summary>Bytes the footer occupies: the instruction count and four 16-bit counts.</summary>
        public const int FooterLength = 12;

        /// <summary>Bytes the trailing switch-section length occupies.</summary>
        public const int TrailerLength = 2;

        /// <summary>
        ///     Shortest record the layout can express: the name byte, the footer and the trailer.
        /// </summary>
        /// <remarks>
        ///     The switch section is not counted, because it is the one part that can be absent
        ///     entirely - see <see cref="OmitsSwitchBlockCount"/>.
        /// </remarks>
        public const int MinimumLength = 1 + FooterLength + TrailerLength;

        /// <summary>Largest switch-section length the trailer can state.</summary>
        public const int MaxSwitchSectionLength = 0xFFFF;

        /// <summary>Most switch blocks the section's one-byte count can express.</summary>
        public const int MaxSwitchBlocks = 0xFF;

        /// <summary>Largest value one of the footer's four counts can hold.</summary>
        public const int MaxFooterCount = 0xFFFF;

        private byte[]? _nameBytes;

        /// <summary>The script id, which on this index is its group id.</summary>
        public int Id { get; set; } = -1;

        /// <summary>
        ///     The optional leading name, as the file stores it, or <c>null</c> when absent.
        /// </summary>
        /// <remarks>
        ///     Stored state, with <see cref="Name"/> as the text view - see
        ///     <see cref="ClientScriptText"/> for why the bytes are kept. Absent and empty are the
        ///     same thing on the wire: <c>RSBuffer.method1222(-1)</c> (<c>RSBuffer.java:427-438</c>)
        ///     returns null the moment the first byte is 0, so a zero-length name can only ever be
        ///     read back as absent. Assigning an empty value therefore clears the field rather than
        ///     pretending the distinction survives a save.
        ///     <para>
        ///     No script in either supported cache carries one, so the byte-identity sweep says
        ///     nothing about this field at all and the synthetic codec tests are the only thing
        ///     defending it.
        ///     </para>
        /// </remarks>
        public byte[]? NameBytes {
            get => _nameBytes == null ? null : (byte[]) _nameBytes.Clone();
            set => _nameBytes = value == null || value.Length == 0 ? null : (byte[]) value.Clone();
        }

        /// <summary>The optional leading name as text, or <c>null</c> when absent.</summary>
        public string? Name {
            get => _nameBytes == null ? null : ClientScriptText.Decode(_nameBytes);
            set {
                byte[] encoded = ClientScriptText.Encode(value);
                _nameBytes = encoded.Length == 0 ? null : encoded;
            }
        }

        /// <summary>The instruction stream, in execution order.</summary>
        public IList<ClientScriptInstruction> Instructions { get; } = new List<ClientScriptInstruction>();

        /// <summary>The switch tables, indexed by the operand of an opcode 51 instruction.</summary>
        public IList<ClientScriptSwitchBlock> SwitchBlocks { get; } = new List<ClientScriptSwitchBlock>();

        /// <summary>
        ///     How many integer local variables the script's frame holds.
        /// </summary>
        /// <remarks>
        ///     Settled from what the client does with it, which is not what the decompiled field name
        ///     <c>integerArgCount</c> suggests. <c>Class247.java:7881</c> allocates the callee's whole
        ///     integer array as <c>new int[integerArgCount]</c> and then fills only the first
        ///     <see cref="IntegerParameterCount"/> entries from the caller's stack, so this is the
        ///     frame size and the other field is the parameter count. The data agrees: this is never
        ///     below <see cref="IntegerParameterCount"/> in either supported cache.
        /// </remarks>
        public int IntegerLocalCount { get; set; }

        /// <summary>How many string local variables the script's frame holds.</summary>
        /// <remarks>The string counterpart of <see cref="IntegerLocalCount"/>, <c>Class247.java:7882</c>.</remarks>
        public int StringLocalCount { get; set; }

        /// <summary>
        ///     How many integers the script takes as parameters.
        /// </summary>
        /// <remarks>
        ///     <c>Class247.java:7884-7892</c> copies exactly this many values off the caller's
        ///     integer stack into the new frame and then pops them.
        /// </remarks>
        public int IntegerParameterCount { get; set; }

        /// <summary>How many strings the script takes as parameters.</summary>
        /// <remarks>The string counterpart, <c>Class247.java:7888-7893</c>.</remarks>
        public int StringParameterCount { get; set; }

        /// <summary>
        ///     Whether the record stores an empty switch section with no block-count byte at all.
        /// </summary>
        /// <remarks>
        ///     The one representation choice this format allows, and neither supported cache makes
        ///     it: all 4149 declared scripts store a length of 1 and a zero count byte even when
        ///     there are no blocks. A length of 0 decodes identically in the client, because the
        ///     count byte it then reads is the high byte of the trailing length field, which is 0 -
        ///     so the two encodings are aliases and the choice cannot be recomputed from the decoded
        ///     content. It is recorded here so a record that made the other choice is written back as
        ///     it was found rather than growing by a byte.
        /// </remarks>
        public bool OmitsSwitchBlockCount { get; set; }

        /// <summary>
        ///     How many bytes the switch section occupies, which the trailer states.
        /// </summary>
        /// <remarks>
        ///     Derived, and therefore never stored: it is the count byte plus every block, and the
        ///     one case where the count byte is absent is carried by
        ///     <see cref="OmitsSwitchBlockCount"/> instead.
        /// </remarks>
        public int SwitchSectionLength {
            get {
                if (OmitsSwitchBlockCount && SwitchBlocks.Count == 0)
                    return 0;

                int length = 1;
                foreach (ClientScriptSwitchBlock block in SwitchBlocks)
                    length += block.StoredLength;
                return length;
            }
        }

        /// <summary>
        ///     Reads one script from the whole of <paramref name="stream"/>.
        /// </summary>
        /// <remarks>
        ///     Every offset the format derives is checked rather than trusted, and each check is the
        ///     exact-consumption assertion the usual sweep harness cannot make here: appending
        ///     sentinel bytes past a record would move the trailer this decoder reads, so an
        ///     over-read cannot be probed from outside and is caught in here instead. The three
        ///     that matter are that the switch section ends exactly on the trailer, that the
        ///     instruction stream ends exactly on the footer, and that the footer's own instruction
        ///     count agrees with the number decoded.
        /// </remarks>
        /// <param name="stream">The decompressed script file, whole.</param>
        /// <returns>This definition.</returns>
        /// <exception cref="ArgumentNullException">No stream was supplied.</exception>
        /// <exception cref="InvalidOperationException">The record's own offsets do not agree.</exception>
        public ClientScriptDefinition Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int length = stream.Length;
            if (length < MinimumLength)
                throw new InvalidOperationException(
                    "A CS2 script is at least " + MinimumLength + " bytes; this one is " + length + ".");

            stream.Seek(length - TrailerLength);
            int switchSectionLength = stream.ReadUnsignedShort();

            int footerOffset = length - TrailerLength - switchSectionLength - FooterLength;
            if (footerOffset < 1)
                throw new InvalidOperationException(
                    "The trailer states a " + switchSectionLength + " byte switch section, which puts the " +
                    "footer at " + footerOffset + " in a " + length + " byte script. The client reads a name " +
                    "byte at offset 0 before anything else, so the footer cannot start below 1.");

            stream.Seek(footerOffset);
            int declaredInstructions = stream.ReadInt();
            IntegerLocalCount = stream.ReadUnsignedShort();
            StringLocalCount = stream.ReadUnsignedShort();
            IntegerParameterCount = stream.ReadUnsignedShort();
            StringParameterCount = stream.ReadUnsignedShort();

            DecodeSwitchSection(stream, switchSectionLength, length);

            stream.Seek(0);
            DecodeName(stream);
            DecodeInstructions(stream, footerOffset);

            if (Instructions.Count != declaredInstructions)
                throw new InvalidOperationException(
                    "The footer declares " + declaredInstructions + " instructions and the stream holds " +
                    Instructions.Count + ", so the operand widths are out of step with the file.");

            //Leave the stream on the last byte, so a caller measuring consumption sees the whole
            //record consumed rather than the position the instruction loop happened to stop at.
            stream.Seek(length);
            return this;
        }

        /// <summary>Writes this script back to the bytes the cache would store for it.</summary>
        /// <remarks>
        ///     Every derived field is recomputed here rather than replayed - the instruction count,
        ///     the switch-section length and the footer offset all follow from the content, which is
        ///     what the byte-identity sweep over every declared script proves.
        /// </remarks>
        /// <returns>The encoded script, positioned at 0.</returns>
        /// <exception cref="InvalidOperationException">A derived field does not fit its stored width.</exception>
        public JagStream Encode() {
            int switchSectionLength = SwitchSectionLength;
            if (switchSectionLength > MaxSwitchSectionLength)
                throw new InvalidOperationException(
                    "The switch section is " + switchSectionLength + " bytes and its length is stored as an " +
                    "unsigned short, so it cannot be written.");

            if (SwitchBlocks.Count > MaxSwitchBlocks)
                throw new InvalidOperationException(
                    "The switch block count is stored in one unsigned byte, so " + SwitchBlocks.Count +
                    " blocks cannot be written.");

            //Every count below is a 16-bit field. Truncating one silently writes a file the client
            //would allocate the wrong frame for, so the width is checked rather than masked.
            RequireStorableCount(IntegerLocalCount, nameof(IntegerLocalCount));
            RequireStorableCount(StringLocalCount, nameof(StringLocalCount));
            RequireStorableCount(IntegerParameterCount, nameof(IntegerParameterCount));
            RequireStorableCount(StringParameterCount, nameof(StringParameterCount));

            var stream = new JagStream();

            if (_nameBytes == null) {
                stream.WriteByte(0);
            }
            else {
                stream.Write(_nameBytes, 0, _nameBytes.Length);
                stream.WriteByte(0);
            }

            foreach (ClientScriptInstruction instruction in Instructions)
                instruction.Encode(stream);

            stream.WriteInteger(Instructions.Count);
            stream.WriteShort(IntegerLocalCount);
            stream.WriteShort(StringLocalCount);
            stream.WriteShort(IntegerParameterCount);
            stream.WriteShort(StringParameterCount);

            if (switchSectionLength > 0) {
                stream.WriteByte((byte) SwitchBlocks.Count);
                foreach (ClientScriptSwitchBlock block in SwitchBlocks)
                    block.Encode(stream);
            }

            stream.WriteShort(switchSectionLength);
            return stream.Flip();
        }

        /// <summary>Refuses a footer count that would not survive its 16-bit field.</summary>
        /// <param name="value">The count.</param>
        /// <param name="field">The property it came from, for the message.</param>
        /// <exception cref="InvalidOperationException">The count does not fit.</exception>
        private static void RequireStorableCount(int value, string field) {
            if (value < 0 || value > MaxFooterCount)
                throw new InvalidOperationException(
                    field + " is stored as an unsigned short, so " + value + " cannot be written.");
        }

        /// <summary>Reads the optional leading name.</summary>
        /// <param name="stream">The script, positioned at 0.</param>
        private void DecodeName(JagStream stream) {
            if (stream.PeekUnsignedByte() == 0) {
                stream.Skip(1);
                _nameBytes = null;
                return;
            }

            _nameBytes = ClientScriptInstruction.ReadTerminatedBytes(stream);
        }

        /// <summary>Reads instructions until the stream reaches the footer.</summary>
        /// <param name="stream">The script, positioned after the name.</param>
        /// <param name="footerOffset">Where the footer starts.</param>
        /// <exception cref="InvalidOperationException">An instruction straddles the footer boundary.</exception>
        private void DecodeInstructions(JagStream stream, int footerOffset) {
            Instructions.Clear();

            while (stream.Position < footerOffset)
                Instructions.Add(ClientScriptInstruction.Decode(stream));

            if (stream.Position != footerOffset)
                throw new InvalidOperationException(
                    "The instruction stream ends at " + stream.Position + " and the footer starts at " +
                    footerOffset + ", so an operand was read at the wrong width.");
        }

        /// <summary>Reads the switch section, which sits between the footer and the trailer.</summary>
        /// <param name="stream">The script, positioned at the end of the footer.</param>
        /// <param name="switchSectionLength">The section length the trailer states.</param>
        /// <param name="length">The whole record's length.</param>
        /// <exception cref="InvalidOperationException">The blocks do not fill the section exactly.</exception>
        private void DecodeSwitchSection(JagStream stream, int switchSectionLength, int length) {
            SwitchBlocks.Clear();
            OmitsSwitchBlockCount = switchSectionLength == 0;

            if (OmitsSwitchBlockCount)
                return;

            int blocks = stream.ReadUnsignedByte();
            for (int index = 0 ; index < blocks ; index++)
                SwitchBlocks.Add(ClientScriptSwitchBlock.Decode(stream));

            int trailerOffset = length - TrailerLength;
            if (stream.Position != trailerOffset)
                throw new InvalidOperationException(
                    "The switch section ends at " + stream.Position + " and the trailer starts at " +
                    trailerOffset + ", so the " + switchSectionLength + " bytes it declares do not match " +
                    "the " + blocks + " blocks it holds.");
        }
    }
}
