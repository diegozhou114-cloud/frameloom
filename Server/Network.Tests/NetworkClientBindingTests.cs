using System;
using NUnit.Framework;
using ZWebGLFrameWork.Network;

namespace ZWebGLFrameWork.Network.ServerTests
{
    public sealed class NetworkClientBindingTests
    {
        [Test]
        public void NetworkManager_Reconnect_DoesNotLeakOrDispatchStaleCallbacks()
        {
            int generation = 1;
            int connectedCount = 0;
            string disconnectReason = null;
            string failureReason = null;
            byte[] received = null;
            var first = new FakeNetworkClient();

            var firstBinding = new NetworkClientBinding(
                first,
                generation,
                () => generation,
                () => connectedCount++,
                reason => disconnectReason = reason,
                reason => failureReason = reason,
                data => received = data);

            first.RaiseConnected();
            Assert.That(connectedCount, Is.EqualTo(1));

            generation = 2;
            first.RaiseConnected();
            first.RaiseDisconnected("stale-disconnect");
            first.RaiseConnectFailed("stale-failure");
            first.RaiseData(new byte[] { 1 });

            Assert.That(connectedCount, Is.EqualTo(1));
            Assert.That(disconnectReason, Is.Null);
            Assert.That(failureReason, Is.Null);
            Assert.That(received, Is.Null);

            firstBinding.Dispose();
            Assert.That(first.TotalSubscriberCount, Is.Zero);

            var second = new FakeNetworkClient();
            using (new NetworkClientBinding(
                second,
                generation,
                () => generation,
                () => connectedCount++,
                reason => disconnectReason = reason,
                reason => failureReason = reason,
                data => received = data))
            {
                second.RaiseDisconnected("current-disconnect");
                second.RaiseConnectFailed("current-failure");
                second.RaiseData(new byte[] { 2 });

                Assert.That(disconnectReason, Is.EqualTo("current-disconnect"));
                Assert.That(failureReason, Is.EqualTo("current-failure"));
                Assert.That(received, Is.EqualTo(new byte[] { 2 }));
                Assert.That(second.TotalSubscriberCount, Is.EqualTo(4));
            }

            Assert.That(second.TotalSubscriberCount, Is.Zero);
        }

        private sealed class FakeNetworkClient : INetworkClient
        {
            private Action _connected;
            private Action<string> _disconnected;
            private Action<byte[]> _dataReceived;
            private Action<string> _connectFailed;

            public ConnectionState State => ConnectionState.Disconnected;
            public NetworkProtocol Protocol => NetworkProtocol.TCP;
            public bool IsConnected => false;
            public int TotalSubscriberCount { get; private set; }

            public event Action OnConnected
            {
                add { _connected += value; TotalSubscriberCount++; }
                remove { _connected -= value; TotalSubscriberCount--; }
            }

            public event Action<string> OnDisconnected
            {
                add { _disconnected += value; TotalSubscriberCount++; }
                remove { _disconnected -= value; TotalSubscriberCount--; }
            }

            public event Action<byte[]> OnDataReceived
            {
                add { _dataReceived += value; TotalSubscriberCount++; }
                remove { _dataReceived -= value; TotalSubscriberCount--; }
            }

            public event Action<string> OnConnectFailed
            {
                add { _connectFailed += value; TotalSubscriberCount++; }
                remove { _connectFailed -= value; TotalSubscriberCount--; }
            }

            public void Connect(string host, int port) { }
            public void Disconnect() { }
            public void Send(byte[] data) { }
            public void Send(byte[] data, int offset, int length) { }
            public void Update() { }

            public void RaiseConnected() => _connected?.Invoke();
            public void RaiseDisconnected(string reason) => _disconnected?.Invoke(reason);
            public void RaiseData(byte[] data) => _dataReceived?.Invoke(data);
            public void RaiseConnectFailed(string reason) => _connectFailed?.Invoke(reason);
        }
    }
}
