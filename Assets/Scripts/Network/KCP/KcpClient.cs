using System;
using UnityEngine;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// KCP 客户端 — 基于 KcpCore 的可靠 UDP 传输，实现 INetworkClient 接口。
    /// 
    /// 架构：UDP Socket → KcpCore → KcpClient
    /// 定时驱动 KcpCore.Update() 处理超时重传和流量控制。
    /// 
    /// 适用场景：实时对战、状态同步等需要低延迟可靠传输的场景。
    /// </summary>
    public class KcpClient : INetworkClient
    {
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public NetworkProtocol Protocol => NetworkProtocol.KCP;
        public bool IsConnected => State == ConnectionState.Connected;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<byte[]> OnDataReceived;
        public event Action<string> OnConnectFailed;

        private KcpCore _kcp;
        private UdpClient _udpClient;
        private uint _conv;

        /// <summary>KCP Update 间隔（毫秒）</summary>
        public uint UpdateInterval
        {
            get => _kcp?.Interval ?? 10;
            set { if (_kcp != null) _kcp.Interval = value; }
        }

        /// <summary>是否启用 nodelay 模式</summary>
        public bool NoDelay
        {
            set => _kcp?.SetNoDelay(
                value ? 1 : 0,
                KcpCore.IKCP_INTERVAL,
                value ? 2 : 0,
                value ? 1 : 0);
        }

        // ==================== 连接 ====================

        public void Connect(string host, int port)
        {
            if (State == ConnectionState.Connected) return;

            _conv = (uint)new System.Random().Next(1, int.MaxValue);
            _kcp = new KcpCore(_conv, OnKcpOutput);
            _kcp.SetNoDelay(1, KcpCore.IKCP_INTERVAL, 2, 1); // nodelay 模式
            _kcp.SetWindowSize(128, 128);
            _kcp.SetMtu(512);

            _udpClient = new UdpClient();
            _udpClient.OnConnected += HandleUdpConnected;
            _udpClient.OnDataReceived += OnUdpData;
            _udpClient.OnDisconnected += HandleUdpDisconnected;
            _udpClient.OnConnectFailed += (err) => OnConnectFailed?.Invoke(err);

            _udpClient.Connect(host, port);
        }

        public void Disconnect()
        {
            if (_udpClient != null)
            {
                _udpClient.OnConnected -= HandleUdpConnected;
                _udpClient.OnDataReceived -= OnUdpData;
                _udpClient.OnDisconnected -= HandleUdpDisconnected;
                _udpClient.Disconnect();
                _udpClient = null;
            }

            if (_kcp != null)
            {
                _kcp.Release();
                _kcp = null;
            }

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
            if (_kcp == null || State != ConnectionState.Connected) return;

            int ret = _kcp.Send(data, offset, length);
            if (ret < 0)
            {
                Debug.LogError($"[KcpClient] 发送失败: {ret}");
            }
        }

        // ==================== 更新（必须每帧调用！） ====================

        public void Update()
        {
            if (_kcp == null || _udpClient == null) return;

            uint current = (uint)(Time.unscaledTime * 1000);
            _kcp.Update(current);

            // 接收 KCP 可靠数据
            int peekSize = _kcp.PeekSize();
            while (peekSize > 0)
            {
                var buffer = new byte[peekSize];
                int received = _kcp.Recv(buffer);
                if (received > 0)
                {
                    var data = new byte[received];
                    Buffer.BlockCopy(buffer, 0, data, 0, received);
                    OnDataReceived?.Invoke(data);
                }
                peekSize = _kcp.PeekSize();
            }

            // 驱动 UDP 更新
            _udpClient.Update();
        }

        // ==================== UDP 回调 ====================

        private void HandleUdpConnected()
        {
            State = ConnectionState.Connected;
            OnConnected?.Invoke();
            Debug.Log($"[KcpClient] KCP 连接建立, conv={_conv}");
        }

        private void HandleUdpDisconnected(string reason)
        {
            State = ConnectionState.Disconnected;
            OnDisconnected?.Invoke(reason);
        }

        private void OnUdpData(byte[] data)
        {
            // 喂入 KCP 进行可靠传输处理
            int ret = _kcp.Input(data);
            if (ret < 0)
            {
                Debug.LogWarning($"[KcpClient] KCP Input 错误: {ret}");
            }
        }

        // ==================== KCP 输出回调 ====================

        private void OnKcpOutput(byte[] data, int length)
        {
            // KCP 产出数据 → 通过 UDP 发送
            _udpClient?.Send(data, 0, length);
        }

        // ==================== 调试 ====================

        /// <summary>获取 KCP 内部状态（调试用）</summary>
        public string GetDebugInfo()
        {
            if (_kcp == null) return "KCP null";
            return $"snd_una={_kcp.SndUna} snd_nxt={_kcp.SndNxt} " +
                   $"rcv_nxt={_kcp.RcvNxt} cwnd={_kcp.Cwnd} " +
                   $"srtt={_kcp.RxSrtt} rto={_kcp.RxRto} " +
                   $"snd_queue={_kcp.SndQueueCount} rcv_queue={_kcp.RcvQueueCount}";
        }
    }
}
