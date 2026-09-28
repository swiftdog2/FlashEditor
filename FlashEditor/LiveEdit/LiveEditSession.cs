using System;
using System.Collections.Concurrent;
using FlashEditor.Cache;
using FlashEditor.Map;

using MapRegion = FlashEditor.Cache.Region.Region;

namespace FlashEditor.LiveEdit {
    /// <summary>
    ///     The one live-preview pipeline the editor holds for its lifetime: what the Map tab and
    ///     the Config tab mark goes through one coalescer to one <see cref="LiveEditLink"/>.
    /// </summary>
    /// <remarks>
    ///     Nothing is marked while the link is disabled, so an editor with live preview off does
    ///     no encoding under the map store's lock. Nothing here stages into <see cref="RSCache"/>.
    /// </remarks>
    public sealed class LiveEditSession : IDisposable {
        /// <summary>The server's default <c>editor_link_port</c>, and the <c>liveEditPort</c> setting's default.</summary>
        public const int DefaultPort = 43600;

        private readonly LiveEditCoalescer coalescer;

        //Captured on the thread that committed the definition, because the flush runs on a timer
        //thread and RSCache's staging overlay is written from the UI thread without a lock.
        private readonly ConcurrentDictionary<(LiveEditKind Kind, int Id), byte[]> committed =
            new ConcurrentDictionary<(LiveEditKind, int), byte[]>();

        /// <summary>Creates a session whose link starts disabled.</summary>
        /// <param name="port">The server's <c>editor_link_port</c>.</param>
        /// <param name="runExclusive">
        ///     Runs its argument holding the map square store's exclusive section; squares are
        ///     encoded inside it on the flush thread.
        /// </param>
        public LiveEditSession(int port, Action<Action> runExclusive) {
            Link = new LiveEditLink(port);
            coalescer = new LiveEditCoalescer(runExclusive, ReadCommitted, batch => Link.Send(batch));
        }

        /// <summary>The connection, for its switch and its status.</summary>
        public LiveEditLink Link { get; }

        /// <summary>
        ///     Marks every square <paramref name="edit"/> touches: each of a
        ///     <see cref="CompositeEdit"/>'s targets, else the edit's own target.
        /// </summary>
        public void MarkEdit(IMapEdit edit) {
            ArgumentNullException.ThrowIfNull(edit);
            if (!Link.Enabled) return;

            if (edit is CompositeEdit composite) {
                foreach (MapRegion square in composite.Targets)
                    coalescer.Mark(square);
            }
            else {
                coalescer.Mark(edit.Target);
            }
        }

        /// <summary>
        ///     Marks a staged index-2 file when it is a floor underlay or overlay; any other file
        ///     is ignored. Reads the staged bytes now, so call it after the write.
        /// </summary>
        public void MarkStaged(RSCache cache, int indexId, int groupId, int fileId) {
            ArgumentNullException.ThrowIfNull(cache);
            if (!Link.Enabled || indexId != RSConstants.CONFIG) return;

            LiveEditKind kind;
            if (groupId == RSConstants.FLOOR_UNDERLAY_GROUP) kind = LiveEditKind.FloorUnderlay;
            else if (groupId == RSConstants.FLOOR_OVERLAY_GROUP) kind = LiveEditKind.FloorOverlay;
            else return;

            committed[(kind, fileId)] = cache.ReadFileBytes(indexId, groupId, fileId);
            coalescer.MarkDefinition(kind, fileId);
        }

        private byte[] ReadCommitted(LiveEditKind kind, int id) =>
            committed.TryGetValue((kind, id), out byte[]? bytes)
                ? bytes
                : throw new InvalidOperationException("No staged bytes were captured for this definition.");

        /// <summary>Stops the coalescer, then the link; anything not yet sent is dropped.</summary>
        public void Dispose() {
            coalescer.Dispose();
            Link.Dispose();
        }
    }
}
