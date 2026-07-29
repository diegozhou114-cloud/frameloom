using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Network.WebGL;

namespace ZWebGLFrameWork.Network.WebGL
{
    internal sealed class NativeWebSocketClientFactory : INativeWebSocketClientFactory
    {
        public INativeWebSocketClient Create(
            Uri uri, IReadOnlyList<string> subprotocols) =>
            throw new NotSupportedException("The native WebSocket adapter is unavailable in .NET tests.");
    }
}
