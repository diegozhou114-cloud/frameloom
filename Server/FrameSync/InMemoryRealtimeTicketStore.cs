using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class InMemoryRealtimeTicketStore : IRealtimeTicketStore
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, RealtimeSessionClaims> _claimsByTicket =
            new Dictionary<string, RealtimeSessionClaims>(StringComparer.Ordinal);

        public void Add(byte[] opaqueTicket, RealtimeSessionClaims claims)
        {
            if (opaqueTicket == null || opaqueTicket.Length == 0)
                throw new ArgumentException("Ticket is required.", nameof(opaqueTicket));
            if (claims == null) throw new ArgumentNullException(nameof(claims));
            lock (_gate)
                _claimsByTicket[Key(opaqueTicket)] = claims.Copy();
        }

        public ProtocolError Validate(byte[] opaqueTicket, long utcUnixSeconds,
            out RealtimeSessionClaims? claims)
        {
            claims = null;
            if (opaqueTicket == null || opaqueTicket.Length == 0)
                return ProtocolError.AuthenticationFailed;
            lock (_gate)
            {
                if (!_claimsByTicket.TryGetValue(
                        Key(opaqueTicket), out RealtimeSessionClaims? stored))
                    return ProtocolError.AuthenticationFailed;
                if (utcUnixSeconds >= stored.ExpiresAtUnixSeconds)
                    return ProtocolError.ExpiredTicket;
                claims = stored.Copy();
                return ProtocolError.None;
            }
        }

        private static string Key(byte[] ticket) => Convert.ToBase64String(ticket);
    }
}
