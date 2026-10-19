using MiniSiegeHost;

if (args.Contains("--verify"))
{
    await SiegeVerifier.RunAsync(args);
    return;
}
var builder = WebApplication.CreateBuilder(args);
if (!args.Contains("--urls")) builder.WebHost.UseUrls("http://127.0.0.1:8787");
var app = builder.Build();
var lobby = new SiegeLobby();
app.UseWebSockets();
app.MapGet("/health", () => lobby.Status());
app.Map("/siege", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await lobby.ConnectAsync(socket, context.RequestAborted);
});
app.Lifetime.ApplicationStopping.Register(lobby.Stop);
await app.RunAsync();
