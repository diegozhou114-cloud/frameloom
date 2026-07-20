namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeClock : IMonotonicClock
    {
        long UtcUnixSeconds { get; }
    }
}
