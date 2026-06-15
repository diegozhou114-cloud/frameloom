using System;
using System.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗编排器 — 管理阶段流转。所有规则参数从 CombatMode 读取。
    /// 通过 Update-driven Tick 驱动帧循环，替代不可靠的 async Task.Yield。
    /// </summary>
    public class CombatOrchestrator
    {
        public const string EventPhaseChanged = "CombatPhaseChanged";
        public const string EventCombatEnded = "CombatEnded";

        public CombatPhase CurrentPhase { get; private set; } = CombatPhase.Idle;

        // ==================== 部署阶段状态 ====================

        private float _deployElapsed;
        private float _deployTimeLimit;
        private TaskCompletionSource<bool> _deployTcs;

        // ==================== 战斗阶段状态 ====================

        private SystemGroup _battleSystems;
        private UnitAI _battleAI;
        private DeathSystem _battleDeathSys;
        private CombatMode _battleMode;
        private UnitSpawner _battleSpawner;
        private int _battleWave;
        private float _battleWaveTimer;
        private float _battleElapsed;
        private TaskCompletionSource<bool> _battleTcs;

        // ==================== 结束阶段状态 ====================

        private float _endPhaseTimer;
        private TaskCompletionSource<bool> _endTcs;

        // ==================== 公共 API ====================

        /// <summary>开始部署阶段（非阻塞，返回 Task 在完成时 resolve）</summary>
        public Task RunDeployPhaseAsync(float timeLimit)
        {
            _deployTcs = new TaskCompletionSource<bool>();

            if (timeLimit <= 0)
            {
                _deployTcs.SetResult(true);
                return _deployTcs.Task;
            }

            _deployElapsed = 0f;
            _deployTimeLimit = timeLimit;
            SetPhase(CombatPhase.Deploying);
            Debug.Log($"[CombatOrchestrator] 部署阶段，{timeLimit}s");

            return _deployTcs.Task;
        }

        /// <summary>开始战斗阶段（非阻塞，由 CombatManager.Update 驱动）</summary>
        public Task RunBattlePhaseAsync(
            SystemGroup systems, UnitAI ai, DeathSystem deathSys,
            CombatMode mode, UnitSpawner spawner)
        {
            _battleTcs = new TaskCompletionSource<bool>();

            _battleSystems = systems;
            _battleAI = ai;
            _battleDeathSys = deathSys;
            _battleMode = mode;
            _battleSpawner = spawner;
            _battleWave = 0;
            _battleWaveTimer = mode.FirstWaveDelay;
            _battleElapsed = 0f;

            SetPhase(CombatPhase.Battling);
            Debug.Log($"[CombatOrchestrator] 战斗开始 [{mode.DisplayName}]");

            return _battleTcs.Task;
        }

        /// <summary>开始结束阶段</summary>
        public Task RunEndPhaseAsync(CombatEndEvent result)
        {
            _endTcs = new TaskCompletionSource<bool>();
            _endPhaseTimer = 2f;

            SetPhase(CombatPhase.Ended);
            Debug.Log($"[CombatOrchestrator] 结算: winner={result.Winner}, stars={result.Stars}");

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

        public async Task CleanupAsync(SystemGroup systems)
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

        // ==================== 兼容旧 API ====================

        [Obsolete("使用 RunDeployPhaseAsync + Tick 替代")]
        public async Task RunDeployPhase(float timeLimit) => await RunDeployPhaseAsync(timeLimit);

        [Obsolete("使用 RunBattlePhaseAsync + Tick 替代")]
        public async Task RunBattlePhase(
            SystemGroup systems, UnitAI ai, DeathSystem deathSys,
            CombatMode mode, UnitSpawner spawner)
        {
            await RunBattlePhaseAsync(systems, ai, deathSys, mode, spawner);
            // 旧调用路径会死锁 — 调用方应改用 async + Tick
        }

        [Obsolete("使用 RunEndPhaseAsync + Tick 替代")]
        public async Task RunEndPhase(CombatEndEvent result) => await RunEndPhaseAsync(result);

        private void SetPhase(CombatPhase phase)
        {
            CurrentPhase = phase;
            EventBus.Publish(EventPhaseChanged, phase);
        }
    }
}
