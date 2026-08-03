using System;

namespace FlashEditor.Definitions.Interfaces {
    /// <summary>
    ///     A NUL-terminated cache string that remembers the bytes it was read from.
    /// </summary>
    /// <remarks>
    ///     The cache's modified cp1252 is <b>not</b> injective. Five of the thirty-two slots in the
    ///     0x80-0x9F band are unassigned - 0x81, 0x8D, 0x8F, 0x90 and 0x9D -
    ///     <see cref="JagStream.ReadJagexString"/> decodes each of them to <c>'?'</c>, and
    ///     <see cref="JagStream.WriteJagexString"/> writes <c>'?'</c> back as 0x3F. A component that
    ///     carried one of those bytes and was re-encoded from the decoded text alone would come back
    ///     a different file, and the archive CRC with it.
    ///     <para>
    ///     No string in index 3 carries one today: the only bytes above 0x7F anywhere in the index
    ///     are 0xC4, 0xD6, 0xDC and 0xDF, two occurrences each, all of which round trip exactly. That
    ///     is precisely the shape of hazard <c>CLAUDE.md</c> warns about - a normalisation whose
    ///     trigger is absent from the cache is invisible to a byte-identity sweep - so the raw bytes
    ///     are kept rather than trusted to be recoverable, and
    ///     <c>InterfaceComponentCodecTests</c> pins it synthetically.
    ///     </para>
    ///     <para>
    ///     Mutable by design: the editor writes <see cref="Text"/>, which re-encodes the raw bytes.
    ///     An unedited string is written back byte for byte.
    ///     </para>
    /// </remarks>
    public sealed class InterfaceText {
        /// <summary>No characters, which the format stores as the terminator alone.</summary>
        private static readonly byte[] NoCharacters = Array.Empty<byte>();

        private byte[] raw;
        private string? text;

        private InterfaceText(byte[] rawBytes) {
            raw = rawBytes;
        }

        /// <summary>An empty string, which the format stores as the terminator alone.</summary>
        public static InterfaceText EmptyText => new InterfaceText(NoCharacters);

        /// <summary>Whether the stored string holds no characters.</summary>
        public bool IsEmpty => raw.Length == 0;

        /// <summary>How many bytes this occupies on the wire, terminator included.</summary>
        public int EncodedLength => raw.Length + 1;

        /// <summary>
        ///     The decoded text. Assigning re-encodes the stored bytes.
        /// </summary>
        /// <remarks>
        ///     Decoded lazily, because a full index-3 sweep reads a quarter of a million strings and
        ///     shows almost none of them.
        /// </remarks>
        public string Text {
            get => text ??= Decode(raw);
            set {
                raw = Encode(value ?? string.Empty);
                //Cleared rather than assigned, so the getter always answers what the bytes now say.
                //The two differ whenever the assignment was lossy, and a caller that read back the
                //value it just wrote would otherwise never find out.
                text = null;
            }
        }

        /// <summary>
        ///     The bytes as stored, without the terminator.
        /// </summary>
        /// <returns>A copy, so a caller cannot mutate the capture.</returns>
        public byte[] RawBytes() => (byte[]) raw.Clone();

        /// <summary>Builds a string from text, for a component the editor creates rather than reads.</summary>
        /// <param name="value">The text.</param>
        /// <returns>The string.</returns>
        public static InterfaceText FromText(string value) {
            return new InterfaceText(Encode(value ?? string.Empty));
        }

        /// <summary>Builds a string from raw bytes, for a test that needs an exact byte sequence.</summary>
        /// <param name="rawBytes">The bytes, without the terminator.</param>
        /// <returns>The string.</returns>
        public static InterfaceText FromRawBytes(byte[] rawBytes) {
            if (rawBytes == null)
                throw new ArgumentNullException(nameof(rawBytes));
            return new InterfaceText((byte[]) rawBytes.Clone());
        }

        /// <summary>
        ///     Reads a NUL-terminated string, keeping the bytes it spanned.
        /// </summary>
        /// <param name="stream">The stream, positioned at the first character.</param>
        /// <returns>The string.</returns>
        public static InterfaceText Read(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int start = stream.Position;
            int b;
            while ((b = stream.ReadByte()) != 0) {
                if (b < 0)
                    throw new System.IO.EndOfStreamException(
                        "An interface string ran off the end of its component without a terminator.");
            }

            int length = stream.Position - start - 1;
            byte[] captured = new byte[length];
            for (int i = 0; i < length; i++)
                captured[i] = stream.Get(start + i);
            return new InterfaceText(captured);
        }

        /// <summary>
        ///     Reads the version-prefixed string form, <c>RSBuffer.method1223</c>.
        /// </summary>
        /// <remarks>
        ///     One leading byte that must be zero (<c>RSBuffer.java:440-447</c> throws
        ///     "Bad version number in gjstr2" otherwise), then the string. Only the param table uses
        ///     it, and that table is gated on a version byte no file in this cache sets - see
        ///     <see cref="InterfaceComponentDefinition"/>.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the version byte.</param>
        /// <returns>The string.</returns>
        public static InterfaceText ReadVersioned(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            int version = stream.ReadUnsignedByte();
            if (version != 0)
                throw new InvalidOperationException(
                    "Bad version number in gjstr2: expected 0 but read " + version + ".");
            return Read(stream);
        }

        /// <summary>Writes the stored bytes and the terminator.</summary>
        /// <param name="stream">The stream to write to.</param>
        public void Write(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.Write(raw, 0, raw.Length);
            stream.WriteByte(0);
        }

        /// <summary>Writes the version byte and then the string.</summary>
        /// <param name="stream">The stream to write to.</param>
        public void WriteVersioned(JagStream stream) {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.WriteByte(0);
            Write(stream);
        }

        /// <summary>The decoded text, so a debugger and a list column show something readable.</summary>
        /// <returns>The text.</returns>
        public override string ToString() => Text;

        private static string Decode(byte[] rawBytes) {
            //Routed through JagStream rather than reimplemented: the cp1252 remap table lives there
            //and a second copy of it would be free to drift.
            byte[] terminated = new byte[rawBytes.Length + 1];
            Array.Copy(rawBytes, terminated, rawBytes.Length);
            return new JagStream(terminated).ReadJagexString();
        }

        private static byte[] Encode(string value) {
            var stream = new JagStream();
            stream.WriteJagexString(value);
            byte[] written = stream.Flip().ToArray();
            byte[] withoutTerminator = new byte[written.Length - 1];
            Array.Copy(written, withoutTerminator, withoutTerminator.Length);
            return withoutTerminator;
        }
    }
}
