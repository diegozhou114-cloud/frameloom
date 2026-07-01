using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 夜世界对抗模式 — 同时攻防，无时间限制。
    /// 先清空对方建筑者胜；同时清空则比摧毁率。
    /// </summary>
    public class BuilderBaseMode : CombatMode
    {
        public override string ModeId => "builder_base";
        public override string DisplayName => "夜世界对抗";

        public override float DeployTimeLimit => 0f;     // 无部署
        public override bool AllowDeployment => false;
        public override float BattleTimeout => 0f;        // 无超时（打到一方全灭）
        public override bool UseWaves => false;
        public override float FirstWaveDelay => 1f;

        /// <summary>对手摧毁率（由服务器同步）</summary>
        public float OpponentDestructionPercent { get; set; }

        public override Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime)
        {
            // 先清空对方者胜
            bool attackerWiped = deathSys.AttackerAlive == 0;
            bool defenderWiped = deathSys.DefenderAlive == 0;

            if (attackerWiped && defenderWiped)
            {
                // 双方同时清空 → 比摧毁率
                float myDestruction = deathSys.AttackerDestroyedPercent;
                return myDestruction >= OpponentDestructionPercent
                    ? Team.Attacker : Team.Defender;
            }

            if (attackerWiped) return Team.Defender;
            if (defenderWiped) return Team.Attacker;

            return null;
        }

        public override CombatEndEvent CalculateResult(
            Team winner, int totalBuildings, int destroyedBuildings, float battleDuration)
        {
            float pct = totalBuildings > 0 ? (float)destroyedBuildings / totalBuildings : 0f;
            int stars = pct >= 1f ? 3 : pct >= 0.5f ? 2 : pct > 0f ? 1 : 0;

            // 对抗模式奖励基于百分比
            return new CombatEndEvent
            {
                Winner = winner,
                Stars = stars,
                LootGold = (int)(pct * 300),
                LootElixir = (int)(pct * 200),
            };
        }

        public override void SpawnDefenders(UnitSpawner spawner, VillageData village)
        {
            // 夜世界建筑直接映射
            foreach (var building in village.Buildings)
            {
                var gridPos = new UnityEngine.Vector2Int(building.GridX.Value, building.GridY.Value);
                var entity = spawner.CreateBuildingEntity(gridPos, building.Level.Value,
                    Team.Defender, 400, 150, 40, 4f, 1.2f);
            }
        }

        public override void SpawnAttackerWave(UnitSpawner spawner, int wave)
        {
            throw new System.NotSupportedException("BuilderBaseMode does not use waves");
        }
    }
}
