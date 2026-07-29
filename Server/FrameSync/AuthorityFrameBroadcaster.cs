using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class AuthorityFrameBroadcaster : IFrameBroadcaster
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Guid, IRealtimeOutboundSession> _sessions =
            new Dictionary<Guid, IRealtimeOutboundSession>();

        public void Register(IRealtimeOutboundSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (session.SessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.", nameof(session));
            if (string.IsNullOrWhiteSpace(session.MatchId))
                throw new ArgumentException("Match ID is required.", nameof(session));

            lock (_gate)
            {
                if (_sessions.TryGetValue(session.SessionId,
                        out IRealtimeOutboundSession? existing) &&
                    !ReferenceEquals(existing, session))
                    throw new InvalidOperationException(
                        "An outbound recipient is already registered for this session.");
                _sessions[session.SessionId] = session;
            }
        }

        public void Unregister(IRealtimeOutboundSession session)
        {
            if (session == null) return;
            lock (_gate)
            {
                if (_sessions.TryGetValue(session.SessionId,
                        out IRealtimeOutboundSession? existing) &&
                    ReferenceEquals(existing, session))
                    _sessions.Remove(session.SessionId);
            }
        }

        public void Broadcast(AuthorityFrame frame)
        {
            byte[] completeMessage = RealtimeCodec.Encode(frame);
            IRealtimeOutboundSession[] recipients;
            lock (_gate)
            {
                recipients = _sessions.Values
                    .Where(session => string.Equals(session.MatchId, frame.MatchId,
                        StringComparison.Ordinal))
                    .ToArray();
            }

            foreach (IRealtimeOutboundSession recipient in recipients)
            {
                try
                {
                    recipient.TryEnqueue((byte[])completeMessage.Clone());
                }
                catch
                {
                    // A closed or faulty transport must not fault authoritative simulation.
                }
            }
        }
    }
}
