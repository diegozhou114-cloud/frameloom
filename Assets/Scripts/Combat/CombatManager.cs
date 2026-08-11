using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public class CombatManager : SingletonDontDestroy<CombatManager>
    {
        public const string EventPhaseChanged = "CombatPhaseChanged";

        private const float TickInterval = 1f / 20f;
        private ISimBattleSessionFactory _sessionFactory;
        private ISimBattleSession _session;
        private IBufferedSimBattleSession _bufferedSession;
        private SimTickOutputBuffer _tickOutput;
        private BattleInitFactory _battleInitFactory;
        private SessionPresentationBridge _presentation;
        private UniTaskCompletionSource<SimBattleResult> _completion;
        private byte[] _canonicalManifestBytes;
        private ISimLogger _logger;
        private float _accumulator;
        private int _nextSequence = 1;
        private int _nextTick;
        private int _attackerPlayerId;
        private int _attackerAliveCount;
        private int _defenderAliveCount;
        private SimModeProfile _modeProfile;
        private int[] _waveTicks = Array.Empty<int>();

        public SimCombatPhase CurrentPhase { get; private set; } = SimCombatPhase.Idle;
        public SimBattleResult LastResult { get; private set; }
        public int CompletedTick { get; private set; } = -1;
        public int ActiveViewCount => _presentation?.ViewCount ?? 0;
        internal bool HasActiveSession => _session != null;
        public int MaxDeployCount => _modeProfile?.MaxDeployCount ?? 0;
        public int DeployDurationTicks => _modeProfile?.DeployDurationTicks ?? 0;
        public int RemainingBattleTicks => _modeProfile == null
            ? 0 : Math.Max(0, _modeProfile.TimeoutTicks - Math.Max(0, CompletedTick));
        public int AttackerAliveCount => CountAlive(SimTeam.Attacker);
        public int DefenderAliveCount => CountAlive(SimTeam.Defender);
        public int CurrentWave
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _waveTicks.Length; index++)
                    if (_waveTicks[index] <= CompletedTick) count++;
                return count;
            }
        }

        protected override void OnSingletonAwake()
        {
            _sessionFactory = new SimBattleSessionFactory();
            _canonicalManifestBytes = FrameworkBattleContent.CanonicalBytes();
            _battleInitFactory = new BattleInitFactory(_canonicalManifestBytes);
            var viewConfig = Resources.Load<ViewConfig>("Config/ViewConfig");
            _presentation = new SessionPresentationBridge(
                new UnityEntityViewFactory(viewConfig));
        }

        public async UniTask<SimBattleResult> StartBattleAsync(BattleParams parameters,
            CancellationToken cancellationToken)
        {
            StopBattle();
            cancellationToken.ThrowIfCancellationRequested();
            BattleInit init = _battleInitFactory.Create(parameters);
            ISimBattleSession candidate = null;
            StartResult started;
            SimBattleState initialState;
            SimModeProfile modeProfile;
            IBufferedSimBattleSession buffered = null;
            SimTickOutputBuffer tickOutput = null;
            try
            {
                candidate = _sessionFactory.Create(_canonicalManifestBytes, _logger);
                started = candidate.Start(init);
                if (!started.Accepted)
                    throw new InvalidOperationException(
                        "Session start rejected: " + started.Code);
                initialState = candidate.State;
                modeProfile = FrameworkBattleContent.GetModeProfile(parameters.ModeId);
                buffered = candidate as IBufferedSimBattleSession;
                if (buffered != null)
                    tickOutput = new SimTickOutputBuffer(
                        SimTickOutputFields.Presentation,
                        started.InitialFrame.Entities.Count);
            }
            catch
            {
                tickOutput?.Dispose();
                candidate?.Dispose();
                throw;
            }

            var completion = new UniTaskCompletionSource<SimBattleResult>();
            _session = candidate;
            _bufferedSession = buffered;
            _tickOutput = tickOutput;
            _completion = completion;
            _attackerPlayerId = parameters.AttackerPlayerId;
            _modeProfile = modeProfile;
            _waveTicks = modeProfile.WaveTicks;
            _nextSequence = 1;
            _nextTick = initialState.NextTick;
            _accumulator = 0f;
            LastResult = null;
            CompletedTick = -1;
            CountAlive(started.InitialFrame.Entities,
                out _attackerAliveCount, out _defenderAliveCount);
            using (cancellationToken.Register(() => CancelBattle(completion, cancellationToken)))
            {
                if (!OwnsBattle(candidate, completion))
                    return await completion.Task;
                try
                {
                    SetPhase(initialState.Phase);
                    if (!OwnsBattle(candidate, completion))
                        return await completion.Task;
                    _presentation.Apply(started.InitialFrame);
                    if (!OwnsBattle(candidate, completion))
                        return await completion.Task;
                }
                catch (Exception exception)
                {
                    if (!OwnsBattle(candidate, completion))
                        return await completion.Task;
                    completion.TrySetException(exception);
                    Cleanup(completion, false);
                    return await completion.Task;
                }

                return await completion.Task;
            }
        }

        public CommandSubmitResult SubmitDeployUnit(Vector2Int gridPos, int archetypeId)
        {
            if (_session == null) return WrongPhase();
            return _session.Submit(new DeployCmd
            {
                Tick = NextCommandTick(),
                PlayerId = _attackerPlayerId,
                Sequence = _nextSequence++,
                UnitTypeId = archetypeId,
                GridX = gridPos.x,
                GridY = gridPos.y,
            });
        }

        public CommandSubmitResult SubmitMove(int entityId, Vector2Int target)
        {
            if (_session == null) return WrongPhase();
            return _session.Submit(new MoveCmd
            {
                Tick = NextCommandTick(),
                PlayerId = _attackerPlayerId,
                Sequence = _nextSequence++,
                EntityId = entityId,
                TargetX = target.x,
                TargetY = target.y,
            });
        }

        public CommandSubmitResult SubmitSkill(int casterId, int skillId, Vector2Int target)
        {
            if (_session == null) return WrongPhase();
            return _session.Submit(new SkillCmd
            {
                Tick = NextCommandTick(),
                PlayerId = _attackerPlayerId,
                Sequence = _nextSequence++,
                CasterId = casterId,
                SkillId = skillId,
                TargetX = target.x,
                TargetY = target.y,
            });
        }

        public CommandSubmitResult EndDeployPhase()
        {
            if (_session == null) return WrongPhase();
            return _session.Submit(new EndDeploymentCmd
            {
                Tick = NextCommandTick(),
                PlayerId = _attackerPlayerId,
                Sequence = _nextSequence++,
            });
        }

        public void StopBattle()
        {
            UniTaskCompletionSource<SimBattleResult> completion = _completion;
            completion?.TrySetCanceled(CancellationToken.None);
            Cleanup(completion, CurrentPhase == SimCombatPhase.Ended && LastResult != null);
        }

        internal void ConfigureForTests(ISimBattleSessionFactory sessionFactory,
            byte[] canonicalManifestBytes, IEntityViewFactory viewFactory, ISimLogger logger = null)
        {
            if (sessionFactory == null) throw new ArgumentNullException(nameof(sessionFactory));
            if (canonicalManifestBytes == null)
                throw new ArgumentNullException(nameof(canonicalManifestBytes));
            if (viewFactory == null) throw new ArgumentNullException(nameof(viewFactory));
            if (_session != null)
                throw new InvalidOperationException("Cannot reconfigure an active battle.");

            _presentation?.Cleanup();
            _sessionFactory = sessionFactory;
            _canonicalManifestBytes = (byte[])canonicalManifestBytes.Clone();
            _battleInitFactory = new BattleInitFactory(_canonicalManifestBytes);
            _presentation = new SessionPresentationBridge(viewFactory);
            _logger = logger;
        }

        private void Update()
        {
            if (_session == null) return;

            _accumulator += Time.deltaTime;
            ISimBattleSession activeSession = _session;
            UniTaskCompletionSource<SimBattleResult> activeCompletion = _completion;
            try
            {
                while (_session != null && _accumulator >= TickInterval)
                {
                    _accumulator -= TickInterval;
                    activeSession = _session;
                    activeCompletion = _completion;
                    SimBattleResult result;
                    if (_bufferedSession != null)
                    {
                        SimTickOutputBuffer output = _tickOutput;
                        _bufferedSession.TickInto(output);
                        if (!OwnsBattle(activeSession, activeCompletion)) return;
                        int completedTick = output.Tick;
                        SimCombatPhase phase = output.Phase;
                        long revision = output.Revision;
                        result = output.Result;
                        CountAlive(output.Presentation.Entities,
                            out int attackerAlive, out int defenderAlive);
                        _nextTick = completedTick + 1;
                        _attackerAliveCount = attackerAlive;
                        _defenderAliveCount = defenderAlive;
                        _presentation.Apply(output.Presentation, revision);
                        if (!OwnsBattle(activeSession, activeCompletion)) return;
                        CompletedTick = completedTick;
                        SetPhase(phase);
                    }
                    else
                    {
                        TickResult tick = activeSession.Tick();
                        if (!OwnsBattle(activeSession, activeCompletion)) return;
                        _presentation.Apply(tick.Presentation);
                        if (!OwnsBattle(activeSession, activeCompletion)) return;
                        CompletedTick = tick.CompletedTick;
                        SetPhase(activeSession.State.Phase);
                        result = tick.Result;
                    }
                    if (!OwnsBattle(activeSession, activeCompletion)) return;
                    if (result == null) continue;

                    CompleteBattle(activeSession, result);
                    return;
                }

                if (_session != null)
                {
                    activeSession = _session;
                    activeCompletion = _completion;
                    _presentation.Sample(_accumulator / TickInterval);
                }
            }
            catch (Exception exception)
            {
                if (!OwnsBattle(activeSession, activeCompletion)) return;
                activeCompletion?.TrySetException(exception);
                LogException(exception);
                Cleanup(activeCompletion, false);
            }
        }

        private void CompleteBattle(ISimBattleSession activeSession, SimBattleResult result)
        {
            if (!ReferenceEquals(_session, activeSession)) return;
            UniTaskCompletionSource<SimBattleResult> completion = _completion;
            if (completion == null || !completion.TrySetResult(result))
            {
                Cleanup(completion, false);
                return;
            }
            if (!ReferenceEquals(_session, activeSession) ||
                !ReferenceEquals(_completion, completion))
                return;

            LastResult = result;
            Cleanup(completion, true);
        }

        private void CancelBattle(UniTaskCompletionSource<SimBattleResult> completion,
            CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(_completion, completion)) return;
            completion.TrySetCanceled(cancellationToken);
            Cleanup(completion, false);
        }

        private bool OwnsBattle(ISimBattleSession session,
            UniTaskCompletionSource<SimBattleResult> completion) =>
            ReferenceEquals(_session, session) && ReferenceEquals(_completion, completion);

        private void Cleanup(UniTaskCompletionSource<SimBattleResult> expectedCompletion,
            bool preserveTerminalPhase)
        {
            if (expectedCompletion != null && !ReferenceEquals(_completion, expectedCompletion))
                return;

            ISimBattleSession session = _session;
            SimTickOutputBuffer tickOutput = _tickOutput;
            _session = null;
            _bufferedSession = null;
            _tickOutput = null;
            _completion = null;
            _modeProfile = null;
            _waveTicks = Array.Empty<int>();
            _attackerPlayerId = 0;
            _attackerAliveCount = 0;
            _defenderAliveCount = 0;
            _accumulator = 0f;
            _nextSequence = 1;
            _nextTick = 0;
            tickOutput?.Dispose();
            _presentation?.Cleanup();
            session?.Dispose();
            if (!preserveTerminalPhase)
                SetPhase(SimCombatPhase.Idle);
        }

        private int CountAlive(int teamId)
        {
            if (_session == null) return 0;
            if (_bufferedSession != null)
                return teamId == SimTeam.Attacker
                    ? _attackerAliveCount : _defenderAliveCount;
            PresentationFrame snapshot = _session.State.Presentation;
            if (snapshot == null) return 0;
            int count = 0;
            for (int index = 0; index < snapshot.Entities.Count; index++)
            {
                EntityPresentationState state = snapshot.Entities[index];
                if (state.TeamId == teamId && state.Health > 0) count++;
            }
            return count;
        }

        private int NextCommandTick() => _bufferedSession == null
            ? _session.State.NextTick : _nextTick;

        private static void CountAlive(IReadOnlyList<EntityPresentationState> states,
            out int attackerAlive, out int defenderAlive)
        {
            attackerAlive = 0;
            defenderAlive = 0;
            for (int index = 0; index < states.Count; index++)
            {
                EntityPresentationState state = states[index];
                if (state.Health <= 0) continue;
                if (state.TeamId == SimTeam.Attacker) attackerAlive++;
                else if (state.TeamId == SimTeam.Defender) defenderAlive++;
            }
        }

        private void SetPhase(SimCombatPhase phase)
        {
            if (CurrentPhase == phase) return;
            CurrentPhase = phase;
            EventBus.Publish(EventPhaseChanged, phase);
        }

        private void LogException(Exception exception)
        {
            if (_logger != null)
                _logger.Error("[CombatManager] Session tick failed: " + exception);
            else
                Debug.LogException(exception);
        }

        private static CommandSubmitResult WrongPhase() =>
            new CommandSubmitResult(CommandDisposition.WrongPhase);

        protected override void OnDestroy()
        {
            StopBattle();
            base.OnDestroy();
        }
    }
}
