using System;
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
    }
}
