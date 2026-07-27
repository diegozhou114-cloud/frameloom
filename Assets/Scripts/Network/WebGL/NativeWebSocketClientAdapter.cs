using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Network.WebGL
{
    internal sealed class NativeWebSocketClientFactory : INativeWebSocketClientFactory
    {
        public INativeWebSocketClient Create(Uri uri, IReadOnlyList<string> subprotocols)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (subprotocols == null) throw new ArgumentNullException(nameof(subprotocols));
            return new NativeWebSocketClientAdapter(new NativeWebSocket.WebSocket(
                uri.AbsoluteUri, new List<string>(subprotocols)));
        }
    }

    internal sealed class NativeWebSocketClientAdapter : INativeWebSocketClient
    {
        private readonly NativeWebSocket.IWebSocket _socket;
        public event Action Opened;
        public event Action<byte[]> MessageReceived;
        public event Action<string> Error;
        public event Action<ushort> Closed;

        public NativeWebSocketClientAdapter(NativeWebSocket.IWebSocket socket)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _socket.OnOpen += () => Opened?.Invoke();
            _socket.OnMessage += bytes => MessageReceived?.Invoke(bytes);
            _socket.OnError += error => Error?.Invoke(error);
            _socket.OnClose += code => Closed?.Invoke((ushort)code);
        }

        public Task ConnectAsync() => _socket.Connect();

        public Task SendAsync(byte[] completeMessage)
        {
            if (completeMessage == null) throw new ArgumentNullException(nameof(completeMessage));
            return _socket.Send(completeMessage);
        }

        public Task CloseAsync() => _socket.Close();

        public void DispatchMessageQueue()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            (_socket as NativeWebSocket.WebSocket)?.DispatchMessageQueue();
#endif
        }

        public void Dispose() { }
    }
}
