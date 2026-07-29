using System.Net;
using System.Collections.Concurrent;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class KcpServerIngressHost : IDisposable
    {
        private readonly object _gate = new object();
        private readonly KcpIngress _ingress;
        private readonly INativeSessionKeyStore _keys;
        private readonly kcp2k.KcpServer _server;
        private readonly Dictionary<int, NativeSession> _sessions = new();
        private readonly ConcurrentQueue<IncomingPacket> _incoming = new();
        private readonly ConcurrentQueue<OutgoingPacket> _outgoing = new();
        private readonly IRealtimeOutboundSessionRegistry _outboundSessions;
        private CancellationTokenSource _cancellation;
        private Task _pump;

        public IPEndPoint LocalEndPoint => _server.LocalEndPoint as IPEndPoint;

        public KcpServerIngressHost(KcpIngress ingress, INativeSessionKeyStore keys,
            IRealtimeOutboundSessionRegistry outboundSessions = null)
        {
            _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _outboundSessions = outboundSessions;
            _server = new kcp2k.KcpServer(Connected, Data, Disconnected,
                (_, _, _) => { }, new kcp2k.KcpConfig(DualMode: false,
                    Interval: 10, FastResend: 2, CongestionWindow: false,
                    SendWindowSize: 256, ReceiveWindowSize: 256, Timeout: 10000));
        }

        public void Start(ushort port)
        {
            lock (_gate)
            {
                if (_cancellation != null)
                    throw new InvalidOperationException("KCP host is already running.");
                _server.Start(port);
                _cancellation = new CancellationTokenSource();
                _pump = Task.Run(() => PumpAsync(_cancellation.Token));
            }
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            Task pump;
            NativeSession[] sessions;
            lock (_gate)
            {
                cancellation = _cancellation;
                pump = _pump;
                _cancellation = null;
                _pump = null;
            }
            cancellation?.Cancel();
            try { pump?.Wait(TimeSpan.FromSeconds(1)); }
            catch (AggregateException) { }
            lock (_gate)
            {
                _server.Stop();
                sessions = _sessions.Values.ToArray();
                _sessions.Clear();
                while (_outgoing.TryDequeue(out _))
                {
                }
            }
            foreach (NativeSession session in sessions)
            {
                if (session.OutboundRegistered)
                {
                    session.OutboundRegistered = false;
                    _outboundSessions?.Unregister(session);
                }
                _ingress.Disconnect(session);
            }
            cancellation?.Dispose();
        }

        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        _server.TickIncoming();
                        FlushOutgoingLocked();
                        _server.TickOutgoing();
                    }
                    await RouteIncomingAsync(cancellationToken).ConfigureAwait(false);
                    await Task.Delay(1, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void Connected(int connectionId)
        {
            lock (_gate)
                _sessions[connectionId] = new NativeSession(connectionId, TryEnqueue);
        }

        private void Disconnected(int connectionId)
        {
            NativeSession session;
            bool unregister;
            lock (_gate)
            {
                if (!_sessions.Remove(connectionId, out session)) return;
                unregister = session.OutboundRegistered;
                session.OutboundRegistered = false;
            }
            if (unregister) _outboundSessions?.Unregister(session);
            _ingress.Disconnect(session);
        }

        private void Data(int connectionId, ArraySegment<byte> segment,
            kcp2k.KcpChannel channel)
        {
            if (channel != kcp2k.KcpChannel.Reliable) return;
            var packet = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, packet, 0, segment.Count);
            _incoming.Enqueue(new IncomingPacket(connectionId, packet));
        }

        private async Task RouteIncomingAsync(CancellationToken cancellationToken)
        {
            while (_incoming.TryDequeue(out IncomingPacket incoming))
            {
                NativeSession session;
                lock (_gate)
                    _sessions.TryGetValue(incoming.ConnectionId, out session);
                if (session == null) continue;
                ProtocolError result = await _ingress.RouteAuthenticatedPacketAsync(
                    session, incoming.Packet, cancellationToken).ConfigureAwait(false);
                if (result != ProtocolError.None || !session.IsAuthenticated ||
                    session.AcceptedSent)
                    continue;
                lock (_gate)
                {
                    if (_sessions.TryGetValue(incoming.ConnectionId,
                            out NativeSession current) && ReferenceEquals(current, session))
                    {
                        SendAccepted(incoming.ConnectionId, session);
                        if (_outboundSessions != null && !session.OutboundRegistered)
                        {
                            _outboundSessions.Register(session);
                            session.OutboundRegistered = true;
                        }
                    }
                }
            }
        }

        private bool TryEnqueue(NativeSession session, byte[] completeMessage)
        {
            if (completeMessage == null || completeMessage.Length == 0 ||
                completeMessage.Length > RealtimeProtocol.HeaderBytes +
                RealtimeProtocol.MaxMessageBytes)
                return false;
            lock (_gate)
            {
                if (!session.IsAuthenticated || !session.OutboundRegistered ||
                    !_sessions.TryGetValue(session.ConnectionId,
                        out NativeSession current) || !ReferenceEquals(current, session) ||
                    _keys.TryGetMacKey(session.SessionId, out byte[] key) !=
                        ProtocolError.None)
                    return false;
                byte[] packet = AuthenticatedPacketCodec.Encode(session.SessionId,
                    ++session.SendSequence, completeMessage, key);
                _outgoing.Enqueue(new OutgoingPacket(session.ConnectionId, session, packet));
                return true;
            }
        }

        private void FlushOutgoingLocked()
        {
            while (_outgoing.TryDequeue(out OutgoingPacket outgoing))
            {
                if (_sessions.TryGetValue(outgoing.ConnectionId,
                        out NativeSession current) &&
                    ReferenceEquals(current, outgoing.Session))
                    _server.Send(outgoing.ConnectionId,
                        new ArraySegment<byte>(outgoing.Packet), kcp2k.KcpChannel.Reliable);
            }
        }

        private void SendAccepted(int connectionId, NativeSession session)
        {
            RealtimeSessionClaims claims = session.Claims;
            if (_keys.TryGetMacKey(claims.SessionId, out byte[] key) != ProtocolError.None)
                return;
            byte[] accepted = RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = claims.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            });
            byte[] packet = AuthenticatedPacketCodec.Encode(claims.SessionId,
                ++session.SendSequence, accepted, key);
            _server.Send(connectionId, new ArraySegment<byte>(packet),
                kcp2k.KcpChannel.Reliable);
            session.AcceptedSent = true;
        }

        private sealed class NativeSession : IRealtimeServerSession,
            IRealtimeOutboundSession
        {
            private readonly Func<NativeSession, byte[], bool> _enqueue;
            public int ConnectionId { get; }
            public bool AcceptedSent { get; set; }
            public bool OutboundRegistered { get; set; }
            public ulong SendSequence { get; set; }
            public bool IsAuthenticated => Claims != null;
            public RealtimeSessionClaims Claims { get; private set; }
            public Guid SessionId => Claims?.SessionId ?? Guid.Empty;
            public string MatchId => Claims?.MatchId ?? string.Empty;

            public NativeSession(int connectionId,
                Func<NativeSession, byte[], bool> enqueue)
            {
                ConnectionId = connectionId;
                _enqueue = enqueue ?? throw new ArgumentNullException(nameof(enqueue));
            }

            public void Bind(RealtimeSessionClaims claims)
            {
                if (claims == null || Claims != null) throw new InvalidOperationException();
                Claims = claims;
            }

            public bool TryEnqueue(byte[] completeMessage) => _enqueue(this, completeMessage);
        }

        private readonly struct IncomingPacket
        {
            public int ConnectionId { get; }
            public byte[] Packet { get; }

            public IncomingPacket(int connectionId, byte[] packet)
            {
                ConnectionId = connectionId;
                Packet = packet;
            }
        }

        private readonly struct OutgoingPacket
        {
            public int ConnectionId { get; }
            public NativeSession Session { get; }
            public byte[] Packet { get; }

            public OutgoingPacket(int connectionId, NativeSession session, byte[] packet)
            {
                ConnectionId = connectionId;
                Session = session;
                Packet = packet;
            }
        }
    }
}
