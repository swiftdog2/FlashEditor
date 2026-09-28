using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FlashEditor.LiveEdit;
using Xunit;

namespace FlashEditor.Tests.LiveEdit {
    /// <summary>
    ///     Drives the link against a real loopback listener on an ephemeral port, standing in for
    ///     the HydraScape server's leg-A endpoint. Nothing here reaches a live server.
    /// </summary>
    public sealed class LiveEditLinkTests : IDisposable {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 0);

        public LiveEditLinkTests() => listener.Start();

        public void Dispose() => listener.Stop();

        private int Port => ((IPEndPoint) listener.LocalEndpoint).Port;

        private static LiveEditBatch Batch(params LiveEditFrame[] frames) =>
            new LiveEditBatch(frames, Array.Empty<LiveEditFailure>());

        private static readonly byte[] BatchEndBytes = { 0x46, 0x4C, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

        private static byte[] Wire(params LiveEditFrame[] frames) {
            var wire = new MemoryStream();
            foreach (LiveEditFrame frame in frames)
                wire.Write(LiveEditFrameCodec.Encode(frame));
            wire.Write(BatchEndBytes);
            return wire.ToArray();
        }

        /// <summary>
        ///     Reads <paramref name="frames"/> frames off one connection, the batch end included,
        ///     answering the batch end with 0 and every other frame with <paramref name="ack"/>.
        /// </summary>
        private static async Task<byte[]> ServeFrames(NetworkStream stream, int frames, byte ack = 0) {
            var received = new MemoryStream();
            for (int i = 0 ; i < frames ; i++) {
                byte[] header = new byte[10];
                await stream.ReadExactlyAsync(header);
                int length = (header[6] << 24) | (header[7] << 16) | (header[8] << 8) | header[9];
                byte[] payload = new byte[length];
                await stream.ReadExactlyAsync(payload);
                received.Write(header);
                received.Write(payload);
                stream.WriteByte(header[3] == (byte) LiveEditKind.BatchEnd ? (byte) 0 : ack);
            }
            return received.ToArray();
        }

        [Fact]
        public async Task ABatchArrivesAsTheExactFramesAndTheLinkReportsConnectedWithAnAckTime() {
            var time = new ManualTimeProvider();
            using var link = new LiveEditLink(Port, time) { Enabled = true };
            Assert.Equal(LiveEditLinkStatus.Disconnected, link.Status);
            Assert.Null(link.LastAcknowledged);

            Task<byte[]> server = Task.Run(async () => {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                return await ServeFrames(peer.GetStream(), 3);
            });

            await link.Send(Batch(
                new LiveEditFrame(LiveEditKind.Terrain, 0x3232, new byte[] { 0x01 }),
                new LiveEditFrame(LiveEditKind.Locations, 0x3232, new byte[] { 0x02, 0x03 })))
                .WaitAsync(Patience);

            Assert.Equal(new byte[] {
                0x46, 0x4C, 0x01, 0x00, 0x32, 0x32, 0x00, 0x00, 0x00, 0x01, 0x01,
                0x46, 0x4C, 0x01, 0x01, 0x32, 0x32, 0x00, 0x00, 0x00, 0x02, 0x02, 0x03,
                0x46, 0x4C, 0x01, 0x07, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
            }, await server.WaitAsync(Patience));
            Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            Assert.Equal(time.GetUtcNow(), link.LastAcknowledged);
            Assert.Null(link.LastError);
        }

        [Fact]
        public async Task WithNoListenerTheLinkReportsDisconnectedAndNothingEscapes() {
            int port = Port;
            listener.Stop();
            var changes = 0;
            using var link = new LiveEditLink(port) { Enabled = true };
            link.StatusChanged += (_, _) => Interlocked.Increment(ref changes);

            await link.Send(Batch(new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 })))
                .WaitAsync(Patience);

