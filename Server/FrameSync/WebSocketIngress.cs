using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class WebSocketIngress
    {
        private readonly RealtimeSessionRouter _router;

        public WebSocketIngress(RealtimeSessionRouter router)
        {
            _router = router ?? throw new ArgumentNullException(nameof(router));
        }

        public Task<ProtocolError> RouteCompleteMessageAsync(
            IRealtimeServerSession session, byte[] completeMessage,
            CancellationToken cancellationToken)
        {
            return _router.RouteAsync(session, completeMessage, cancellationToken);
        }
    }
}
