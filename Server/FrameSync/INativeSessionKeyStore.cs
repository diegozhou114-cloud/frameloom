using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public interface INativeSessionKeyStore
    {
        ProtocolError TryGetMacKey(Guid sessionId, out byte[]? macKey);
    }
}
