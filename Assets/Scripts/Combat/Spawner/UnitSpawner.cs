using UnityEngine;
using ZWebGLFrameWork.ECS;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.Gameplay;

namespace ZWebGLFrameWork.Combat
{
    /// <summary>
    /// 单位生成器 — 创建攻击方和防守方的 ECS 实体。
    /// </summary>
    public class UnitSpawner
    {
        private readonly World _world;
        private readonly GridManager _grid;

        public UnitSpawner(World world)
        {
            _world = world;
            _grid = GridManager.Instance;
        }

        /// <summary>创建建筑实体（防守方），返回 Entity</summary>
        public Entity CreateBuildingEntity(Vector2Int gridPos, int level, Team team,
            int baseHp, int hpPerLevel, int baseDamage, float range, float cooldown,
            float attackBonus = 0f)
        {
            var worldPos = _grid.GridToWorld(gridPos);
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, TransformComponent.Create(worldPos, gridPos));
            _world.AddComponent(entity, HealthComponent.Create(baseHp + level * hpPerLevel));
            _world.AddComponent(entity, AttackComponent.Create(
                baseDamage + (int)(level * attackBonus), range, cooldown));
            _world.AddComponent(entity, TeamComponent.Create(team));
            return entity;
        }

        /// <summary>生成攻击方波次（灵活参数）</summary>
        public int SpawnAttackerWave(int wave, float cellSize,
            int baseHp, int hpPerWave, int baseDmg, int dmgPerWave,
            float range, float cooldown, float speed, int? countOverride = null)
        {
            int count = countOverride ?? (3 + wave);
            int edgeY = _grid.Rows - 1;

            for (int i = 0; i < count; i++)
            {
                var edgePos = new Vector2Int(Random.Range(0, _grid.Columns), edgeY);
                var worldPos = _grid.GridToWorld(edgePos);

                var entity = _world.CreateEntity();
                _world.AddComponent(entity, TransformComponent.Create(worldPos, edgePos));
                _world.AddComponent(entity, HealthComponent.Create(baseHp + wave * hpPerWave));
                _world.AddComponent(entity, AttackComponent.Create(
                    baseDmg + wave * dmgPerWave, range, cooldown));
                _world.AddComponent(entity, MovementComponent.Create(speed, cellSize));
                _world.AddComponent(entity, TeamComponent.Create(Team.Attacker));
            }

            Debug.Log($"[UnitSpawner] 波次 {wave}: 生成 {count} 个攻击单位");
            return count;
        }

        /// <summary>生成防守方（从村庄数据，标准参数）</summary>
        public void SpawnDefenders(VillageData village,
            int baseHp = 500, int hpPerLevel = 200,
            int baseDmg = 50, float range = 5f, float cooldown = 1f,
            float dmgPerLevel = 0f)
        {
            foreach (var building in village.Buildings)
            {
                var gridPos = new Vector2Int(building.GridX.Value, building.GridY.Value);
                CreateBuildingEntity(gridPos, building.Level.Value, Team.Defender,
                    baseHp, hpPerLevel, baseDmg, range, cooldown, dmgPerLevel);
            }
            Debug.Log($"[UnitSpawner] 生成 {village.Buildings.Count} 个防守建筑");
        }

        /// <summary>在指定网格位置部署一个单位</summary>
        public Entity SpawnUnit(Vector2Int gridPos, UnitConfig config, Team team)
        {
            var worldPos = _grid.GridToWorld(gridPos);
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, TransformComponent.Create(worldPos, gridPos));
            _world.AddComponent(entity, HealthComponent.Create(config.HP));
            _world.AddComponent(entity, AttackComponent.Create(
                config.Damage, config.Range, config.AttackSpeed));
            _world.AddComponent(entity, MovementComponent.Create(config.MoveSpeed, 1f));
            _world.AddComponent(entity, TeamComponent.Create(team));
            _world.AddComponent(entity, TargetComponent.Create());
            Debug.Log($"[UnitSpawner] 部署 {config.Name} 在 {gridPos}");
            return entity;
        }

        public World World => _world;
    }
}
