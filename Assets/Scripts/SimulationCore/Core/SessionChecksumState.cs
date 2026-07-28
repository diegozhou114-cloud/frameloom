using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>所有会影响 Session 后续结果的标量状态。</summary>
    public readonly struct SessionChecksumState
    {
        private readonly int _protocolVersion;
        private readonly int _schemaVersion;
        private readonly byte[] _contentHash;
        private readonly int _seed;
        private readonly SessionLifecycle _lifecycle;
        private readonly SimCombatPhase _phase;
        private readonly int _completedTick;
        private readonly int _nextTick;
        private readonly int _deployedCount;
        private readonly bool _hasTerminalOutcome;
        private readonly int _terminalWinner;
        private readonly int _terminalStars;
        private readonly int _terminalLootGold;
        private readonly int _terminalLootElixir;
        private readonly int _terminalFinalTick;
        private readonly ulong _commandHash;
        private readonly ulong _acceptedSequenceHash;

        public SessionChecksumState(int protocolVersion, int schemaVersion, byte[] contentHash,
            int seed, SessionLifecycle lifecycle, SimCombatPhase phase,
            int completedTick, int nextTick, int deployedCount,
            SimTerminalOutcome terminalOutcome, int terminalFinalTick, ulong commandHash,
            ulong acceptedSequenceHash)
        {
            _protocolVersion = protocolVersion;
            _schemaVersion = schemaVersion;
            _contentHash = (byte[])(contentHash ?? Array.Empty<byte>()).Clone();
            _seed = seed;
            _lifecycle = lifecycle;
            _phase = phase;
            _completedTick = completedTick;
            _nextTick = nextTick;
            _deployedCount = deployedCount;
            _hasTerminalOutcome = terminalOutcome != null;
            _terminalWinner = terminalOutcome?.WinnerTeamId ?? 0;
            _terminalStars = terminalOutcome?.Stars ?? 0;
            _terminalLootGold = terminalOutcome?.LootGold ?? 0;
            _terminalLootElixir = terminalOutcome?.LootElixir ?? 0;
            _terminalFinalTick = terminalFinalTick;
            _commandHash = commandHash;
            _acceptedSequenceHash = acceptedSequenceHash;
        }

        public ulong HashInto(ulong hash)
        {
            var state = new DeterministicHash64(hash);
            state.AddInt32(_protocolVersion);
            state.AddInt32(_schemaVersion);
            state.AddBytes(_contentHash);
            state.AddInt32(_seed);
            state.AddInt32((int)_lifecycle);
            state.AddInt32((int)_phase);
            state.AddInt32(_completedTick);
            state.AddInt32(_nextTick);
            state.AddInt32(_deployedCount);
            state.AddBool(_hasTerminalOutcome);
            if (_hasTerminalOutcome)
            {
                state.AddInt32(_terminalWinner);
                state.AddInt32(_terminalStars);
                state.AddInt32(_terminalLootGold);
                state.AddInt32(_terminalLootElixir);
                state.AddInt32(_terminalFinalTick);
            }
            state.AddUInt64(_commandHash);
            state.AddUInt64(_acceptedSequenceHash);
            return state.Value;
        }
    }
}
