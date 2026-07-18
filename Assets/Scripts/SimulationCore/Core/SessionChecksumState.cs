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
        private readonly ulong _commandHash;

        public SessionChecksumState(int protocolVersion, int schemaVersion, byte[] contentHash,
            int seed, SessionLifecycle lifecycle, SimCombatPhase phase,
            int completedTick, int nextTick, ulong commandHash)
        {
            _protocolVersion = protocolVersion;
            _schemaVersion = schemaVersion;
            _contentHash = (byte[])(contentHash ?? Array.Empty<byte>()).Clone();
            _seed = seed;
            _lifecycle = lifecycle;
            _phase = phase;
            _completedTick = completedTick;
            _nextTick = nextTick;
            _commandHash = commandHash;
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
            state.AddUInt64(_commandHash);
            return state.Value;
        }
    }
}
