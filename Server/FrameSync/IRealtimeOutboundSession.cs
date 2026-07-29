namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeOutboundSession
    {
        Guid SessionId { get; }
        string MatchId { get; }

        // Implementations own transport-specific ordering and backpressure.
        bool TryEnqueue(byte[] completeMessage);
    }

    public interface IRealtimeOutboundSessionRegistry
    {
        void Register(IRealtimeOutboundSession session);
        void Unregister(IRealtimeOutboundSession session);
    }
}
