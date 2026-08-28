using System;

namespace ZWebGLFrameWork.Network.Native
{
    internal interface IKcp2kClient : IDisposable
    {
        void Connect(string host, ushort port);
        void Send(byte[] message);
        void TickIncoming();
        void TickOutgoing();
    }

    internal sealed class KcpNativeCallbacks
    {
        public Action Connected { get; set; }
        public Action<ArraySegment<byte>, kcp2k.KcpChannel> Data { get; set; }
        public Action Disconnected { get; set; }
        public Action<kcp2k.ErrorCode, string> Error { get; set; }
    }

    internal sealed class Kcp2kClientFacade : IKcp2kClient
    {
        private readonly kcp2k.KcpClient _client;

        public Kcp2kClientFacade(KcpNativeCallbacks callbacks, kcp2k.KcpConfig config)
        {
            if (callbacks == null) throw new ArgumentNullException(nameof(callbacks));
            if (config == null) throw new ArgumentNullException(nameof(config));
            _client = new kcp2k.KcpClient(
                callbacks.Connected,
                callbacks.Data,
                callbacks.Disconnected,
                callbacks.Error,
                config);
        }

        public void Connect(string host, ushort port) => _client.Connect(host, port);

        public void Send(byte[] message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            _client.Send(new ArraySegment<byte>(message), kcp2k.KcpChannel.Reliable);
        }

        public void TickIncoming() => _client.TickIncoming();
        public void TickOutgoing() => _client.TickOutgoing();
        public void Dispose() => _client.Disconnect();
    }
}
