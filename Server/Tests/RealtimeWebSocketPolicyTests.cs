using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Server.Host;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class RealtimeWebSocketPolicyTests
    {
        [Test]
        public void Policy_RequiresExactPathOriginAndSubprotocol()
        {
            var allowedOrigins = new HashSet<string>
            {
                "https://127.0.0.1:7443"
            };

            Assert.That(RealtimeWebSocketPolicy.IsAllowed("/v1/realtime",
                "https://127.0.0.1:7443", new[] { "zweb.framesync.v1" },
                allowedOrigins), Is.True);
            Assert.That(RealtimeWebSocketPolicy.IsAllowed("/realtime",
                "https://127.0.0.1:7443", new[] { "zweb.framesync.v1" },
                allowedOrigins), Is.False);
            Assert.That(RealtimeWebSocketPolicy.IsAllowed("/v1/realtime",
                "https://other.example", new[] { "zweb.framesync.v1" },
                allowedOrigins), Is.False);
            Assert.That(RealtimeWebSocketPolicy.IsAllowed("/v1/realtime",
                "https://127.0.0.1:7443", new[] { "other.protocol" },
                allowedOrigins), Is.False);
        }

        [Test]
        public void GuestLoginResponse_UsesExistingClientCredentialFieldNames()
        {
            var outcome = new GuestLoginOutcome(
                new RealtimeLoginAssignment("match-1", 0),
                new IssuedRealtimeCredentials(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    0, new byte[32], new byte[32], new byte[32], 1000));

            string json = JsonSerializer.Serialize(GuestLoginResponse.From(outcome));

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Assert.That(root.GetProperty("playerId").GetString(), Is.EqualTo("0"));
            Assert.That(root.GetProperty("sessionId").GetString(),
                Is.EqualTo("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
            Assert.That(root.GetProperty("ticket").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("resumeToken").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("macKey").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("expiresAtUnixSeconds").GetInt64(), Is.EqualTo(1000));
        }

        [Test]
        public void HostConfiguration_MapsGuestLoginAndRealtimeWebSocketEndpoints()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            WebApplication application = builder.Build();
            try
            {
                RealtimeHostApplication.Configure(application,
                    new RealtimeSessionRouter(new InMemoryRealtimeTicketStore(),
                        new SessionRateLimiter(64, 128), new NullSink(), new Clock()),
                    new GuestLoginService(new InMemoryRealtimeTicketStore(),
                        new UnavailableAllocator()), new Clock(),
                    new HashSet<string> { "https://127.0.0.1:7443" });

                var routes = ((IEndpointRouteBuilder)application).DataSources
                    .SelectMany(source => source.Endpoints)
                    .OfType<RouteEndpoint>().Select(endpoint =>
                        endpoint.RoutePattern.RawText).ToArray();
                Assert.That(routes, Does.Contain("/v1/login/guest"));
                Assert.That(routes, Does.Contain("/v1/realtime"));
            }
            finally
            {
                application.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        [Test]
        public void HostOptions_RejectInvalidIngressConfiguration()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeHostOptions
            {
                NativePort = 0
            }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new RealtimeHostOptions
            {
                MessagesPerSecond = 0
            }.Validate());
            Assert.Throws<ArgumentException>(() => new RealtimeHostOptions
            {
                AllowedOrigins = Array.Empty<string>()
            }.Validate());
        }

        private sealed class Clock : IRealtimeClock
        {
            public long Milliseconds => 0;
            public long UtcUnixSeconds => 100;
        }

        private sealed class NullSink : IRealtimeMatchSink
        {
            public System.Threading.Tasks.Task<ZWebGLFrameWork.Network.Contracts.Protocol.ProtocolError>
                PostAsync(string matchId, int authenticatedPlayerId,
                    ZWebGLFrameWork.Network.Contracts.Protocol.CommandEnvelope command,
                    System.Threading.CancellationToken cancellationToken) =>
                System.Threading.Tasks.Task.FromResult(
                    ZWebGLFrameWork.Network.Contracts.Protocol.ProtocolError.None);
        }

        private sealed class UnavailableAllocator : IRealtimeLoginAllocator
        {
            public bool TryAssignGuest(out RealtimeLoginAssignment assignment)
            {
                assignment = null;
                return false;
            }
        }
    }
}
