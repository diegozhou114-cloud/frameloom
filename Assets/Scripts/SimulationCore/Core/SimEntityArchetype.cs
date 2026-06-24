using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 实体原型 — 定义给定 ArchetypeId 应挂载哪些组件及其初始值。
    /// 用于 SimOrchestrator.StartBattle() 和 ReplayValidator 重放。
    /// </summary>
    public class SimEntityArchetype
    {
        public int ArchetypeId { get; }
        public string Name { get; }
        public int DefaultHealth { get; }
        public float DefaultSpeed { get; }
        public int DefaultDamage { get; }
        public float DefaultRange { get; }
        public float DefaultCooldown { get; }
        public bool CanMove { get; }
        public bool CanAttack { get; }

        public SimEntityArchetype(int archetypeId, string name,
            int defaultHealth = 100,
            float defaultSpeed = 0f,
            int defaultDamage = 0,
            float defaultRange = 0f,
            float defaultCooldown = 0f)
        {
            ArchetypeId = archetypeId;
            Name = name;
            DefaultHealth = defaultHealth;
            DefaultSpeed = defaultSpeed;
            DefaultDamage = defaultDamage;
            DefaultRange = defaultRange;
            DefaultCooldown = defaultCooldown;
            CanMove = defaultSpeed > 0f;
            CanAttack = defaultDamage > 0;
        }

        /// <summary>将原型应用到新创建的实体上</summary>
        public void ApplyTo(SimWorld world, SimEntity entity, int gridX, int gridY, int teamId)
        {
            world.AddComponent(entity, SimTransformComponent.Create(gridX, gridY, LogicInt.FromInt(1)));
            world.AddComponent(entity, SimHealthComponent.Create(DefaultHealth));
            world.AddComponent(entity, SimTeamComponent.Create(teamId));

            if (CanMove)
                world.AddComponent(entity, SimMovementComponent.Create(DefaultSpeed, 1f));

            if (CanAttack)
            {
                world.AddComponent(entity, SimAttackComponent.Create(DefaultDamage, DefaultRange, DefaultCooldown, 20));
                world.AddComponent(entity, SimTargetComponent.Create());
            }
        }
    }

    /// <summary>
    /// 原型注册表 — 管理所有 SimEntityArchetype。
    /// 客户端和服务器必须共享相同的注册表（同一份配置）。
    /// </summary>
    public class SimArchetypeRegistry
    {
        private readonly Dictionary<int, SimEntityArchetype> _archetypes = new();

        public void Register(SimEntityArchetype archetype)
        {
            _archetypes[archetype.ArchetypeId] = archetype;
        }

        public SimEntityArchetype Get(int archetypeId)
        {
            _archetypes.TryGetValue(archetypeId, out var a);
            return a;
        }

        public bool Has(int archetypeId) => _archetypes.ContainsKey(archetypeId);

        /// <summary>创建默认注册表（硬编码，后续可替换为配置表驱动）</summary>
        public static SimArchetypeRegistry CreateDefault()
        {
            var reg = new SimArchetypeRegistry();
            reg.Register(new SimEntityArchetype(1, "TownHall",    defaultHealth: 500));
            reg.Register(new SimEntityArchetype(2, "Cannon",      defaultHealth: 200));
            reg.Register(new SimEntityArchetype(3, "ArcherTower", defaultHealth: 300));
            reg.Register(new SimEntityArchetype(10, "Barbarian",  defaultHealth: 100, defaultSpeed: 2f, defaultDamage: 20, defaultRange: 1f, defaultCooldown: 1f));
            reg.Register(new SimEntityArchetype(11, "Archer",     defaultHealth: 60,  defaultSpeed: 1.5f, defaultDamage: 15, defaultRange: 3f, defaultCooldown: 1.5f));
            return reg;
        }
    }
}
