using System;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Contracts.Transport
{
    public interface IRealtimeChannel : IDisposable
    {
        RealtimeChannelState State { get; }
        Task ConnectAsync(
            RealtimeEndpoint endpoint, RealtimeCredentials credentials,
            CancellationToken cancellationToken);
        Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken);
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
        Task CloseAsync(RealtimeCloseReason reason, CancellationToken cancellationToken);
    }
}
