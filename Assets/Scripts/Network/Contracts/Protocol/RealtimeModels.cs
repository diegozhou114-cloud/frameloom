using System;

namespace ZWebGLFrameWork.Network.Contracts.Protocol
{
    public sealed class RealtimeCredentials
    {
        public Guid SessionId { get; set; }
        public byte[] OpaqueTicket { get; set; } = Array.Empty<byte>();
        public byte[] ResumeToken { get; set; } = Array.Empty<byte>();
        public byte[] MacKey { get; set; } = Array.Empty<byte>();
        public long ExpiresAtUnixSeconds { get; set; }
    }

    public sealed class AuthHello
    {
        public ushort Version { get; set; }
        public Guid SessionId { get; set; }
        public byte[] OpaqueTicket { get; set; } = Array.Empty<byte>();
    }

    public sealed class ResumeHello
    {
        public ushort Version { get; set; }
        public Guid SessionId { get; set; }
        public byte[] ResumeToken { get; set; } = Array.Empty<byte>();
        public uint LastAcceptedSequence { get; set; }
        public int LastAuthorityTick { get; set; }
        public long TimelineRevision { get; set; }
    }

    public sealed class AuthAccepted
    {
        public Guid SessionId { get; set; }
        public int LastAuthorityTick { get; set; }
        public long TimelineRevision { get; set; }
    }

    public readonly struct RealtimeEndpoint
    {
        public RealtimeTransport Transport { get; }
        public Uri Uri { get; }

        public RealtimeEndpoint(RealtimeTransport transport, Uri uri)
        {
            Transport = transport;
            Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }
    }

    public sealed class CommandEnvelope
    {
        public ushort Version { get; set; }
        public string MatchId { get; set; }
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public uint Sequence { get; set; }
        public uint MessageId { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public bool IsNoOp => MessageId == 0;

        public static CommandEnvelope NoOp(int tick, int playerId, uint sequence) =>
            new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                Tick = tick,
                PlayerId = playerId,
                Sequence = sequence,
                MessageId = 0,
                Payload = Array.Empty<byte>()
            };

        public static CommandEnvelope Create(
            int tick, int playerId, uint sequence, uint messageId, byte[] payload) =>
            new CommandEnvelope
            {
                Version = RealtimeProtocol.Version,
                Tick = tick,
                PlayerId = playerId,
                Sequence = sequence,
                MessageId = messageId,
                Payload = payload ?? Array.Empty<byte>()
            };
    }

    public sealed class ResyncSnapshotEnvelope
    {
        public string MatchId { get; set; }
        public string SessionId { get; set; }
        public int SchemaVersion { get; set; }
        public byte[] ContentHash { get; set; } = Array.Empty<byte>();
        public int CompletedTick { get; set; }
        public int NextTick { get; set; }
        public long TimelineRevision { get; set; }
        public int UncompressedLength { get; set; }
        public ulong StateChecksum { get; set; }
        public byte[] Payload { get; set; } = Array.Empty<byte>();
        public byte[] PayloadSha256 { get; set; } = Array.Empty<byte>();
    }

    public sealed class AuthorityFrame
    {
        public string MatchId { get; set; }
        public int Tick { get; set; }
        public CommandEnvelope[] OrderedInputs { get; set; } = Array.Empty<CommandEnvelope>();
        public uint AckPlayer0 { get; set; }
        public uint AckPlayer1 { get; set; }
        public ulong Checksum { get; set; }
        public ResyncSnapshotEnvelope ResyncSnapshot { get; set; }

        public uint AckForPlayer(int playerId)
        {
            if (playerId == 0) return AckPlayer0;
            if (playerId == 1) return AckPlayer1;
            throw new ArgumentOutOfRangeException(nameof(playerId));
        }

        public static AuthorityFrame Create(
            int tick, CommandEnvelope[] orderedInputs,
            uint ackPlayer0, uint ackPlayer1, ulong checksum) =>
            new AuthorityFrame
            {
                Tick = tick,
                OrderedInputs = orderedInputs ?? Array.Empty<CommandEnvelope>(),
                AckPlayer0 = ackPlayer0,
                AckPlayer1 = ackPlayer1,
                Checksum = checksum
            };
    }

    public sealed class DecodeResult
    {
        public ProtocolError Error { get; set; }
        public RealtimeMessageKind Kind { get; set; }
        public AuthHello AuthHello { get; set; }
        public ResumeHello ResumeHello { get; set; }
        public AuthAccepted AuthAccepted { get; set; }
        public CommandEnvelope Command { get; set; }
        public AuthorityFrame Authority { get; set; }
        public ResyncSnapshotEnvelope ResyncSnapshot { get; set; }
    }
}
