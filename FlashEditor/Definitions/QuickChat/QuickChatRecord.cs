using System;
using System.Collections.Generic;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     The pieces the menu and message records share: the stored-string field, and the plumbing
    ///     for building one opcode's payload.
    /// </summary>
    /// <remarks>
    ///     Shared rather than duplicated because the two formats are two readers over one bank
    ///     (<see cref="QuickChatBank"/>) and their opcode 1 is the same field. Keeping one copy is
    ///     what stops the two drifting the way an edit to only one of them would.
    /// </remarks>
    internal static class QuickChatRecord {
        /// <summary>
        ///     Reads a NUL-terminated cp1252 string and returns its bytes without the terminator.
        /// </summary>
        /// <remarks>
        ///     The bytes rather than the text, because the decode is lossy - see
        ///     <see cref="QuickChatText"/>. The client's reader has no length prefix either
        ///     (RSBuffer.java:878-894), so the extent is only known once the terminator is found.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the first byte of the string.</param>
        /// <returns>The stored bytes.</returns>
        /// <exception cref="System.IO.EndOfStreamException">The string has no terminator.</exception>
        public static byte[] ReadStoredString(JagStream stream) {
            int start = stream.Position;
            while (stream.ReadUnsignedByte() != 0) {
            }

            int end = stream.Position - 1;
            stream.Position = start;
            byte[] stored = stream.ReadBytes(end - start);
            stream.Position = end + 1;
            return stored;
        }

        /// <summary>Writes stored string bytes back with their terminator.</summary>
        /// <param name="buffer">The payload buffer.</param>
        /// <param name="stored">The stored bytes, without a terminator.</param>
        public static void WriteStoredString(JagStream buffer, byte[] stored) {
            if (stored.Length > 0)
                buffer.Write(stored, 0, stored.Length);
            buffer.WriteByte(0);
        }

        /// <summary>Builds one opcode's payload into its own buffer.</summary>
        /// <param name="opcode">The opcode the payload belongs to.</param>
        /// <param name="write">Writes the payload.</param>
        /// <returns>The opcode paired with its bytes.</returns>
        public static KeyValuePair<int, byte[]> Payload(int opcode, Action<JagStream> write) {
            JagStream buffer = new JagStream();
            write(buffer);
            return new KeyValuePair<int, byte[]>(opcode, buffer.Flip().ToArray());
        }

        /// <summary>
        ///     Adds or drops a bare flag opcode.
        /// </summary>
        /// <remarks>
        ///     A flag with no payload exists only in the recorded stream, so clearing it has to
        ///     remove the opcode. Clearing a field instead would leave the opcode for the replay to
        ///     put back: the editor would show the change, the save would report success, and the
        ///     flag would still be set in the cache.
        /// </remarks>
        /// <param name="opcodes">The record's recorded opcode stream.</param>
        /// <param name="opcode">The flag opcode.</param>
        /// <param name="set">Whether the flag should be present.</param>
        public static void SetFlag(OpcodeStream opcodes, int opcode, bool set) {
            if (set == opcodes.Has(opcode))
                return;

            if (set)
                opcodes.Add(opcode, Array.Empty<byte>());
            else
                opcodes.Remove(opcode);
        }

        /// <summary>
        ///     Refuses a list longer than its one-byte count can express.
        /// </summary>
        /// <param name="count">The list length.</param>
        /// <param name="opcode">The opcode being written, so a failure names it.</param>
        public static void RequireByteCount(int count, int opcode) {
            if (count > byte.MaxValue)
                throw new InvalidOperationException(
                    "Opcode " + opcode + " stores its length in one byte, so it cannot hold " +
                    count + " entries.");
        }

        /// <summary>
        ///     Refuses an id that does not fit the sixteen bits the format stores it in.
        /// </summary>
        /// <remarks>
        ///     Sixteen bits, not fifteen. It is tempting to reject anything with the second-bank bit
        ///     set, but only a record <i>in</i> index 25 is barred from carrying it - the client ORs
        ///     that bit onto every id such a record stores (Node_Sub46_Sub1.method1531). A record in
        ///     index 24 may legitimately name an id of 0x8000 or more, which the client resolves
        ///     against index 25 (Class212.java:65-66). The codec does not know which index it is
        ///     serving, so the narrower rule belongs to whatever writes a record back, not here.
        /// </remarks>
        /// <param name="id">The id to be stored.</param>
        /// <param name="what">What the id names, for the failure message.</param>
        public static void RequireStoredId(int id, string what) {
            if (id < 0 || id > 0xFFFF)
                throw new InvalidOperationException(
                    "Quick-chat " + what + " " + id + " does not fit the stored 16 bits.");
        }
    }
}
