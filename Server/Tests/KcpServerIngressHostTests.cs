using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class KcpServerIngressHostTests
    {
        [Test]
        public async Task KcpHost_AuthenticatedHelloReceivesProtectedAcceptance()
        {
            var clock = new Clock();
            var tickets = new InMemoryRealtimeTicketStore();
            IssuedRealtimeCredentials credentials = tickets.Issue("match-1", 0,
                clock.UtcUnixSeconds);
            var router = new RealtimeSessionRouter(tickets,
                new SessionRateLimiter(64, 128), new NullSink(), clock);
            using var host = new KcpServerIngressHost(new KcpIngress(router, tickets), tickets);
            host.Start(0);
            IPEndPoint endpoint = host.LocalEndPoint;
            Assert.That(endpoint, Is.Not.Null);

            var accepted = new TaskCompletionSource<DecodeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var connected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new kcp2k.KcpClient(
                () => connected.TrySetResult(true),
                (segment, _) => accepted.TrySetResult(Decode(segment, credentials)),
                () => { }, (_, message) => accepted.TrySetException(
                    new InvalidOperationException(message)), Config());
            using var cancellation = new CancellationTokenSource();
            Task pump = Task.Run(async () =>
            {
                while (!cancellation.Token.IsCancellationRequested)
                {
                    client.TickIncoming();
                    client.TickOutgoing();
                    await Task.Delay(1, cancellation.Token);
                }
            });
            try
            {
                client.Connect("127.0.0.1", (ushort)endpoint.Port);
                await WaitAsync(connected.Task);
                byte[] hello = RealtimeCodec.Encode(new AuthHello
                {
                    Version = RealtimeProtocol.Version,
                    SessionId = credentials.SessionId,
                    OpaqueTicket = credentials.OpaqueTicket
                });
                client.Send(new ArraySegment<byte>(AuthenticatedPacketCodec.Encode(
                    credentials.SessionId, 1, hello, credentials.MacKey)),
                    kcp2k.KcpChannel.Reliable);

                DecodeResult result = await WaitAsync(accepted.Task);
                Assert.That(result.Error, Is.EqualTo(ProtocolError.None));
                Assert.That(result.Kind, Is.EqualTo(RealtimeMessageKind.AuthAccepted));
                Assert.That(result.AuthAccepted.SessionId, Is.EqualTo(credentials.SessionId));
            }
            finally
            {
                cancellation.Cancel();
                try { await pump; }
                catch (OperationCanceledException) { }
                client.Disconnect();
            }
        }

        [Test]
        public async Task KcpHost_AuthenticatedSessionReceivesProtectedAuthorityFrame()
        {
            var clock = new Clock();
            var tickets = new InMemoryRealtimeTicketStore();
            IssuedRealtimeCredentials credentials = tickets.Issue("match-1", 0,
                clock.UtcUnixSeconds);
            var router = new RealtimeSessionRouter(tickets,
                new SessionRateLimiter(64, 128), new NullSink(), clock);
            var registry = new RecordingOutboundRegistry();
            using var host = new KcpServerIngressHost(new KcpIngress(router, tickets),
                tickets, registry);
            host.Start(0);
            IPEndPoint endpoint = host.LocalEndPoint;
            var accepted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var authority = new TaskCompletionSource<DecodeResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var connected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new kcp2k.KcpClient(
                () => connected.TrySetResult(true),
                (segment, _) => Receive(segment, credentials, accepted, authority),
                () => { }, (_, message) => authority.TrySetException(
                    new InvalidOperationException(message)), Config());
            using var cancellation = new CancellationTokenSource();
            Task pump = Task.Run(async () =>
            {
                while (!cancellation.Token.IsCancellationRequested)
                {
                    client.TickIncoming();
                    client.TickOutgoing();
                    await Task.Delay(1, cancellation.Token);
                }
            });
            try
            {
                client.Connect("127.0.0.1", (ushort)endpoint.Port);
                await WaitAsync(connected.Task);
                byte[] hello = RealtimeCodec.Encode(new AuthHello
                {
                    Version = RealtimeProtocol.Version,
                    SessionId = credentials.SessionId,
                    OpaqueTicket = credentials.OpaqueTicket
                });
                client.Send(new ArraySegment<byte>(AuthenticatedPacketCodec.Encode(
                    credentials.SessionId, 1, hello, credentials.MacKey)),
                    kcp2k.KcpChannel.Reliable);
                await WaitAsync(accepted.Task);

                registry.Broadcaster.Broadcast(Frame("match-1", 5));

                DecodeResult result = await WaitAsync(authority.Task);
                Assert.That(result.Error, Is.EqualTo(ProtocolError.None));
                Assert.That(result.Kind, Is.EqualTo(RealtimeMessageKind.Authority));
                Assert.That(result.Authority.Tick, Is.EqualTo(5));
                host.Dispose();
                Assert.That(registry.Registered, Has.Count.EqualTo(1));
                Assert.That(registry.Unregistered, Is.EqualTo(registry.Registered));
            }
            finally
            {
                cancellation.Cancel();
                try { await pump; }
                catch (OperationCanceledException) { }
                client.Disconnect();
            }
        }

        private static async Task<T> WaitAsync<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5))) != task)
                throw new TimeoutException("Timed out waiting for KCP ingress.");
            return await task;
        }

        private static kcp2k.KcpConfig Config() => new kcp2k.KcpConfig(
            DualMode: false, Interval: 10, FastResend: 2, CongestionWindow: false,
            SendWindowSize: 256, ReceiveWindowSize: 256, Timeout: 10000);

        private static DecodeResult Decode(ArraySegment<byte> segment,
            IssuedRealtimeCredentials credentials)
        {
            var replay = new SequenceReplayWindow();
            byte[] packet = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, packet, 0, segment.Count);
            ProtocolError error = AuthenticatedPacketCodec.TryDecode(packet,
                credentials.SessionId, credentials.MacKey, replay,
                out AuthenticatedPacket decoded);
            return error == ProtocolError.None ? RealtimeCodec.Decode(decoded.Payload) :
                new DecodeResult { Error = error };
        }

        private static void Receive(ArraySegment<byte> segment,
            IssuedRealtimeCredentials credentials, TaskCompletionSource<bool> accepted,
            TaskCompletionSource<DecodeResult> authority)
        {
            DecodeResult result = Decode(segment, credentials);
            if (result.Error != ProtocolError.None)
                authority.TrySetResult(result);
            else if (result.Kind == RealtimeMessageKind.AuthAccepted)
                accepted.TrySetResult(true);
            else if (result.Kind == RealtimeMessageKind.Authority)
                authority.TrySetResult(result);
        }

        private static AuthorityFrame Frame(string matchId, int tick) =>
            new AuthorityFrame
            {
                MatchId = matchId,
                Tick = tick,
                OrderedInputs = new[]
                {
                    NoOp(tick, 0),
                    NoOp(tick, 1)
                },
                AckPlayer0 = 1,
                AckPlayer1 = 1,
                Checksum = 99
            };

        private static CommandEnvelope NoOp(int tick, int playerId) =>
            new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                MatchId = "match-1",
                Tick = tick,
                PlayerId = playerId,
                Sequence = 1,
                MessageId = 0,
                Payload = Array.Empty<byte>()
            };

        private sealed class Clock : IRealtimeClock
        {
            public long Milliseconds => 0;
            public long UtcUnixSeconds => 100;
        }

        private sealed class NullSink : IRealtimeMatchSink
        {
            public Task<ProtocolError> PostAsync(string matchId,
                int authenticatedPlayerId, CommandEnvelope command,
                CancellationToken cancellationToken) =>
                Task.FromResult(ProtocolError.None);
        }

        private sealed class RecordingOutboundRegistry :
            IRealtimeOutboundSessionRegistry
        {
            public AuthorityFrameBroadcaster Broadcaster { get; } =
                new AuthorityFrameBroadcaster();
            public List<IRealtimeOutboundSession> Registered { get; } =
                new List<IRealtimeOutboundSession>();
            public List<IRealtimeOutboundSession> Unregistered { get; } =
                new List<IRealtimeOutboundSession>();

            public void Register(IRealtimeOutboundSession session)
            {
                Registered.Add(session);
                Broadcaster.Register(session);
            }

            public void Unregister(IRealtimeOutboundSession session)
            {
                Unregistered.Add(session);
                Broadcaster.Unregister(session);
            }
        }
    }
}
