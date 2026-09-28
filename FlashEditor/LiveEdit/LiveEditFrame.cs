using System;

namespace FlashEditor.LiveEdit {
    /// <summary>
    ///     One leg-A frame: a full replacement of one file for the server session, never a delta.
    /// </summary>
    /// <param name="Kind">Which file family the payload belongs to.</param>
    /// <param name="Id">The square id for a map kind, the definition's file id for a floor kind.</param>
    /// <param name="Payload">
    ///     The decompressed file bytes, as <c>RegionCodec</c> returns them for a map kind and as
    ///     the index-2 file stores them for a floor kind.
    /// </param>
    public sealed record LiveEditFrame(LiveEditKind Kind, int Id, byte[] Payload) {
        /// <summary>The payload, never null.</summary>
        public byte[] Payload { get; init; } = Payload ?? throw new ArgumentNullException(nameof(Payload));

        /// <summary>The one valid <see cref="LiveEditKind.BatchEnd"/> frame.</summary>
        public static LiveEditFrame BatchEnd { get; } = new LiveEditFrame(LiveEditKind.BatchEnd, 0, Array.Empty<byte>());
    }
}