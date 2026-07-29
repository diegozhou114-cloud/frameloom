using ZWebGLFrameWork.Network.Contracts.Protocol;
using System.Net.WebSockets;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class WebSocketIngress
    {
        private const int ReceiveBufferBytes = 4096;
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

        public async Task ProcessAsync(WebSocket socket,
            IRealtimeServerSession session, CancellationToken cancellationToken)
        {
            if (socket == null) throw new ArgumentNullException(nameof(socket));
            if (session == null) throw new ArgumentNullException(nameof(session));

            try
            {
                var buffer = new byte[ReceiveBufferBytes];
                while (socket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), cancellationToken)
                            .ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            await CloseAsync(socket, ProtocolError.InvalidPayload,
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }
                        if (message.Length + result.Count >
                            RealtimeProtocol.MaxMessageBytes)
                        {
                            await CloseAsync(socket, ProtocolError.MessageTooLarge,
                                cancellationToken).ConfigureAwait(false);
                            return;
                        }
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    bool wasAuthenticated = session.IsAuthenticated;
                    ProtocolError error = await RouteCompleteMessageAsync(
                        session, message.ToArray(), cancellationToken)
                        .ConfigureAwait(false);
                    if (error == ProtocolError.None)
                    {
                        if (!wasAuthenticated && session.IsAuthenticated)
                            await SendAuthAcceptedAsync(socket, session,
                                cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    await CloseAsync(socket, error, cancellationToken)
                        .ConfigureAwait(false);
                    return;
                }
            }
            finally
            {
                _router.Disconnect(session);
            }
        }

        private static Task CloseAsync(WebSocket socket, ProtocolError error,
            CancellationToken cancellationToken)
        {
            return socket.CloseAsync((WebSocketCloseStatus)CloseCodeFor(error),
                error.ToString(), cancellationToken);
        }

        private static Task SendAuthAcceptedAsync(WebSocket socket,
            IRealtimeServerSession session, CancellationToken cancellationToken)
        {
            RealtimeSessionClaims claims = session.Claims ??
                throw new InvalidOperationException("Authenticated session has no claims.");
            byte[] accepted = RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = claims.SessionId,
                LastAuthorityTick = -1,
                TimelineRevision = 0
            });
            return socket.SendAsync(new ArraySegment<byte>(accepted),
                WebSocketMessageType.Binary, true, cancellationToken);
        }

        private static RealtimeCloseCode CloseCodeFor(ProtocolError error)
        {
            return error switch
            {
                ProtocolError.AuthenticationFailed => RealtimeCloseCode.Authentication,
                ProtocolError.WrongSession => RealtimeCloseCode.Authentication,
                ProtocolError.WrongPlayer => RealtimeCloseCode.Authentication,
                ProtocolError.WrongSource => RealtimeCloseCode.Authentication,
                ProtocolError.ReplayedPacket => RealtimeCloseCode.Authentication,
                ProtocolError.UnsupportedVersion => RealtimeCloseCode.Version,
                ProtocolError.MessageTooLarge => RealtimeCloseCode.Overload,
                ProtocolError.RateLimited => RealtimeCloseCode.Overload,
                ProtocolError.ExpiredTicket => RealtimeCloseCode.ExpiredSession,
                _ => RealtimeCloseCode.Malformed
            };
        }
    }
}
