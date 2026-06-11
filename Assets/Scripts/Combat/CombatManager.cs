using System;
using System.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Combat.ECS;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>战斗阶段</summary>
    public enum CombatPhase
    {
        Idle,           // 未开始
        Deploying,      // 部署阶段（玩家放置单位）
        Battling,       // 战斗中（ECS 运行）
        Ended,          // 战斗结束
    }

    /// <summary>
    /// 战斗管理器 — 跨场景单例，管理整个战斗生命周期。
    /// 三阶段流程：Deploying → Battling → Ended，全部 async 驱动。
    /// </summary>
    public class CombatManager : SingletonDontDestroy<CombatManager>
    {
        /// <summary>EventBus：战斗阶段变化</summary>
        public const string EventPhaseChanged = "CombatPhaseChanged";
        /// <summary>EventBus：战斗结束</summary>
        public const string EventCombatEnded = "CombatEnded";

        /// <summary>当前阶段</summary>
        public CombatPhase CurrentPhase { get; private set; } = CombatPhase.Idle;

        /// <summary>ECS 世界</summary>
        public World ECSWorld { get; private set; }

        /// <summary>死亡系统（暴露胜负信息）</summary>
        public DeathSystem DeathSys { get; private set; }

        private SystemGroup _systems;
        private UnitAI _unitAI;

        [Header("部署")]
        [SerializeField] private float _deployTimeLimit = 30f; // 部署时间限制（秒）

        [Header("波次")]
        [SerializeField] private bool _useWaves = true;
        [SerializeField] private float _waveInterval = 5f;      // 波次间隔（秒）

        protected override void Awake()
        {
            base.Awake();
            ECSWorld = new World(256);
        }

        // ==================== 开始战斗 ====================

        /// <summary>启动战斗（异步）</summary>
        public async Task StartBattleAsync()
        {
            if (CurrentPhase != CombatPhase.Idle)
            {
                Debug.LogWarning("[CombatManager] 战斗已在进行中");
                return;
            }

            Debug.Log("[CombatManager] ===== 战斗开始 =====");

            // 1. 初始化 ECS
            InitializeSystems();

            // 2. 生成防守方建筑和单位
            SpawnDefenders();

            // 3. 部署阶段
            await RunDeployPhase();

            // 4. 战斗阶段
            await RunBattlePhase();

            // 5. 结算
            await RunEndPhase();
        }

        // ==================== 三阶段 ====================

        private async Task RunDeployPhase()
        {
            SetPhase(CombatPhase.Deploying);
            Debug.Log($"[CombatManager] 部署阶段，{_deployTimeLimit}s 时间限制");

            // 等待玩家部署完成或超时
            float elapsed = 0f;
            while (elapsed < _deployTimeLimit)
            {
                // TODO: 检查玩家是否点击"开始战斗"
                await AsyncManager.Delay(100);
                elapsed += 0.1f;

                // 模拟：延迟结束后自动开始
                await Task.Yield();
            }
        }

        private async Task RunBattlePhase()
        {
            SetPhase(CombatPhase.Battling);
            Debug.Log("[CombatManager] 战斗开始！");

            // 启动 UnitAI
            _unitAI = new UnitAI(ECSWorld);

            int wave = 0;
            float waveTimer = 0f;

            // 主循环
            while (CurrentPhase == CombatPhase.Battling)
            {
                // 波次生成（攻击方）
                if (_useWaves)
                {
                    waveTimer += Time.deltaTime;
                    if (waveTimer >= _waveInterval)
                    {
                        waveTimer = 0f;
                        wave++;
                        SpawnAttackerWave(wave);
                    }
                }

                // ECS 系统管线
                _unitAI.Update();
                _systems.UpdateAll();

                // 检查胜负
                if (DeathSys.Winner.HasValue)
                {
                    Debug.Log($"[CombatManager] 战斗结束，胜者: {DeathSys.Winner}");
                    break;
                }

                await Task.Yield(); // 下一帧
            }
        }

        private async Task RunEndPhase()
        {
            SetPhase(CombatPhase.Ended);

            var winner = DeathSys.Winner ?? Team.Defender;

            // 结算
            var evt = new CombatEndEvent
            {
                Winner = winner,
                Stars = CalculateStars(winner),
                LootGold = CalculateLoot(winner),
                LootElixir = CalculateLoot(winner),
            };

            EventBus.Publish(EventCombatEnded, evt);
            Debug.Log($"[CombatManager] 结算: winner={winner}, stars={evt.Stars}, " +
                      $"gold={evt.LootGold}, elixir={evt.LootElixir}");

            // 等待结算动画
            await AsyncManager.DelaySeconds(2f);

            // 清理
            await _systems.CleanupAllAsync();
            ECSWorld = new World(256);
            SetPhase(CombatPhase.Idle);
        }

        // ==================== 单位生成 ====================

        private void SpawnDefenders()
        {
            // 从 VillageData 读取建筑位置，生成防守建筑实体
            var village = Data.DataManager.Instance.Village;
            foreach (var building in village.Buildings)
            {
                var gridPos = new Vector2Int(building.GridX.Value, building.GridY.Value);
                var worldPos = GridManager.Instance.GridToWorld(gridPos);

                var entity = ECSWorld.CreateEntity();
                ECSWorld.AddComponent(entity, TransformComponent.Create(worldPos, gridPos));
                ECSWorld.AddComponent(entity, HealthComponent.Create(500 + building.Level.Value * 200));
                ECSWorld.AddComponent(entity, AttackComponent.Create(50, 5f, 1f));
                ECSWorld.AddComponent(entity, TeamComponent.Create(Team.Defender));
            }

            Debug.Log($"[CombatManager] 生成 {village.Buildings.Count} 个防守建筑");
        }

        private void SpawnAttackerWave(int wave)
        {
            // 在村庄边缘生成攻击单位
            var grid = GridManager.Instance;
            int count = 3 + wave; // 每波递增

            for (int i = 0; i < count; i++)
            {
                // 随机边缘位置
                var edgePos = new Vector2Int(
                    UnityEngine.Random.Range(0, grid.Columns),
                    grid.Rows - 1); // 顶部边缘

                var worldPos = grid.GridToWorld(edgePos);

                var entity = ECSWorld.CreateEntity();
                ECSWorld.AddComponent(entity, TransformComponent.Create(worldPos, edgePos));
                ECSWorld.AddComponent(entity, HealthComponent.Create(100 + wave * 20));
                ECSWorld.AddComponent(entity, AttackComponent.Create(20 + wave * 5, 2f, 0.8f));
                ECSWorld.AddComponent(entity, TeamComponent.Create(Team.Attacker));
            }

            Debug.Log($"[CombatManager] 波次 {wave}: 生成 {count} 个攻击单位");
        }

        // ==================== 结算 ====================

        private int CalculateStars(Team winner)
        {
            if (winner != Team.Attacker) return 0;

            // 根据防守方剩余和摧毁率计算星级
            int totalBuildings = Data.DataManager.Instance.Village.Buildings.Count;
            int destroyed = totalBuildings - DeathSys.DefenderAlive;
            float destructionPercent = totalBuildings > 0 ? (float)destroyed / totalBuildings : 0f;

            if (destructionPercent >= 1f) return 3;
            if (destructionPercent >= 0.5f) return 2;
            return 1;
        }

        private int CalculateLoot(Team winner)
        {
            if (winner != Team.Attacker) return 0;
            return UnityEngine.Random.Range(100, 500);
        }

        // ==================== 阶段管理 ====================

        private void SetPhase(CombatPhase phase)
        {
            CurrentPhase = phase;
            EventBus.Publish(EventPhaseChanged, phase);
        }

        // ==================== 系统初始化 ====================

        private void InitializeSystems()
        {
            DeathSys = new DeathSystem();
            _systems = new SystemGroup();
            _systems.Add(
                new MovementSystem(),
                new AttackSystem(),
                new DamageSystem(),
                DeathSys
            );

            // 同步初始化（战斗系统 InitAsync 通常无异步逻辑）
            _systems.InitAllAsync(ECSWorld).GetAwaiter().GetResult();
        }
    }

    /// <summary>战斗结束事件</summary>
    public struct CombatEndEvent
    {
        public Team Winner;
        public int Stars;
        public int LootGold;
        public int LootElixir;
    }
}
