using System;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace ZWebGLFrameWork.Network.WebGL
{
    internal sealed class WebGlRealtimeChannelFactory : IRealtimeChannelFactory
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            RealtimeChannelFactoryRegistry.Install(
                RealtimeTransport.WebSocket, new WebGlRealtimeChannelFactory());
        }

        public IRealtimeChannel Create(RealtimeTransport transport)
        {
            if (transport != RealtimeTransport.WebSocket)
                throw new ArgumentOutOfRangeException(nameof(transport));
            return new WebGlRealtimeChannel();
        }
    }
}
