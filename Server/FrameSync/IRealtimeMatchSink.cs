using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeMatchSink
    {
        Task<ProtocolError> PostAsync(string matchId,
            int authenticatedPlayerId, CommandEnvelope command,
            CancellationToken cancellationToken);
    }
}
