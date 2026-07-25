using System;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;
using UnityEngine;

namespace ZWebGLFrameWork.Network.Native
{
    internal sealed class NativeRealtimeChannelFactory : IRealtimeChannelFactory
    {
        public IRealtimeChannel Create(RealtimeTransport transport)
        {
            if (transport != RealtimeTransport.NativeKcp)
                throw new ArgumentOutOfRangeException(nameof(transport));
            return new Kcp2kRealtimeChannel();
        }

#if !UNITY_WEBGL
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            RealtimeChannelFactoryRegistry.Install(new NativeRealtimeChannelFactory());
        }
#endif
    }
}
