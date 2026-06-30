using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
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

        /// <summary>当前波次（供 BattleHUD 读取）</summary>
        public int CurrentWave => _orchestrator?.CurrentWave ?? 0;

        /// <summary>攻击方存活数（供 BattleHUD 读取）</summary>
        public int AttackerAliveCount => DeathSys?.AttackerAlive ?? 0;

        /// <summary>防守方存活数（供 BattleHUD 读取）</summary>
        public int DefenderAliveCount => DeathSys?.DefenderAlive ?? 0;

        /// <summary>在指定网格部署一个攻击方单位（供 DeployPanel 调用）</summary>
        public void SpawnDeployUnit(Vector2Int gridPos, UnitConfig config)
        {
            _spawner?.SpawnUnit(gridPos, config, Team.Attacker);
        }

        /// <summary>手动结束部署阶段（供 DeployPanel "开始战斗" 按钮调用）</summary>
        public void EndDeployPhase()
        {
            _orchestrator?.EndDeployPhase();
        }

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
        private IntAStarPathfinding _simPathfinding;
        private int _simAILastTick = -1;
        private bool _simSpawnedAttackers;
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

            var gridManager = GridManager.Instance;
            if (gridManager == null)
            {
                Debug.LogError("[CombatManager] GridManager 未初始化，无法开始战斗");
                return;
            }
            if (Data.DataManager.Instance?.Village == null)
            {
                Debug.LogError("[CombatManager] VillageData 未就绪，无法开始战斗");
                return;
            }

            _pathfinding = new AStarPathfinding(gridManager);
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
            if (winner == Team.None)
            {
                Debug.LogWarning("[CombatManager] 双方同时全灭，平局按攻方失败处理");
                winner = Team.Defender;
            }
            var village = Data.DataManager.Instance?.Village;
            int destroyed = (village?.Buildings?.Count ?? 0) - DeathSys.DefenderAlive;
            var result = mode.CalculateResult(winner, village?.Buildings?.Count ?? 0, destroyed,
                Time.time);

            EventBus.Publish(CombatOrchestrator.EventCombatEnded, result);
            Debug.Log($"[CombatManager] 结算: winner={result.Winner}, stars={result.Stars}");

            await _orchestrator.RunEndPhaseAsync(result);
            await _orchestrator.CleanupAsync(_systems);
            FreshBattle();
        }

        // ==================== SimulationCore 路径 ====================
        // TODO: Consider splitting Sim* methods (StartSimBattle, SimUpdate, SimAI, etc.)
        // into a separate SimCombatRunner class to keep CombatManager focused on orchestration.

        /// <summary>以 SimulationCore 引擎启动本地战斗</summary>
        public void StartSimBattle()
        {
            _simWorld = new SimWorld(ComponentTypeRegistry.CreateCombatRegistry(), 256);
            _simCommandBuffer = new CommandBuffer();
            var initializer = new SimInitializerImpl(Data.DataManager.Instance.Village, seed: 42);
            _simSystems = SimCombatBootstrap.Build(_simWorld);
            _simDeathSys = _simSystems.Find<SimDeathSystem>();
            _simOrchestrator = new SimOrchestrator(
                _simWorld, _simSystems, _simDeathSys, _simCommandBuffer,
                recorder: null, initializer,
                SimArchetypeRegistry.CreateDefault());
            _simOrchestrator.StartBattle();

            var viewConfig = Resources.Load<ViewConfig>("Config/ViewConfig");
            if (viewConfig == null)
                viewConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _syncBridge = new SimulationSyncBridge(_simWorld, viewConfig);
            _interpolator = new Interpolator(20f);
            _simTickAccumulator = 0f;

            // 确定性 A* 寻路（纯逻辑网格，无 Unity 依赖）
            var grid = GridManager.Instance;
            _simPathfinding = new IntAStarPathfinding(
                grid.Columns, grid.Rows,
                (x, y) => grid.IsInBounds(new Vector2Int(x, y)));
            _simAILastTick = -1;
            _simSpawnedAttackers = false;

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
                SimAI(_simOrchestrator.CurrentTick);
                _simOrchestrator.Tick();
            }
        }

        // ==================== SimulationCore AI ====================

        /// <summary>简单 AI — 每 10 Tick 为攻击方寻找最近防守方并设置路径</summary>
        private void SimAI(int tick)
        {
            // 首次调用：生成攻击方单位（部署阶段结束后才可操作）
            if (!_simSpawnedAttackers && _simOrchestrator.CurrentPhase == SimCombatPhase.Battling)
            {
                SpawnSimAttackers();
                _simSpawnedAttackers = true;
            }

            // 每 10 Tick 重新分配目标
            if (tick % 10 != 0 || tick == _simAILastTick) return;
            _simAILastTick = tick;

            // 收集所有存活的防守方位置
            var defenders = new List<(SimEntity entity, int gx, int gy)>();
            _simWorld.ForEachEntities<SimHealthComponent>(entity =>
            {
                if (!_simWorld.TryGetComponent<SimTeamComponent>(entity, out var team)) return;
                if (team.TeamId != SimTeam.Defender) return;
                if (!_simWorld.TryGetComponent<SimHealthComponent>(entity, out var hp)) return;
                if (!hp.IsAlive) return;
                if (!_simWorld.TryGetComponent<SimTransformComponent>(entity, out var tf)) return;
                defenders.Add((entity, tf.GridX, tf.GridY));
            });

            if (defenders.Count == 0) return;

            // 为每个攻击方分配最近目标 + 路径
            _simWorld.ForEachEntities<SimTransformComponent>(entity =>
            {
                if (!_simWorld.TryGetComponent<SimTeamComponent>(entity, out var team)) return;
                if (team.TeamId != SimTeam.Attacker) return;
                if (!_simWorld.TryGetComponent<SimTransformComponent>(entity, out var tf)) return;

                // 找最近防守方
                SimEntity nearest = SimEntity.Invalid;
                int bestDist = int.MaxValue;
                int bestGX = 0, bestGY = 0;
                foreach (var (def, dgx, dgy) in defenders)
                {
                    int d = System.Math.Abs(tf.GridX - dgx) + System.Math.Abs(tf.GridY - dgy);
                    if (d < bestDist) { bestDist = d; nearest = def; bestGX = dgx; bestGY = dgy; }
                }

                if (!nearest.IsValid) return;

                // 设置目标
                _simWorld.SetComponent(entity, new SimTargetComponent { TargetEntity = nearest });

                // 寻路
                var path = _simPathfinding.FindPath(tf.GridX, tf.GridY, bestGX, bestGY);
                if (!_simWorld.TryGetComponent<SimMovementComponent>(entity, out var mov)) return;

                if (path.Count > 0)
                {
                    var packed = new List<int>(path.Count);
                    foreach (var (x, y) in path)
                        packed.Add((x << 16) | y);
                    mov.Path = packed;
                    mov.PathIndex = 0;
                    mov.TargetGridX = bestGX;
                    mov.TargetGridY = bestGY;
                }
                else
                {
                    mov.Path = null;
                    mov.PathIndex = 0;
                }
                _simWorld.SetComponent(entity, mov);
            });
        }

        /// <summary>在网格边缘生成攻击方单位</summary>
        private void SpawnSimAttackers()
        {
            var grid = GridManager.Instance;
            int spawnCount = System.Math.Min(8, grid.Rows / 2);

            for (int i = 0; i < spawnCount; i++)
            {
                int gx = 0; // 左边缘
                int gy = 2 + i * (grid.Rows - 4) / System.Math.Max(1, spawnCount - 1);

                var entity = _simWorld.CreateEntity(
                    typeof(SimTransformComponent), typeof(SimHealthComponent), typeof(SimTeamComponent),
                    typeof(SimMovementComponent), typeof(SimAttackComponent), typeof(SimTargetComponent));
                _simWorld.SetComponent(entity, SimTransformComponent.Create(gx, gy, LogicInt.FromInt(1)));
                _simWorld.SetComponent(entity, SimHealthComponent.Create(100));
                _simWorld.SetComponent(entity, SimTeamComponent.Create(SimTeam.Attacker));
                _simWorld.SetComponent(entity, SimMovementComponent.Create(2f, 1f));
                _simWorld.SetComponent(entity, SimAttackComponent.Create(20, 3f, 1f, 20));
                _simWorld.SetComponent(entity, SimTargetComponent.Create());
            }

            Debug.Log($"[CombatManager] SimAI 已生成 {spawnCount} 个攻击方单位");
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
