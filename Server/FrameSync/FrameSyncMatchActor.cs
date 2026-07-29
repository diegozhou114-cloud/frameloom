using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class FrameSyncMatchActor : IDisposable
    {
        private const int MaxTicksPerPump = 4;
        private const int DefaultMailboxEntries = 256;
        private readonly string _matchId;
        private readonly int _inputDeadlineTicks;
        private readonly long _tickIntervalMilliseconds;
        private readonly long _startMilliseconds;
        private readonly ISimBattleSession _session;
        private readonly IAuthorityCommandMapper _commandMapper;
        private readonly Action<int> _sealTick;
        private readonly IFrameBroadcaster _broadcaster;
        private readonly IMonotonicClock _clock;
        private readonly InputInbox _inbox;
        private readonly object _gate = new object();
        private int _nextTick;
        private bool _disposed;
        private bool _isPumping;

        private bool _isEnded;
        private bool _isFaulted;
        public bool IsEnded { get { lock (_gate) return _isEnded; } }
        public bool IsFaulted { get { lock (_gate) return _isFaulted; } }
        public int NextTick { get { lock (_gate) return _nextTick; } }

        public FrameSyncMatchActor(string matchId, int tickRate,
            int inputDeadlineTicks, ISimBattleSession session,
            IAuthorityCommandMapper commandMapper, Action<int> sealTick,
            IFrameBroadcaster broadcaster, IMonotonicClock clock,
            int maxFutureTicks = RealtimeProtocol.RollbackWindowTicks,
            int maxMailboxEntries = DefaultMailboxEntries)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            if (tickRate <= 0 || 1000 % tickRate != 0)
                throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (inputDeadlineTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(inputDeadlineTicks));
            _matchId = matchId;
            _inputDeadlineTicks = inputDeadlineTicks;
            _tickIntervalMilliseconds = 1000 / tickRate;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _commandMapper = commandMapper ??
                throw new ArgumentNullException(nameof(commandMapper));
            _sealTick = sealTick ?? throw new ArgumentNullException(nameof(sealTick));
            _broadcaster = broadcaster ?? throw new ArgumentNullException(nameof(broadcaster));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _inbox = new InputInbox(matchId, maxFutureTicks, maxMailboxEntries);
            _startMilliseconds = clock.Milliseconds;
        }

        public InputAcceptance SubmitInput(
            int authenticatedPlayerId, CommandEnvelope input)
        {
            lock (_gate)
            {
                if (_disposed || _isEnded || _isFaulted)
                    return InputAcceptance.LateRejected;
                return _inbox.Submit(authenticatedPlayerId, input, _nextTick);
            }
        }

        public int Pump()
        {
            lock (_gate) return PumpLocked();
        }

        private int PumpLocked()
        {
            if (_disposed || _isEnded || _isFaulted || _isPumping) return 0;
            _isPumping = true;
            try
            {
                int advanced = 0;
                while (advanced < MaxTicksPerPump && !_isEnded)
                {
                    long sealAt = _startMilliseconds +
                        ((long)_nextTick + _inputDeadlineTicks) *
                        _tickIntervalMilliseconds;
                    if (!_inbox.HasBoth(_nextTick) && _clock.Milliseconds < sealAt)
                        break;

                    try
                    {
                        CommandEnvelope player0 = _inbox.TakeOrNoOp(_nextTick, 0);
                        CommandEnvelope player1 = _inbox.TakeOrNoOp(_nextTick, 1);
                        CommandEnvelope[] ordered = { player0, player1 };
                        foreach (CommandEnvelope envelope in ordered)
                        {
                            if (envelope.IsNoOp) continue;
                            ISimCommand? command = _commandMapper.ToSimulation(envelope);
                            if (command == null ||
                                command.Tick != envelope.Tick ||
                                command.PlayerId != envelope.PlayerId ||
                                command.Sequence != (int)envelope.Sequence ||
                                !_session.Submit(command).Accepted)
                                throw new InvalidOperationException(
                                    "Validated authority input was rejected by SimulationCore.");
                        }

                        _sealTick(_nextTick);
                        TickResult tick = _session.Tick();
                        if (tick == null ||
                            (tick.Code != TickCode.Advanced && tick.Code != TickCode.Ended) ||
                            tick.CompletedTick != _nextTick)
                            throw new InvalidOperationException(
                                $"SimulationCore refused sealed tick {_nextTick}.");

                        AuthorityFrame frame = new AuthorityFrame
                        {
                            MatchId = _matchId,
                            Tick = tick.CompletedTick,
                            OrderedInputs = ordered,
                            AckPlayer0 = _inbox.AckForPlayer(0),
                            AckPlayer1 = _inbox.AckForPlayer(1),
                            Checksum = tick.Checksum
                        };
                        _nextTick++;
                        advanced++;
                        _isEnded = tick.Code == TickCode.Ended;
                        _broadcaster.Broadcast(frame);
                        if (_disposed || _isFaulted || _isEnded) break;
                    }
                    catch
                    {
                        _isFaulted = true;
                        throw;
                    }
                }
                return advanced;
            }
            finally
            {
                _isPumping = false;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _session.Dispose();
            }
        }
    }
}
