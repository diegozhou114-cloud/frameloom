using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.WebGL;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class WebGlRealtimeChannelTests
    {
        private static readonly RealtimeEndpoint Endpoint = new RealtimeEndpoint(
            RealtimeTransport.WebSocket, new Uri("wss://game.example/v1/realtime"));

        [Test]
        public void WebGlRealtimeChannel_ExposesPublicContract()
        {
            Assert.That(Type.GetType(
                "ZWebGLFrameWork.Network.WebGL.WebGlRealtimeChannel, " +
                "ZWebGLFrameWork.Network.WebGL"), Is.Not.Null);
        }

        [Test]
        public void ConnectAsync_RejectsInsecureSchemeBeforeCreatingClient()
        {
            var factory = new FakeNativeWebSocketClientFactory();
            var channel = new WebGlRealtimeChannel(factory);

            Assert.ThrowsAsync<ArgumentException>(() => channel.ConnectAsync(
                new RealtimeEndpoint(RealtimeTransport.WebSocket,
                    new Uri("ws://game.example/v1/realtime")),
                TestCredentials.Valid, CancellationToken.None));
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [Test]
        public void ConnectAsync_RejectsWrongPathBeforeCreatingClient()
        {
            var factory = new FakeNativeWebSocketClientFactory();
            var channel = new WebGlRealtimeChannel(factory);

            Assert.ThrowsAsync<ArgumentException>(() => channel.ConnectAsync(
                new RealtimeEndpoint(RealtimeTransport.WebSocket,
                    new Uri("wss://game.example/not-realtime")),
                TestCredentials.Valid, CancellationToken.None));
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [Test]
        public void ConnectAsync_RejectsWrongTransportBeforeCreatingClient()
        {
            var factory = new FakeNativeWebSocketClientFactory();
            var channel = new WebGlRealtimeChannel(factory);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => channel.ConnectAsync(
                new RealtimeEndpoint(RealtimeTransport.NativeKcp,
                    new Uri("wss://game.example/v1/realtime")),
                TestCredentials.Valid, CancellationToken.None));
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [Test]
        public async Task ConnectAsync_UsesExactSubprotocolAndAuthHelloThenAcceptsMatchingSession()
        {
            var client = new FakeNativeWebSocketClient();
            var factory = new FakeNativeWebSocketClientFactory(client);
            var channel = new WebGlRealtimeChannel(factory);

            try
            {
                Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                    CancellationToken.None);
                client.RaiseOpened();
                await client.WaitForSendAsync();
                Assert.That(RealtimeCodec.Decode(client.Sent.Single()).Kind,
                    Is.EqualTo(RealtimeMessageKind.AuthHello));
                client.RaiseMessage(RealtimeCodec.Encode(new AuthAccepted
                {
                    SessionId = TestCredentials.Valid.SessionId,
                    LastAuthorityTick = -1,
                    TimelineRevision = 0
                }));
                await connect;

                Assert.That(factory.Subprotocols, Is.EqualTo(
                    new[] { RealtimeProtocol.WebSocketSubprotocol }));
                Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Connected));
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
            }
        }

        [Test]
        public async Task ConnectAsync_RejectsNonAcceptanceFrameBeforeAuthentication()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));

            Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                CancellationToken.None);
            client.RaiseOpened();
            await client.WaitForSendAsync();
            client.RaiseMessage(RealtimeCodec.Encode(new AuthHello
            {
                Version = RealtimeProtocol.Version,
                SessionId = TestCredentials.Valid.SessionId,
                OpaqueTicket = TestCredentials.Valid.OpaqueTicket
            }));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await connect);
            Assert.That(client.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConnectAsync_RejectsMismatchedAcceptedSession()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));

            Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                CancellationToken.None);
            client.RaiseOpened();
            await client.WaitForSendAsync();
            client.RaiseMessage(RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = TestCredentials.Foreign.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            }));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await connect);
            Assert.That(client.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConnectAsync_RejectsMalformedFrameBeforeAuthentication()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));

            Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                CancellationToken.None);
            client.RaiseOpened();
            await client.WaitForSendAsync();
            client.RaiseMessage(new byte[] { 7, 8, 9 });

            Assert.ThrowsAsync<InvalidOperationException>(async () => await connect);
            Assert.That(client.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConnectAsync_CancellationUsesTheIdempotentClosePath()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            using var cancellation = new CancellationTokenSource();

            Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                cancellation.Token);
            cancellation.Cancel();

            try
            {
                await connect;
                Assert.Fail("Cancellation must stop the pending connection.");
            }
            catch (OperationCanceledException)
            {
            }
            Assert.That(client.CloseCount, Is.EqualTo(1));
            Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Disconnected));
        }

        [Test]
        public void ConnectAsync_ClosesWhenNativeConnectFaults()
        {
            var client = new FakeNativeWebSocketClient
            {
                ConnectTask = Task.FromException(new InvalidOperationException("connect failed"))
            };
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));

            Assert.ThrowsAsync<InvalidOperationException>(() => channel.ConnectAsync(
                Endpoint, TestCredentials.Valid, CancellationToken.None));
            Assert.That(client.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public void CloseAsync_BeforeConnectPreventsLaterConnection()
        {
            var factory = new FakeNativeWebSocketClientFactory();
            var channel = new WebGlRealtimeChannel(factory);

            channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.ThrowsAsync<InvalidOperationException>(() => channel.ConnectAsync(
                Endpoint, TestCredentials.Valid, CancellationToken.None));
            Assert.That(factory.CreateCount, Is.Zero);
        }

        [Test]
        public async Task ReceiveAsync_ReturnsCompleteMessagesInFifoOrder()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                byte[] first = { 1, 2, 3 };
                byte[] second = { 4, 5, 6 };

                client.RaiseMessage(first);
                client.RaiseMessage(second);

                Assert.That(await channel.ReceiveAsync(CancellationToken.None), Is.EqualTo(first));
                Assert.That(await channel.ReceiveAsync(CancellationToken.None), Is.EqualTo(second));
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
            }
        }

        [Test]
        public async Task CloseAsync_IsIdempotent()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            await ConnectAndAuthenticateAsync(channel, client);

            await Task.WhenAll(
                channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None),
                channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None));

            Assert.That(client.CloseCount, Is.EqualTo(1));
            Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Disconnected));
        }

        [Test]
        public async Task CloseAsync_WaitsForDispatchLoopBeforeDisposingClient()
        {
            var client = new FakeNativeWebSocketClient { BlockSecondDispatch = true };
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            Task close = null;

            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                await client.WaitForBlockedDispatchAsync();

                close = channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
                Task winner = await Task.WhenAny(close, Task.Delay(250));

                Assert.That(winner, Is.Not.SameAs(close),
                    "Close must await the active dispatch loop.");
                Assert.That(client.DisposeCount, Is.Zero,
                    "The native client must outlive its dispatch loop.");
            }
            finally
            {
                client.ReleaseBlockedDispatch();
                if (close != null) await close;
                else await channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
            }

            Assert.That(client.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CloseAsync_ConcurrentCallWaitsForActiveClose()
        {
            var client = new FakeNativeWebSocketClient { BlockSecondDispatch = true };
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            Task firstClose = null;
            Task secondClose = null;

            try
            {
                await ConnectAndAuthenticateAsync(channel, client);
                await client.WaitForBlockedDispatchAsync();

                firstClose = channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
                secondClose = channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
                Task winner = await Task.WhenAny(secondClose, Task.Delay(250));

                Assert.That(winner, Is.Not.SameAs(secondClose),
                    "Concurrent close callers must wait for the active close.");
                Assert.That(client.DisposeCount, Is.Zero);
            }
            finally
            {
                client.ReleaseBlockedDispatch();
                if (firstClose != null) await firstClose;
                if (secondClose != null) await secondClose;
                if (firstClose == null && secondClose == null)
                    await channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
            }

            Assert.That(client.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReceiveAsync_RejectsDuplicatePendingReceiver()
        {
            var client = new FakeNativeWebSocketClient();
            var channel = new WebGlRealtimeChannel(
                new FakeNativeWebSocketClientFactory(client));
            try
            {
                await ConnectAndAuthenticateAsync(channel, client);

                Task<byte[]> first = channel.ReceiveAsync(CancellationToken.None);
                Assert.Throws<InvalidOperationException>(() =>
                    channel.ReceiveAsync(CancellationToken.None));
                byte[] message = { 9, 8, 7 };
                client.RaiseMessage(message);

                Assert.That(await first, Is.EqualTo(message));
            }
            finally
            {
                await channel.CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
            }
        }

        private static async Task ConnectAndAuthenticateAsync(
            WebGlRealtimeChannel channel, FakeNativeWebSocketClient client)
        {
            Task connect = channel.ConnectAsync(Endpoint, TestCredentials.Valid,
                CancellationToken.None);
            client.RaiseOpened();
            await client.WaitForSendAsync();
            client.RaiseMessage(RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = TestCredentials.Valid.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            }));
            await connect;
        }

        private sealed class FakeNativeWebSocketClientFactory : INativeWebSocketClientFactory
        {
            private readonly INativeWebSocketClient _client;

            public int CreateCount { get; private set; }
            public IReadOnlyList<string> Subprotocols { get; private set; }

            public FakeNativeWebSocketClientFactory(INativeWebSocketClient client = null)
            {
                _client = client ?? new FakeNativeWebSocketClient();
            }

            public INativeWebSocketClient Create(
                Uri uri, IReadOnlyList<string> subprotocols)
            {
                CreateCount++;
                Subprotocols = subprotocols;
                return _client;
            }
        }

        private sealed class FakeNativeWebSocketClient : INativeWebSocketClient
        {
            private readonly TaskCompletionSource<bool> _sent =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _dispatchBlocked =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly ManualResetEventSlim _releaseBlockedDispatch = new ManualResetEventSlim();
            private int _dispatchCalls;
            private int _disposeCount;

            public event Action Opened;
            public event Action<byte[]> MessageReceived;
            public event Action<string> Error;
            public event Action<ushort> Closed;

            public List<byte[]> Sent { get; } = new List<byte[]>();
            public Task ConnectTask { get; set; } = Task.CompletedTask;
            public int CloseCount { get; private set; }
            public bool BlockSecondDispatch { get; set; }
            public int DisposeCount => Volatile.Read(ref _disposeCount);

            public Task ConnectAsync() => ConnectTask;

            public Task SendAsync(byte[] completeMessage)
            {
                Sent.Add(completeMessage);
                _sent.TrySetResult(true);
                return Task.CompletedTask;
            }

            public Task CloseAsync()
            {
                CloseCount++;
                Closed?.Invoke(1000);
                return Task.CompletedTask;
            }

            public void DispatchMessageQueue()
            {
                if (!BlockSecondDispatch || Interlocked.Increment(ref _dispatchCalls) != 2) return;
                _dispatchBlocked.TrySetResult(true);
                _releaseBlockedDispatch.Wait();
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCount);
            }

            public void RaiseOpened() => Opened?.Invoke();
            public void RaiseMessage(byte[] message) => MessageReceived?.Invoke(message);
            public void RaiseError(string message) => Error?.Invoke(message);

            public async Task WaitForSendAsync()
            {
                Task winner = await Task.WhenAny(_sent.Task, Task.Delay(1000));
                Assert.That(winner, Is.SameAs(_sent.Task), "AuthHello was not sent.");
                await _sent.Task;
            }

            public async Task WaitForBlockedDispatchAsync()
            {
                Task winner = await Task.WhenAny(_dispatchBlocked.Task, Task.Delay(1000));
                Assert.That(winner, Is.SameAs(_dispatchBlocked.Task),
                    "Dispatch loop did not reach its controlled blocked iteration.");
                await _dispatchBlocked.Task;
            }

            public void ReleaseBlockedDispatch() => _releaseBlockedDispatch.Set();
        }
    }
}
