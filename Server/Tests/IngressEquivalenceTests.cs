using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class IngressEquivalenceTests
    {
        [Test]
        public async Task WebSocketIngress_ReassemblesBinaryFragmentsBeforeRouting()
        {
            RouterSetup setup = RouterSetup.Create();
            byte[] auth = Auth(setup.Claims, setup.Ticket);
            var socket = new ScriptedWebSocket(
                Binary(auth.Take(3).ToArray(), endOfMessage: false),
                Binary(auth.Skip(3).ToArray(), endOfMessage: true),
                Close());

            await new WebSocketIngress(setup.Router).ProcessAsync(
                socket, new TestSession(), CancellationToken.None);

            Assert.That(socket.CloseStatus, Is.Null);
        }

        [Test]
        public async Task WebSocketIngress_AuthenticatedHelloReceivesAuthAccepted()
        {
            RouterSetup setup = RouterSetup.Create();
            var socket = new ScriptedWebSocket(
                Binary(Auth(setup.Claims, setup.Ticket), endOfMessage: true), Close());

            await new WebSocketIngress(setup.Router).ProcessAsync(
                socket, new TestSession(), CancellationToken.None);

            Assert.That(socket.Sent, Has.Count.EqualTo(1));
            DecodeResult accepted = RealtimeCodec.Decode(socket.Sent[0]);
            Assert.That(accepted.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(accepted.Kind, Is.EqualTo(RealtimeMessageKind.AuthAccepted));
            Assert.That(accepted.AuthAccepted.SessionId, Is.EqualTo(setup.Claims.SessionId));
        }

        [Test]
        public async Task WebSocketIngress_RejectsTextMessagesWithMalformedCloseCode()
        {
            RouterSetup setup = RouterSetup.Create();
            var socket = new ScriptedWebSocket(Text(new byte[] { 1 }));

            await new WebSocketIngress(setup.Router).ProcessAsync(
                socket, new TestSession(), CancellationToken.None);

            Assert.That(socket.CloseStatus, Is.EqualTo((WebSocketCloseStatus)4003));
        }

        [Test]
        public async Task WebSocketIngress_RejectsUnauthenticatedFirstMessage()
        {
            RouterSetup setup = RouterSetup.Create();
            byte[] command = RealtimeCodec.Encode(Command(setup.Claims));
            var socket = new ScriptedWebSocket(Binary(command, endOfMessage: true));

            await new WebSocketIngress(setup.Router).ProcessAsync(
                socket, new TestSession(), CancellationToken.None);

            Assert.That(socket.CloseStatus, Is.EqualTo((WebSocketCloseStatus)4001));
        }

        [Test]
        public async Task WebSocketIngress_RejectsMessagesLargerThanProtocolLimit()
        {
            RouterSetup setup = RouterSetup.Create();
            var socket = new ScriptedWebSocket(OversizeBinaryFrames().ToArray());

            await new WebSocketIngress(setup.Router).ProcessAsync(
                socket, new TestSession(), CancellationToken.None);

            Assert.That(socket.CloseStatus, Is.EqualTo((WebSocketCloseStatus)4004));
        }

        [Test]
        public async Task WssAndNativeIngress_RouteTheSameCompleteCommand()
        {
            RouterSetup setup = RouterSetup.Create();
            var tickets = setup.Tickets;
            var router = setup.Router;
            var wssClaims = setup.Claims;
            var nativeClaims = Claims();
            byte[] wssTicket = { 1 };
            byte[] nativeTicket = { 2 };
            tickets.Add(wssTicket, wssClaims);
            tickets.Add(nativeTicket, nativeClaims);

            var wss = new WebSocketIngress(router);
            byte[] nativeMacKey = Enumerable.Repeat((byte)7, 32).ToArray();
            var nativeKeys = new TestNativeSessionKeyStore();
            nativeKeys.Add(nativeClaims.SessionId, nativeMacKey);
            var native = new KcpIngress(router, nativeKeys);
            var wssSession = new TestSession();
            var nativeSession = new TestSession();
            byte[] command = RealtimeCodec.Encode(new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                MatchId = "match-1",
                Tick = 3,
                PlayerId = 0,
                Sequence = 9,
                MessageId = 1,
                Payload = new byte[] { 4, 5 }
            });

            Assert.That(await wss.RouteCompleteMessageAsync(
                    wssSession, Auth(wssClaims, wssTicket), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(await native.RouteAuthenticatedPacketAsync(
                    nativeSession, AuthenticatedPacketCodec.Encode(nativeClaims.SessionId,
                        1, Auth(nativeClaims, nativeTicket), nativeMacKey),
                    CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            Assert.That(await wss.RouteCompleteMessageAsync(
                    wssSession, command, CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(await native.RouteAuthenticatedPacketAsync(
                    nativeSession, AuthenticatedPacketCodec.Encode(nativeClaims.SessionId,
                        2, command, nativeMacKey), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            Assert.That(setup.Sink.Accepted, Has.Count.EqualTo(2));
            Assert.That(RealtimeCodec.Encode(setup.Sink.Accepted[0]),
                Is.EqualTo(RealtimeCodec.Encode(setup.Sink.Accepted[1])));
        }

        [Test]
        public async Task KcpIngress_RejectsForgedAndReplayedAuthenticatedPackets()
        {
            RouterSetup setup = RouterSetup.Create();
            byte[] macKey = Enumerable.Repeat((byte)3, 32).ToArray();
            var keys = new TestNativeSessionKeyStore();
            keys.Add(setup.Claims.SessionId, macKey);
            var ingress = new KcpIngress(setup.Router, keys);
            var session = new TestSession();
            byte[] authPacket = AuthenticatedPacketCodec.Encode(setup.Claims.SessionId,
                4, Auth(setup.Claims, setup.Ticket), macKey);

            byte[] forged = (byte[])authPacket.Clone();
            forged[^1] ^= 1;
            Assert.That(await ingress.RouteAuthenticatedPacketAsync(
                    session, forged, CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));
            Assert.That(await ingress.RouteAuthenticatedPacketAsync(
                    session, authPacket, CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(await ingress.RouteAuthenticatedPacketAsync(
                    session, authPacket, CancellationToken.None),
                Is.EqualTo(ProtocolError.ReplayedPacket));
        }

        [Test]
        public async Task KcpIngress_AuthenticatedSessionRejectsValidForeignSessionPacket()
        {
            RouterSetup setup = RouterSetup.Create();
            RealtimeSessionClaims foreignClaims = Claims();
            byte[] ownKey = Enumerable.Repeat((byte)4, 32).ToArray();
            byte[] foreignKey = Enumerable.Repeat((byte)5, 32).ToArray();
            var keys = new TestNativeSessionKeyStore();
            keys.Add(setup.Claims.SessionId, ownKey);
            keys.Add(foreignClaims.SessionId, foreignKey);
            var ingress = new KcpIngress(setup.Router, keys);
            var session = new TestSession();

            Assert.That(await ingress.RouteAuthenticatedPacketAsync(session,
                    AuthenticatedPacketCodec.Encode(setup.Claims.SessionId, 1,
                        Auth(setup.Claims, setup.Ticket), ownKey),
                    CancellationToken.None), Is.EqualTo(ProtocolError.None));

            Assert.That(await ingress.RouteAuthenticatedPacketAsync(session,
                    AuthenticatedPacketCodec.Encode(foreignClaims.SessionId, 1,
                        RealtimeCodec.Encode(Command(setup.Claims)), foreignKey),
                    CancellationToken.None), Is.EqualTo(ProtocolError.WrongSession));
            Assert.That(setup.Sink.Accepted, Is.Empty);
        }

        [Test]
        public async Task KcpIngress_DisconnectPermitsRetainedSessionResume()
        {
            RouterSetup setup = RouterSetup.Create();
            byte[] macKey = Enumerable.Repeat((byte)5, 32).ToArray();
            var keys = new TestNativeSessionKeyStore();
            keys.Add(setup.Claims.SessionId, macKey);
            var ingress = new KcpIngress(setup.Router, keys);
            var first = new TestSession();
            var replacement = new TestSession();

            Assert.That(await ingress.RouteAuthenticatedPacketAsync(first,
                    AuthenticatedPacketCodec.Encode(setup.Claims.SessionId, 1,
                        Auth(setup.Claims, setup.Ticket), macKey),
                    CancellationToken.None), Is.EqualTo(ProtocolError.None));

            ingress.Disconnect(first);

            Assert.That(await ingress.RouteAuthenticatedPacketAsync(replacement,
                    AuthenticatedPacketCodec.Encode(setup.Claims.SessionId, 1,
                        Resume(setup.Claims), macKey),
                    CancellationToken.None), Is.EqualTo(ProtocolError.None));
        }

        private static RealtimeSessionClaims Claims() => new RealtimeSessionClaims(
            Guid.NewGuid(), "match-1", 0, new byte[] { 1, 2, 3 }, 1_000);

        private static byte[] Auth(RealtimeSessionClaims claims, byte[] ticket) =>
            RealtimeCodec.Encode(new AuthHello
            {
                Version = RealtimeProtocol.Version,
                SessionId = claims.SessionId,
                OpaqueTicket = ticket
            });

        private static byte[] Resume(RealtimeSessionClaims claims) =>
            RealtimeCodec.Encode(new ResumeHello
            {
                Version = RealtimeProtocol.Version,
                SessionId = claims.SessionId,
                ResumeToken = claims.ResumeToken,
                LastAcceptedSequence = 0,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            });

        private static CommandEnvelope Command(RealtimeSessionClaims claims) =>
            new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                MatchId = claims.MatchId,
                Tick = 0,
                PlayerId = claims.PlayerId,
                Sequence = 1,
                MessageId = 1,
                Payload = new byte[] { 1 }
            };

        private static ScriptedFrame Binary(byte[] data, bool endOfMessage) =>
            new ScriptedFrame(WebSocketMessageType.Binary, data, endOfMessage);

        private static ScriptedFrame Text(byte[] data) =>
            new ScriptedFrame(WebSocketMessageType.Text, data, true);

        private static ScriptedFrame Close() =>
            new ScriptedFrame(WebSocketMessageType.Close, Array.Empty<byte>(), true);

        private static IEnumerable<ScriptedFrame> OversizeBinaryFrames()
        {
            const int fragmentBytes = 4096;
            for (int offset = 0; offset < RealtimeProtocol.MaxMessageBytes;
                 offset += fragmentBytes)
                yield return Binary(new byte[fragmentBytes], endOfMessage: false);
            yield return Binary(new byte[] { 1 }, endOfMessage: true);
        }

        private sealed class RouterSetup
        {
            public InMemoryRealtimeTicketStore Tickets { get; }
            public RecordingSink Sink { get; }
            public RealtimeSessionRouter Router { get; }
            public RealtimeSessionClaims Claims { get; }
            public byte[] Ticket { get; }

            private RouterSetup(InMemoryRealtimeTicketStore tickets,
                RecordingSink sink, RealtimeSessionRouter router,
                RealtimeSessionClaims claims, byte[] ticket)
            {
                Tickets = tickets;
                Sink = sink;
                Router = router;
                Claims = claims;
                Ticket = ticket;
            }

            public static RouterSetup Create()
            {
                var tickets = new InMemoryRealtimeTicketStore();
                var sink = new RecordingSink();
                var router = new RealtimeSessionRouter(
                    tickets, new SessionRateLimiter(64, 128), sink, new TestClock());
                RealtimeSessionClaims claims = Claims();
                byte[] ticket = { 1 };
                tickets.Add(ticket, claims);
                return new RouterSetup(tickets, sink, router, claims, ticket);
            }
        }

        private sealed class TestClock : IRealtimeClock
        {
            public long Milliseconds => 0;
            public long UtcUnixSeconds => 100;
        }

        private sealed class TestSession : IRealtimeServerSession
        {
            public bool IsAuthenticated => Claims != null;
            public RealtimeSessionClaims Claims { get; private set; }

            public void Bind(RealtimeSessionClaims claims)
            {
                if (claims == null) throw new ArgumentNullException(nameof(claims));
                if (Claims != null) throw new InvalidOperationException();
                Claims = claims;
            }
        }

        private sealed class RecordingSink : IRealtimeMatchSink
        {
            public List<CommandEnvelope> Accepted { get; } = new List<CommandEnvelope>();

            public Task<ProtocolError> PostAsync(string matchId,
                int authenticatedPlayerId, CommandEnvelope command,
                CancellationToken cancellationToken)
            {
                Accepted.Add(command);
                return Task.FromResult(ProtocolError.None);
            }
        }

        private sealed class TestNativeSessionKeyStore : INativeSessionKeyStore
        {
            private readonly Dictionary<Guid, byte[]> _keys = new Dictionary<Guid, byte[]>();

            public void Add(Guid sessionId, byte[] macKey) =>
                _keys.Add(sessionId, (byte[])macKey.Clone());

            public ProtocolError TryGetMacKey(Guid sessionId, out byte[] macKey)
            {
                if (_keys.TryGetValue(sessionId, out byte[] key))
                {
                    macKey = (byte[])key.Clone();
                    return ProtocolError.None;
                }
                macKey = null;
                return ProtocolError.WrongSession;
            }
        }

        private sealed class ScriptedWebSocket : WebSocket
        {
            private readonly Queue<ScriptedFrame> _frames;
            private WebSocketState _state = WebSocketState.Open;
            private WebSocketCloseStatus? _closeStatus;
            private string _closeStatusDescription;
            public List<byte[]> Sent { get; } = new List<byte[]>();

            public override WebSocketCloseStatus? CloseStatus => _closeStatus;
            public override string CloseStatusDescription => _closeStatusDescription;
            public override WebSocketState State => _state;
            public override string SubProtocol => RealtimeProtocol.WebSocketSubprotocol;

            public ScriptedWebSocket(params ScriptedFrame[] frames) =>
                _frames = new Queue<ScriptedFrame>(frames);

            public override void Abort() => _state = WebSocketState.Aborted;

            public override Task CloseAsync(WebSocketCloseStatus closeStatus,
                string statusDescription, CancellationToken cancellationToken)
            {
                _closeStatus = closeStatus;
                _closeStatusDescription = statusDescription;
                _state = WebSocketState.Closed;
                return Task.CompletedTask;
            }

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus,
                string statusDescription, CancellationToken cancellationToken) =>
                CloseAsync(closeStatus, statusDescription, cancellationToken);

            public override void Dispose() => _state = WebSocketState.Closed;

            public override Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer, CancellationToken cancellationToken)
            {
                if (_frames.Count == 0)
                    throw new InvalidOperationException("No scripted frame remains.");
                ScriptedFrame frame = _frames.Dequeue();
                Buffer.BlockCopy(frame.Data, 0, buffer.Array, buffer.Offset, frame.Data.Length);
                return Task.FromResult(new WebSocketReceiveResult(
                    frame.Data.Length, frame.Type, frame.EndOfMessage));
            }

            public override Task SendAsync(ArraySegment<byte> buffer,
                WebSocketMessageType messageType, bool endOfMessage,
                CancellationToken cancellationToken)
            {
                Assert.That(messageType, Is.EqualTo(WebSocketMessageType.Binary));
                Assert.That(endOfMessage, Is.True);
                var copy = new byte[buffer.Count];
                Buffer.BlockCopy(buffer.Array, buffer.Offset, copy, 0, copy.Length);
                Sent.Add(copy);
                return Task.CompletedTask;
            }
        }

        private readonly struct ScriptedFrame
        {
            public WebSocketMessageType Type { get; }
            public byte[] Data { get; }
            public bool EndOfMessage { get; }

            public ScriptedFrame(WebSocketMessageType type, byte[] data,
                bool endOfMessage)
            {
                Type = type;
                Data = data;
                EndOfMessage = endOfMessage;
            }
        }
    }
}
