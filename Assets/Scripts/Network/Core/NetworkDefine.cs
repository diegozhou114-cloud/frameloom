using System;

namespace ZWebGLFrameWork.Network
{
    /// <summary>连接状态</summary>
    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
    }

    /// <summary>网络协议类型</summary>
    public enum NetworkProtocol
    {
        TCP,
        UDP,
        HTTP,
        KCP,
    }

    /// <summary>网络类型（底层物理网络）</summary>
    public enum NetworkType
    {
        Unknown,
        WiFi,
        Cellular,
    }

    /// <summary>IP 地址族
    /// 注意：与 System.Net.Sockets.AddressFamily 命名冲突，当前通过命名空间区分。
    /// 若摩擦加剧可重命名为 IpAddressFamily 以彻底消除歧义。
    /// </summary>
    public enum AddressFamily
    {
        IPv4,
        IPv6,
    }

    /// <summary>网络常量</summary>
    public static class NetworkDefine
    {
        /// <summary>默认端口</summary>
        public const int DefaultPort = 8888;

        /// <summary>接收缓冲区大小</summary>
        public const int ReceiveBufferSize = 64 * 1024; // 64KB

        /// <summary>发送缓冲区大小</summary>
        public const int SendBufferSize = 64 * 1024;

        /// <summary>心跳间隔（秒）</summary>
        public const float HeartbeatInterval = 5f;

        /// <summary>心跳超时（秒）</summary>
        public const float HeartbeatTimeout = 15f;

        /// <summary>重连基础间隔（秒）</summary>
        public const float ReconnectBaseInterval = 1f;

        /// <summary>重连最大间隔（秒）</summary>
        public const float ReconnectMaxInterval = 30f;

        /// <summary>重连指数退避倍数</summary>
        public const float ReconnectBackoffMultiplier = 2f;

        /// <summary>Happy Eyeballs: IPv6 优先等待时间（毫秒）</summary>
        public const int HappyEyeballsIPv6Delay = 250;

        /// <summary>连接超时（秒）</summary>
        public const float ConnectTimeout = 10f;

        /// <summary>DNS 缓存时间（秒）</summary>
        public const float DnsCacheTTL = 300f;

        // ==================== 消息 ID 预留范围 ====================

        /// <summary>系统消息 ID 起始</summary>
        public const ushort SystemMsgStart = 0;

        /// <summary>心跳请求</summary>
        public const ushort MsgHeartbeatReq = 1;

        /// <summary>心跳响应</summary>
        public const ushort MsgHeartbeatRes = 2;

        /// <summary>登录消息 ID 起始</summary>
        public const ushort LoginMsgStart = 100;

        /// <summary>游戏消息 ID 起始</summary>
        public const ushort GameMsgStart = 1000;
    }

    /// <summary>网络事件数据</summary>
    public struct NetworkStateChangedEvent
    {
        public ConnectionState OldState;
        public ConnectionState NewState;
        public NetworkProtocol Protocol;
        public string Message;
        public override string ToString() => $"[{Protocol}] {OldState} → {NewState}";
    }
}
