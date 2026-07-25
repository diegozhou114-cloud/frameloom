using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class RealtimeChannelFactoryRegistryTests
    {
        [SetUp]
        public void SetUp() => RealtimeChannelFactoryRegistry.ResetForTests();

        [TearDown]
        public void TearDown() => RealtimeChannelFactoryRegistry.ResetForTests();

        [Test]
        public void Registry_ExposesTransportKeyedInstall()
        {
            MethodInfo install = typeof(RealtimeChannelFactoryRegistry).GetMethod(
                "Install", new[] { typeof(RealtimeTransport), typeof(IRealtimeChannelFactory) });

            Assert.That(install, Is.Not.Null);
        }

        [Test]
        public void Registry_RoutesBothTransportsWithoutOverwrite()
        {
            var webSocketChannel = new StubChannel();
            var nativeChannel = new StubChannel();
            var webSocketFactory = new StubFactory(RealtimeTransport.WebSocket, webSocketChannel);
            var nativeFactory = new StubFactory(RealtimeTransport.NativeKcp, nativeChannel);

            RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, nativeFactory);
            RealtimeChannelFactoryRegistry.Install(RealtimeTransport.WebSocket, webSocketFactory);

            Assert.That(RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.NativeKcp),
                Is.SameAs(nativeChannel));
            Assert.That(RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.WebSocket),
                Is.SameAs(webSocketChannel));
            Assert.That(nativeFactory.CreateCount, Is.EqualTo(1));
            Assert.That(webSocketFactory.CreateCount, Is.EqualTo(1));
        }

        [Test]
        public void Registry_RejectsNullDuplicateAndMissingRegistrations()
        {
            var factory = new StubFactory(RealtimeTransport.NativeKcp, new StubChannel());

            Assert.Throws<ArgumentNullException>(() =>
                RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, null));
            RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, factory);
            Assert.Throws<InvalidOperationException>(() =>
                RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp, factory));
            Assert.Throws<InvalidOperationException>(() =>
                RealtimeChannelFactoryRegistry.Current.Create(RealtimeTransport.WebSocket));
        }

        [Test]
        public void Registry_ResetClearsEntriesButKeepsStableRouter()
        {
            IRealtimeChannelFactory router = RealtimeChannelFactoryRegistry.Current;
            RealtimeChannelFactoryRegistry.Install(RealtimeTransport.NativeKcp,
                new StubFactory(RealtimeTransport.NativeKcp, new StubChannel()));

            RealtimeChannelFactoryRegistry.ResetForTests();

            Assert.That(RealtimeChannelFactoryRegistry.Current, Is.SameAs(router));
            Assert.Throws<InvalidOperationException>(() =>
                router.Create(RealtimeTransport.NativeKcp));
        }

        [Test]
        public async Task ConnectionController_ConsumesStableRegistryRouter()
        {
            var channel = new StubChannel();
            RealtimeChannelFactoryRegistry.Install(
                RealtimeTransport.NativeKcp,
                new StubFactory(RealtimeTransport.NativeKcp, channel));
            IRealtimeChannelFactory router = RealtimeChannelFactoryRegistry.Current;
            var controller = new RealtimeConnectionController(router);

            await controller.ConnectAsync(
                new RealtimeEndpoint(
                    RealtimeTransport.NativeKcp, new Uri("udp://127.0.0.1:8888")),
                new RealtimeCredentials
                {
                    SessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    OpaqueTicket = new byte[] { 1 },
                    ResumeToken = new byte[] { 2 },
                    MacKey = new byte[32],
                    ExpiresAtUnixSeconds = long.MaxValue
                },
                CancellationToken.None);

            Assert.That(controller.Current, Is.SameAs(channel));
            Assert.That(RealtimeChannelFactoryRegistry.Current, Is.SameAs(router));
        }

        private sealed class StubFactory : IRealtimeChannelFactory
        {
            private readonly RealtimeTransport _expected;
            private readonly IRealtimeChannel _channel;
            public int CreateCount { get; private set; }

            public StubFactory(RealtimeTransport expected, IRealtimeChannel channel)
            {
                _expected = expected;
                _channel = channel;
            }

            public IRealtimeChannel Create(RealtimeTransport transport)
            {
                Assert.That(transport, Is.EqualTo(_expected));
                CreateCount++;
                return _channel;
            }
        }

        private sealed class StubChannel : IRealtimeChannel
        {
            public RealtimeChannelState State => RealtimeChannelState.Disconnected;
            public Task ConnectAsync(RealtimeEndpoint endpoint, RealtimeCredentials credentials,
                CancellationToken cancellationToken) => Task.CompletedTask;
            public Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken) =>
                Task.CompletedTask;
            public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken) =>
                Task.FromResult(Array.Empty<byte>());
            public Task CloseAsync(RealtimeCloseReason reason, CancellationToken cancellationToken) =>
                Task.CompletedTask;
            public void Dispose() { }
        }
    }
}
