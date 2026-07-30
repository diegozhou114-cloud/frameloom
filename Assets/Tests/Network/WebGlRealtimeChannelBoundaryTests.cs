using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.WebGL;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class WebGlRealtimeChannelBoundaryTests
    {
        private static readonly RealtimeEndpoint Endpoint = new RealtimeEndpoint(
            RealtimeTransport.WebSocket, new Uri("wss://game.example/v1/realtime"));
        private static readonly RealtimeCredentials Credentials =
            new RealtimeCredentials
            {
                SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                OpaqueTicket = new byte[] { 1 },
                ExpiresAtUnixSeconds = 4102444800L
            };

        [Test]
        public async Task SendAsync_RejectsEmptyFrames()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    channel.SendAsync(Array.Empty<byte>(), CancellationToken.None));
                Assert.That(client.Sent, Has.Count.EqualTo(1));
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal,
                    CancellationToken.None);
            }
        }

        [Test]
        public async Task SendAsync_RejectsFramesLargerThanRealtimeProtocolLimit()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                byte[] oversized = new byte[RealtimeProtocol.HeaderBytes +
                    RealtimeProtocol.MaxMessageBytes + 1];

                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    channel.SendAsync(oversized, CancellationToken.None));
                Assert.That(client.Sent, Has.Count.EqualTo(1));
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal,
                    CancellationToken.None);
            }
        }

        [Test]
        public async Task ReceiveAsync_DropsEmptyFramesBeforeReceiveIsRequested()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            Task<byte[]> pending = null;
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                client.RaiseMessage(Array.Empty<byte>());
                pending = channel.ReceiveAsync(CancellationToken.None);

                Assert.That(pending.IsCompleted, Is.False);
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal,
                    CancellationToken.None);
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pending);
        }

        [Test]
        public async Task ReceiveAsync_DropsEmptyFrames()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            Task<byte[]> pending = null;
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                pending = channel.ReceiveAsync(CancellationToken.None);
                client.RaiseMessage(Array.Empty<byte>());

                Assert.That(pending.IsCompleted, Is.False);
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal,
                    CancellationToken.None);
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pending);
        }

        [Test]
        public async Task ReceiveAsync_DropsFramesLargerThanRealtimeProtocolLimit()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            Task<byte[]> pending = null;
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                pending = channel.ReceiveAsync(CancellationToken.None);
                client.RaiseMessage(new byte[RealtimeProtocol.HeaderBytes +
                    RealtimeProtocol.MaxMessageBytes + 1]);

                Assert.That(pending.IsCompleted, Is.False);
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal,
                    CancellationToken.None);
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await pending);
        }

        private static async Task ConnectAndAuthenticateAsync(
            WebGlRealtimeChannel channel, FakeNativeWebSocketClient client)
        {
            Task connect = channel.ConnectAsync(Endpoint, Credentials,
                CancellationToken.None);
            client.RaiseOpened();
            client.RaiseMessage(RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = Credentials.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            }));
            await connect;
        }

        private sealed class FakeNativeWebSocketClientFactory : INativeWebSocketClientFactory
        {
            private readonly INativeWebSocketClient _client;

            public FakeNativeWebSocketClientFactory(INativeWebSocketClient client) =>
                _client = client;

            public INativeWebSocketClient Create(
                Uri uri, IReadOnlyList<string> subprotocols) => _client;
        }

        private sealed class FakeNativeWebSocketClient : INativeWebSocketClient
        {
            public event Action Opened;
            public event Action<byte[]> MessageReceived;
            public event Action<string> Error;
            public event Action<ushort> Closed;

            public List<byte[]> Sent { get; } = new List<byte[]>();

            public Task ConnectAsync() => Task.CompletedTask;
            public Task SendAsync(byte[] completeMessage)
            {
                Sent.Add(completeMessage);
                return Task.CompletedTask;
            }

            public Task CloseAsync() => Task.CompletedTask;
            public void DispatchMessageQueue() { }
            public void Dispose() { }
            public void RaiseOpened() => Opened?.Invoke();
            public void RaiseMessage(byte[] message) => MessageReceived?.Invoke(message);
            public void RaiseError(string message) => Error?.Invoke(message);
            public void RaiseClosed(ushort code) => Closed?.Invoke(code);
        }
    }
}
