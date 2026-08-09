using System;
using FlashEditor.IO;

namespace FlashEditor.Definitions.QuickChat {
    /// <summary>
    ///     Converts between a quick-chat record's stored cp1252 bytes and the text they display as.
    /// </summary>
    /// <remarks>
    ///     <b>The bytes are the stored state and the string is derived from them.</b> That direction
    ///     matters because the conversion is not reversible: five byte values in the 0x80-0x9F band
    ///     are unassigned in the client's modified cp1252 - 0x81, 0x8D, 0x8F, 0x90 and 0x9D - and
    ///     both <c>RSBuffer.readString</c> (RSBuffer.java:878-894) and
    ///     <see cref="JagStream.ReadJagexString"/> map all five to a question mark. Decoding to a
    ///     string and re-encoding would turn any of them into 0x3F.
    ///     <para>
    ///     No string in either supported cache contains one - or any byte above 0x7F at all - so a
    ///     byte-identity sweep over both indexes would pass just as happily on a codec that kept only
    ///     the string. Keeping the bytes is pinned by a synthetic test instead, which is the only
    ///     thing that can defend it.
    ///     </para>
    /// </remarks>
    public static class QuickChatText {
        /// <summary>
        ///     Reads the text a stored string decodes to.
        /// </summary>
        /// <param name="stored">The stored bytes, without their terminator.</param>
        /// <returns>The displayed text.</returns>
        public static string ToText(byte[] stored) {
            if (stored == null)
                throw new ArgumentNullException(nameof(stored));

            //ReadJagexString scans to a zero byte, so the terminator the record's opcode payload
            //carries has to be put back before it can read anything.
            JagStream buffer = new JagStream(stored.Length + 1);
            buffer.Write(stored, 0, stored.Length);
            buffer.WriteByte(0);
            return buffer.Flip().ReadJagexString();
        }

        /// <summary>
        ///     Encodes replacement text to the bytes a record stores for it.
        /// </summary>
        /// <remarks>
        ///     Only for text a user actually typed. Re-encoding text that was merely read back is
        ///     what loses the five unassigned bytes, so a record that has not been edited must write
        ///     its stored bytes rather than passing its own displayed string back through here.
        /// </remarks>
        /// <param name="text">The text to store.</param>
        /// <returns>The stored bytes, without a terminator.</returns>
        public static byte[] ToBytes(string text) {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            JagStream buffer = new JagStream();
            buffer.WriteJagexString(text);
            byte[] withTerminator = buffer.Flip().ToArray();

            //WriteJagexString appends the terminator; the record's opcode writer adds its own, so
            //the stored form here stops short of it.
            byte[] stored = new byte[withTerminator.Length - 1];
            Array.Copy(withTerminator, stored, stored.Length);
            return stored;
        }
    }
}
