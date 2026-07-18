using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class RealtimeConnectionControllerTests
    {
        [Test]
        public async Task NetworkManager_Reconnect_DoesNotLeakOrDispatchStaleCallbacks()
        {
            var first = new ControlledChannel();
            var second = new ControlledChannel();
            var controller = new RealtimeConnectionController(
                new QueueChannelFactory(first, second));

            Task firstConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            Task secondConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            second.CompleteConnect();
            await secondConnect;
            first.CompleteConnect();
            await firstConnect;

            Assert.That(controller.Current, Is.SameAs(second));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.Zero);
            Assert.That(controller.Generation, Is.EqualTo(2));
        }

        [Test]
        public async Task TcpClient_DisconnectDuringConnect_DiscardsLateSocket()
        {
            var channel = new ControlledChannel();
            var controller = new RealtimeConnectionController(
                new QueueChannelFactory(channel));
            Task connect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);

            await controller.DisconnectAsync(CancellationToken.None);
            channel.CompleteConnect();
            await connect;

            Assert.That(controller.Current, Is.Null);
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task Disconnect_IsIdempotentAndClosesCurrentExactlyOnce()
        {
            var channel = new ControlledChannel();
            var controller = new RealtimeConnectionController(
                new QueueChannelFactory(channel));
            Task connect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            channel.CompleteConnect();
            await connect;

            await controller.DisconnectAsync(CancellationToken.None);
            await controller.DisconnectAsync(CancellationToken.None);

            Assert.That(controller.Current, Is.Null);
            Assert.That(channel.CloseCount, Is.EqualTo(1));
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ConnectAfterDisconnect_OwnsFreshGeneration()
        {
            var first = new ControlledChannel();
            var second = new ControlledChannel();
            var controller = new RealtimeConnectionController(
                new QueueChannelFactory(first, second));

            Task firstConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            first.CompleteConnect();
            await firstConnect;
            await controller.DisconnectAsync(CancellationToken.None);

            Task secondConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            second.CompleteConnect();
            await secondConnect;

            Assert.That(controller.Current, Is.SameAs(second));
            Assert.That(first.DisposeCount, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.Zero);
        }

        private static RealtimeEndpoint TestEndpoint() => new RealtimeEndpoint(
            RealtimeTransport.NativeKcp, new Uri("udp://127.0.0.1:8888"));

        private static RealtimeCredentials TestCredentials() => new RealtimeCredentials
        {
            SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            OpaqueTicket = new byte[] { 1 },
            ResumeToken = new byte[] { 2 },
            MacKey = new byte[32],
            ExpiresAtUnixSeconds = long.MaxValue
        };

        private sealed class QueueChannelFactory : IRealtimeChannelFactory
        {
            private readonly Queue<IRealtimeChannel> _channels;

            public QueueChannelFactory(params IRealtimeChannel[] channels)
            {
                _channels = new Queue<IRealtimeChannel>(channels);
            }

            public IRealtimeChannel Create(RealtimeTransport transport)
            {
                if (_channels.Count == 0)
                    throw new InvalidOperationException("No controlled channel remains.");
                return _channels.Dequeue();
            }
        }

        private sealed class ControlledChannel : IRealtimeChannel
        {
            private readonly TaskCompletionSource<bool> _connect =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _disposed;

            public RealtimeChannelState State { get; private set; } =
                RealtimeChannelState.Disconnected;
            public int DisposeCount { get; private set; }
            public int CloseCount { get; private set; }

            public Task ConnectAsync(
                RealtimeEndpoint endpoint, RealtimeCredentials credentials,
                CancellationToken cancellationToken)
            {
                State = RealtimeChannelState.Connecting;
                return _connect.Task;
            }

            public Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken) =>
                Task.FromResult(Array.Empty<byte>());

            public Task CloseAsync(
                RealtimeCloseReason reason, CancellationToken cancellationToken)
            {
                CloseCount++;
                State = RealtimeChannelState.Disconnected;
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                DisposeCount++;
                State = RealtimeChannelState.Disconnected;
            }

            public void CompleteConnect()
            {
                State = RealtimeChannelState.Connected;
                _connect.TrySetResult(true);
            }
        }
    }
}
