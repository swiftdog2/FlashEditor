namespace FlashEditor.Definitions.Config {
    /// <summary>
    ///     The single-byte character decode two index 2 families store a type letter as.
    /// </summary>
    /// <remarks>
    ///     Groups 11 and 19 both store their type as one raw byte read with <c>readSignedByte</c> and
    ///     passed through <c>Class64_Sub7.method576</c> (Class64_Sub7.java:9-28), which is the same
    ///     modified cp1252 remap the string reader applies: bytes 0x80-0x9F name characters nothing
    ///     like the Latin-1 code points of the same value, and the five unassigned slots fall back to
    ///     '?'.
    ///     <para>
    ///     The <b>byte</b> is the model and the character is a view of it. That is not pedantry: the
    ///     remap is not injective once '?' is involved, so a character round-tripped back to a byte
    ///     is not always the byte that was stored, and one record in each of these two groups stores
    ///     0x80.
    ///     </para>
    /// </remarks>
    public static class ConfigText {
        /// <summary>The character a stored type byte names.</summary>
        /// <remarks>
        ///     Routed through <see cref="JagStream.ReadJagexString"/> so the remap table is stated
        ///     once in this solution rather than copied. The client throws outright for a stored 0;
        ///     this answers NUL instead, and the byte is kept either way so the record still
        ///     re-encodes.
        /// </remarks>
        /// <param name="raw">The stored byte, 0..255.</param>
        /// <returns>The character it names, or NUL when the byte is 0.</returns>
        public static char ToCharacter(int raw) {
            if ((raw & 0xFF) == 0)
                return '\0';

            string decoded = new JagStream(new byte[] { (byte) raw, 0 }).ReadJagexString();
            return decoded.Length == 0 ? '\0' : decoded[0];
        }

        /// <summary>
        ///     Reads a <c>gjstr2</c>: a leading version byte that has to be 0, then the string.
        /// </summary>
        /// <remarks>
        ///     <c>RSBuffer.method1223</c> (:440-462) throws <c>IllegalStateException</c> on any other
        ///     version byte, so 0 is the only legal encoding and the byte carries no information to
        ///     preserve. Refusing here rather than skipping keeps that guarantee, which is what lets
        ///     <see cref="WriteVersionedString"/> write a literal 0 back and still be byte-exact.
        /// </remarks>
        /// <param name="stream">The definition file, positioned at the version byte.</param>
        /// <returns>The string.</returns>
        public static string ReadVersionedString(JagStream stream) {
            int version = stream.ReadUnsignedByte();
            if (version != 0)
                throw new System.IO.InvalidDataException(
                    "Bad version number " + version + " in gjstr2; the client accepts only 0.");

            return stream.ReadJagexString();
        }

        /// <summary>Writes a <c>gjstr2</c>, version byte included.</summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="value">The string, treated as empty when null.</param>
        public static void WriteVersionedString(JagStream stream, string value) {
            stream.WriteByte(0);
            stream.WriteJagexString(value ?? "");
        }
    }
}
