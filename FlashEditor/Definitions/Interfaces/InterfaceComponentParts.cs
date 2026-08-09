using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     One row of a component's slot table: a slot index, a twelve-bit value and two signed
    ///     bytes.
    /// </summary>
    /// <remarks>
    ///     The client scatters these into three parallel arrays of eleven
    ///     (<c>RSInterface.java:1181-1210</c>), which loses both the order the entries were written
    ///     in and any duplicate slot. Keeping them as a list in stream order is what lets the encoder
    ///     put the bytes back where they were.
    /// </remarks>
    public struct InterfaceSlotEntry {
        /// <summary>The largest slot the client's three parallel arrays can hold.</summary>
        public const int MaxSlot = 10;

        /// <summary>The twelve-bit value that means "none".</summary>
        public const int NoValue = 4095;

        /// <summary>Binds one slot table row.</summary>
        /// <param name="slot">The slot index, <c>(header &gt;&gt; 4) - 1</c>.</param>
        /// <param name="rawValue">The twelve-bit value exactly as stored.</param>
        /// <param name="first">The first signed byte.</param>
        /// <param name="second">The second signed byte.</param>
        public InterfaceSlotEntry(int slot, int rawValue, sbyte first, sbyte second) {
            if (rawValue < 0 || rawValue > NoValue)
                throw new ArgumentOutOfRangeException(nameof(rawValue), rawValue,
                    "A slot value is stored in twelve bits.");

            Slot = slot;
            RawValue = rawValue;
            First = first;
            Second = second;
        }

        /// <summary>The slot index the entry addresses.</summary>
        public int Slot { get; set; }

        /// <summary>
        ///     The value as stored, 0..4095.
        /// </summary>
        /// <remarks>
        ///     Raw rather than the decoded value. 4095 decodes to -1 and has exactly one encoding, so
        ///     nothing is ambiguous here - but the header byte packs this field's top four bits
        ///     alongside the slot index, and rebuilding it from a signed value is where that goes
        ///     wrong.
        /// </remarks>
        public int RawValue { get; set; }

        /// <summary>The value the client sees, with 4095 read as -1.</summary>
        public int Value => RawValue == NoValue ? -1 : RawValue;

        /// <summary>The first signed byte. A non-zero one sets the client's <c>aBoolean2222</c>.</summary>
        public sbyte First { get; set; }

        /// <summary>The second signed byte.</summary>
        public sbyte Second { get; set; }
    }

    /// <summary>
    ///     One element of a CS2 hook array.
    /// </summary>
    /// <remarks>
    ///     <b>The type byte is not recoverable from the element.</b> <c>loadCS2Bytecode</c>
    ///     (<c>RSInterface.java:398-427</c>) reads an int for type 0 and a string for type 1, and for
    ///     every other value reads <b>nothing at all</b>, leaving the element null. So 2..255 are two
    ///     hundred and fifty-four aliases of each other, and an encoder that wrote "null means type
    ///     2" would change the byte on any file that used a different one. The byte is therefore
    ///     stored, not derived.
    ///     <para>
    ///     No file in this cache uses one: the index's 47,538 hook elements are 46,033 ints and 1,505
    ///     strings and nothing else, so the alias is latent. A byte-identity sweep cannot defend it.
    ///     </para>
    /// </remarks>
    public struct InterfaceScriptOperand {
        /// <summary>Type byte for an integer operand.</summary>
        public const int IntegerType = 0;

        /// <summary>Type byte for a string operand.</summary>
        public const int StringType = 1;

        /// <summary>Binds one operand.</summary>
        /// <param name="typeByte">The type byte exactly as stored.</param>
        /// <param name="integer">The integer, when the type byte is 0.</param>
        /// <param name="text">The string, when the type byte is 1.</param>
        public InterfaceScriptOperand(int typeByte, int integer, InterfaceText? text) {
            if (typeByte < 0 || typeByte > 255)
                throw new ArgumentOutOfRangeException(nameof(typeByte), typeByte,
                    "An operand type is a single byte.");

            TypeByte = typeByte;
            Integer = integer;
            Text = text;
        }

        /// <summary>The type byte as stored.</summary>
        public int TypeByte { get; set; }

        /// <summary>The integer operand, meaningful only when <see cref="TypeByte"/> is 0.</summary>
        public int Integer { get; set; }

        /// <summary>The string operand, non-null only when <see cref="TypeByte"/> is 1.</summary>
        public InterfaceText? Text { get; set; }

        /// <summary>An integer operand.</summary>
        /// <param name="value">The integer.</param>
        /// <returns>The operand.</returns>
        public static InterfaceScriptOperand OfInteger(int value) {
            return new InterfaceScriptOperand(IntegerType, value, null);
        }

        /// <summary>A string operand.</summary>
        /// <param name="value">The string.</param>
        /// <returns>The operand.</returns>
        public static InterfaceScriptOperand OfString(InterfaceText value) {
            return new InterfaceScriptOperand(StringType, 0,
                value ?? throw new ArgumentNullException(nameof(value)));
        }

        /// <summary>Reads one operand.</summary>
        /// <param name="stream">The stream, positioned at the type byte.</param>
        /// <returns>The operand.</returns>
        public static InterfaceScriptOperand Read(JagStream stream) {
            int type = stream.ReadUnsignedByte();
            if (type == IntegerType)
                return new InterfaceScriptOperand(type, stream.ReadInt(), null);
            if (type == StringType)
                return new InterfaceScriptOperand(type, 0, InterfaceText.Read(stream));

            //Every other type byte reads no payload and leaves the element null in the client.
            //Never exercised by this cache; see the type remark.
            return new InterfaceScriptOperand(type, 0, null);
        }

        /// <summary>Writes one operand.</summary>
        /// <param name="stream">The stream to write to.</param>
        public readonly void Write(JagStream stream) {
            stream.WriteByte(TypeByte);
            if (TypeByte == IntegerType) {
                stream.WriteInteger(Integer);
                return;
            }
            if (TypeByte == StringType) {
                (Text ?? InterfaceText.EmptyText).Write(stream);
            }
        }
    }

    /// <summary>
    ///     One entry of the component parameter table.
    /// </summary>
    /// <remarks>
    ///     Read only when the version byte is non-negative (<c>RSInterface.java:1289-1307</c>), which
    ///     no file in this cache is. The key is the big-endian unsigned 24-bit reader,
    ///     <c>RSBuffer.method1186</c>.
    /// </remarks>
    public struct InterfaceParameter {
        /// <summary>Binds one parameter.</summary>
        /// <param name="key">The 24-bit key.</param>
        /// <param name="integer">The integer value, for an integer parameter.</param>
        /// <param name="text">The string value, or null for an integer parameter.</param>
        public InterfaceParameter(int key, int integer, InterfaceText? text) {
            if (key < 0 || key > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(key), key,
                    "A parameter key is stored in twenty-four bits.");

            Key = key;
            Integer = integer;
            Text = text;
        }

        /// <summary>The 24-bit key.</summary>
        public int Key { get; set; }

        /// <summary>The integer value, for an entry in the integer table.</summary>
        public int Integer { get; set; }

        /// <summary>The string value, for an entry in the string table.</summary>
        public InterfaceText? Text { get; set; }
    }
}
