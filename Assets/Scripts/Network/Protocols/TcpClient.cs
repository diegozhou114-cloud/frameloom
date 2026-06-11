using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// TCP 客户端 — 异步 Socket 实现，双栈支持（IPv4/IPv6）。
    /// 后台线程接收数据，通过回调上抛到主线程。
    /// </summary>
    public class TcpClient : INetworkClient
    {
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public NetworkProtocol Protocol => NetworkProtocol.TCP;
        public bool IsConnected => State == ConnectionState.Connected;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<byte[]> OnDataReceived;
        public event Action<string> OnConnectFailed;

        private Socket _socket;
        private readonly MessageProtocol _protocol = new MessageProtocol();
        private readonly byte[] _receiveBuffer = new byte[NetworkDefine.ReceiveBufferSize];
        private Thread _receiveThread;
        private bool _isReceiving;
        private float _lastHeartbeatTime;
        private float _reconnectTimer;
        private int _reconnectAttempts;
        private string _lastHost;
        private int _lastPort;

        // ==================== 连接 ====================

        public async void Connect(string host, int port)
        {
            if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
                return;

            _lastHost = host;
            _lastPort = port;
            State = ConnectionState.Connecting;
            _reconnectAttempts = 0;

            try
            {
                var (socket, _) = await AddressResolver.ConnectWithHappyEyeballsAsync(host, port);

                _socket = socket;
                _socket.NoDelay = true;           // 禁用 Nagle 算法，降低延迟
                _socket.ReceiveTimeout = 0;        // 阻塞模式不需要超时
                _socket.SendTimeout = 5000;

                State = ConnectionState.Connected;
                _reconnectAttempts = 0;
                _lastHeartbeatTime = Time.unscaledTime;

                // 启动接收线程
                _isReceiving = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "TcpClient-Recv",
                };
                _receiveThread.Start();

                OnConnected?.Invoke();
                Debug.Log($"[TcpClient] 已连接: {host}:{port}");
            }
            catch (Exception ex)
            {
                State = ConnectionState.Disconnected;
                OnConnectFailed?.Invoke(ex.Message);
                Debug.LogError($"[TcpClient] 连接失败: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _isReceiving = false;

            try
            {
                _socket?.Shutdown(SocketShutdown.Both);
                _socket?.Close();
            }
            catch { }
            finally
            {
                _socket = null;
            }

            if (State != ConnectionState.Disconnected)
            {
                State = ConnectionState.Disconnected;
                _protocol.Reset();
                OnDisconnected?.Invoke("主动断开");
            }
        }

        // ==================== 发送 ====================

        public void Send(byte[] data)
        {
            Send(data, 0, data.Length);
        }

        public void Send(byte[] data, int offset, int length)
        {
            if (_socket == null || !_socket.Connected) return;

            try
            {
                _socket.Send(data, offset, length, SocketFlags.None);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TcpClient] 发送失败: {ex.Message}");
                HandleDisconnect("发送失败");
            }
        }

        // ==================== 更新 ====================

        public void Update()
        {
            // 心跳
            if (State == ConnectionState.Connected)
            {
                if (Time.unscaledTime - _lastHeartbeatTime > NetworkDefine.HeartbeatInterval)
                {
                    SendHeartbeat();
                    _lastHeartbeatTime = Time.unscaledTime;
                }
            }

            // 自动重连
            if (State == ConnectionState.Disconnected && !string.IsNullOrEmpty(_lastHost))
            {
                _reconnectTimer += Time.unscaledDeltaTime;
                float interval = Mathf.Min(
                    NetworkDefine.ReconnectBaseInterval *
                    Mathf.Pow(NetworkDefine.ReconnectBackoffMultiplier, _reconnectAttempts),
                    NetworkDefine.ReconnectMaxInterval);

                if (_reconnectTimer >= interval)
                {
                    _reconnectTimer = 0f;
                    _reconnectAttempts++;
                    Debug.Log($"[TcpClient] 重连尝试 #{_reconnectAttempts}...");
                    Connect(_lastHost, _lastPort);
                }
            }
        }

        // ==================== 接收线程 ====================

        private void ReceiveLoop()
        {
            while (_isReceiving && _socket != null && _socket.Connected)
            {
                try
                {
                    int received = _socket.Receive(_receiveBuffer, 0, _receiveBuffer.Length,
                        SocketFlags.None);

                    if (received == 0)
                    {
                        // 连接正常关闭
                        HandleDisconnect("服务器关闭连接");
                        break;
                    }

                    // 协议解码
                    byte[] message;
                    while ((message = _protocol.TryDecode(_receiveBuffer, received)) != null)
                    {
                        OnDataReceived?.Invoke(message);
                    }
                }
                catch (SocketException ex)
                {
                    HandleDisconnect($"Socket 异常: {ex.Message}");
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[TcpClient] 接收异常: {ex.Message}");
                    break;
                }
            }
        }

        // ==================== 内部 ====================

        private void SendHeartbeat()
        {
            var frame = MessageProtocol.Encode(new byte[] { 0, 0 }); // 空消息作心跳
            Send(frame);
        }

        private void HandleDisconnect(string reason)
        {
            _isReceiving = false;

            try { _socket?.Close(); } catch { }
            _socket = null;
            State = ConnectionState.Disconnected;
            _protocol.Reset();

            OnDisconnected?.Invoke(reason);
            Debug.LogWarning($"[TcpClient] 断开: {reason}");
        }
    }
}
