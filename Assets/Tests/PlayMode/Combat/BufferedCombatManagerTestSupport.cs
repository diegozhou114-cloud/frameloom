using System;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    internal sealed class CombatSessionSpyFactory : ISimBattleSessionFactory
    {
        private readonly bool _buffered;
        private readonly SimCombatPhase? _initialPhase;
        public CombatSessionSpy LastSession { get; private set; }

        public CombatSessionSpyFactory(bool buffered, SimCombatPhase? initialPhase = null)
        {
            _buffered = buffered;
            _initialPhase = initialPhase;
        }

        public ISimBattleSession Create(byte[] canonicalManifestBytes,
            ISimLogger logger = null)
        {
            ISimBattleSession inner = new SimBattleSessionFactory().Create(
                canonicalManifestBytes, logger);
            LastSession = _buffered
                ? new BufferedCombatSessionSpy(inner, _initialPhase)
                : new CombatSessionSpy(inner);
            return LastSession;
        }
    }

    internal class CombatSessionSpy : ISimBattleSession
    {
        protected readonly ISimBattleSession Inner;
        private bool _started;
        private bool _dirty;

        public CombatSessionSpy(ISimBattleSession inner) =>
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));

        public int StateCallsAfterStart { get; private set; }
        public int TickCalls { get; protected set; }
        public int DisposeCalls { get; private set; }
        public int LastCompletedTick { get; protected set; } = -1;

        public virtual SimBattleState State
        {
            get
            {
                if (_started) StateCallsAfterStart++;
                if (_dirty) throw new InvalidOperationException(
                    "Combat read owning State after a buffered tick.");
                return Inner.State;
            }
        }

        public StartResult Start(BattleInit init)
        {
            StartResult result = Inner.Start(init);
            _started = result.Accepted;
            return result;
        }

        public CommandSubmitResult Submit(ISimCommand command) => Inner.Submit(command);

        public virtual TickResult Tick()
        {
            TickCalls++;
            TickResult result = Inner.Tick();
            LastCompletedTick = result.CompletedTick;
            return result;
        }

        public SimSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();
        public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) =>
            Inner.RestoreSnapshot(snapshot);

        public void Dispose()
        {
            DisposeCalls++;
            Inner.Dispose();
        }

        protected void MarkDirty() => _dirty = true;
    }

    internal sealed class BufferedCombatSessionSpy : CombatSessionSpy,
        IBufferedSimBattleSession
    {
        private readonly SimCombatPhase? _initialPhase;
        private SimTickOutputBuffer _firstOutput;

        public BufferedCombatSessionSpy(ISimBattleSession inner,
            SimCombatPhase? initialPhase = null) : base(inner) =>
            _initialPhase = initialPhase;

        public int TickIntoCalls { get; private set; }
        public int StateCallsBeforeFirstTickInto { get; private set; } = -1;
        public bool ReusedSingleOutput { get; private set; } = true;
        public SimTickOutputFields LastFields { get; private set; }

        public override SimBattleState State
        {
            get
            {
                SimBattleState state = base.State;
                return !_initialPhase.HasValue ? state : new SimBattleState(
                    state.Lifecycle, _initialPhase.Value, state.CompletedTick,
                    state.NextTick, state.TimelineRevision, state.Checksum,
                    state.Presentation, state.Result);
            }
        }

        public override TickResult Tick()
        {
            TickCalls++;
            throw new InvalidOperationException("Combat used owning Tick().");
        }

        public void TickInto(SimTickOutputBuffer destination)
        {
            if (TickIntoCalls == 0)
                StateCallsBeforeFirstTickInto = StateCallsAfterStart;
            TickIntoCalls++;
            LastFields = destination.Fields;
            if (_firstOutput == null) _firstOutput = destination;
            else if (!ReferenceEquals(_firstOutput, destination)) ReusedSingleOutput = false;
            ((IBufferedSimBattleSession)Inner).TickInto(destination);
            LastCompletedTick = destination.Tick;
            MarkDirty();
        }
    }
}
