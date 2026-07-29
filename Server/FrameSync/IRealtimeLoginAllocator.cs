namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeLoginAllocator
    {
        bool TryAssignGuest(out RealtimeLoginAssignment? assignment);
    }
}
