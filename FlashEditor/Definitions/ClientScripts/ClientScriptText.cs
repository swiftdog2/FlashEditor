using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.ClientScripts {
    /// <summary>
    ///     Converts a CS2 string field between the bytes the cache stores and readable text.
    /// </summary>
    /// <remarks>
    ///     Cache strings are in the client's modified cp1252, so
    ///     <see cref="JagStream.ReadJagexString"/> is the reader and
    ///     <see cref="JagStream.WriteJagexString"/> the writer. The pairing is not a bijection: five
    ///     byte values in the 0x80-0x9F band are unassigned, decode to <c>'?'</c> and re-encode as
    ///     0x3F, and a NUL cannot survive a round trip at all because it terminates the field. That
    ///     is why every string field on a script keeps the stored bytes and treats the text as a
    ///     view over them - decoding to text alone would silently rewrite any script carrying one of
    ///     those bytes, and neither supported cache holds one, so no sweep would notice.
    /// </remarks>
    internal static class ClientScriptText {
        /// <summary>Reads stored bytes as text.</summary>
        /// <param name="stored">The field's bytes, without the terminator.</param>
        /// <returns>The decoded text, empty when the field is empty.</returns>
        internal static string Decode(byte[]? stored) {
            if (stored == null || stored.Length == 0)
                return string.Empty;

            //ReadJagexString consumes up to a terminator, so one is appended rather than the
            //decode being reimplemented here against a different table.
            byte[] terminated = new byte[stored.Length + 1];
            Array.Copy(stored, terminated, stored.Length);
            return new JagStream(terminated).ReadJagexString();
        }

        /// <summary>Writes text as the bytes that would be stored for it.</summary>
        /// <param name="text">The text, or <c>null</c> for an empty field.</param>
        /// <returns>The stored bytes, without the terminator.</returns>
        internal static byte[] Encode(string? text) {
            if (string.IsNullOrEmpty(text))
                return Array.Empty<byte>();

            var buffer = new JagStream();
            buffer.WriteJagexString(text);

            //WriteJagexString appends the terminator; the caller owns where that goes, because the
            //name field and an operand sit at different places in the record.
            byte[] written = buffer.Flip().ToArray();
            byte[] stored = new byte[written.Length - 1];
            Array.Copy(written, stored, stored.Length);
            return stored;
        }
    }
}
