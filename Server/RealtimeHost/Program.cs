using ZWebGLFrameWork.FrameSync;

namespace ZWebGLFrameWork.Server.Host;

public static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        RealtimeHostOptions options = builder.Configuration
            .GetSection("Realtime").Get<RealtimeHostOptions>() ?? new RealtimeHostOptions();
        options.Validate();
        var clock = new SystemRealtimeClock();
        var tickets = new InMemoryRealtimeTicketStore();
        var matches = new FrameSyncMatchRegistry();
        var authorityFrames = new AuthorityFrameBroadcaster();
        var router = new RealtimeSessionRouter(tickets,
            new SessionRateLimiter(options.MessagesPerSecond, options.Burst), matches, clock);
        var login = new GuestLoginService(tickets, new UnavailableLoginAllocator());
        using var native = new KcpServerIngressHost(new KcpIngress(router, tickets),
            tickets, authorityFrames);
        native.Start(options.NativePort);

        WebApplication application = builder.Build();
        RealtimeHostApplication.Configure(application, router, login, clock,
            new HashSet<string>(options.AllowedOrigins, StringComparer.Ordinal),
            authorityFrames);
        application.Lifetime.ApplicationStopping.Register(matches.Dispose);
        await application.RunAsync().ConfigureAwait(false);
    }

    private sealed class UnavailableLoginAllocator : IRealtimeLoginAllocator
    {
        // TODO: Replace with the approved matchmaker once formal battle content exists.
        public bool TryAssignGuest(out RealtimeLoginAssignment? assignment)
        {
            assignment = null;
            return false;
        }
    }
}
