using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public enum InputAcceptance
    {
        Accepted = 0,
        WrongPlayer,
        WrongMatch,
        UnsupportedVersion,
        InvalidInput,
        LateRejected,
        TooFarAhead,
        DuplicateOrReorderedSequence,
        DuplicateInput,
        MailboxOverloaded
    }

    public sealed class InputInbox
    {
        private readonly string _matchId;
        private readonly int _maxFutureTicks;
        private readonly int _maxEntries;
        private readonly Dictionary<(int Tick, int PlayerId), CommandEnvelope> _byTickPlayer = new();
        private readonly uint[] _acceptedAcks = new uint[2];
        private readonly bool[] _hasAcceptedSequence = new bool[2];

        public uint AckForPlayer(int playerId) =>
            IsPlayer(playerId) ? _acceptedAcks[playerId] :
            throw new ArgumentOutOfRangeException(nameof(playerId));

        public InputInbox(string matchId, int maxFutureTicks, int maxEntries)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            if (maxFutureTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(maxFutureTicks));
            if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            _matchId = matchId;
            _maxFutureTicks = maxFutureTicks;
            _maxEntries = maxEntries;
        }

        public InputAcceptance Submit(int authenticatedPlayerId,
            CommandEnvelope? input, int nextUnsealedTick)
        {
            if (!IsPlayer(authenticatedPlayerId) || input == null ||
                input.PlayerId != authenticatedPlayerId)
                return InputAcceptance.WrongPlayer;
            if (input.Version != RealtimeProtocol.Version)
                return InputAcceptance.UnsupportedVersion;
            if (!string.Equals(input.MatchId, _matchId, StringComparison.Ordinal))
                return InputAcceptance.WrongMatch;
            if (input.IsNoOp || input.Payload == null ||
                input.Payload.Length > RealtimeProtocol.MaxMessageBytes)
                return InputAcceptance.InvalidInput;
            if (input.Tick < nextUnsealedTick) return InputAcceptance.LateRejected;
            if ((long)input.Tick > (long)nextUnsealedTick + _maxFutureTicks)
                return InputAcceptance.TooFarAhead;
            if (_hasAcceptedSequence[authenticatedPlayerId] &&
                input.Sequence <= _acceptedAcks[authenticatedPlayerId])
                return InputAcceptance.DuplicateOrReorderedSequence;
            var key = (input.Tick, authenticatedPlayerId);
            if (_byTickPlayer.ContainsKey(key)) return InputAcceptance.DuplicateInput;
            if (_byTickPlayer.Count >= _maxEntries)
                return InputAcceptance.MailboxOverloaded;

            _byTickPlayer.Add(key, Copy(input));
            _acceptedAcks[authenticatedPlayerId] = input.Sequence;
            _hasAcceptedSequence[authenticatedPlayerId] = true;
            return InputAcceptance.Accepted;
        }

        public bool HasBoth(int tick) =>
            _byTickPlayer.ContainsKey((tick, 0)) &&
            _byTickPlayer.ContainsKey((tick, 1));

        public CommandEnvelope TakeOrNoOp(int tick, int playerId)
        {
            if (!IsPlayer(playerId)) throw new ArgumentOutOfRangeException(nameof(playerId));
            if (_byTickPlayer.Remove((tick, playerId), out CommandEnvelope? input))
                return input;
            CommandEnvelope noOp = CommandEnvelope.NoOp(
                tick, playerId, _acceptedAcks[playerId]);
            noOp.MatchId = _matchId;
            return noOp;
        }

        private static bool IsPlayer(int playerId) => playerId == 0 || playerId == 1;

        private static CommandEnvelope Copy(CommandEnvelope input) =>
            new CommandEnvelope
            {
                Version = input.Version,
                MatchId = input.MatchId,
                Tick = input.Tick,
                PlayerId = input.PlayerId,
                Sequence = input.Sequence,
                MessageId = input.MessageId,
                Payload = (byte[])input.Payload.Clone()
            };
    }
}
