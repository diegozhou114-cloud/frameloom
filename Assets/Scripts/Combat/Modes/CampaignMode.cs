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

        public override Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime)
        {
            if (elapsedTime >= BattleTimeout) return Team.Defender;
            if (deathSys.AttackerAlive == 0) return Team.Defender;

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
        }

        public override void SpawnAttackerWave(UnitSpawner spawner, int wave)
        {
            _currentWave = wave;

            // 关卡：敌方为"防守方"，玩家为"攻击方"
            int count = 2 + wave;
            float hp = 80 + wave * 30;
            int damage = 15 + wave * 8;
            float speed = 2f + wave * 0.3f;

            spawner.SpawnAttackerWave(wave, 1f,
                (int)hp, hp, damage, (int)(damage * 0.5f), 2.5f, 1f, speed);
        }
    }
}