            Assert.Equal(LiveEditLinkStatus.Disconnected, link.Status);
            Assert.Null(link.LastAcknowledged);
            Assert.False(string.IsNullOrEmpty(link.LastError));
            Assert.True(changes > 0);
        }

        [Fact]
        public async Task ADisabledLinkSendsNothingAndSaysSo() {
            using var link = new LiveEditLink(Port);
            Assert.Equal(LiveEditLinkStatus.Disabled, link.Status);

            await link.Send(Batch(new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 })))
                .WaitAsync(Patience);

            Assert.Equal(LiveEditLinkStatus.Disabled, link.Status);
            Assert.False(listener.Pending());
        }

        [Fact]
        public async Task ARejectedFrameIsReportedWithoutDroppingTheConnection() {
            using var link = new LiveEditLink(Port) { Enabled = true };
            Task server = Task.Run(async () => {
                using TcpClient peer = await listener.AcceptTcpClientAsync();
                await ServeFrames(peer.GetStream(), 2, ack: 1);
            });

            await link.Send(Batch(new LiveEditFrame(LiveEditKind.FloorOverlay, 9, new byte[] { 1 })))
                .WaitAsync(Patience);
            await server.WaitAsync(Patience);

            Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            Assert.Null(link.LastAcknowledged);
            Assert.Contains("rejected", link.LastError);
        }

        [Fact]
        public async Task SkippedSquaresInABatchAreReportedAsTheLastError() {
            using var link = new LiveEditLink(Port) { Enabled = true };
            await link.Send(new LiveEditBatch(
                    Array.Empty<LiveEditFrame>(),
                    new[] { new LiveEditFailure(LiveEditKind.Terrain, 0x3232, "height not encodable") }))
                .WaitAsync(Patience);

            Assert.Contains("height not encodable", link.LastError);
            Assert.False(listener.Pending(), "A batch with no frames must not even connect.");
        }

        [Fact]
        public async Task AfterTheServerDropsTheConnectionTheNextSendReconnectsAndDelivers() {
            var time = new ManualTimeProvider();
            using var link = new LiveEditLink(Port, time) { Enabled = true };
            var first = new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 });
            var second = new LiveEditFrame(LiveEditKind.Terrain, 2, new byte[] { 2 });

            Task<byte[]> server = Task.Run(async () => {
                using (TcpClient peer = await listener.AcceptTcpClientAsync())
                    await ServeFrames(peer.GetStream(), 2);
                //The first connection is now closed, as a server restart would leave it.
                using TcpClient again = await listener.AcceptTcpClientAsync();
                return await ServeFrames(again.GetStream(), 2);
            });

            await link.Send(Batch(first)).WaitAsync(Patience);
            await Task.Delay(200);
            await link.Send(Batch(second)).WaitAsync(Patience);

            Assert.Equal(Wire(second), await server.WaitAsync(Patience));
            Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            Assert.Null(link.LastError);
        }

        [Fact]
        public async Task AFrameThatMissedADownServerIsSentAheadOfTheNextBatchOnceItIsBack() {
            int port = Port;
            listener.Stop();
            using var link = new LiveEditLink(port) { Enabled = true };
            var stale = new LiveEditFrame(LiveEditKind.Terrain, 7, new byte[] { 1 });
            var missed = new LiveEditFrame(LiveEditKind.Terrain, 7, new byte[] { 2 });
            var other = new LiveEditFrame(LiveEditKind.FloorUnderlay, 3, new byte[] { 3 });
            var next = new LiveEditFrame(LiveEditKind.Locations, 8, new byte[] { 4 });

            await link.Send(Batch(stale, other)).WaitAsync(Patience);
            await link.Send(Batch(missed)).WaitAsync(Patience);
            Assert.Equal(LiveEditLinkStatus.Disconnected, link.Status);

            var restarted = new TcpListener(IPAddress.Loopback, port);
            restarted.Start();
            try {
                Task<byte[]> server = Task.Run(async () => {
                    using TcpClient peer = await restarted.AcceptTcpClientAsync();
                    return await ServeFrames(peer.GetStream(), 4);
                });

                await link.Send(Batch(next)).WaitAsync(Patience);

                //The newer bytes for square 7 replaced the older ones rather than following them,
                //and the held frames travel inside the new batch, ahead of its own frames.
                Assert.Equal(Wire(other, missed, next), await server.WaitAsync(Patience));
                Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            }
            finally {
                restarted.Stop();
            }
        }

        [Fact]
        public async Task ABatchWhoseBatchEndIsNeverAcknowledgedIsHeldWholeAndResentInTheNextBatch() {
            using var link = new LiveEditLink(Port) { Enabled = true };
            var terrain = new LiveEditFrame(LiveEditKind.Terrain, 7, new byte[] { 1 });
            var locations = new LiveEditFrame(LiveEditKind.Locations, 7, new byte[] { 2 });
            var next = new LiveEditFrame(LiveEditKind.FloorOverlay, 3, new byte[] { 3 });

            Task<byte[]> server = Task.Run(async () => {
                using (TcpClient peer = await listener.AcceptTcpClientAsync()) {
                    //Both frames acknowledged, then the connection dies with the batch end unread,
                    //so a real server discards the whole batch.
                    NetworkStream stream = peer.GetStream();
                    await ServeFrames(stream, 2);
                    await stream.ReadExactlyAsync(new byte[BatchEndBytes.Length]);
                }
                using TcpClient again = await listener.AcceptTcpClientAsync();
                return await ServeFrames(again.GetStream(), 4);
            });

            await link.Send(Batch(terrain, locations)).WaitAsync(Patience);
            Assert.Equal(LiveEditLinkStatus.Disconnected, link.Status);
            Assert.Null(link.LastAcknowledged);

            await link.Send(Batch(next)).WaitAsync(Patience);

            Assert.Equal(Wire(terrain, locations, next), await server.WaitAsync(Patience));
            Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            Assert.Null(link.LastError);
        }

        [Fact]
        public async Task AHeldFrameIsNotResentWhenTheNextBatchCarriesTheSameFile() {
            int port = Port;
            listener.Stop();
            using var link = new LiveEditLink(port) { Enabled = true };
            var missed = new LiveEditFrame(LiveEditKind.Terrain, 7, new byte[] { 1 });
            var newer = new LiveEditFrame(LiveEditKind.Terrain, 7, new byte[] { 2 });

            await link.Send(Batch(missed)).WaitAsync(Patience);

            var restarted = new TcpListener(IPAddress.Loopback, port);
            restarted.Start();
            try {
                Task<byte[]> server = Task.Run(async () => {
                    using TcpClient peer = await restarted.AcceptTcpClientAsync();
                    return await ServeFrames(peer.GetStream(), 2);
                });

                await link.Send(Batch(newer)).WaitAsync(Patience);

                Assert.Equal(Wire(newer), await server.WaitAsync(Patience));
            }
            finally {
                restarted.Stop();
            }
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        [InlineData(65536, false)]
        [InlineData(1, true)]
        [InlineData(65535, true)]
        public void IsValidPortAcceptsOnlyAPortTheLinkCanDial(int port, bool valid) {
            Assert.Equal(valid, LiveEditLink.IsValidPort(port));
        }

        [Fact]
        public void PortZeroIsRefusedByTheConstructorAndTheSetter() {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LiveEditLink(0));
            using var link = new LiveEditLink(Port);
            Assert.Throws<ArgumentOutOfRangeException>(() => link.Port = 0);
        }

        [Fact]
        public async Task DisablingALiveLinkClosesTheConnection() {
            using var link = new LiveEditLink(Port) { Enabled = true };
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            Task sent = link.Send(Batch(new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 })));
            using TcpClient peer = await accepted.WaitAsync(Patience);
            await ServeFrames(peer.GetStream(), 2).WaitAsync(Patience);
            await sent.WaitAsync(Patience);

            link.Enabled = false;
            Assert.Equal(LiveEditLinkStatus.Disabled, link.Status);

            //A closed peer reads as end of stream.
            int read = await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(Patience);
            Assert.Equal(0, read);
        }
    }
}