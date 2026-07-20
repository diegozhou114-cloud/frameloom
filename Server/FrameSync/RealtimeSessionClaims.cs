namespace ZWebGLFrameWork.FrameSync
{
    public sealed class RealtimeSessionClaims
    {
        private readonly byte[] _resumeToken;

        public Guid SessionId { get; }
        public string MatchId { get; }
        public int PlayerId { get; }
        public byte[] ResumeToken => (byte[])_resumeToken.Clone();
        public long ExpiresAtUnixSeconds { get; }

        public RealtimeSessionClaims(Guid sessionId, string matchId, int playerId,
            byte[] resumeToken, long expiresAtUnixSeconds)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            if (playerId != 0 && playerId != 1)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            if (resumeToken == null || resumeToken.Length == 0)
                throw new ArgumentException("Resume token is required.", nameof(resumeToken));
            if (expiresAtUnixSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(expiresAtUnixSeconds));
            SessionId = sessionId;
            MatchId = matchId;
            PlayerId = playerId;
            _resumeToken = (byte[])resumeToken.Clone();
            ExpiresAtUnixSeconds = expiresAtUnixSeconds;
        }

        public RealtimeSessionClaims Copy() => new RealtimeSessionClaims(
            SessionId, MatchId, PlayerId, _resumeToken, ExpiresAtUnixSeconds);
    }
}
