using System;
using System.Buffers.Binary;

namespace FlashEditor.LiveEdit {
    /// <summary>
    ///     Encodes and decodes leg-A frames, editor to server, exactly as
    ///     <c>reference/live-edit-protocol.md</c> specifies them. No I/O.
    /// </summary>
    /// <remarks>
    ///     The server's Java codec reads the same bytes, so this layout is shared with another
    ///     codebase and changes only together with the document and its <see cref="Version"/>.
    ///     <see cref="Decode"/> exists so tests and tools can read a frame back; the editor itself
    ///     only encodes.
    /// </remarks>
    public static class LiveEditFrameCodec {
        /// <summary><c>"FL"</c>. A frame not starting with it closes the server connection unacknowledged.</summary>
        public const ushort Magic = 0x464C;

        /// <summary>The protocol version. A server that does not speak it closes the connection unacknowledged.</summary>
        public const byte Version = 1;

        /// <summary>Magic, version, kind, id and length: the bytes before the payload.</summary>
        public const int HeaderLength = 10;

        /// <summary>
        ///     The largest payload the server accepts, and so the largest file that can be pushed,
        ///     since a frame always carries a whole file. A frame over it is rejected and the
        ///     connection closed, because the stream cannot be resynchronised after a length nobody
        ///     will read.
        /// </summary>
        public const int MaxPayloadLength = 262144;

        /// <summary>
        ///     Encodes one frame.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     The kind is not one leg A carries (0 to 5 and 7), the id does not fit 16 bits, the
        ///     payload is over <see cref="MaxPayloadLength"/>, or a batch end carries an id or a
        ///     payload. The server would reject any of them, and an oversize one would cost the
        ///     connection.
        /// </exception>
        public static byte[] Encode(LiveEditFrame frame) {
            ArgumentNullException.ThrowIfNull(frame);
            if (!IsLegAKind((byte) frame.Kind))
                throw new ArgumentException($"Kind {frame.Kind} is not carried on leg A.", nameof(frame));
            if (frame.Id < 0 || frame.Id > ushort.MaxValue)
                throw new ArgumentException($"Id {frame.Id} does not fit the 16-bit id field.", nameof(frame));
            if (frame.Payload.Length > MaxPayloadLength)
                throw new ArgumentException(
                    $"Payload of {frame.Payload.Length} bytes is over the {MaxPayloadLength}-byte limit.", nameof(frame));
            if (IsMalformedBatchEnd(frame.Kind, frame.Id, frame.Payload.Length))
                throw new ArgumentException("A batch end carries id 0 and no payload.", nameof(frame));

            byte[] bytes = new byte[HeaderLength + frame.Payload.Length];
            Span<byte> header = bytes;
            BinaryPrimitives.WriteUInt16BigEndian(header, Magic);
            header[2] = Version;
            header[3] = (byte) frame.Kind;
            BinaryPrimitives.WriteUInt16BigEndian(header[4..], (ushort) frame.Id);
            BinaryPrimitives.WriteUInt32BigEndian(header[6..], (uint) frame.Payload.Length);
            frame.Payload.CopyTo(bytes, HeaderLength);
            return bytes;
        }

        /// <summary>
        ///     Decodes exactly one frame occupying all of <paramref name="bytes"/>.
        /// </summary>
        /// <exception cref="LiveEditProtocolException">
        ///     Bad magic, version or kind, a length over <see cref="MaxPayloadLength"/>, a batch end
        ///     with an id or a payload, or a buffer that is not exactly one frame long.
        /// </exception>
        public static LiveEditFrame Decode(ReadOnlySpan<byte> bytes) {
            if (bytes.Length < HeaderLength)
                throw new LiveEditProtocolException($"A frame needs {HeaderLength} header bytes; got {bytes.Length}.");

            ushort magic = BinaryPrimitives.ReadUInt16BigEndian(bytes);
            if (magic != Magic)
                throw new LiveEditProtocolException($"Bad magic 0x{magic:X4}; expected 0x{Magic:X4}.");
            if (bytes[2] != Version)
                throw new LiveEditProtocolException($"Unsupported version {bytes[2]}; expected {Version}.");
            var kind = (LiveEditKind) bytes[3];
            if (!IsLegAKind(bytes[3]))
                throw new LiveEditProtocolException($"Kind {bytes[3]} is not carried on leg A.");

            int id = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes[6..]);
            if (length > MaxPayloadLength)
                throw new LiveEditProtocolException($"Length {length} is over the {MaxPayloadLength}-byte limit.");
            if (bytes.Length != HeaderLength + length)
                throw new LiveEditProtocolException(
                    $"Frame declares {length} payload bytes but carries {bytes.Length - HeaderLength}.");
            if (IsMalformedBatchEnd(kind, id, (int) length))
                throw new LiveEditProtocolException("A batch end carries id 0 and no payload.");

            return new LiveEditFrame(kind, id, bytes[HeaderLength..].ToArray());
        }

        private static bool IsLegAKind(byte kind) =>
            kind <= (byte) LiveEditKind.FloorOverlay || kind == (byte) LiveEditKind.BatchEnd;

        private static bool IsMalformedBatchEnd(LiveEditKind kind, int id, int length) =>
            kind == LiveEditKind.BatchEnd && (id != 0 || length != 0);
    }

    /// <summary>
    ///     A byte sequence that is not a valid leg-A frame.
    /// </summary>
    public sealed class LiveEditProtocolException : Exception {
        /// <summary>Creates the exception with the reason the frame was refused.</summary>
        public LiveEditProtocolException(string message) : base(message) { }
    }
}