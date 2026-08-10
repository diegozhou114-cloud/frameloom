namespace ZWebGLFrameWork.SimulationCore
{
    public sealed partial class SimBattleSession
    {
        private SessionLifecycle _lifecycle;
        private SimCombatPhase _phase;
        private int _completedTick;
        private int _nextTick;
        private long _timelineRevision;
        private ulong _checksum;
        private SimBattleResult _result;
        private bool _stateDirty;

        private TickResult AdvanceTick(SimTickOutputBuffer destination)
        {
            int completedTick = _nextTick;
            int nextTick = completedTick + 1;
            long revision = _timelineRevision;
            ClearCommandWorkingSets();
            try
            {
                _commandBuffer.DrainInto(completedTick, _executedCommands);
                foreach (ISimCommand command in _executedCommands)
                {
                    CommandExecutionResult outcome = ExecuteCommand(command);
                    _commandOutcomes.Add(outcome);
                    if (outcome.Disposition == CommandDisposition.Accepted)
                        _acceptedCommands.Add(command);
                }
                SimCombatPhase phase = _phase;
                SessionLifecycle lifecycle = _lifecycle;
                if (phase == SimCombatPhase.Deploying && _modeProfile.ManualDeployment &&
                    nextTick >= _modeProfile.DeployDurationTicks)
                    phase = SimCombatPhase.Battling;
                bool systemsUpdated = false;
                if (phase == SimCombatPhase.Battling)
                {
                    _systems.UpdateAll();
                    systemsUpdated = true;
                    SimTerminalOutcome terminalOutcome = _modeRules.Evaluate(
                        completedTick, _deathSystem.AttackerAlive,
                        _deathSystem.DefenderAlive, _deathSystem.DestructionBasisPoints,
                        _modeProfile.CountWavesAfter(completedTick),
                        nextTick >= _modeProfile.TimeoutTicks, true);
                    if (terminalOutcome != null)
                    {
                        phase = SimCombatPhase.Ended;
                        lifecycle = SessionLifecycle.Ended;
                        _terminalOutcome = terminalOutcome;
                        _terminalFinalTick = completedTick;
                    }
                }
                SimPresentationOutputBuffer presentation = destination != null &&
                    destination.Includes(SimTickOutputFields.Presentation)
                    ? destination.Presentation : null;
                _presentationFrames.Advance(_world, presentation);
                if (systemsUpdated)
                    BuildTickEvents(_presentationFrames.Destroyed, _tickEvents);
                ulong checksum = ComputeChecksum(lifecycle, phase, completedTick, nextTick);
                SimBattleResult result = _terminalOutcome == null ? null :
                    SimBattleResult.FromOutcome(
                        _init.MatchId, _terminalOutcome, _terminalFinalTick, checksum);
                TickCode code = lifecycle == SessionLifecycle.Ended
                    ? TickCode.Ended : TickCode.Advanced;
                _sealedTick = -1;
                if (destination != null)
                {
                    SetAuthoritativeState(lifecycle, phase, completedTick,
                        nextTick, revision, checksum, result);
                    destination.CopyCollections(
                        _acceptedCommands, _commandOutcomes, _tickEvents);
                    destination.Publish(code, lifecycle, phase, completedTick,
                        revision, checksum, result);
                    return null;
                }
                PresentationFrame full = _presentationFrames.BuildOwnedCurrent(
                    completedTick, nextTick, revision);
                _currentFrame = _presentationFrames.BuildOwnedDelta(
                    completedTick, nextTick, revision);
                SetOwnedState(lifecycle, phase, completedTick,
                    nextTick, revision, checksum, full, result);
                return TickResult.TakeOwnership(code, completedTick, nextTick, revision,
                    _acceptedCommands.ToArray(), _commandOutcomes.ToArray(),
                    _tickEvents.ToArray(), _currentFrame, checksum, result);
            }
            finally
            {
                ClearCommandWorkingSets();
                _presentationFrames.ClearAdvanceWorkingSets();
            }
        }

        private SimBattleState MaterializeState()
        {
            if (!_stateDirty) return _state;
            PresentationFrame full = _presentationFrames.BuildOwnedCurrent(
                _completedTick, _nextTick, _timelineRevision);
            _currentFrame = full;
            _state = new SimBattleState(_lifecycle, _phase, _completedTick,
                _nextTick, _timelineRevision, _checksum, full, _result);
            _stateDirty = false;
            return _state;
        }

        private void SetAuthoritativeState(SessionLifecycle lifecycle, SimCombatPhase phase,
            int completedTick, int nextTick, long revision, ulong checksum,
            SimBattleResult result)
        {
            _lifecycle = lifecycle;
            _phase = phase;
            _completedTick = completedTick;
            _nextTick = nextTick;
            _timelineRevision = revision;
            _checksum = checksum;
            _result = result;
            _stateDirty = true;
        }

        private void SetOwnedState(SessionLifecycle lifecycle, SimCombatPhase phase,
            int completedTick, int nextTick, long revision, ulong checksum,
            PresentationFrame presentation, SimBattleResult result)
        {
            SetAuthoritativeState(lifecycle, phase, completedTick,
                nextTick, revision, checksum, result);
            _state = new SimBattleState(lifecycle, phase, completedTick,
                nextTick, revision, checksum, presentation, result);
            _stateDirty = false;
        }
    }
}
