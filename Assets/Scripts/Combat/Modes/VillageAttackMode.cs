using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 标准村庄攻击 — 部落冲突经典模式。
    /// 3星制，摧毁50%=1星，大本营=1星，100%=3星。
    /// </summary>
    public class VillageAttackMode : CombatMode
    {
        public override string ModeId => "village_attack";
        public override string DisplayName => "村庄攻击";

        /// <summary>目标大本营等级（用于额外1星判定）</summary>
        public int TargetTownHallLevel { get; set; } = 1;

        public override Team? CheckWinCondition(DeathSystem deathSys, float elapsedTime)
        {
            // 超时或全灭
            if (elapsedTime >= BattleTimeout) return Team.Defender;
            if (deathSys.AttackerAlive == 0) return Team.Defender;
            if (deathSys.DefenderAlive == 0) return Team.Attacker;
            return null;
        }

        public override CombatEndEvent CalculateResult(
            Team winner, int totalBuildings, int destroyedBuildings, float battleDuration)
        {
            int stars = 0;
            if (winner == Team.Attacker)
            {
                float pct = totalBuildings > 0 ? (float)destroyedBuildings / totalBuildings : 0f;
                if (pct >= 1f) stars = 3;
                else if (pct >= 0.5f) stars = 2;
                else if (pct > 0f) stars = 1;
            }

            return new CombatEndEvent
            {
                Winner = winner,
                Stars = stars,
                LootGold = winner == Team.Attacker
                    ? UnityEngine.Random.Range(100, 500) : 0,
                LootElixir = winner == Team.Attacker
                    ? UnityEngine.Random.Range(50, 300) : 0,
            };
        }

        public override void SpawnDefenders(UnitSpawner spawner, VillageData village)
        {
            foreach (var building in village.Buildings)
            {
                var gridPos = new UnityEngine.Vector2Int(building.GridX.Value, building.GridY.Value);
                var entity = spawner.CreateBuildingEntity(gridPos, building.Level.Value,
                    Team.Defender, 500, 200, 50, 5f, 1f);
            }
        }

        public override void SpawnAttackerWave(UnitSpawner spawner, int wave)
        {
            int count = 3 + wave;
            spawner.SpawnAttackerWave(wave, 1f, 100, 20, 20, 5, 2f, 0.8f, 3f);
        }
    }
}
