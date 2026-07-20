using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public interface IFrameBroadcaster
    {
        void Broadcast(AuthorityFrame frame);
    }
}
