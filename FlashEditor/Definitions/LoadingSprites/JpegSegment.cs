using System;

namespace FlashEditor.Definitions.LoadingSprites {
    /// <summary>
    ///     One marker segment of a JPEG file, held so it can be written back unchanged.
    /// </summary>
    /// <remarks>
    ///     The payload excludes the two-byte length field, which is recomputed on the way out. That
    ///     is safe only because <see cref="JagexJpeg.Decode"/> refuses a segment whose declared
    ///     length disagrees with the bytes that follow it, so a file that survives the parse is one
    ///     whose lengths are already consistent - and <see cref="JagexJpeg.ToBytes"/> is compared
    ///     against the stored bytes by the sweep rather than trusted.
    /// </remarks>
    public sealed class JpegSegment {
        /// <summary>The marker byte, the one after the <c>0xFF</c>.</summary>
        public byte Marker { get; }

        /// <summary>
        ///     Whether this marker carries a length field and a payload.
        /// </summary>
        /// <remarks>
        ///     <c>SOI</c>, <c>EOI</c>, <c>TEM</c> and the eight restart markers are two bytes and
        ///     nothing else. Writing a length for one of them would corrupt the file, and reading
        ///     one would consume the following segment's header as a length.
        /// </remarks>
        public bool HasPayload { get; }

        /// <summary>The segment body, excluding the marker and the length field.</summary>
        public byte[] Payload { get; }

        /// <summary>Binds a marker to its body.</summary>
        /// <param name="marker">The marker byte.</param>
        /// <param name="payload">The body, or <c>null</c> for a standalone marker.</param>
        public JpegSegment(byte marker, byte[] payload) {
            Marker = marker;
            HasPayload = payload != null;
            Payload = payload ?? Array.Empty<byte>();
        }

        /// <summary>How many bytes this segment occupies in the file.</summary>
        public int StoredLength => HasPayload ? 4 + Payload.Length : 2;

        /// <summary>Whether a marker is one of the eight restart markers, <c>D0</c>..<c>D7</c>.</summary>
        /// <param name="marker">The marker byte.</param>
        /// <returns>Whether it is a restart marker.</returns>
        public static bool IsRestart(byte marker) => marker >= 0xD0 && marker <= 0xD7;

        /// <summary>Whether a marker is standalone, so carries no length field.</summary>
        /// <param name="marker">The marker byte.</param>
        /// <returns>Whether the marker is two bytes on its own.</returns>
        public static bool IsStandalone(byte marker) {
            return marker == JagexJpeg.MarkerSoi || marker == JagexJpeg.MarkerEoi
                || marker == 0x01 || IsRestart(marker);
        }
    }
}
