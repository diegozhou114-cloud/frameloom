using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Auth;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class KcpIngress
    {
        private const int AuthenticatedPacketSessionOffset = 2;
        private const int MinimumAuthenticatedPacketBytes = 2 + 16 + 8 + 4 + 1 + 32;
        private readonly RealtimeSessionRouter _router;
        private readonly INativeSessionKeyStore _keys;
        private readonly object _replayGate = new object();
        private readonly Dictionary<Guid, SequenceReplayWindow> _replayWindows =
            new Dictionary<Guid, SequenceReplayWindow>();

        public KcpIngress(RealtimeSessionRouter router, INativeSessionKeyStore keys)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        }

        public Task<ProtocolError> RouteAuthenticatedMessageAsync(
            IRealtimeServerSession session, byte[] completeMessage,
            CancellationToken cancellationToken)
        {
            return _router.RouteAsync(session, completeMessage, cancellationToken);
        }

        public void Disconnect(IRealtimeServerSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            RealtimeSessionClaims? claims = session.Claims;
            if (claims != null)
            {
                lock (_replayGate) _replayWindows.Remove(claims.SessionId);
            }
            _router.Disconnect(session);
        }

        public async Task<ProtocolError> RouteAuthenticatedPacketAsync(
            IRealtimeServerSession session, byte[] packet,
            CancellationToken cancellationToken)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!TryReadSessionId(packet, out Guid sessionId))
                return ProtocolError.Truncated;
            RealtimeSessionClaims? claims = session.Claims;
            if (claims != null && claims.SessionId != sessionId)
                return ProtocolError.WrongSession;
            ProtocolError keyError = _keys.TryGetMacKey(sessionId, out byte[]? macKey);
            if (keyError != ProtocolError.None) return keyError;

            SequenceReplayWindow replayWindow;
            lock (_replayGate)
            {
                if (!_replayWindows.TryGetValue(sessionId, out replayWindow!))
                {
                    replayWindow = new SequenceReplayWindow();
                    _replayWindows.Add(sessionId, replayWindow);
                }
            }

            ProtocolError decodeError = AuthenticatedPacketCodec.TryDecode(
                packet, sessionId, macKey!, replayWindow,
                out AuthenticatedPacket decoded);
            return decodeError == ProtocolError.None
                ? await RouteAuthenticatedMessageAsync(
                    session, decoded.Payload, cancellationToken).ConfigureAwait(false)
                : decodeError;
        }

        private static bool TryReadSessionId(byte[] packet, out Guid sessionId)
        {
            sessionId = Guid.Empty;
            if (packet == null || packet.Length < MinimumAuthenticatedPacketBytes)
                return false;
            var sessionBytes = new byte[16];
            Buffer.BlockCopy(packet, AuthenticatedPacketSessionOffset,
                sessionBytes, 0, sessionBytes.Length);
            sessionId = new Guid(sessionBytes);
            return sessionId != Guid.Empty;
        }
    }
}
