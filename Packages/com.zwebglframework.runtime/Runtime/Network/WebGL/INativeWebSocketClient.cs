using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("ZWebGLFrameWork.Network.Tests")]

namespace ZWebGLFrameWork.Network.WebGL
{
    internal interface INativeWebSocketClient : IDisposable
    {
        event Action Opened;
        event Action<byte[]> MessageReceived;
        event Action<string> Error;
        event Action<ushort> Closed;
        Task ConnectAsync();
        Task SendAsync(byte[] completeMessage);
        Task CloseAsync();
        void DispatchMessageQueue();
    }

    internal interface INativeWebSocketClientFactory
    {
        INativeWebSocketClient Create(Uri uri, IReadOnlyList<string> subprotocols);
    }
}
