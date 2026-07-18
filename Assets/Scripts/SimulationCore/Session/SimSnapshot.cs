namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimSnapshot
    {
        private readonly byte[] _contentHash;
        private readonly byte[] _payload;
        private readonly byte[] _payloadSha256;

        public string MatchId { get; }
        public string SessionId { get; }
        public int SchemaVersion { get; }
        public byte[] ContentHash => (byte[])_contentHash.Clone();
        public int CompletedTick { get; }
        public int NextTick { get; }
        public long TimelineRevision { get; }
        public int UncompressedLength { get; }
        public ulong StateChecksum { get; }
        public byte[] Payload => (byte[])_payload.Clone();
        public byte[] PayloadSha256 => (byte[])_payloadSha256.Clone();

        public SimSnapshot(string matchId, string sessionId, int schemaVersion, byte[] contentHash,
            int completedTick, int nextTick, long timelineRevision, int uncompressedLength,
            ulong stateChecksum, byte[] payload, byte[] payloadSha256)
        {
            MatchId = matchId ?? throw new System.ArgumentNullException(nameof(matchId));
            SessionId = sessionId ?? throw new System.ArgumentNullException(nameof(sessionId));
            SchemaVersion = schemaVersion;
            _contentHash = (byte[])(contentHash ?? throw new System.ArgumentNullException(nameof(contentHash))).Clone();
            CompletedTick = completedTick;
            NextTick = nextTick;
            TimelineRevision = timelineRevision;
            UncompressedLength = uncompressedLength;
            StateChecksum = stateChecksum;
            _payload = (byte[])(payload ?? throw new System.ArgumentNullException(nameof(payload))).Clone();
            _payloadSha256 = (byte[])(payloadSha256 ?? throw new System.ArgumentNullException(nameof(payloadSha256))).Clone();
        }
    }
}
