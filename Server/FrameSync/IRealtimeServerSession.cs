namespace ZWebGLFrameWork.FrameSync
{
    public interface IRealtimeServerSession
    {
        bool IsAuthenticated { get; }
        RealtimeSessionClaims? Claims { get; }
        void Bind(RealtimeSessionClaims claims);
    }
}
