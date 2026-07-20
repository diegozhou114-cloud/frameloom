namespace ZWebGLFrameWork.Network.Contracts.Protocol
{
    public static class RealtimeProtocol
    {
        public const ushort Version = 1;
        public const int HeaderBytes = 12;
        public const int MaxMessageBytes = 65536;
        public const int MaxCommandsPerAuthorityFrame = 64;
        public const int RollbackWindowTicks = 64;
        public const int NativeReplayWindowPackets = 1024;
        public const string WebSocketPath = "/v1/realtime";
        public const string WebSocketSubprotocol = "zweb.framesync.v1";
    }

    public enum ProtocolError
    {
        None = 0,
        Truncated,
        UnsupportedVersion,
        UnknownMessageKind,
        InvalidLength,
        InvalidCount,
        InvalidPayload,
        UnorderedCommands,
        AuthenticationFailed,
        ReplayedPacket,
        WrongSession,
        ExpiredTicket,
        WrongPlayer,
        WrongSource,
        MessageTooLarge,
        RateLimited,
        LateRejected
    }

    public enum RealtimeMessageKind : byte
    {
        AuthHello = 1,
        ResumeHello = 2,
        AuthAccepted = 3,
        Command = 4,
        Authority = 5,
        ResyncRequest = 6,
        ResyncSnapshot = 7,
        Close = 8
    }

    public enum RealtimeTransport { WebSocket, NativeKcp }
    public enum RealtimeChannelState { Disconnected, Connecting, Connected, Closing }
    public enum RealtimeCloseReason { Normal, AuthenticationFailed, ProtocolError, Replaced }

    public enum RealtimeCloseCode : ushort
    {
        Authentication = 4001,
        Version = 4002,
        Malformed = 4003,
        Overload = 4004,
        ExpiredSession = 4005
    }
}
