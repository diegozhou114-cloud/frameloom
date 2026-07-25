using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Native;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class Kcp2kRealtimeChannelLifecycleTests
    {
        private static readonly RealtimeEndpoint Endpoint = new RealtimeEndpoint(
            RealtimeTransport.NativeKcp, new Uri("kcp://127.0.0.1:7777"));

        [Test]
        public async Task SynchronousConnectFailure_DisposesClientAndStopsPump()
        {
            var client = new ControlledKcp2kClient(TestCredentials.Valid)
            {
                ConnectException = new SocketException((int)SocketError.AddressNotAvailable)
            };
            using var channel = CreateChannel(client);

            SocketException failure = null;
            try
            {
                await channel.ConnectAsync(
                    Endpoint, TestCredentials.Valid, CancellationToken.None);
            }
            catch (SocketException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Disconnected));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
            AssertTicksStop(client);
        }

        [Test]
        public async Task NullClientFactoryResult_LeavesChannelDisconnected()
        {
            using var channel = new Kcp2kRealtimeChannel((callbacks, config) => null);

            InvalidOperationException failure = null;
            try
            {
                await channel.ConnectAsync(
                    Endpoint, TestCredentials.Valid, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Disconnected));
        }

        [Test]
        public async Task RemoteDisconnect_StopsPumpAndWakesPendingReceiver()
        {
            var client = new ControlledKcp2kClient(TestCredentials.Valid);
            using var channel = CreateChannel(client);
            await channel.ConnectAsync(
                Endpoint, TestCredentials.Valid, CancellationToken.None);
            Task<byte[]> firstReceive =
                channel.ReceiveAsync(CancellationToken.None);
            Task<byte[]> secondReceive =
                channel.ReceiveAsync(CancellationToken.None);

            client.DisconnectFromTransport();

            await AssertDisconnectedAsync(firstReceive);
            await AssertDisconnectedAsync(secondReceive);
            await AssertDisconnectedAsync(
                channel.ReceiveAsync(CancellationToken.None));
            Assert.That(channel.State, Is.EqualTo(RealtimeChannelState.Disconnected));
            Assert.That(client.DisposeCount, Is.EqualTo(1));
            AssertTicksStop(client);
        }

        [Test]
        public async Task ClosedChannel_CannotBeReusedAcrossSessions()
        {
            var first = new ControlledKcp2kClient(TestCredentials.Valid);
            var second = new ControlledKcp2kClient(TestCredentials.Valid);
            int created = 0;
            using var channel = new Kcp2kRealtimeChannel((callbacks, config) =>
            {
                ControlledKcp2kClient client = created++ == 0 ? first : second;
                client.Attach(callbacks);
                return client;
            });
            await channel.ConnectAsync(
                Endpoint, TestCredentials.Valid, CancellationToken.None);
            await channel.CloseAsync(
                RealtimeCloseReason.Normal, CancellationToken.None);

            InvalidOperationException failure = null;
            try
            {
                await channel.ConnectAsync(
                    Endpoint, TestCredentials.Valid, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.Not.Null);
            Assert.That(created, Is.EqualTo(1));
            Assert.That(second.DisposeCount, Is.Zero);
        }

        [Test]
        public async Task ClosedBeforeConnect_CannotBeConnected()
        {
            var client = new ControlledKcp2kClient(TestCredentials.Valid);
            int created = 0;
            using var channel = new Kcp2kRealtimeChannel((callbacks, config) =>
            {
                created++;
                client.Attach(callbacks);
                return client;
            });
            await channel.CloseAsync(
                RealtimeCloseReason.Normal, CancellationToken.None);

            InvalidOperationException failure = null;
            try
            {
                await channel.ConnectAsync(
                    Endpoint, TestCredentials.Valid, CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(created, Is.Zero);
        }

        private static Kcp2kRealtimeChannel CreateChannel(
            ControlledKcp2kClient client)
        {
            return new Kcp2kRealtimeChannel((callbacks, config) =>
            {
                client.Attach(callbacks);
                return client;
            });
        }

        private static void AssertTicksStop(ControlledKcp2kClient client)
        {
            int tickCount = client.TickCount;
            Thread.Sleep(25);
            Assert.That(client.TickCount, Is.EqualTo(tickCount));
        }

        private static async Task AssertDisconnectedAsync(Task<byte[]> receive)
        {
            Assert.That(
                await Task.WhenAny(receive, Task.Delay(TimeSpan.FromSeconds(1))),
                Is.SameAs(receive));
            InvalidOperationException failure = null;
            try
            {
                await receive;
            }
            catch (InvalidOperationException exception)
            {
                failure = exception;
            }
            Assert.That(failure, Is.Not.Null);
        }
    }

    internal sealed class ControlledKcp2kClient : IKcp2kClient
    {
        private readonly RealtimeCredentials _credentials;
        private KcpNativeCallbacks _callbacks;
        private int _accepted;
        private int _disposeCount;
        private int _tickCount;

        public Exception ConnectException { get; set; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int TickCount => Volatile.Read(ref _tickCount);

        public ControlledKcp2kClient(RealtimeCredentials credentials)
        {
            _credentials = credentials;
        }

        public void Attach(KcpNativeCallbacks callbacks)
        {
            _callbacks = callbacks;
        }

        public void Connect(string host, ushort port)
        {
            if (ConnectException != null) throw ConnectException;
            _callbacks.Connected();
        }

        public void Send(byte[] message)
        {
            if (Interlocked.Exchange(ref _accepted, 1) != 0) return;
            byte[] accepted = RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = _credentials.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            });
            byte[] packet = AuthenticatedPacketCodec.Encode(
                _credentials.SessionId, 1, accepted, _credentials.MacKey);
            _callbacks.Data(
                new ArraySegment<byte>(packet), kcp2k.KcpChannel.Reliable);
        }

        public void TickIncoming()
        {
            Interlocked.Increment(ref _tickCount);
        }

        public void TickOutgoing()
        {
            Interlocked.Increment(ref _tickCount);
        }

        public void DisconnectFromTransport()
        {
            _callbacks.Disconnected();
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
        }
    }
}
