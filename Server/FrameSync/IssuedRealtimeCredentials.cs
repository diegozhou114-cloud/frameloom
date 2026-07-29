namespace ZWebGLFrameWork.FrameSync
{
    public sealed class IssuedRealtimeCredentials
    {
        private readonly byte[] _opaqueTicket;
        private readonly byte[] _resumeToken;
        private readonly byte[] _macKey;

        public Guid SessionId { get; }
        public int PlayerId { get; }
        public byte[] OpaqueTicket => (byte[])_opaqueTicket.Clone();
        public byte[] ResumeToken => (byte[])_resumeToken.Clone();
        public byte[] MacKey => (byte[])_macKey.Clone();
        public long ExpiresAtUnixSeconds { get; }

        public IssuedRealtimeCredentials(Guid sessionId, int playerId,
            byte[] opaqueTicket, byte[] resumeToken, byte[] macKey,
            long expiresAtUnixSeconds)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (playerId != 0 && playerId != 1)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (opaqueTicket == null || opaqueTicket.Length != 32)
                throw new ArgumentException("Ticket must be 32 bytes.", nameof(opaqueTicket));
            if (resumeToken == null || resumeToken.Length != 32)
                throw new ArgumentException("Resume token must be 32 bytes.", nameof(resumeToken));
            if (macKey == null || macKey.Length != 32)
                throw new ArgumentException("MAC key must be 32 bytes.", nameof(macKey));
            if (expiresAtUnixSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(expiresAtUnixSeconds));
            SessionId = sessionId;
            PlayerId = playerId;
            _opaqueTicket = (byte[])opaqueTicket.Clone();
            _resumeToken = (byte[])resumeToken.Clone();
            _macKey = (byte[])macKey.Clone();
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }
    }
}
