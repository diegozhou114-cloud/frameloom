using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class IngressEquivalenceTests
    {
        [Test]
        public async Task WssAndNativeIngress_RouteTheSameCompleteCommand()
        {
            var clock = new TestClock();
            var tickets = new InMemoryRealtimeTicketStore();
            var sink = new RecordingSink();
            var router = new RealtimeSessionRouter(
                tickets, new SessionRateLimiter(64, 128), sink, clock);
            var wssClaims = Claims();
            var nativeClaims = Claims();
            byte[] wssTicket = { 1 };
            byte[] nativeTicket = { 2 };
            tickets.Add(wssTicket, wssClaims);
            tickets.Add(nativeTicket, nativeClaims);

            var wss = new WebSocketIngress(router);
            var native = new KcpIngress(router);
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
            Assert.That(await native.RouteAuthenticatedMessageAsync(
                    nativeSession, Auth(nativeClaims, nativeTicket), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            Assert.That(await wss.RouteCompleteMessageAsync(
                    wssSession, command, CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(await native.RouteAuthenticatedMessageAsync(
                    nativeSession, command, CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            Assert.That(sink.Accepted, Has.Count.EqualTo(2));
            Assert.That(RealtimeCodec.Encode(sink.Accepted[0]),
                Is.EqualTo(RealtimeCodec.Encode(sink.Accepted[1])));
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
    }
}
