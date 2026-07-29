using System.Net.WebSockets;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Host;

public static class RealtimeHostApplication
{
    public static void Configure(WebApplication application,
        RealtimeSessionRouter router, GuestLoginService login,
        IRealtimeClock clock, ISet<string> allowedOrigins,
        IRealtimeOutboundSessionRegistry? outboundSessions = null)
    {
        if (application == null) throw new ArgumentNullException(nameof(application));
        if (router == null) throw new ArgumentNullException(nameof(router));
        if (login == null) throw new ArgumentNullException(nameof(login));
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (allowedOrigins == null) throw new ArgumentNullException(nameof(allowedOrigins));

        application.UseWebSockets();
        application.MapPost("/v1/login/guest", () =>
        {
            GuestLoginOutcome outcome = login.Login(clock.UtcUnixSeconds);
            return outcome.IsAvailable
                ? Results.Json(GuestLoginResponse.From(outcome))
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });
        application.Map("/v1/realtime", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest ||
                !RealtimeWebSocketPolicy.IsAllowed(context.Request.Path,
                    context.Request.Headers.Origin,
                    context.WebSockets.WebSocketRequestedProtocols, allowedOrigins))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync(
                RealtimeProtocol.WebSocketSubprotocol).ConfigureAwait(false);
            using var session = new WebSocketOutboundSession(socket);
            await new WebSocketIngress(router, outboundSessions).ProcessAsync(socket,
                session, context.RequestAborted).ConfigureAwait(false);
        });
    }
}
