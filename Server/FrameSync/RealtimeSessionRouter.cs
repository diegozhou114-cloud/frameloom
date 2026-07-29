using ZWebGLFrameWork.Network.Contracts.Protocol;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class RealtimeSessionRouter
    {
        private const long DisconnectedRetentionMilliseconds = 10_000;
        private readonly IRealtimeTicketStore _tickets;
        private readonly SessionRateLimiter _rateLimiter;
        private readonly IRealtimeMatchSink _matches;
        private readonly IRealtimeClock _clock;
        private readonly object _activeGate = new object();
        private readonly Dictionary<Guid, IRealtimeServerSession> _activeSessions =
            new Dictionary<Guid, IRealtimeServerSession>();
        private readonly Dictionary<IRealtimeServerSession, Guid> _sessionByConnection =
            new Dictionary<IRealtimeServerSession, Guid>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Guid, ActiveRealtimeSession> _activeStateBySession =
            new Dictionary<Guid, ActiveRealtimeSession>();
        private readonly Dictionary<Guid, RetainedRealtimeSession> _retainedSessions =
            new Dictionary<Guid, RetainedRealtimeSession>();
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
                return await AuthenticateAsync(connection, decoded)
                    .ConfigureAwait(false);
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

            ProtocolError result = await _matches.PostAsync(
                authenticated.MatchId, authenticated.PlayerId,
                decoded.Command, cancellationToken).ConfigureAwait(false);
            if (result == ProtocolError.None)
                RecordAcceptedSequence(authenticated.SessionId, connection,
                    decoded.Command.Sequence);
            return result;
        }

        public void Disconnect(IRealtimeServerSession connection)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            lock (_activeGate)
            {
                _disconnected.GetValue(
                    connection, static _ => DisconnectedMarker.Instance);
                ReleaseLocked(connection, retain: connection.Claims != null);
            }
        }

        private Task<ProtocolError> AuthenticateAsync(
            IRealtimeServerSession connection, DecodeResult decoded)
        {
            if (decoded.Kind == RealtimeMessageKind.AuthHello &&
                decoded.AuthHello != null)
                return Task.FromResult(AuthenticateTicket(connection, decoded.AuthHello));
            if (decoded.Kind == RealtimeMessageKind.ResumeHello &&
                decoded.ResumeHello != null)
                return Task.FromResult(Resume(connection, decoded.ResumeHello));
            return Task.FromResult(ProtocolError.AuthenticationFailed);
        }

        private ProtocolError AuthenticateTicket(
            IRealtimeServerSession connection, AuthHello hello)
        {
            ProtocolError validation = _tickets.Validate(
                hello.OpaqueTicket, _clock.UtcUnixSeconds,
                out RealtimeSessionClaims? claims);
            if (validation != ProtocolError.None) return validation;
            if (claims == null || claims.SessionId != hello.SessionId)
                return ProtocolError.WrongSession;
            var state = new ActiveRealtimeSession(claims);
            if (!Reserve(connection, state)) return ProtocolError.AuthenticationFailed;
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

        private ProtocolError Resume(IRealtimeServerSession connection, ResumeHello hello)
        {
            RetainedRealtimeSession? retained;
            lock (_activeGate)
            {
                if (_disconnected.TryGetValue(connection, out _) ||
                    _sessionByConnection.ContainsKey(connection) ||
                    !_retainedSessions.TryGetValue(hello.SessionId, out retained))
                    return ProtocolError.AuthenticationFailed;
                long elapsed = _clock.Milliseconds - retained.DisconnectedAtMilliseconds;
                if (elapsed < 0 || elapsed >= DisconnectedRetentionMilliseconds)
                {
                    _retainedSessions.Remove(hello.SessionId);
                    return ProtocolError.ExpiredTicket;
                }
                if (!CryptographicOperations.FixedTimeEquals(
                        hello.ResumeToken, retained.Claims.ResumeToken) ||
                    hello.LastAcceptedSequence != retained.LastAcceptedSequence ||
                    hello.LastAuthorityTick != retained.LastAuthorityTick ||
                    hello.TimelineRevision != retained.TimelineRevision ||
                    _activeSessions.ContainsKey(hello.SessionId))
                    return ProtocolError.AuthenticationFailed;

                var state = new ActiveRealtimeSession(retained.Claims,
                    retained.LastAcceptedSequence, retained.LastAuthorityTick,
                    retained.TimelineRevision);
                _activeSessions.Add(hello.SessionId, connection);
                _sessionByConnection.Add(connection, hello.SessionId);
                _activeStateBySession.Add(hello.SessionId, state);
                _retainedSessions.Remove(hello.SessionId);
            }
            try
            {
                connection.Bind(retained.Claims.Copy());
            }
            catch
            {
                Release(connection);
                throw;
            }
            return IsOwner(hello.SessionId, connection)
                ? ProtocolError.None
                : ProtocolError.AuthenticationFailed;
        }

        private bool Reserve(IRealtimeServerSession connection,
            ActiveRealtimeSession state)
        {
            lock (_activeGate)
            {
                RemoveExpiredRetentionLocked(state.Claims.SessionId);
                if (_disconnected.TryGetValue(connection, out _) ||
                    _sessionByConnection.ContainsKey(connection) ||
                    _retainedSessions.ContainsKey(state.Claims.SessionId) ||
                    !_activeSessions.TryAdd(state.Claims.SessionId, connection))
                    return false;
                _sessionByConnection.Add(connection, state.Claims.SessionId);
                _activeStateBySession.Add(state.Claims.SessionId, state);
                return true;
            }
        }

        private void RemoveExpiredRetentionLocked(Guid sessionId)
        {
            if (_retainedSessions.TryGetValue(sessionId,
                    out RetainedRealtimeSession? retained) &&
                _clock.Milliseconds - retained.DisconnectedAtMilliseconds >=
                    DisconnectedRetentionMilliseconds)
                _retainedSessions.Remove(sessionId);
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
                ReleaseLocked(connection, retain: false);
        }

        private void ReleaseLocked(IRealtimeServerSession connection, bool retain)
        {
            if (!_sessionByConnection.Remove(connection, out Guid sessionId))
                return;
            if (_activeSessions.TryGetValue(
                    sessionId, out IRealtimeServerSession? owner) &&
                ReferenceEquals(owner, connection))
            {
                _activeSessions.Remove(sessionId);
                if (_activeStateBySession.Remove(sessionId,
                        out ActiveRealtimeSession? state) && retain)
                    _retainedSessions[sessionId] = new RetainedRealtimeSession(
                        state.Claims, _clock.Milliseconds, state.LastAcceptedSequence,
                        state.LastAuthorityTick, state.TimelineRevision);
            }
        }

        private void RecordAcceptedSequence(Guid sessionId,
            IRealtimeServerSession connection, uint sequence)
        {
            lock (_activeGate)
            {
                if (_activeSessions.TryGetValue(sessionId,
                        out IRealtimeServerSession? owner) &&
                    ReferenceEquals(owner, connection) &&
                    _activeStateBySession.TryGetValue(sessionId,
                        out ActiveRealtimeSession? state) &&
                    sequence > state.LastAcceptedSequence)
                    state.LastAcceptedSequence = sequence;
            }
        }

        private sealed class ActiveRealtimeSession
        {
            public RealtimeSessionClaims Claims { get; }
            public uint LastAcceptedSequence { get; set; }
            public int LastAuthorityTick { get; }
            public long TimelineRevision { get; }

            public ActiveRealtimeSession(RealtimeSessionClaims claims,
                uint lastAcceptedSequence = 0, int lastAuthorityTick = -1,
                long timelineRevision = 0)
            {
                Claims = claims?.Copy() ?? throw new ArgumentNullException(nameof(claims));
                LastAcceptedSequence = lastAcceptedSequence;
                LastAuthorityTick = lastAuthorityTick;
                TimelineRevision = timelineRevision;
            }
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
