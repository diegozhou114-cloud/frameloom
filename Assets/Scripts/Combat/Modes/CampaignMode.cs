using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 关卡模式 — 固定波次，无部署阶段，清完所有波次即胜利。
    /// 类似部落冲突的哥布林地图/单人关卡。
    /// </summary>
    public class CampaignMode : CombatMode
    {
        public override string ModeId => "campaign";
        public override string DisplayName => "单人关卡";

        public override float DeployTimeLimit => 0f; // 无部署
        public override bool AllowDeployment => false;
        public override float BattleTimeout => 120f;
        public override float FirstWaveDelay => 2f;

        /// <summary>关卡总波次数</summary>
        public int TotalWaves { get; set; } = 5;

        /// <summary>当前波次</summary>
        private int _currentWave;

        /// <summary>已消灭波次数</summary>
        public int WavesCleared { get; private set; }

        /// <summary>当前波次是否已标记为清除（防止每帧重复计数）</summary>
        private bool _currentWaveCleared;

        public override Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime)
        {
            if (elapsedTime >= BattleTimeout) return Team.Defender;
            if (deathSys.AttackerAlive == 0) return Team.Defender;

            // 防守方全灭 → 标记当前波次已清除（防止每帧重复计数）
            if (deathSys.DefenderAlive == 0 && !_currentWaveCleared)
            {
                _currentWaveCleared = true;
                WavesCleared++;
            }

            // 所有波次清完 + 无剩余防守方 = 胜利
            if (WavesCleared >= TotalWaves && deathSys.DefenderAlive == 0)
                return Team.Attacker;

            return null;
        }

        public override CombatEndEvent CalculateResult(
            Team winner, int totalBuildings, int destroyedBuildings, float battleDuration)
        {
            int stars = 0;
            if (winner == Team.Attacker)
            {
                if (WavesCleared >= TotalWaves) stars = 3;
                else if (WavesCleared >= TotalWaves / 2) stars = 2;
                else stars = 1;
            }

            return new CombatEndEvent
            {
                Winner = winner,
                Stars = stars,
                LootGold = WavesCleared * 200 + stars * 100,
                LootElixir = WavesCleared * 100 + stars * 50,
            };
        }

        public override void SpawnDefenders(UnitSpawner spawner, VillageData village)
        {
            // 关卡不依赖村庄数据，使用预定义布局
            WavesCleared = 0;
            _currentWave = 0;
            _currentWaveCleared = false;

            // 预置防守建筑（固定布局，独立于村庄数据）
            spawner.CreateBuildingEntity(new UnityEngine.Vector2Int(5, 5), 1, Team.Defender,
                300, 0, 40, 5f, 1.5f);
            spawner.CreateBuildingEntity(new UnityEngine.Vector2Int(15, 5), 1, Team.Defender,
                250, 0, 35, 5f, 1.5f);
            spawner.CreateBuildingEntity(new UnityEngine.Vector2Int(10, 15), 1, Team.Defender,
                500, 0, 60, 6f, 1.2f);
            spawner.CreateBuildingEntity(new UnityEngine.Vector2Int(5, 15), 1, Team.Defender,
                200, 0, 30, 4.5f, 2f);
            spawner.CreateBuildingEntity(new UnityEngine.Vector2Int(15, 15), 1, Team.Defender,
                350, 0, 45, 5f, 1.5f);
        }

        public override void SpawnAttackerWave(UnitSpawner spawner, int wave)
        {
            _currentWave = wave;
            _currentWaveCleared = false;

            // 关卡：敌方为"防守方"，玩家为"攻击方"
            int count = 2 + wave;
            float hp = 80 + wave * 30;
            int damage = 15 + wave * 8;
            float speed = 2f + wave * 0.3f;

            // NOTE: SpawnAttackerWave spawns player-side units (not enemy waves) —
            // the method name is misleading. Consider renaming to SpawnReinforcements
            // or SpawnPlayerWave to clarify it spawns attacker-team units, not enemies.
            spawner.SpawnAttackerWave(wave, 1f,
                (int)hp, (int)hp, damage, (int)(damage * 0.5f), 2.5f, 1f, speed);
        }
    }
}
