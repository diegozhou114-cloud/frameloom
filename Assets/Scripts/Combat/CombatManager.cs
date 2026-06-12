using System.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.ECS;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 战斗管理器 — 薄门面，跨场景单例。
    /// 接收 CombatMode 参数，委托给 CombatOrchestrator / UnitSpawner。
    /// </summary>
    public class CombatManager : SingletonDontDestroy<CombatManager>
    {
        public static string EventPhaseChanged => CombatOrchestrator.EventPhaseChanged;
        public static string EventCombatEnded => CombatOrchestrator.EventCombatEnded;

        internal World ECSWorld { get; private set; }
        internal DeathSystem DeathSys { get; private set; }
        public CombatPhase CurrentPhase => _orchestrator.CurrentPhase;
        public CombatMode CurrentMode { get; private set; }

        private CombatOrchestrator _orchestrator;
        private UnitSpawner _spawner;
        private UnitAI _ai;
        private SystemGroup _systems;
        private AStarPathfinding _pathfinding;

        protected override void Awake()
        {
            base.Awake();
            FreshBattle();
        }

        /// <summary>以指定模式启动战斗</summary>
        public async Task StartBattleAsync(CombatMode mode)
        {
            if (CurrentPhase != CombatPhase.Idle) return;
            CurrentMode = mode;

            Debug.Log($"[CombatManager] ===== 战斗开始 [{mode.DisplayName}] =====");

            _pathfinding = new AStarPathfinding(GridManager.Instance);
            _systems = await CombatBootstrap.BuildAsync(ECSWorld);
            DeathSys = _systems.Find<DeathSystem>();
            _ai = new UnitAI(ECSWorld, _pathfinding);

            // 委托模式生成防守方
            mode.SpawnDefenders(_spawner, Data.DataManager.Instance.Village);

            // 部署阶段
            if (mode.AllowDeployment)
                await _orchestrator.RunDeployPhase(mode.DeployTimeLimit);

            // 战斗阶段
            await _orchestrator.RunBattlePhase(_systems, _ai, DeathSys, mode, _spawner);

            // 结算
            var winner = DeathSys.Winner ?? Team.Defender;
            var village = Data.DataManager.Instance.Village;
            int destroyed = village.Buildings.Count - DeathSys.DefenderAlive;
            var result = mode.CalculateResult(winner, village.Buildings.Count, destroyed,
                Time.time); // battleDuration 从 Orchestrator 获取更精确

            EventBus.Publish(CombatOrchestrator.EventCombatEnded, result);
            Debug.Log($"[CombatManager] 结算: winner={result.Winner}, stars={result.Stars}");

            await _orchestrator.RunEndPhase(result);
            await _orchestrator.CleanupAsync(_systems);
            FreshBattle();
        }

        private void FreshBattle()
        {
            ECSWorld = new World(256);
            _spawner = new UnitSpawner(ECSWorld);
            _orchestrator = new CombatOrchestrator();
        }
    }
}
