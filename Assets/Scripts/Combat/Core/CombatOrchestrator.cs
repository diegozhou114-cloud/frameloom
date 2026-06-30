using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗编排器 — 管理阶段流转。所有规则参数从 CombatMode 读取。
    /// 通过 Update-driven Tick 驱动帧循环。
    /// </summary>
    public class CombatOrchestrator
    {
        public const string EventPhaseChanged = "CombatPhaseChanged";
        public const string EventCombatEnded = "CombatEnded";

        public CombatPhase CurrentPhase { get; private set; } = CombatPhase.Idle;

        private float _deployElapsed;
        private float _deployTimeLimit;
        private UniTaskCompletionSource<bool> _deployTcs;

        private SystemGroup _battleSystems;
        private UnitAI _battleAI;
        private DeathSystem _battleDeathSys;
        private CombatMode _battleMode;
        private UnitSpawner _battleSpawner;
        private int _battleWave;
        private float _battleWaveTimer;
        private float _battleElapsed;
        private UniTaskCompletionSource<bool> _battleTcs;

        private float _endPhaseTimer;
        private UniTaskCompletionSource<bool> _endTcs;

        public UniTask RunDeployPhaseAsync(float timeLimit)
        {
            _deployTcs = new UniTaskCompletionSource<bool>();
            if (timeLimit <= 0) { _deployTcs.TrySetResult(true); return _deployTcs.Task; }
            _deployElapsed = 0f; _deployTimeLimit = timeLimit;
            SetPhase(CombatPhase.Deploying);
            return _deployTcs.Task;
        }

        public UniTask RunBattlePhaseAsync(
            SystemGroup systems, UnitAI ai, DeathSystem deathSys,
            CombatMode mode, UnitSpawner spawner)
        {
            _battleTcs = new UniTaskCompletionSource<bool>();
            _battleSystems = systems; _battleAI = ai;
            _battleDeathSys = deathSys; _battleMode = mode;
            _battleSpawner = spawner;
            _battleWave = 0; _battleWaveTimer = mode.FirstWaveDelay;
            _battleElapsed = 0f;
            SetPhase(CombatPhase.Battling);
            return _battleTcs.Task;
        }

        public UniTask RunEndPhaseAsync(CombatEndEvent result)
        {
            _endTcs = new UniTaskCompletionSource<bool>();
            _endPhaseTimer = 2f;
            SetPhase(CombatPhase.Ended);
            return _endTcs.Task;
        }

        /// <summary>每帧由 CombatManager.Update 调用，驱动当前阶段</summary>
        public void Tick(float deltaTime)
        {
            switch (CurrentPhase)
            {
                case CombatPhase.Deploying:
                    TickDeploy(deltaTime);
                    break;
                case CombatPhase.Battling:
                    TickBattle(deltaTime);
                    break;
                case CombatPhase.Ended:
                    TickEndPhase(deltaTime);
                    break;
            }
        }

        public bool IsRunning => CurrentPhase != CombatPhase.Idle;

        /// <summary>当前波次（供外部读取）</summary>
        public int CurrentWave => _battleWave;

        /// <summary>手动结束部署阶段</summary>
        public void EndDeployPhase()
        {
            if (CurrentPhase == CombatPhase.Deploying)
            {
                _deployTcs?.TrySetResult(true);
                Debug.Log("[CombatOrchestrator] 手动结束部署阶段");
            }
        }

        public async UniTask CleanupAsync(SystemGroup systems)
        {
            await systems.CleanupAllAsync();
            SetPhase(CombatPhase.Idle);
        }

        // ==================== 阶段 Tick ====================

        private void TickDeploy(float dt)
        {
            _deployElapsed += dt;
            if (_deployElapsed >= _deployTimeLimit)
            {
                _deployTcs?.TrySetResult(true);
            }
        }

        private void TickBattle(float dt)
        {
            // 安全守卫：若外部直接调用 TickBattle 而非通过 Tick()，防止在错误阶段执行战斗逻辑
            if (CurrentPhase != CombatPhase.Battling) return;

            _battleElapsed += dt;

            // 超时保护
            if (_battleMode.BattleTimeout > 0 && _battleElapsed > _battleMode.BattleTimeout)
            {
                Debug.LogWarning("[CombatOrchestrator] 战斗超时，强制结束");
                _battleDeathSys.ForceWinner(Team.Defender);
                _battleTcs?.TrySetResult(true);
                return;
            }

            // 波次生成
            if (_battleMode.UseWaves)
            {
                _battleWaveTimer += dt;
                if (_battleWaveTimer >= _battleMode.WaveInterval)
                {
                    _battleWaveTimer = 0f;
                    _battleWave++;
                    _battleMode.SpawnAttackerWave(_battleSpawner, _battleWave);
                }
            }

            _battleAI.Update();
            _battleSystems.UpdateAll();

            // 模式自定义胜负判定
            var winner = _battleMode.CheckWinCondition(_battleDeathSys, _battleElapsed);
            if (winner.HasValue)
            {
                Debug.Log($"[CombatOrchestrator] 战斗结束 [{_battleMode.DisplayName}], 胜者: {winner}");
                _battleTcs?.TrySetResult(true);
            }
        }

        private void TickEndPhase(float dt)
        {
            _endPhaseTimer -= dt;
            if (_endPhaseTimer <= 0f)
            {
                _endTcs?.TrySetResult(true);
            }
        }

        private void SetPhase(CombatPhase phase)
        {
            CurrentPhase = phase;
            EventBus.Publish(EventPhaseChanged, phase);
        }
    }
}
