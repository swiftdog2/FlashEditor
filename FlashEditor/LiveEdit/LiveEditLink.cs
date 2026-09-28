using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace FlashEditor.LiveEdit {
    /// <summary>Where the link stands, as the Map tab shows it.</summary>
    public enum LiveEditLinkStatus {
        /// <summary>Live preview is off; batches are dropped unsent.</summary>
        Disabled,

        /// <summary>On, with no open connection: none attempted yet, or the last attempt failed.</summary>
        Disconnected,

        /// <summary>On, and the last batch reached the server.</summary>
        Connected
    }

    /// <summary>
    ///     Delivers leg-A frames to the HydraScape server's editor listener on
    ///     <c>127.0.0.1</c>, connecting on demand, on its own worker, and failing quietly.
    /// </summary>
    /// <remarks>
    ///     Nothing here may stall or interrupt editing, so no member blocks on the network and none
    ///     throws because of it: a failure becomes <see cref="LastError"/> and
    ///     <see cref="LiveEditLinkStatus.Disconnected"/>, and the next <see cref="Send"/> connects
    ///     again. Every frame that did not reach the server is held, the latest per kind and id,
    ///     and goes ahead of the next batch; without that a square edited while the server was
    ///     down stays stale in game until it happens to be edited again. A frame the server
    ///     rejected is not held, because it would be rejected again.
    ///     Only the loopback address is ever dialled; the port is the only thing configurable.
    ///     The link never touches the cache.
    /// </remarks>
    public sealed class LiveEditLink : IDisposable {
        /// <summary>How long a connection attempt may take before it fails; the batch's frames are then held for the next batch.</summary>
        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

        /// <summary>How long the server may take to acknowledge one frame before the connection is abandoned.</summary>
        public static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(5);

        private readonly object gate = new object();
        private readonly TimeProvider time;
        private readonly Channel<Func<Task>> work =
            Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
        private readonly CancellationTokenSource disposal = new CancellationTokenSource();
        private readonly Task worker;

        private int port;
        private LiveEditLinkStatus status = LiveEditLinkStatus.Disabled;
        private DateTimeOffset? lastAcknowledged;
        private string? lastError;

        //Touched only on the worker.
        private TcpClient? client;
        private List<LiveEditFrame> held = new List<LiveEditFrame>();

        /// <summary>
        ///     Creates a disabled link to <c>127.0.0.1:</c><paramref name="port"/>.
        /// </summary>
        /// <param name="port">The server's <c>editor_link_port</c>.</param>
        /// <param name="time">Stamps <see cref="LastAcknowledged"/>; <see cref="TimeProvider.System"/> when null.</param>
        public LiveEditLink(int port, TimeProvider? time = null) {
            this.port = ValidPort(port);
            this.time = time ?? TimeProvider.System;
            worker = Task.Run(RunWorker);
        }

        /// <summary>
        ///     Raised whenever <see cref="Status"/>, <see cref="LastAcknowledged"/> or
        ///     <see cref="LastError"/> changes. Raised on the worker, or on whichever thread set
        ///     <see cref="Enabled"/> or <see cref="Port"/>, so a control handling it must marshal.
        /// </summary>
        public event EventHandler? StatusChanged;

        /// <summary>
        ///     The port on <c>127.0.0.1</c> to connect to. Changing it closes any open connection;
        ///     the next batch dials the new port.
        /// </summary>
        public int Port {
            get { lock (gate) return port; }
            set {
                ValidPort(value);
                lock (gate) {
                    if (port == value) return;
                    port = value;
                }
                EnqueueClose();
                Update(s => s == LiveEditLinkStatus.Connected ? LiveEditLinkStatus.Disconnected : s);
            }
        }

        /// <summary>
        ///     Whether batches are sent. Off drops every batch and closes any open connection.
        /// </summary>
        public bool Enabled {
            get => Status != LiveEditLinkStatus.Disabled;
            set {
                if (!value) EnqueueClose();
                Update(s => !value ? LiveEditLinkStatus.Disabled
                    : s == LiveEditLinkStatus.Disabled ? LiveEditLinkStatus.Disconnected : s);
            }
        }

        /// <summary>The connection state.</summary>
        public LiveEditLinkStatus Status {
            get { lock (gate) return status; }
        }

        /// <summary>When the server last acknowledged a frame as applied, or null if it never has.</summary>
        public DateTimeOffset? LastAcknowledged {
            get { lock (gate) return lastAcknowledged; }
        }

        /// <summary>
        ///     Why the last batch did not fully arrive: a connection failure, a rejected frame, or
        ///     a square the coalescer could not encode. Cleared by a batch that arrives whole.
        /// </summary>
        public string? LastError {
            get { lock (gate) return lastError; }
        }

        /// <summary>
        ///     Queues a batch for the worker and returns at once.
        /// </summary>
        /// <returns>
        ///     A task that completes when the batch has been delivered or dropped. It never faults,
        ///     so a caller that does not await it loses nothing.
        /// </returns>
        public Task Send(LiveEditBatch batch) {
            ArgumentNullException.ThrowIfNull(batch);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Enqueue(async () => {
                try {
                    await Deliver(batch);
                }
                finally {
                    done.TrySetResult();
                }
            }))
                done.TrySetResult();
            return done.Task;
        }

        private async Task Deliver(LiveEditBatch batch) {
            if (!Enabled || disposal.IsCancellationRequested) return;

            var failures = new List<LiveEditFailure>(batch.Failures);
            var encoded = new List<(LiveEditFrame Frame, byte[] Bytes)>();
            foreach (LiveEditFrame frame in WithHeldFrames(batch.Frames)) {
                try {
                    encoded.Add((frame, LiveEditFrameCodec.Encode(frame)));
                }
                catch (ArgumentException e) {
                    failures.Add(new LiveEditFailure(frame.Kind, frame.Id, e.Message));
                }
            }

            var errors = new List<string>();
            foreach (LiveEditFailure failure in failures)
                errors.Add($"Skipped {failure.Kind} {failure.Id}: {failure.Message}");

            if (encoded.Count == 0) {
                held = new List<LiveEditFrame>();
                if (errors.Count > 0) Update(s => s, errors: errors);
                return;
            }

            int next = 0;
            bool acknowledged = false;
            bool connected = true;
            try {
                //A connection the server dropped while idle - a restart, typically - still reads as
                //connected until it is used, so a failure on a reused connection earns one retry on
                //a fresh one. Resending frames that did arrive is harmless: each is a full replacement.
                for (int attempt = 0 ; ; attempt++) {
                    (NetworkStream stream, bool reused) = await Connect();
                    try {
                        for (; next < encoded.Count ; next++) {
                            (LiveEditFrame frame, byte[] bytes) = encoded[next];
                            byte ack = await Exchange(stream, bytes);
                            if (ack == 0) acknowledged = true;
                            else errors.Add($"Server rejected {frame.Kind} {frame.Id} (status {ack}).");
                        }
                        break;
                    }
                    catch (IOException) when (reused && attempt == 0 && !disposal.IsCancellationRequested) {
                        CloseConnection();
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException
                                            or ObjectDisposedException) {
                CloseConnection();
                connected = false;
                errors.Add(e.Message);
            }

            //The frame at next may have been written before the failure; resending it is harmless.
            held = encoded.GetRange(next, encoded.Count - next).ConvertAll(e => e.Frame);

            LiveEditLinkStatus outcome = connected ? LiveEditLinkStatus.Connected : LiveEditLinkStatus.Disconnected;
            Update(s => s == LiveEditLinkStatus.Disabled ? s : outcome,
                acknowledged ? time.GetUtcNow() : null, errors);
        }

        /// <summary>The held frames the batch does not supersede, in the order they were held, then the batch.</summary>
        private List<LiveEditFrame> WithHeldFrames(IReadOnlyList<LiveEditFrame> frames) {
            var superseded = new HashSet<(LiveEditKind, int)>();
            foreach (LiveEditFrame frame in frames)
                superseded.Add((frame.Kind, frame.Id));
            List<LiveEditFrame> result = held.FindAll(h => !superseded.Contains((h.Kind, h.Id)));
            result.AddRange(frames);
            return result;
        }

        private async Task<(NetworkStream Stream, bool Reused)> Connect() {
            if (client is { Connected: true })
                return (client.GetStream(), true);

            CloseConnection();
            var fresh = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            try {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);
                timeout.CancelAfter(ConnectTimeout);
                await fresh.ConnectAsync(IPAddress.Loopback, Port, timeout.Token);
            }
            catch {
                fresh.Dispose();
                throw;
            }
            client = fresh;
            return (fresh.GetStream(), false);
        }

        private async Task<byte> Exchange(NetworkStream stream, byte[] frame) {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(disposal.Token);
            timeout.CancelAfter(AckTimeout);
            await stream.WriteAsync(frame, timeout.Token);
            byte[] ack = new byte[1];
            if (await stream.ReadAsync(ack, timeout.Token) == 0)
                throw new IOException("The server closed the connection without acknowledging the frame.");
            return ack[0];
        }

        private void CloseConnection() {
            client?.Dispose();
            client = null;
        }

        private void EnqueueClose() => Enqueue(() => {
            CloseConnection();
            return Task.CompletedTask;
        });

        private bool Enqueue(Func<Task> item) => work.Writer.TryWrite(item);

        private async Task RunWorker() {
            //Not cancellable: Dispose completes the writer and the loop drains what is queued, so
            //every Send task completes; Deliver returns at once once disposal is requested.
            await foreach (Func<Task> item in work.Reader.ReadAllAsync()) {
                try {
                    await item();
                }
                catch {
                    //Deliver catches every network failure; this guards the worker against
                    //anything else ending it, which would silently stop all future pushes.
                }
            }
            CloseConnection();
        }

        /// <param name="next">The new status from the current one.</param>
        /// <param name="acknowledged">A new acknowledgement time, or null to keep the last one.</param>
        /// <param name="errors">A batch result: replaces <see cref="LastError"/>, empty clearing it. Null keeps it.</param>
        private void Update(Func<LiveEditLinkStatus, LiveEditLinkStatus> next,
            DateTimeOffset? acknowledged = null, List<string>? errors = null) {
            bool changed;
            lock (gate) {
                LiveEditLinkStatus newStatus = next(status);
                DateTimeOffset? newAck = acknowledged ?? lastAcknowledged;
                string? newError = errors == null ? lastError
                    : errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
                changed = newStatus != status || newAck != lastAcknowledged || newError != lastError;
                status = newStatus;
                lastAcknowledged = newAck;
                lastError = newError;
            }
            if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Whether <paramref name="port"/> is a TCP port number this link accepts.</summary>
        public static bool IsValidPort(int port) => port is >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;

        private static int ValidPort(int value) =>
            IsValidPort(value)
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value), value, "Not a TCP port.");

        /// <summary>Stops the worker and closes the connection; batches still queued are dropped.</summary>
        public void Dispose() {
            work.Writer.TryComplete();
            disposal.Cancel();
            try {
                worker.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException) {
            }
            disposal.Dispose();
        }
    }
}
