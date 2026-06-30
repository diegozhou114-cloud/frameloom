using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// UDP 客户端 — 无连接数据报收发，适合实时同步（位置、状态）和 KCP 底层。
    /// 支持 IPv4/IPv6 双栈，通过 AddressResolver 解析。
    /// </summary>
    public class UdpClient : INetworkClient
    {
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public NetworkProtocol Protocol => NetworkProtocol.UDP;
        public bool IsConnected => State == ConnectionState.Connected;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<byte[]> OnDataReceived;
        public event Action<string> OnConnectFailed;

        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private readonly byte[] _receiveBuffer = new byte[NetworkDefine.ReceiveBufferSize];
        private Thread _receiveThread;
        private volatile bool _isReceiving;
        private readonly ConcurrentQueue<byte[]> _pendingData = new ConcurrentQueue<byte[]>();

        // ==================== 连接 ====================

        public async void Connect(string host, int port)
        {
            if (State == ConnectionState.Connected) return;

            State = ConnectionState.Connecting;

            try
            {
                var dnsResult = await AddressResolver.ResolveAsync(host);

                string ip = dnsResult.HasIPv6 ? dnsResult.IPv6Address : dnsResult.IPv4Address;
                if (string.IsNullOrEmpty(ip))
                {
                    OnConnectFailed?.Invoke($"DNS 解析失败: {host}");
                    State = ConnectionState.Disconnected;
                    return;
                }

                var addressFamily = dnsResult.HasIPv6
                    ? System.Net.Sockets.AddressFamily.InterNetworkV6
                    : System.Net.Sockets.AddressFamily.InterNetwork;

                _socket = new Socket(addressFamily, SocketType.Dgram, ProtocolType.Udp);

                // 双栈模式：IPv6 socket 同时支持 IPv4
                if (addressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    _socket.DualMode = true;
                }

                _socket.Bind(new IPEndPoint(
                    addressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                        ? IPAddress.IPv6Any
                        : IPAddress.Any,
                    0));

                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                State = ConnectionState.Connected;

                // 启动接收线程
                _isReceiving = true;
                _receiveThread = new Thread(ReceiveLoop)
                {
                    IsBackground = true,
                    Name = "UdpClient-Recv",
                };
                _receiveThread.Start();

                OnConnected?.Invoke();
                Debug.Log($"[UdpClient] UDP 已绑定: {host}:{port}");
            }
            catch (Exception ex)
            {
                State = ConnectionState.Disconnected;
                OnConnectFailed?.Invoke(ex.Message);
                Debug.LogError($"[UdpClient] 绑定失败: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            _isReceiving = false;

            try { _socket?.Close(); } catch { }
            _socket = null;
            _remoteEndPoint = null;

            if (State != ConnectionState.Disconnected)
            {
                State = ConnectionState.Disconnected;
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
            if (_socket == null || _remoteEndPoint == null) return;

            try
            {
                _socket.SendTo(data, offset, length, SocketFlags.None, _remoteEndPoint);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UdpClient] 发送失败: {ex.Message}");
            }
        }

        // ==================== 更新 ====================

        public void Update()
        {
            ProcessPendingData();
        }

        /// <summary>
        /// 在主线程调用，将接收线程入队的数据安全地分发给上层。
        /// </summary>
        public void ProcessPendingData()
        {
            while (_pendingData.TryDequeue(out var data))
            {
                OnDataReceived?.Invoke(data);
            }
        }

        // ==================== 接收线程 ====================

        private void ReceiveLoop()
        {
            EndPoint remoteEP = new IPEndPoint(
                _socket.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? IPAddress.IPv6Any
                    : IPAddress.Any,
                0);

            while (_isReceiving && _socket != null)
            {
                try
                {
                    int received = _socket.ReceiveFrom(_receiveBuffer, ref remoteEP);

                    if (received > 0)
                    {
                        var data = new byte[received];
                        Buffer.BlockCopy(_receiveBuffer, 0, data, 0, received);
                        _pendingData.Enqueue(data);
                    }
                }
                catch (SocketException)
                {
                    if (_isReceiving) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UdpClient] 接收异常: {ex.Message}");
                    break;
                }
            }
        }

        /// <summary>获取底层 Socket（供 KCP 使用）</summary>
        public Socket GetSocket() => _socket;

        /// <summary>获取远端地址</summary>
        public IPEndPoint GetRemoteEndPoint() => _remoteEndPoint;
    }
}
