using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// HTTP 客户端 — 封装 UnityWebRequest，支持 GET/POST 异步请求。
    /// 适用于登录认证、CDN 资源下载、配置拉取等场景。
    /// 注意：HTTP 客户端不维持长连接，每次请求独立。
    /// </summary>
    public class HttpClient : INetworkClient
    {
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public NetworkProtocol Protocol => NetworkProtocol.HTTP;
        public bool IsConnected => State == ConnectionState.Connected;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<byte[]> OnDataReceived;
        public event Action<string> OnConnectFailed;

        private string _baseUrl;
        private MonoBehaviour _coroutineOwner;

        /// <summary>超时时间（秒）</summary>
        public float Timeout { get; set; } = 10f;

        /// <summary>重试次数</summary>
        public int MaxRetries { get; set; } = 2;

        public HttpClient(MonoBehaviour coroutineOwner = null)
        {
            _coroutineOwner = coroutineOwner;
            State = ConnectionState.Connected; // HTTP 无连接概念，始终就绪
        }

        // ==================== INetworkClient 实现 ====================

        public void Connect(string host, int port)
        {
            _baseUrl = $"http://{host}:{port}";
            State = ConnectionState.Connected;
            OnConnected?.Invoke();
        }

        public void Disconnect()
        {
            State = ConnectionState.Disconnected;
            OnDisconnected?.Invoke("HTTP disconnect");
        }

        public void Send(byte[] data)
        {
            // HTTP 不支持直接 Send 字节流，使用 GET/POST 方法
            Debug.LogWarning("[HttpClient] 请使用 Get/Post 方法，不支持原始 Send");
        }

        public void Send(byte[] data, int offset, int length)
        {
            Send(data);
        }

        public void Update()
        {
            // HTTP 无状态维护
        }

        // ==================== HTTP 特有 API ====================

        /// <summary>GET 请求</summary>
        /// <param name="url">完整 URL 或相对路径</param>
        /// <param name="onSuccess">成功回调（响应字节）</param>
        /// <param name="onError">失败回调（错误信息）</param>
        public void Get(string url, Action<byte[]> onSuccess, Action<string> onError = null)
        {
            string fullUrl = BuildUrl(url);
            var owner = _coroutineOwner ?? CoroutineHelper.Instance;
            owner.StartCoroutine(GetCoroutine(fullUrl, onSuccess, onError, MaxRetries));
        }

        /// <summary>GET 请求（返回字符串）</summary>
        public void GetString(string url, Action<string> onSuccess, Action<string> onError = null)
        {
            Get(url,
                bytes => onSuccess?.Invoke(Encoding.UTF8.GetString(bytes)),
                onError);
        }

        /// <summary>POST 请求</summary>
        /// <param name="url">完整 URL 或相对路径</param>
        /// <param name="body">请求体</param>
        /// <param name="contentType">Content-Type（默认 application/json）</param>
        public void Post(string url, byte[] body, Action<byte[]> onSuccess,
            Action<string> onError = null, string contentType = "application/json")
        {
            string fullUrl = BuildUrl(url);
            var owner = _coroutineOwner ?? CoroutineHelper.Instance;
            owner.StartCoroutine(PostCoroutine(fullUrl, body, contentType, onSuccess, onError, MaxRetries));
        }

        /// <summary>POST JSON 请求</summary>
        public void PostJson(string url, string json, Action<string> onSuccess,
            Action<string> onError = null)
        {
            var body = Encoding.UTF8.GetBytes(json);
            Post(url, body,
                bytes => onSuccess?.Invoke(Encoding.UTF8.GetString(bytes)),
                onError,
                "application/json");
        }

        // ==================== 协程 ====================

        private IEnumerator GetCoroutine(string url, Action<byte[]> onSuccess,
            Action<string> onError, int retriesLeft)
        {
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = (int)Timeout;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.data);
                }
                else if (retriesLeft > 0)
                {
                    Debug.LogWarning($"[HttpClient] GET {url} 失败，剩余重试 {retriesLeft}: {request.error}");
                    var owner = _coroutineOwner ?? CoroutineHelper.Instance;
                    yield return owner.StartCoroutine(
                        GetCoroutine(url, onSuccess, onError, retriesLeft - 1));
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        private IEnumerator PostCoroutine(string url, byte[] body, string contentType,
            Action<byte[]> onSuccess, Action<string> onError, int retriesLeft)
        {
            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", contentType);
                request.timeout = (int)Timeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    onSuccess?.Invoke(request.downloadHandler.data);
                }
                else if (retriesLeft > 0)
                {
                    Debug.LogWarning($"[HttpClient] POST {url} 失败，剩余重试 {retriesLeft}: {request.error}");
                    var owner = _coroutineOwner ?? CoroutineHelper.Instance;
                    yield return owner.StartCoroutine(
                        PostCoroutine(url, body, contentType, onSuccess, onError, retriesLeft - 1));
                }
                else
                {
                    onError?.Invoke(request.error);
                }
            }
        }

        private string BuildUrl(string path)
        {
            if (path.StartsWith("http://") || path.StartsWith("https://"))
                return path;

            if (_baseUrl == null)
                throw new InvalidOperationException("请先调用 Connect 设置 baseUrl");

            return _baseUrl + (path.StartsWith("/") ? path : "/" + path);
        }
    }

    /// <summary>
    /// 协程辅助 MonoBehaviour — 供非 MonoBehaviour 的 HttpClient 启动协程。
    /// </summary>
    internal class CoroutineHelper : MonoBehaviour
    {
        private static CoroutineHelper _instance;

        public static CoroutineHelper Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("__CoroutineHelper__");
                    _instance = go.AddComponent<CoroutineHelper>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }
    }
}
