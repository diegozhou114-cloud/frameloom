using System;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// 网络管理器 — 跨场景单例，统一管理 TCP/UDP/HTTP/KCP 四种协议。
    /// 运行时协议切换，整合地址解析、网络感知、消息分发。
    /// 连接状态变更通过 EventBus 广播。
    /// </summary>
    public class NetworkManager : SingletonDontDestroy<NetworkManager>
    {
        /// <summary>EventBus 事件键：网络连接状态变更</summary>
        public const string EventNetworkStateChanged = "NetworkStateChanged";

        /// <summary>当前使用的协议</summary>
        public NetworkProtocol CurrentProtocol { get; private set; }

        /// <summary>当前连接状态</summary>
        public ConnectionState State => _currentClient?.State ?? ConnectionState.Disconnected;

        /// <summary>是否已连接</summary>
        public bool IsConnected => _currentClient?.IsConnected ?? false;

        /// <summary>消息分发器</summary>
        public MessageDispatcher Dispatcher { get; } = new MessageDispatcher();

        private INetworkClient _currentClient;
        private INetworkClient _tcpClient;
        private INetworkClient _udpClient;
        private INetworkClient _httpClient;
        private INetworkClient _kcpClient;

        private string _lastHost;
        private int _lastPort;

        protected override void Awake()
        {
            base.Awake();
            // 协议客户端改为懒加载，仅在首次 Connect 时创建
        }

        private INetworkClient GetOrCreateClient(NetworkProtocol protocol) => protocol switch
        {
            NetworkProtocol.TCP => _tcpClient ??= new TcpClient(),
            NetworkProtocol.UDP => _udpClient ??= new UdpClient(),
            NetworkProtocol.HTTP => _httpClient ??= new HttpClient(this),
            NetworkProtocol.KCP => _kcpClient ??= new KcpClient(),
            _ => throw new ArgumentException($"未知协议: {protocol}"),
        };

        private void Update()
        {
            _currentClient?.Update();
            Dispatcher.Dispatch();
        }

        // ==================== 公开 API ====================

        /// <summary>连接到服务器</summary>
        public void Connect(NetworkProtocol protocol, string host, int port)
        {
            Disconnect();

            _lastHost = host;
            _lastPort = port;
            CurrentProtocol = protocol;

            _currentClient = GetOrCreateClient(protocol);

            // 绑定事件
            _currentClient.OnConnected += HandleConnected;
            _currentClient.OnDisconnected += HandleDisconnected;
            _currentClient.OnConnectFailed += HandleConnectFailed;
            _currentClient.OnDataReceived += HandleDataReceived;

            Debug.Log($"[NetworkManager] 正在连接: {protocol}://{host}:{port}");
            _currentClient.Connect(host, port);
        }

        /// <summary>断开连接</summary>
        public void Disconnect()
        {
            if (_currentClient != null)
            {
                _currentClient.OnConnected -= HandleConnected;
                _currentClient.OnDisconnected -= HandleDisconnected;
                _currentClient.OnConnectFailed -= HandleConnectFailed;
                _currentClient.OnDataReceived -= HandleDataReceived;

                _currentClient.Disconnect();
                _currentClient = null;
            }
        }

        /// <summary>发送消息（自动编码为长度前缀帧）</summary>
        public void Send(ushort messageId, byte[] body)
        {
            if (!IsConnected) return;

            var frame = MessageDispatcher.BuildMessage(messageId, body);
            _currentClient?.Send(frame);
        }

        /// <summary>发送纯消息 ID（无消息体，如心跳响应）</summary>
        public void Send(ushort messageId)
        {
            if (!IsConnected) return;

            var frame = MessageDispatcher.BuildMessage(messageId);
            _currentClient?.Send(frame);
        }

        /// <summary>直接发送原始数据（不经过帧协议）</summary>
        public void SendRaw(byte[] data)
        {
            if (!IsConnected) return;
            _currentClient?.Send(data);
        }

        /// <summary>注册消息处理器</summary>
        public void RegisterHandler(ushort messageId, MessageHandler handler)
        {
            Dispatcher.Register(messageId, handler);
        }

        /// <summary>取消注册消息处理器</summary>
        public void UnregisterHandler(ushort messageId, MessageHandler handler)
        {
            Dispatcher.Unregister(messageId, handler);
        }

        // ==================== 事件处理 ====================

        private void HandleConnected()
        {
            BroadcastState(ConnectionState.Connected, null);
        }

        private void HandleDisconnected(string reason)
        {
            BroadcastState(ConnectionState.Disconnected, reason);
        }

        private void HandleConnectFailed(string error)
        {
            BroadcastState(ConnectionState.Disconnected, error);
        }

        private void HandleDataReceived(byte[] data)
        {
            // 各传输层客户端（TcpClient/KcpClient）已经完成帧解码，
            // NetworkManager 收到的是已解码的消息体，直接入队分发。
            Dispatcher.Enqueue(data);
        }

        private void BroadcastState(ConnectionState newState, string message)
        {
            var evt = new NetworkStateChangedEvent
            {
                OldState = ConnectionState.Disconnected,
                NewState = newState,
                Protocol = CurrentProtocol,
                Message = message ?? "",
            };

            EventBus.Publish(EventNetworkStateChanged, evt);
        }

        protected override void OnDestroy()
        {
            Disconnect();
            Dispatcher.Clear();
            base.OnDestroy();
        }

        // ==================== 调试 ====================

        /// <summary>获取当前客户端（调试用）</summary>
        public INetworkClient GetCurrentClient() => _currentClient;

        /// <summary>获取指定协议的客户端</summary>
        public INetworkClient GetClient(NetworkProtocol protocol) => protocol switch
        {
            NetworkProtocol.TCP => _tcpClient,
            NetworkProtocol.UDP => _udpClient,
            NetworkProtocol.HTTP => _httpClient,
            NetworkProtocol.KCP => _kcpClient,
            _ => null,
        };
    }
}
