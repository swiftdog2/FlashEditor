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

        /// <summary>Reads <paramref name="frames"/> frames off one connection, answering each with <paramref name="ack"/>.</summary>
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
                stream.WriteByte(ack);
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
                return await ServeFrames(peer.GetStream(), 2);
            });

            await link.Send(Batch(
                new LiveEditFrame(LiveEditKind.Terrain, 0x3232, new byte[] { 0x01 }),
                new LiveEditFrame(LiveEditKind.Locations, 0x3232, new byte[] { 0x02, 0x03 })))
                .WaitAsync(Patience);

            Assert.Equal(new byte[] {
                0x46, 0x4C, 0x01, 0x00, 0x32, 0x32, 0x00, 0x00, 0x00, 0x01, 0x01,
                0x46, 0x4C, 0x01, 0x01, 0x32, 0x32, 0x00, 0x00, 0x00, 0x02, 0x02, 0x03
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
                await ServeFrames(peer.GetStream(), 1, ack: 1);
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
        }

        [Fact]
        public async Task AfterTheServerDropsTheConnectionTheNextSendReconnectsAndDelivers() {
            var time = new ManualTimeProvider();
            using var link = new LiveEditLink(Port, time) { Enabled = true };
            var first = new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 });
            var second = new LiveEditFrame(LiveEditKind.Terrain, 2, new byte[] { 2 });

            Task<byte[]> server = Task.Run(async () => {
                using (TcpClient peer = await listener.AcceptTcpClientAsync())
                    await ServeFrames(peer.GetStream(), 1);
                //The first connection is now closed, as a server restart would leave it.
                using TcpClient again = await listener.AcceptTcpClientAsync();
                return await ServeFrames(again.GetStream(), 1);
            });

            await link.Send(Batch(first)).WaitAsync(Patience);
            await Task.Delay(200);
            await link.Send(Batch(second)).WaitAsync(Patience);

            Assert.Equal(LiveEditFrameCodec.Encode(second), await server.WaitAsync(Patience));
            Assert.Equal(LiveEditLinkStatus.Connected, link.Status);
            Assert.Null(link.LastError);
        }

        [Fact]
        public async Task DisablingALiveLinkClosesTheConnection() {
            using var link = new LiveEditLink(Port) { Enabled = true };
            Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
            Task sent = link.Send(Batch(new LiveEditFrame(LiveEditKind.Terrain, 1, new byte[] { 1 })));
            using TcpClient peer = await accepted.WaitAsync(Patience);
            await ServeFrames(peer.GetStream(), 1).WaitAsync(Patience);
            await sent.WaitAsync(Patience);

            link.Enabled = false;
            Assert.Equal(LiveEditLinkStatus.Disabled, link.Status);

            //A closed peer reads as end of stream.
            int read = await peer.GetStream().ReadAsync(new byte[1]).AsTask().WaitAsync(Patience);
            Assert.Equal(0, read);
        }
    }
}