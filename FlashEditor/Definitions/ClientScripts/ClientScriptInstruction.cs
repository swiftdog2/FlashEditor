using System;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     One CS2 instruction: a 16-bit opcode and the single operand stored beside it.
    /// </summary>
    /// <remarks>
    ///     This is deliberately raw. The opcode is kept as the number the file holds rather than
    ///     lowered to a mnemonic, because naming it needs an opcode table spanning the three
    ///     dispatchers in <c>Class247</c> - the in-line chain below 100 (<c>:7781-7988</c>),
    ///     <c>method3148</c> for 100..4999 and <c>method3156</c> for 5000..9999 - and that table is a
    ///     separate piece of work from being able to read and write the bytes losslessly.
    /// </remarks>
    public sealed class ClientScriptInstruction {
        /// <summary>The opcode whose operand is a string rather than a number.</summary>
        /// <remarks>
        ///     <c>Class22.java:62-63</c> tests it before the width rule, so it wins even though it is
        ///     below <see cref="WideOperandCeiling"/>. <c>Class247.java:7792</c> pushes it onto the
        ///     string stack.
        /// </remarks>
        public const int TextOpcode = 3;

        /// <summary>The opcode below which an operand is stored as a four byte integer.</summary>
        public const int WideOperandCeiling = 100;

        /// <summary>Largest opcode the two stored bytes can hold.</summary>
        public const int MaxOpcode = 0xFFFF;

        /// <summary>Largest value a byte-width operand can hold.</summary>
        public const int MaxByteOperand = 0xFF;

        /// <summary>
        ///     The three opcodes below <see cref="WideOperandCeiling"/> whose operand is still a
        ///     single byte.
        /// </summary>
        /// <remarks>
        ///     They are the sub-100 opcodes the interpreter never reads an operand for: 21 returns
        ///     from the current script (<c>Class247.java:7820</c>), 38 pops the integer stack
        ///     (<c>:7869</c>) and 39 pops the string stack (<c>:7871</c>). Every other sub-100 arm
        ///     indexes <c>is_265_[current]</c>, which is the operand array. The byte is stored
        ///     regardless and is padding. The obfuscated source spells the carve-outs as
        ///     <c>(type ^ 0xffffffff) > -101 &amp;&amp; type != 21 &amp;&amp; (type ^ 0xffffffff) != -39 &amp;&amp; type != 39</c>
        ///     at <c>Class22.java:64</c>, which is <c>&lt; 100</c> and not 21, 38 or 39. A reader that
        ///     misses one of them desynchronises the stream and never recovers.
        /// </remarks>
        public static readonly int[] NarrowOperandExceptions = { 21, 38, 39 };

        private byte[] _textOperand = Array.Empty<byte>();

        /// <summary>The stored opcode, 0..<see cref="MaxOpcode"/>.</summary>
        /// <remarks>
        ///     Read-only because it decides how the operand is stored, so changing it on an existing
        ///     instruction would leave an operand of the wrong width behind. Replace the instruction
        ///     instead.
        /// </remarks>
        public int Opcode { get; }

        /// <summary>How this instruction's operand is stored, which follows from the opcode.</summary>
        public ClientScriptOperandKind OperandKind => OperandKindOf(Opcode);

        /// <summary>
        ///     The numeric operand, for every opcode except <see cref="TextOpcode"/>.
        /// </summary>
        /// <remarks>
        ///     Holds both widths. A <see cref="ClientScriptOperandKind.Byte"/> operand is the raw
        ///     unsigned byte, 0..255, and <see cref="Encode"/> refuses anything wider rather than
        ///     masking it - a masked operand writes a file that decodes to a different script.
        /// </remarks>
        public int IntegerOperand { get; set; }

        /// <summary>
        ///     The string operand exactly as the file stores it, without the terminator.
        /// </summary>
        /// <remarks>
        ///     This is the stored state and <see cref="TextOperand"/> is a view over it, because the
        ///     cp1252 mapping loses information in both directions for five byte values. Assigning
        ///     here keeps whatever bytes are given; assigning the text re-encodes them.
        /// </remarks>
        public byte[] TextOperandBytes {
            get => (byte[]) _textOperand.Clone();
            set => _textOperand = value == null || value.Length == 0
                ? Array.Empty<byte>()
                : (byte[]) value.Clone();
        }

        /// <summary>The string operand as text.</summary>
        public string TextOperand {
            get => ClientScriptText.Decode(_textOperand);
            set => _textOperand = ClientScriptText.Encode(value);
        }

        /// <summary>Creates an instruction with the default operand for its opcode.</summary>
        /// <param name="opcode">The opcode, 0..<see cref="MaxOpcode"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">The opcode does not fit two bytes.</exception>
        public ClientScriptInstruction(int opcode) {
            if (opcode < 0 || opcode > MaxOpcode)
                throw new ArgumentOutOfRangeException(nameof(opcode), opcode,
                    "A CS2 opcode is stored as an unsigned short, so it cannot be outside 0.." + MaxOpcode + ".");

            Opcode = opcode;
        }

        /// <summary>
        ///     How an opcode's operand is stored.
        /// </summary>
        /// <remarks>
        ///     The order of the tests matters and mirrors <c>Class22.java:62-68</c>: opcode 3 is
        ///     checked first, so it takes a string despite being below the ceiling.
        /// </remarks>
        /// <param name="opcode">The opcode.</param>
        /// <returns>The operand width the opcode implies.</returns>
        public static ClientScriptOperandKind OperandKindOf(int opcode) {
            if (opcode == TextOpcode)
                return ClientScriptOperandKind.Text;

            if (opcode >= WideOperandCeiling)
                return ClientScriptOperandKind.Byte;

            foreach (int exception in NarrowOperandExceptions)
                if (opcode == exception)
                    return ClientScriptOperandKind.Byte;

            return ClientScriptOperandKind.Integer;
        }

        /// <summary>Reads one instruction, leaving the stream on the byte after its operand.</summary>
        /// <param name="stream">The script, positioned at the opcode.</param>
        /// <returns>The decoded instruction.</returns>
        public static ClientScriptInstruction Decode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var instruction = new ClientScriptInstruction(stream.ReadUnsignedShort());

            switch (instruction.OperandKind) {
                case ClientScriptOperandKind.Text:
                    instruction._textOperand = ReadTerminatedBytes(stream);
                    break;

                case ClientScriptOperandKind.Byte:
                    instruction.IntegerOperand = stream.ReadUnsignedByte();
                    break;

                default:
                    instruction.IntegerOperand = stream.ReadInt();
                    break;
            }

            return instruction;
        }

        /// <summary>Writes this instruction at the stream's current position.</summary>
        /// <param name="stream">The stream to append to.</param>
        /// <exception cref="InvalidOperationException">A byte-width operand does not fit a byte.</exception>
        public void Encode(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.WriteShort(Opcode);

            switch (OperandKind) {
                case ClientScriptOperandKind.Text:
                    stream.Write(_textOperand, 0, _textOperand.Length);
                    stream.WriteByte(0);
                    break;

                case ClientScriptOperandKind.Byte:
                    if (IntegerOperand < 0 || IntegerOperand > MaxByteOperand)
                        throw new InvalidOperationException(
                            "Opcode " + Opcode + " stores its operand in one unsigned byte, so " +
                            IntegerOperand + " cannot be written. Masking it would produce a file that " +
                            "decodes to a different instruction.");
                    stream.WriteByte((byte) IntegerOperand);
                    break;

                default:
                    stream.WriteInteger(IntegerOperand);
                    break;
            }
        }

        /// <summary>How many bytes this instruction occupies, opcode included.</summary>
        public int StoredLength {
            get {
                switch (OperandKind) {
                    case ClientScriptOperandKind.Text:
                        return 2 + _textOperand.Length + 1;
                    case ClientScriptOperandKind.Byte:
                        return 3;
                    default:
                        return 6;
                }
            }
        }

        /// <summary>Reads a NUL-terminated field and returns its bytes without the terminator.</summary>
        /// <remarks>
        ///     The bytes are taken verbatim rather than through
        ///     <see cref="JagStream.ReadJagexString"/> so nothing is lost before the encoder sees
        ///     them; the text view decodes them on demand.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the first byte of the field.</param>
        /// <returns>The field's bytes.</returns>
        /// <exception cref="System.IO.EndOfStreamException">The field has no terminator.</exception>
        internal static byte[] ReadTerminatedBytes(JagStream stream) {
            int start = stream.Position;

            int read;
            while ((read = stream.ReadByte()) > 0) {
                //Scanning for the terminator; the bytes are copied out below in one go.
            }

            if (read < 0)
                throw new System.IO.EndOfStreamException(
                    "A NUL-terminated field starting at " + start + " runs off the end of the script.");

            int afterTerminator = stream.Position;
            stream.Position = start;
            byte[] stored = stream.ReadBytes(afterTerminator - start - 1);
            stream.Position = afterTerminator;
            return stored;
        }
    }
}
