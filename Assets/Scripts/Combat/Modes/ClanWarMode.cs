using UnityEngine;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 部落战模式 — 每人两次攻击机会，取最佳成绩。
    /// 胜负判定：总星数 → 总摧毁率。
    /// </summary>
    public class ClanWarMode : CombatMode
    {
        public override string ModeId => "clan_war";
        public override string DisplayName => "部落战";

        public override float DeployTimeLimit => 30f;
        public override float BattleTimeout => 180f;
        public override bool UseWaves => false; // 一次性部署全部单位

        public override Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime)
        {
            if (elapsedTime >= BattleTimeout) return Team.Defender;
            if (deathSys.AttackerAlive == 0) return Team.Defender;
            if (deathSys.DefenderAlive == 0) return Team.Attacker;
            return null;
        }

        public override CombatEndEvent CalculateResult(
            Team winner, int totalBuildings, int destroyedBuildings, float battleDuration)
        {
            float pct = totalBuildings > 0 ? (float)destroyedBuildings / totalBuildings : 0f;
            int stars = pct >= 1f ? 3 : pct >= 0.5f ? 2 : pct > 0f ? 1 : 0;

            return new CombatEndEvent
            {
                Winner = winner,
                Stars = stars,
                LootGold = (int)(pct * 500),
                LootElixir = (int)(pct * 300),
            };
        }

        public override void SpawnDefenders(UnitSpawner spawner, VillageData village)
        {
            // 部落战防御建筑有额外加成
            foreach (var building in village.Buildings)
            {
                var gridPos = new Vector2Int(building.GridX.Value, building.GridY.Value);
                var entity = spawner.CreateBuildingEntity(gridPos, building.Level.Value,
                    Team.Defender, 600, 250, 60, 5.5f, 0.9f);
            }
        }

        public override void SpawnAttackerWave(UnitSpawner spawner, int wave)
        {
            throw new System.NotSupportedException("ClanWarMode does not use waves");
        }
    }
}
