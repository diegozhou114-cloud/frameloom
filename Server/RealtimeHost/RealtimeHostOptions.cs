namespace ZWebGLFrameWork.Server.Host;

public sealed class RealtimeHostOptions
{
    public string[] AllowedOrigins { get; init; } = new[] { "https://127.0.0.1:7443" };
    public ushort NativePort { get; init; } = 7777;
    public int MessagesPerSecond { get; init; } = 64;
    public int Burst { get; init; } = 128;

    public void Validate()
    {
        if (NativePort == 0) throw new ArgumentOutOfRangeException(nameof(NativePort));
        if (MessagesPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(MessagesPerSecond));
        if (Burst <= 0) throw new ArgumentOutOfRangeException(nameof(Burst));
        if (AllowedOrigins == null || AllowedOrigins.Length == 0 ||
            AllowedOrigins.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one allowed origin is required.",
                nameof(AllowedOrigins));
    }
}
