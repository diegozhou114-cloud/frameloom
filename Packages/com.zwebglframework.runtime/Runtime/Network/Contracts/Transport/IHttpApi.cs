using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ZWebGLFrameWork.Network.Contracts.Transport
{
    public readonly struct HttpMethod
    {
        public static readonly HttpMethod Post = new HttpMethod("POST");
        public string Value { get; }

        public HttpMethod(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("HTTP method is required.", nameof(value));
            Value = value;
        }
    }

    public sealed class HttpRequest
    {
        public HttpMethod Method { get; }
        public Uri Uri { get; }
        public byte[] Body { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }

        public HttpRequest(
            HttpMethod method, Uri uri, byte[] body,
            IReadOnlyDictionary<string, string> headers = null)
        {
            Method = method;
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
            Body = body ?? Array.Empty<byte>();
            Headers = headers ?? new Dictionary<string, string>();
        }
    }

    public sealed class HttpResponse
    {
        public long StatusCode { get; set; }
        public byte[] Body { get; set; } = Array.Empty<byte>();
        public string Error { get; set; }
    }

    public interface IHttpApi
    {
        Task<HttpResponse> SendAsync(HttpRequest request, CancellationToken cancellationToken);
    }
}
