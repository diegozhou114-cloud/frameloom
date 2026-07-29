using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Host;

public static class RealtimeWebSocketPolicy
{
    public static bool IsAllowed(string? path, string? origin,
        IEnumerable<string>? requestedSubprotocols,
        ISet<string> allowedOrigins)
    {
        if (allowedOrigins == null) throw new ArgumentNullException(nameof(allowedOrigins));
        return string.Equals(path, RealtimeProtocol.WebSocketPath,
                   StringComparison.Ordinal) &&
               origin != null && allowedOrigins.Contains(origin) &&
               requestedSubprotocols != null && requestedSubprotocols.Contains(
                   RealtimeProtocol.WebSocketSubprotocol, StringComparer.Ordinal);
    }
}
