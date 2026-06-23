using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.ECS;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.Gameplay;
using ZWebGLFrameWork.SimulationCore;

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

        // ---- SimulationCore fields ----
        private SimWorld _simWorld;
        private CommandBuffer _simCommandBuffer;
        private SimSystemGroup _simSystems;
        private SimDeathSystem _simDeathSys;
        private SimOrchestrator _simOrchestrator;
        private SimulationSyncBridge _syncBridge;
        private Interpolator _interpolator;
        private float _simTickAccumulator;
        // -------------------------------

        protected override void Awake()
        {
            base.Awake();
            FreshBattle();
        }

        private void Update()
        {
            if (GameLauncher.Config != null && GameLauncher.Config.UseSimulationCore)
            {
                SimUpdate();
                return;
            }
            // 驱动编排器帧循环（替代不可靠的 async Task.Yield）
            if (_orchestrator.IsRunning)
            {
                _orchestrator.Tick(Time.deltaTime);
            }
        }

        /// <summary>以指定模式启动战斗</summary>
        public async UniTask StartBattleAsync(CombatMode mode)
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

            // 部署阶段（非阻塞，由 Update Tick 驱动）
            if (mode.AllowDeployment)
                await _orchestrator.RunDeployPhaseAsync(mode.DeployTimeLimit);

            // 战斗阶段
            await _orchestrator.RunBattlePhaseAsync(_systems, _ai, DeathSys, mode, _spawner);

            // 结算
            var winner = DeathSys.Winner ?? Team.Defender;
            var village = Data.DataManager.Instance.Village;
            int destroyed = village.Buildings.Count - DeathSys.DefenderAlive;
            var result = mode.CalculateResult(winner, village.Buildings.Count, destroyed,
                Time.time);

            EventBus.Publish(CombatOrchestrator.EventCombatEnded, result);
            Debug.Log($"[CombatManager] 结算: winner={result.Winner}, stars={result.Stars}");

            await _orchestrator.RunEndPhaseAsync(result);
            await _orchestrator.CleanupAsync(_systems);
            FreshBattle();
        }

        // ==================== SimulationCore 路径 ====================

        /// <summary>以 SimulationCore 引擎启动本地战斗</summary>
        public void StartSimBattle()
        {
            _simWorld = new SimWorld(256);
            _simCommandBuffer = new CommandBuffer();
            var initializer = new SimInitializerImpl(Data.DataManager.Instance.Village, seed: 42);
            _simSystems = SimCombatBootstrap.Build(_simWorld);
            _simDeathSys = _simSystems.Find<SimDeathSystem>();
            _simOrchestrator = new SimOrchestrator(
                _simWorld, _simSystems, _simDeathSys, _simCommandBuffer,
                recorder: null, initializer);
            _simOrchestrator.StartBattle();

            var viewConfig = Resources.Load<ViewConfig>("Config/ViewConfig");
            if (viewConfig == null)
                viewConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _syncBridge = new SimulationSyncBridge(_simWorld, viewConfig);
            _interpolator = new Interpolator(20f);
            _simTickAccumulator = 0f;

            Debug.Log("[CombatManager] SimulationCore 战斗已启动");
        }

        /// <summary>SimulationCore 每帧更新（固定时间步长驱动逻辑 Tick）</summary>
        private void SimUpdate()
        {
            float dt = Time.deltaTime;
            _interpolator.Update(dt);
            _syncBridge.SyncPositions(_interpolator);

            const float simDt = 1f / 20f; // 20 ticks/second
            _simTickAccumulator += dt;
            while (_simTickAccumulator >= simDt)
            {
                _simTickAccumulator -= simDt;
                if (_simOrchestrator.CurrentPhase == SimCombatPhase.Idle ||
                    _simOrchestrator.CurrentPhase == SimCombatPhase.Ended)
                    break;
                _simOrchestrator.Tick();
            }
        }

        // ==================== 旧路径 ====================

        private void FreshBattle()
        {
            ECSWorld = new World(256);
            _spawner = new UnitSpawner(ECSWorld);
            _orchestrator = new CombatOrchestrator();
        }
    }
}
