using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.UnityHttp
{
    public sealed class NetworkPolicyException : InvalidOperationException
    {
        public NetworkPolicyException(string message) : base(message)
        {
        }
    }

    public sealed class UnityHttpApi : IHttpApi
    {
        public static void ValidateOnlineUri(Uri uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
                throw new NetworkPolicyException("Online HTTP endpoint must use HTTPS.");
        }

        public async Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateOnlineUri(request.Uri);
            using (var unityRequest = new UnityWebRequest(request.Uri.AbsoluteUri, request.Method.Value))
            {
                unityRequest.uploadHandler = new UploadHandlerRaw(request.Body);
                unityRequest.downloadHandler = new DownloadHandlerBuffer();
                foreach (var header in request.Headers)
                    unityRequest.SetRequestHeader(header.Key, header.Value);
                using (cancellationToken.Register(unityRequest.Abort))
                {
                    UnityWebRequestAsyncOperation operation = unityRequest.SendWebRequest();
                    while (!operation.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new HttpResponse
                {
                    StatusCode = unityRequest.responseCode,
                    Body = unityRequest.downloadHandler.data ?? Array.Empty<byte>(),
                    Error = unityRequest.error
                };
            }
        }
    }
}
