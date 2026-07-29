using ZWebGLFrameWork.FrameSync;

namespace ZWebGLFrameWork.Server.Host;

public sealed class GuestLoginResponse
{
    public string playerId { get; init; } = string.Empty;
    public string sessionId { get; init; } = string.Empty;
    public string ticket { get; init; } = string.Empty;
    public string resumeToken { get; init; } = string.Empty;
    public string macKey { get; init; } = string.Empty;
    public long expiresAtUnixSeconds { get; init; }

    public static GuestLoginResponse From(GuestLoginOutcome outcome)
    {
        if (outcome == null || !outcome.IsAvailable)
            throw new ArgumentException("A successful login outcome is required.",
                nameof(outcome));
        IssuedRealtimeCredentials credentials = outcome.Credentials!;
        return new GuestLoginResponse
        {
            playerId = credentials.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sessionId = credentials.SessionId.ToString(),
            ticket = Convert.ToBase64String(credentials.OpaqueTicket),
            resumeToken = Convert.ToBase64String(credentials.ResumeToken),
            macKey = Convert.ToBase64String(credentials.MacKey),
            expiresAtUnixSeconds = credentials.ExpiresAtUnixSeconds
        };
    }
}
