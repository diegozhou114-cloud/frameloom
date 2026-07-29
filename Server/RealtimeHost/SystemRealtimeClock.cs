using System.Diagnostics;
using ZWebGLFrameWork.FrameSync;

namespace ZWebGLFrameWork.Server.Host;

public sealed class SystemRealtimeClock : IRealtimeClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    public long Milliseconds => _stopwatch.ElapsedMilliseconds;
    public long UtcUnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
