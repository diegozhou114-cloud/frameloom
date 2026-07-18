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
            Assert.That(first.DisposeCallCount, Is.EqualTo(1));
            Assert.That(second.DisposeCallCount, Is.Zero);
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
            Assert.That(channel.DisposeCallCount, Is.EqualTo(1));
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
            Assert.That(channel.DisposeCallCount, Is.EqualTo(1));
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
            Assert.That(first.DisposeCallCount, Is.EqualTo(1));
            Assert.That(second.DisposeCallCount, Is.Zero);
        }

        [Test]
        public async Task SupersedingConnect_CancelsPendingChannel()
        {
            var first = new ControlledChannel(observeCancellation: true);
            var second = new ControlledChannel();
            var controller = new RealtimeConnectionController(
                new QueueChannelFactory(first, second));

            Task firstConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            Task secondConnect = controller.ConnectAsync(
                TestEndpoint(), TestCredentials(), CancellationToken.None);
            second.CompleteConnect();
            await secondConnect;
            await firstConnect;

            Assert.That(first.CancellationObserved, Is.True);
            Assert.That(first.DisposeCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task FactoryBlockedDuringSupersession_DoesNotUseDisposedTokenSource()
        {
            var first = new ControlledChannel(observeCancellation: true);
            var second = new ControlledChannel();
            using (var factory = new BlockingFactory(first, second))
            {
                var controller = new RealtimeConnectionController(factory);
                Task firstConnect = Task.Run(() => controller.ConnectAsync(
                    TestEndpoint(), TestCredentials(), CancellationToken.None));
                factory.WaitUntilFirstCreateEntered();

                Task secondConnect = controller.ConnectAsync(
                    TestEndpoint(), TestCredentials(), CancellationToken.None);
                second.CompleteConnect();
                await secondConnect;
                factory.ReleaseFirstCreate();
                await firstConnect;

                Assert.That(controller.Current, Is.SameAs(second));
                Assert.That(first.DisposeCallCount, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task StaleTransportFailure_IsSuppressedAfterNewGenerationOwnsConnection()
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
            first.FailConnect(new System.IO.IOException("late failure"));

            Assert.DoesNotThrowAsync(async () => await firstConnect);
            Assert.That(first.DisposeCallCount, Is.EqualTo(1));
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

        private sealed class BlockingFactory : IRealtimeChannelFactory, IDisposable
        {
            private readonly IRealtimeChannel _first;
            private readonly IRealtimeChannel _second;
            private readonly ManualResetEventSlim _entered = new ManualResetEventSlim();
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();
            private int _calls;

            public BlockingFactory(IRealtimeChannel first, IRealtimeChannel second)
            {
                _first = first;
                _second = second;
            }

            public IRealtimeChannel Create(RealtimeTransport transport)
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    _entered.Set();
                    _release.Wait();
                    return _first;
                }
                return _second;
            }

            public void WaitUntilFirstCreateEntered() => _entered.Wait();
            public void ReleaseFirstCreate() => _release.Set();

            public void Dispose()
            {
                _entered.Dispose();
                _release.Dispose();
            }
        }

        private sealed class ControlledChannel : IRealtimeChannel
        {
            private readonly TaskCompletionSource<bool> _connect =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly bool _observeCancellation;
            private int _disposed;
            private int _disposeCalls;
            private int _cancellationObserved;

            public RealtimeChannelState State { get; private set; } =
                RealtimeChannelState.Disconnected;
            public int DisposeCallCount => Volatile.Read(ref _disposeCalls);
            public bool CancellationObserved => Volatile.Read(ref _cancellationObserved) != 0;
            public int CloseCount { get; private set; }

            public ControlledChannel(bool observeCancellation = false)
            {
                _observeCancellation = observeCancellation;
            }

            public Task ConnectAsync(
                RealtimeEndpoint endpoint, RealtimeCredentials credentials,
                CancellationToken cancellationToken)
            {
                State = RealtimeChannelState.Connecting;
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled(cancellationToken);
                if (_observeCancellation)
                {
                    cancellationToken.Register(() =>
                    {
                        Interlocked.Exchange(ref _cancellationObserved, 1);
                        _connect.TrySetCanceled(cancellationToken);
                    });
                }
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
                Interlocked.Increment(ref _disposeCalls);
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                State = RealtimeChannelState.Disconnected;
            }

            public void CompleteConnect()
            {
                State = RealtimeChannelState.Connected;
                _connect.TrySetResult(true);
            }

            public void FailConnect(Exception exception)
            {
                _connect.TrySetException(exception);
            }
        }
    }
}
