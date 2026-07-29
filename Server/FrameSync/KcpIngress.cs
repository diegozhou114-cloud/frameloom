using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class KcpIngress
    {
        private readonly RealtimeSessionRouter _router;

        public KcpIngress(RealtimeSessionRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public Task<ProtocolError> RouteAuthenticatedMessageAsync(
            IRealtimeServerSession session, byte[] completeMessage,
            CancellationToken cancellationToken)
        {
            return _router.RouteAsync(session, completeMessage, cancellationToken);
        }
    }
}
