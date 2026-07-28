using ZWebGLFrameWork.Network.Contracts.Protocol;
using System.Runtime.CompilerServices;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class RealtimeSessionRouter
    {
        private readonly IRealtimeTicketStore _tickets;
        private readonly SessionRateLimiter _rateLimiter;
        private readonly IRealtimeMatchSink _matches;
        private readonly IRealtimeClock _clock;
        private readonly object _activeGate = new object();
        private readonly Dictionary<Guid, IRealtimeServerSession> _activeSessions =
            new Dictionary<Guid, IRealtimeServerSession>();
        private readonly Dictionary<IRealtimeServerSession, Guid> _sessionByConnection =
            new Dictionary<IRealtimeServerSession, Guid>(ReferenceEqualityComparer.Instance);
        private readonly ConditionalWeakTable<IRealtimeServerSession, DisconnectedMarker>
            _disconnected = new ConditionalWeakTable<IRealtimeServerSession, DisconnectedMarker>();

        public RealtimeSessionRouter(IRealtimeTicketStore tickets,
            SessionRateLimiter rateLimiter, IRealtimeMatchSink matches,
            IRealtimeClock clock)
        {
            _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            _rateLimiter = rateLimiter ??
                throw new ArgumentNullException(nameof(rateLimiter));
            _matches = matches ?? throw new ArgumentNullException(nameof(matches));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public async Task<ProtocolError> RouteAsync(
            IRealtimeServerSession connection, byte[] completeMessage,
            CancellationToken cancellationToken)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            cancellationToken.ThrowIfCancellationRequested();
            if (completeMessage == null ||
                completeMessage.Length > RealtimeProtocol.MaxMessageBytes)
                return ProtocolError.MessageTooLarge;

            DecodeResult decoded = RealtimeCodec.Decode(completeMessage);
            if (decoded.Error != ProtocolError.None) return decoded.Error;
            if (!connection.IsAuthenticated)
            {
                if (decoded.Kind != RealtimeMessageKind.AuthHello ||
                    decoded.AuthHello == null)
                    return ProtocolError.AuthenticationFailed;
                ProtocolError validation = _tickets.Validate(
                    decoded.AuthHello.OpaqueTicket, _clock.UtcUnixSeconds,
                    out RealtimeSessionClaims? claims);
                if (validation != ProtocolError.None) return validation;
                if (claims == null || claims.SessionId != decoded.AuthHello.SessionId)
                    return ProtocolError.WrongSession;
                lock (_activeGate)
                {
                    if (_disconnected.TryGetValue(connection, out _) ||
                        _sessionByConnection.ContainsKey(connection) ||
                        !_activeSessions.TryAdd(claims.SessionId, connection))
                        return ProtocolError.AuthenticationFailed;
                    _sessionByConnection.Add(connection, claims.SessionId);
                }
                try
                {
                    connection.Bind(claims.Copy());
                }
                catch
                {
                    Release(connection);
                    throw;
                }
                return IsOwner(claims.SessionId, connection)
                    ? ProtocolError.None
                    : ProtocolError.AuthenticationFailed;
            }

            RealtimeSessionClaims? authenticated = connection.Claims;
            if (authenticated == null) return ProtocolError.AuthenticationFailed;
            if (!IsOwner(authenticated.SessionId, connection))
                return ProtocolError.AuthenticationFailed;
            if (_clock.UtcUnixSeconds >= authenticated.ExpiresAtUnixSeconds)
            {
                Disconnect(connection);
                return ProtocolError.ExpiredTicket;
            }
            if (!_rateLimiter.TryConsume(
                    authenticated.SessionId, _clock.Milliseconds))
                return ProtocolError.RateLimited;
            if (decoded.Kind != RealtimeMessageKind.Command || decoded.Command == null)
                return ProtocolError.UnknownMessageKind;
            if (decoded.Command.PlayerId != authenticated.PlayerId)
                return ProtocolError.WrongPlayer;
            if (!string.Equals(decoded.Command.MatchId,
                    authenticated.MatchId, StringComparison.Ordinal))
                return ProtocolError.WrongSession;

            return await _matches.PostAsync(
                authenticated.MatchId, authenticated.PlayerId,
                decoded.Command, cancellationToken).ConfigureAwait(false);
        }

        public void Disconnect(IRealtimeServerSession connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            lock (_activeGate)
            {
                _disconnected.GetValue(
                    connection, static _ => DisconnectedMarker.Instance);
                ReleaseLocked(connection);
            }
        }

        private bool IsOwner(Guid sessionId, IRealtimeServerSession connection)
        {
            lock (_activeGate)
            {
                return _activeSessions.TryGetValue(
                           sessionId, out IRealtimeServerSession? owner) &&
                       ReferenceEquals(owner, connection);
            }
        }

        private void Release(IRealtimeServerSession connection)
        {
            lock (_activeGate)
                ReleaseLocked(connection);
        }

        private void ReleaseLocked(IRealtimeServerSession connection)
        {
            if (!_sessionByConnection.Remove(connection, out Guid sessionId))
                return;
            if (_activeSessions.TryGetValue(
                    sessionId, out IRealtimeServerSession? owner) &&
                ReferenceEquals(owner, connection))
                _activeSessions.Remove(sessionId);
        }

        private sealed class DisconnectedMarker
        {
            public static readonly DisconnectedMarker Instance =
                new DisconnectedMarker();

            private DisconnectedMarker()
            {
            }
        }
    }
}
