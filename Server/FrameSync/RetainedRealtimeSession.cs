namespace ZWebGLFrameWork.FrameSync
{
    internal sealed class RetainedRealtimeSession
    {
        public RealtimeSessionClaims Claims { get; }
        public long DisconnectedAtMilliseconds { get; }
        public uint LastAcceptedSequence { get; }
        public int LastAuthorityTick { get; }
        public long TimelineRevision { get; }

        public RetainedRealtimeSession(RealtimeSessionClaims claims,
            long disconnectedAtMilliseconds, uint lastAcceptedSequence,
            int lastAuthorityTick, long timelineRevision)
        {
            Claims = claims?.Copy() ?? throw new ArgumentNullException(nameof(claims));
            DisconnectedAtMilliseconds = disconnectedAtMilliseconds;
            LastAcceptedSequence = lastAcceptedSequence;
            LastAuthorityTick = lastAuthorityTick;
            TimelineRevision = timelineRevision;
        }
    }
}
