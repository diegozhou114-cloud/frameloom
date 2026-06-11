using System;

namespace ZWebGLFrameWork.Network
{
    /// <summary>
    /// 统一网络客户端接口 — 所有协议（TCP/UDP/HTTP/KCP）实现此接口。
    /// </summary>
    public interface INetworkClient
    {
        /// <summary>当前连接状态</summary>
        ConnectionState State { get; }

        /// <summary>协议类型</summary>
        NetworkProtocol Protocol { get; }

        /// <summary>是否已连接</summary>
        bool IsConnected { get; }

        // ==================== 连接 ====================

        /// <summary>连接到服务器</summary>
        /// <param name="host">主机名或 IP 地址（支持 IPv4/IPv6）</param>
        /// <param name="port">端口号</param>
        void Connect(string host, int port);

        /// <summary>断开连接</summary>
        void Disconnect();

        // ==================== 发送 ====================

        /// <summary>发送数据</summary>
        void Send(byte[] data);

        /// <summary>发送数据（指定偏移和长度）</summary>
        void Send(byte[] data, int offset, int length);

        // ==================== 更新 ====================

        /// <summary>每帧更新（KCP 需要定时驱动，TCP/UDP/HTTP 可用于消费接收队列）</summary>
        void Update();

        // ==================== 事件 ====================

        /// <summary>连接成功</summary>
        event Action OnConnected;

        /// <summary>断开连接</summary>
        event Action<string> OnDisconnected;

        /// <summary>收到数据</summary>
        event Action<byte[]> OnDataReceived;

        /// <summary>连接失败</summary>
        event Action<string> OnConnectFailed;
    }
}
