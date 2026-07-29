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
        private CancellationTokenSource _cancellation;
        private Task _pump;

        public IPEndPoint LocalEndPoint => _server.LocalEndPoint as IPEndPoint;

        public KcpServerIngressHost(KcpIngress ingress, INativeSessionKeyStore keys)
        {
            _ingress = ingress ?? throw new ArgumentNullException(nameof(ingress));
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
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
                foreach (NativeSession session in _sessions.Values)
                    _ingress.Disconnect(session);
                _sessions.Clear();
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
            lock (_gate) _sessions[connectionId] = new NativeSession();
        }

        private void Disconnected(int connectionId)
        {
            NativeSession session;
            lock (_gate)
            {
                if (!_sessions.Remove(connectionId, out session)) return;
            }
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
                        SendAccepted(incoming.ConnectionId, session);
                }
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

        private sealed class NativeSession : IRealtimeServerSession
        {
            public bool AcceptedSent { get; set; }
            public ulong SendSequence { get; set; }
            public bool IsAuthenticated => Claims != null;
            public RealtimeSessionClaims Claims { get; private set; }
            public void Bind(RealtimeSessionClaims claims)
            {
                if (claims == null || Claims != null) throw new InvalidOperationException();
                Claims = claims;
            }
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
    }
}
