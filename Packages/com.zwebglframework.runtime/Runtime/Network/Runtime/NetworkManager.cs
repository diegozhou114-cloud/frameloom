using System;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Network.Contracts.Transport;
using ZWebGLFrameWork.Network.Runtime;
using ZWebGLFrameWork.Network.UnityHttp;

namespace ZWebGLFrameWork.Network
{
    public sealed class NetworkManager : SingletonDontDestroy<NetworkManager>
    {
        public IHttpApi Http { get; private set; }
        public RealtimeConnectionController Realtime { get; private set; }

        protected override void OnSingletonAwake()
        {
            Http = new UnityHttpApi();
            Realtime = new RealtimeConnectionController(
                RealtimeChannelFactoryRegistry.Current ?? throw new InvalidOperationException(
                    "Realtime platform factory was not installed."));
        }

        public async Task<GuestLoginResult> GuestLoginAsync(
            Uri endpoint, CancellationToken cancellationToken)
        {
            HttpRequest request = GuestLoginRequest.Create(endpoint);
            HttpResponse response = await Http.SendAsync(request, cancellationToken);
            return GuestLoginResult.Parse(response);
        }

        protected override void OnDestroy()
        {
            Realtime?.Dispose();
            base.OnDestroy();
        }
    }
}
