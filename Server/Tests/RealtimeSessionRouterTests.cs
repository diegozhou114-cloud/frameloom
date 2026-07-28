using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class RealtimeSessionRouterTests
    {
        [Test]
        public async Task Router_RequiresAuthHelloFirst_ThenBindsValidTicket()
        {
            RouterFixture fixture = RouterFixture.Create();
            CommandEnvelope command = fixture.Command(0, 1);

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(command), CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(fixture.Session.IsAuthenticated, Is.True);
            Assert.That(fixture.Session.Claims.SessionId, Is.EqualTo(fixture.Claims.SessionId));
        }

        [Test]
        public async Task Router_RejectsExpiredTicketAndWrongSession()
        {
            RouterFixture expired = RouterFixture.Create(expiresAtUnixSeconds: 99);
            expired.Clock.UtcUnixSeconds = 100;
            Assert.That(await expired.Router.RouteAsync(
                    expired.Session, expired.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.ExpiredTicket));

            RouterFixture wrong = RouterFixture.Create();
            Assert.That(await wrong.Router.RouteAsync(
                    wrong.Session, wrong.AuthBytes(Guid.NewGuid()), CancellationToken.None),
                Is.EqualTo(ProtocolError.WrongSession));
        }

        [Test]
        public async Task Router_AuthenticatedCommand_IsAuthorityCheckedAndForwarded()
        {
            RouterFixture fixture = RouterFixture.Create();
            await fixture.AuthenticateAsync();
            CommandEnvelope command = fixture.Command(0, 7);

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(command), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
            Assert.That(fixture.Sink.Accepted, Has.Count.EqualTo(1));
            Assert.That(fixture.Sink.Accepted[0].AuthenticatedPlayerId, Is.Zero);
            Assert.That(fixture.Sink.Accepted[0].Command.Sequence, Is.EqualTo(7));

            command.PlayerId = 1;
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(command), CancellationToken.None),
                Is.EqualTo(ProtocolError.WrongPlayer));
            command.PlayerId = 0;
            command.MatchId = "other-match";
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(command), CancellationToken.None),
                Is.EqualTo(ProtocolError.WrongSession));
        }

        [Test]
        public async Task Router_ExpiredAuthenticatedSession_CannotPostCommands()
        {
            RouterFixture fixture = RouterFixture.Create(expiresAtUnixSeconds: 101);
            await fixture.AuthenticateAsync();
            fixture.Clock.UtcUnixSeconds = 101;

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(fixture.Command(0, 1)),
                    CancellationToken.None), Is.EqualTo(ProtocolError.ExpiredTicket));
            Assert.That(fixture.Sink.Accepted, Is.Empty);
        }

        [Test]
        public async Task Router_RejectsOversizeDecodeErrorsAndRateExcess()
        {
            RouterFixture fixture = RouterFixture.Create(messagesPerSecond: 1, burst: 1);
            await fixture.AuthenticateAsync();

            Assert.That(await fixture.Router.RouteAsync(fixture.Session,
                    new byte[RealtimeProtocol.MaxMessageBytes + 1], CancellationToken.None),
                Is.EqualTo(ProtocolError.MessageTooLarge));
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, new byte[] { 1 }, CancellationToken.None),
                Is.EqualTo(ProtocolError.Truncated));
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(fixture.Command(0, 1)),
                    CancellationToken.None), Is.EqualTo(ProtocolError.None));
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(fixture.Command(1, 2)),
                    CancellationToken.None), Is.EqualTo(ProtocolError.RateLimited));

            fixture.Clock.Milliseconds = 1000;
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, RealtimeCodec.Encode(fixture.Command(1, 2)),
                    CancellationToken.None), Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public void SessionClaimsAndTicketStore_DefendSecretArrays()
        {
            RouterFixture fixture = RouterFixture.Create();
            byte[] resume = fixture.Claims.ResumeToken;
            byte expected = resume[0];

            resume[0] ^= 1;
            fixture.Claims.ResumeToken[0] ^= 1;

            Assert.That(fixture.Claims.ResumeToken[0], Is.EqualTo(expected));
        }

        [Test]
        public void RateLimiter_PreservesPartialRefill_IsolatesSessions_AndIgnoresRollback()
        {
            var limiter = new SessionRateLimiter(messagesPerSecond: 10, burst: 1);
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();

            Assert.That(limiter.TryConsume(first, 0), Is.True);
            Assert.That(limiter.TryConsume(first, 50), Is.False);
            Assert.That(limiter.TryConsume(first, 100), Is.True);
            Assert.That(limiter.TryConsume(second, 100), Is.True);
            Assert.That(limiter.TryConsume(first, 90), Is.False);
        }

        [Test]
        public async Task Router_AllowsOneActiveOwner_AndReleasesOnDisconnect()
        {
            RouterFixture fixture = RouterFixture.Create();
            var second = new TestSession();

            await fixture.AuthenticateAsync();
            Assert.That(await fixture.Router.RouteAsync(
                    second, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));

            fixture.Router.Disconnect(fixture.Session);
            Assert.That(await fixture.Router.RouteAsync(
                    second, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public async Task Router_StaleDisconnectCannotReleaseReplacementOwner()
        {
            RouterFixture fixture = RouterFixture.Create();
            var replacement = new TestSession();
            var third = new TestSession();

            await fixture.AuthenticateAsync();
            fixture.Router.Disconnect(fixture.Session);
            Assert.That(await fixture.Router.RouteAsync(
                    replacement, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            fixture.Router.Disconnect(fixture.Session);

            Assert.That(await fixture.Router.RouteAsync(
                    third, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));
        }

        [Test]
        public async Task Router_DisconnectedOwnerCannotPostAfterReplacementBinds()
        {
            RouterFixture fixture = RouterFixture.Create();
            var replacement = new TestSession();

            await fixture.AuthenticateAsync();
            fixture.Router.Disconnect(fixture.Session);
            Assert.That(await fixture.Router.RouteAsync(
                    replacement, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session,
                    RealtimeCodec.Encode(fixture.Command(0, 1)),
                    CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));
            Assert.That(fixture.Sink.Accepted, Is.Empty);
        }

        [Test]
        public async Task Router_DisconnectDuringBindDoesNotLeakActiveOwnership()
        {
            RouterFixture fixture = RouterFixture.Create();
            var binding = new TestSession();
            binding.OnBind = () => fixture.Router.Disconnect(binding);

            Assert.That(await fixture.Router.RouteAsync(
                    binding, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public async Task Router_DisconnectDuringTicketValidationCannotBecomeOwner()
        {
            RouterFixture fixture = RouterFixture.Create();
            var validating = new TestSession();
            fixture.Tickets.OnValidate = () =>
                fixture.Router.Disconnect(validating);

            Assert.That(await fixture.Router.RouteAsync(
                    validating, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.AuthenticationFailed));

            fixture.Tickets.OnValidate = null;
            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public async Task Router_FailedBindDoesNotLeakActiveOwnership()
        {
            RouterFixture fixture = RouterFixture.Create();
            var throwing = new TestSession { ThrowOnBind = true };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await fixture.Router.RouteAsync(
                    throwing, fixture.AuthBytes(), CancellationToken.None));

            Assert.That(await fixture.Router.RouteAsync(
                    fixture.Session, fixture.AuthBytes(), CancellationToken.None),
                Is.EqualTo(ProtocolError.None));
        }

        [Test]
        public void RateLimiter_LargeClockJumpSaturatesWithoutOverflow()
        {
            var limiter = new SessionRateLimiter(messagesPerSecond: int.MaxValue,
                burst: int.MaxValue);
            Guid session = Guid.NewGuid();

            Assert.That(limiter.TryConsume(session, 0), Is.True);
            Assert.DoesNotThrow(() => limiter.TryConsume(session, long.MaxValue));
            Assert.That(limiter.TryConsume(session, long.MaxValue), Is.True);
        }

        private sealed class RouterFixture
        {
            public ManualRealtimeClock Clock { get; }
            public RealtimeSessionClaims Claims { get; }
            public byte[] Ticket { get; }
            public TestSession Session { get; }
            public RecordingSink Sink { get; }
            public CallbackTicketStore Tickets { get; }
            public RealtimeSessionRouter Router { get; }

            private RouterFixture(ManualRealtimeClock clock,
                RealtimeSessionClaims claims, byte[] ticket, TestSession session,
                RecordingSink sink, CallbackTicketStore tickets,
                RealtimeSessionRouter router)
            {
                Clock = clock;
                Claims = claims;
                Ticket = ticket;
                Session = session;
                Sink = sink;
                Tickets = tickets;
                Router = router;
            }

            public static RouterFixture Create(long expiresAtUnixSeconds = 1000,
                int messagesPerSecond = 64, int burst = 128)
            {
                var clock = new ManualRealtimeClock
                {
                    Milliseconds = 0,
                    UtcUnixSeconds = 100
                };
                byte[] ticket = { 1, 2, 3, 4 };
                var claims = new RealtimeSessionClaims(
                    Guid.NewGuid(), "match-1", 0,
                    new byte[] { 9, 8, 7 }, expiresAtUnixSeconds);
                var ticketStore = new InMemoryRealtimeTicketStore();
                ticketStore.Add(ticket, claims);
                var tickets = new CallbackTicketStore(ticketStore);
                var session = new TestSession();
                var sink = new RecordingSink();
                var router = new RealtimeSessionRouter(
                    tickets, new SessionRateLimiter(messagesPerSecond, burst),
                    sink, clock);
                return new RouterFixture(
                    clock, claims, ticket, session, sink, tickets, router);
            }

            public byte[] AuthBytes(Guid? sessionId = null) =>
                RealtimeCodec.Encode(new AuthHello
                {
                    Version = RealtimeProtocol.Version,
                    SessionId = sessionId ?? Claims.SessionId,
                    OpaqueTicket = (byte[])Ticket.Clone()
                });

            public CommandEnvelope Command(int tick, uint sequence) =>
                new CommandEnvelope
                {
                    Version = RealtimeProtocol.Version,
                    MatchId = Claims.MatchId,
                    Tick = tick,
                    PlayerId = Claims.PlayerId,
                    Sequence = sequence,
                    MessageId = 1,
                    Payload = new byte[] { 1 }
                };

            public async Task AuthenticateAsync()
            {
                Assert.That(await Router.RouteAsync(
                        Session, AuthBytes(), CancellationToken.None),
                    Is.EqualTo(ProtocolError.None));
            }
        }

        private sealed class CallbackTicketStore : IRealtimeTicketStore
        {
            private readonly IRealtimeTicketStore _inner;
            public Action OnValidate { get; set; }

            public CallbackTicketStore(IRealtimeTicketStore inner)
            {
                _inner = inner;
            }

            public ProtocolError Validate(byte[] opaqueTicket, long utcUnixSeconds,
                out RealtimeSessionClaims claims)
            {
                ProtocolError result = _inner.Validate(
                    opaqueTicket, utcUnixSeconds, out claims);
                OnValidate?.Invoke();
                return result;
            }
        }

        private sealed class ManualRealtimeClock : IRealtimeClock
        {
            public long Milliseconds { get; set; }
            public long UtcUnixSeconds { get; set; }
        }

        private sealed class TestSession : IRealtimeServerSession
        {
            public bool ThrowOnBind { get; set; }
            public Action OnBind { get; set; }
            public bool IsAuthenticated => Claims != null;
            public RealtimeSessionClaims Claims { get; private set; }
            public void Bind(RealtimeSessionClaims claims)
            {
                if (ThrowOnBind) throw new InvalidOperationException();
                if (claims == null) throw new ArgumentNullException(nameof(claims));
                if (Claims != null) throw new InvalidOperationException();
                OnBind?.Invoke();
                Claims = claims;
            }
        }

        private sealed class RecordingSink : IRealtimeMatchSink
        {
            public List<(string MatchId, int AuthenticatedPlayerId,
                CommandEnvelope Command)> Accepted { get; } = new();

            public Task<ProtocolError> PostAsync(string matchId,
                int authenticatedPlayerId, CommandEnvelope command,
                CancellationToken cancellationToken)
            {
                Accepted.Add((matchId, authenticatedPlayerId, command));
                return Task.FromResult(ProtocolError.None);
            }
        }
    }
}
