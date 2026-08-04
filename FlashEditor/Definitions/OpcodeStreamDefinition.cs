using System;

namespace FlashEditor.Definitions {
    /// <summary>
    ///     The shared decode loop for every definition stored as a self-delimiting opcode stream:
    ///     an opcode byte, a payload whose width the opcode implies, repeated until a terminator.
    /// </summary>
    /// <remarks>
    ///     Subclasses supply only the per-opcode reader. The loop, the verbatim payload capture and
    ///     the unknown-opcode failure are here so that every codec gets the same three behaviours
    ///     without restating them, and so a codec added later inherits them rather than
    ///     rediscovering why they matter.
    /// </remarks>
    public abstract class OpcodeStreamDefinition {
        /// <summary>
        ///     The point at which a record is treated as corrupt rather than long.
        /// </summary>
        /// <remarks>
        ///     An opcode is a single byte, so a record cannot need more distinct opcodes than this;
        ///     a stream that keeps producing them is one that desynchronised and is now reading its
        ///     own payload as opcodes. Repetition is legal, so the bound is deliberately loose.
        /// </remarks>
        private const int MaxOpcodeOccurrences = 256;

        /// <summary>
        ///     Every opcode this definition was decoded from, in order, with the bytes each carried.
        /// </summary>
        public OpcodeStream Opcodes { get; private set; } = new OpcodeStream();

        /// <summary>
        ///     Reads the payload of one opcode, leaving the stream on the byte after it.
        /// </summary>
        /// <remarks>
        ///     The return value is the only signal the loop has that a payload width is known.
        ///     Returning false rather than consuming nothing is what turns an unrecognised opcode
        ///     into a reported failure instead of a silent desync.
        /// </remarks>
        /// <param name="stream">The stream, positioned at the payload.</param>
        /// <param name="opcode">The opcode just read.</param>
        /// <returns>True when the payload was consumed; false when the opcode is unknown.</returns>
        protected abstract bool DecodeOpcode(JagStream stream, int opcode);

        /// <summary>
        ///     Whether <paramref name="opcode"/> ends the record.
        /// </summary>
        /// <remarks>
        ///     Zero terminates every format here and -1 is end of stream. Override only to add a
        ///     format's own sentinel; the NPC decoder stops on 255 as well.
        /// </remarks>
        /// <param name="opcode">The byte just read where an opcode was expected.</param>
        /// <returns>True to stop reading.</returns>
        protected virtual bool IsTerminator(int opcode) => opcode <= 0;

        /// <summary>
        ///     Drives the opcode loop, recording each occurrence with the exact bytes it consumed.
        /// </summary>
        /// <remarks>
        ///     The payload has no length prefix, so its extent is whatever the per-opcode reader
        ///     consumed. Rewinding over that span and re-reading it is the only way to keep the
        ///     bytes verbatim, and verbatim bytes are all that is left of an occurrence a later one
        ///     superseded.
        /// </remarks>
        /// <param name="stream">The record to read, positioned at its first opcode. Null reads nothing.</param>
        /// <exception cref="InvalidOperationException">
        ///     The stream carried an opcode with no known payload width, or produced more
        ///     occurrences than a record can hold.
        /// </exception>
        protected void DecodeOpcodeStream(JagStream stream) {
            if (stream == null)
                return;

            int seen = 0;

            while (true) {
                int opcode = stream.ReadByte();
                if (IsTerminator(opcode))
                    break;

                int payloadStart = stream.Position;

                /* Reporting where the parse stopped is the only honest outcome. The stream carries
                   no length prefix, so an opcode of unknown width cannot be skipped: the next byte
                   read is then payload mistaken for an opcode, and every field after it is garbage
                   while the decode still appears to succeed. The 637 client consumes nothing and
                   carries on, which is the defect rather than the behaviour to copy. */
                if (!DecodeOpcode(stream, opcode))
                    throw new InvalidOperationException(
                        GetType().Name + " opcode " + opcode + " at offset " + (payloadStart - 1) +
                        " has no known payload size, so the remainder of the stream cannot be parsed");

                int payloadEnd = stream.Position;
                stream.Position = payloadStart;
                Opcodes.Add(opcode, stream.ReadBytes(payloadEnd - payloadStart));

                if (++seen > MaxOpcodeOccurrences)
                    throw new InvalidOperationException(
                        "Opcode overflow while decoding " + GetType().Name);
            }
        }

        /// <summary>
        ///     Gives this instance its own copy of the recorded stream.
        /// </summary>
        /// <remarks>
        ///     <see cref="object.MemberwiseClone"/> copies the reference, so a clone would share the
        ///     original's recorded stream and dropping an opcode on one would drop it on both. Every
        ///     <c>Clone</c> below this type has to call it.
        /// </remarks>
        protected void DetachOpcodeStream() => Opcodes = Opcodes.Clone();
    }
}
