using System;
using System.Threading;

namespace ZWebGLFrameWork.Network
{
    public sealed class NetworkClientBinding : IDisposable
    {
        private readonly INetworkClient _client;
        private readonly int _ownedGeneration;
        private readonly Func<int> _currentGeneration;
        private readonly Action _connected;
        private readonly Action<string> _disconnected;
        private readonly Action<string> _connectFailed;
        private readonly Action<byte[]> _dataReceived;
        private int _disposed;

        public NetworkClientBinding(
            INetworkClient client,
            int ownedGeneration,
            Func<int> currentGeneration,
            Action connected,
            Action<string> disconnected,
            Action<string> connectFailed,
            Action<byte[]> dataReceived)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _ownedGeneration = ownedGeneration;
            _currentGeneration = currentGeneration
                ?? throw new ArgumentNullException(nameof(currentGeneration));

            if (connected == null) throw new ArgumentNullException(nameof(connected));
            if (disconnected == null) throw new ArgumentNullException(nameof(disconnected));
            if (connectFailed == null) throw new ArgumentNullException(nameof(connectFailed));
            if (dataReceived == null) throw new ArgumentNullException(nameof(dataReceived));

            _connected = () => DispatchIfCurrent(connected);
            _disconnected = reason => DispatchIfCurrent(() => disconnected(reason));
            _connectFailed = reason => DispatchIfCurrent(() => connectFailed(reason));
            _dataReceived = data => DispatchIfCurrent(() => dataReceived(data));

            _client.OnConnected += _connected;
            _client.OnDisconnected += _disconnected;
            _client.OnConnectFailed += _connectFailed;
            _client.OnDataReceived += _dataReceived;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _client.OnConnected -= _connected;
            _client.OnDisconnected -= _disconnected;
            _client.OnConnectFailed -= _connectFailed;
            _client.OnDataReceived -= _dataReceived;
        }

        private void DispatchIfCurrent(Action callback)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            if (_currentGeneration() != _ownedGeneration) return;
            callback();
        }
    }
}
