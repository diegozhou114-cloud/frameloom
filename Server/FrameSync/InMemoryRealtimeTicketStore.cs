using ZWebGLFrameWork.Network.Contracts.Protocol;
using System.Security.Cryptography;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class InMemoryRealtimeTicketStore : IRealtimeTicketStore,
        INativeSessionKeyStore
    {
        private const int SecretBytes = 32;
        private const long TicketLifetimeSeconds = 15 * 60;
        private readonly object _gate = new object();
        private readonly Dictionary<string, RealtimeSessionClaims> _claimsByTicket =
            new Dictionary<string, RealtimeSessionClaims>(StringComparer.Ordinal);
        private readonly Dictionary<Guid, byte[]> _macKeysBySession =
            new Dictionary<Guid, byte[]>();

        public IssuedRealtimeCredentials Issue(string matchId, int playerId,
            long utcUnixSeconds)
        {
            if (utcUnixSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(utcUnixSeconds));
            byte[] ticket = RandomSecret();
            byte[] resumeToken = RandomSecret();
            byte[] macKey = RandomSecret();
            long expiresAt = checked(utcUnixSeconds + TicketLifetimeSeconds);
            var claims = new RealtimeSessionClaims(Guid.NewGuid(), matchId, playerId,
                resumeToken, expiresAt);
            Add(ticket, claims, macKey);
            return new IssuedRealtimeCredentials(claims.SessionId, claims.PlayerId,
                ticket, resumeToken, macKey, expiresAt);
        }

        public void Add(byte[] opaqueTicket, RealtimeSessionClaims claims)
        {
            Add(opaqueTicket, claims, null);
        }

        public ProtocolError TryGetMacKey(Guid sessionId, out byte[]? macKey)
        {
            lock (_gate)
            {
                if (_macKeysBySession.TryGetValue(sessionId, out byte[]? stored))
                {
                    macKey = (byte[])stored.Clone();
                    return ProtocolError.None;
                }
            }
            macKey = null;
            return ProtocolError.WrongSession;
        }

        private void Add(byte[] opaqueTicket, RealtimeSessionClaims claims,
            byte[]? macKey)
        {
            if (opaqueTicket == null || opaqueTicket.Length == 0)
                throw new ArgumentException("Ticket is required.", nameof(opaqueTicket));
            if (claims == null) throw new ArgumentNullException(nameof(claims));
            if (macKey != null && macKey.Length != SecretBytes)
                throw new ArgumentException("MAC key must be 32 bytes.", nameof(macKey));
            lock (_gate)
            {
                _claimsByTicket[Key(opaqueTicket)] = claims.Copy();
                if (macKey != null)
                    _macKeysBySession[claims.SessionId] = (byte[])macKey.Clone();
            }
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

        private static byte[] RandomSecret()
        {
            var secret = new byte[SecretBytes];
            RandomNumberGenerator.Fill(secret);
            return secret;
        }
    }
}
