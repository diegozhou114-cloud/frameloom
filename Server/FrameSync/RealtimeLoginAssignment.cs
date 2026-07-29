namespace ZWebGLFrameWork.FrameSync
{
    public sealed class RealtimeLoginAssignment
    {
        public string MatchId { get; }
        public int PlayerId { get; }

        public RealtimeLoginAssignment(string matchId, int playerId)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            if (playerId != 0 && playerId != 1)
                throw new ArgumentOutOfRangeException(nameof(playerId));
            MatchId = matchId;
            PlayerId = playerId;
        }
    }
}
