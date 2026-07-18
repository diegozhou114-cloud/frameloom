using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.Runtime
{
    public interface IRealtimeChannelFactory
    {
        IRealtimeChannel Create(RealtimeTransport transport);
    }
}
