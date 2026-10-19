using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Net.WebSockets;
#else
using System.Runtime.InteropServices;
#endif

namespace ZWebGLFrameWork.MiniSiege
{
    /// <summary>Ordered WebSocket transport; dispatches all simulation messages on Unity's thread.</summary>
    public sealed class SiegeConnection : IDisposable
    {
        private readonly ConcurrentQueue<string> _incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();
        public event Action<string> Message;
        public event Action<string> Error;
        public bool Connected { get; private set; }
        public bool Disposed { get; private set; }
#if !UNITY_WEBGL || UNITY_EDITOR
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1);
#else
        private int _handle;
        [DllImport("__Internal")] private static extern int SiegeOpen(string url, string receiver);
        [DllImport("__Internal")] private static extern void SiegeSend(int handle, string message);
        [DllImport("__Internal")] private static extern void SiegeClose(int handle);
#endif

        /// <summary>Connect without blocking the render loop.</summary>
        public void Connect(string url, string receiver)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            _ = ReceiveLoop(url);
#else
            _handle = SiegeOpen(url, receiver);
#endif
        }

        public void ReceiveWebMessage(string message) => _incoming.Enqueue(message);
        public void ReceiveWebError(string message) => _errors.Enqueue(message);
        public void ReceiveWebOpen() => Connected = true;

        /// <summary>Consume in wire order; if the application falls behind it does not skip frames.</summary>
        public void Pump()
        {
            int count = 0;
            while (count++ < 128 && _incoming.TryDequeue(out string message)) Message?.Invoke(message);
            while (_errors.TryDequeue(out string error)) Error?.Invoke(error);
        }

        public void Send(string message)
        {
            if (Disposed || !Connected) return;
#if !UNITY_WEBGL || UNITY_EDITOR
            _ = SendNative(message);
#else
            SiegeSend(_handle, message);
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private async Task ReceiveLoop(string url)
        {
            try
            {
                await _socket.ConnectAsync(new Uri(url), _cancel.Token).ConfigureAwait(false);
                Connected = true;
                var buffer = new byte[32768];
                while (!_cancel.IsCancellationRequested)
                {
                    int length = 0;
                    WebSocketReceiveResult part;
                    do
                    {
                        part = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer, length,
                            buffer.Length - length), _cancel.Token).ConfigureAwait(false);
                        if (part.MessageType == WebSocketMessageType.Close)
                            throw new InvalidOperationException("连接已关闭，请重新连接");
                        length += part.Count;
                        if (length >= buffer.Length) throw new InvalidOperationException("数据帧过大");
                    } while (!part.EndOfMessage);
                    if (_incoming.Count >= 512) throw new InvalidOperationException("客户端处理过慢，请重新连接");
                    _incoming.Enqueue(Encoding.UTF8.GetString(buffer, 0, length));
                }
            }
            catch (Exception exception)
            {
                if (!Disposed) _errors.Enqueue(exception.Message);
            }
            finally { Connected = false; }
        }

        private async Task SendNative(string message)
        {
            bool entered = false;
            try
            {
                await _sendGate.WaitAsync(_cancel.Token).ConfigureAwait(false);
                entered = true;
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                    true, _cancel.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                if (!Disposed) _errors.Enqueue(exception.Message);
            }
            finally { if (entered) _sendGate.Release(); }
        }
#endif

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Connected = false;
#if !UNITY_WEBGL || UNITY_EDITOR
            _cancel.Cancel();
            _socket.Abort();
            _socket.Dispose();
#else
            SiegeClose(_handle);
#endif
        }
    }
}
