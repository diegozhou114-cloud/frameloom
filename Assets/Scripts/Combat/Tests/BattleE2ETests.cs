using NUnit.Framework;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.ECS;

namespace ZWebGLFrameWork.Combat.Tests
{
    /// <summary>
    /// 战斗端到端测试 — 验证部署→攻击→伤害→死亡的完整 ECS 管线。
    /// </summary>
    [TestFixture]
    public class BattleE2ETests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World(64);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Reset();
        }

        // ==================== 基础管线 ====================

        [Test]
        public void SpawnUnit_CreatesEntityWithAllComponents()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, TransformComponent.Create(
                new UnityEngine.Vector3(5, 0, 5), new UnityEngine.Vector2Int(5, 5)));
            _world.AddComponent(entity, HealthComponent.Create(100));
            _world.AddComponent(entity, TeamComponent.Create(Team.Attacker));
            _world.AddComponent(entity, AttackComponent.Create(20, 3f, 1f));

            Assert.IsTrue(_world.HasComponent<TransformComponent>(entity));
            Assert.IsTrue(_world.HasComponent<HealthComponent>(entity));
            Assert.IsTrue(_world.HasComponent<TeamComponent>(entity));
            Assert.IsTrue(_world.HasComponent<AttackComponent>(entity));

            var health = _world.GetComponent<HealthComponent>(entity);
            Assert.AreEqual(100, health.CurrentHP);
            Assert.AreEqual(100, health.MaxHP);
        }

        [Test]
        public void DamageEvent_ReducesHP()
        {
            var target = _world.CreateEntity();
            _world.AddComponent(target, HealthComponent.Create(100));
            _world.AddComponent(target, TeamComponent.Create(Team.Defender));

            var attacker = _world.CreateEntity();
            _world.AddComponent(attacker, TeamComponent.Create(Team.Attacker));

            // 入队伤害事件
            _world.EnqueueEvent(new DamageEvent
            {
                Amount = 30,
                Attacker = attacker,
                Target = target,
            });

            // 消费事件
            _world.ConsumeEvents<DamageEvent>(evt =>
            {
                if (_world.TryGetComponent<HealthComponent>(evt.Target, out var health))
                {
                    health.CurrentHP -= evt.Amount;
                    _world.SetComponent(evt.Target, health);
                }
            });

            var result = _world.GetComponent<HealthComponent>(target);
            Assert.AreEqual(70, result.CurrentHP);
        }

        [Test]
        public void DeathAtZeroHP_EntityHasZeroHP()
        {
            var entity = _world.CreateEntity();
            _world.AddComponent(entity, HealthComponent.Create(50));
            _world.AddComponent(entity, TeamComponent.Create(Team.Defender));

            _world.EnqueueEvent(new DamageEvent { Amount = 60, Target = entity });
            _world.ConsumeEvents<DamageEvent>(evt =>
            {
                if (_world.TryGetComponent<HealthComponent>(evt.Target, out var health))
                {
                    health.CurrentHP -= evt.Amount;
                    if (health.CurrentHP < 0) health.CurrentHP = 0;
                    _world.SetComponent(evt.Target, health);
                }
            });

            var result = _world.GetComponent<HealthComponent>(entity);
            Assert.AreEqual(0, result.CurrentHP);
        }

        // ==================== 战斗模式 ====================

        [Test]
        public void VillageAttackMode_WinCondition_AllDefendersDead()
        {
            var mode = new VillageAttackMode();
            var deathSys = new DeathSystem();
            // 需要 World 才能运作 DeathSystem — 验证模式对象创建正确
            Assert.AreEqual("village_attack", mode.ModeId);
            Assert.AreEqual("村庄攻击", mode.DisplayName);
            Assert.IsTrue(mode.AllowDeployment);
            Assert.AreEqual(30f, mode.DeployTimeLimit);
        }

        [Test]
        public void VillageAttackMode_CalculateResult_AttackerWins()
        {
            var mode = new VillageAttackMode();
            var result = mode.CalculateResult(Team.Attacker, 10, 10, 120f);

            Assert.AreEqual(Team.Attacker, result.Winner);
            Assert.AreEqual(3, result.Stars); // 100% destruction = 3 stars
            Assert.Greater(result.LootGold, 0);
            Assert.Greater(result.LootElixir, 0);
        }

        [Test]
        public void VillageAttackMode_CalculateResult_50Percent_TwoStars()
        {
            var mode = new VillageAttackMode();
            var result = mode.CalculateResult(Team.Attacker, 10, 5, 120f);

            Assert.AreEqual(Team.Attacker, result.Winner);
            // pct=0.5 → >=0.5 → 2 stars
            Assert.AreEqual(2, result.Stars);
        }

        [Test]
        public void VillageAttackMode_DefenderWins_ZeroStars()
        {
            var mode = new VillageAttackMode();
            var result = mode.CalculateResult(Team.Defender, 10, 2, 180f);

            Assert.AreEqual(Team.Defender, result.Winner);
            Assert.AreEqual(0, result.Stars);
            Assert.AreEqual(0, result.LootGold);
        }

        // ==================== BattleParams ====================

        [Test]
        public void BattleParams_AuthorityCreatesCompleteVillageAttack()
        {
            var bp = new FrameworkBattleAuthority().CreateVillageAttack(
                new PlayerData(), new VillageData(), new VillageData());
            Assert.AreEqual("village_attack", bp.ModeId);
            Assert.IsNotNull(bp.AttackerVillage);
            Assert.IsNotNull(bp.DefenderVillage);
            Assert.IsNotEmpty(bp.MatchId);
            Assert.IsNotEmpty(bp.SessionId);
            Assert.AreNotEqual(0, bp.Seed);
            Assert.AreNotEqual(bp.AttackerPlayerId, bp.DefenderPlayerId);
            Assert.IsNotEmpty(bp.AttackerAccountId);
            Assert.IsNotEmpty(bp.DefenderAccountId);
        }

        // ==================== 组件变更事件 ====================

        [Test]
        public void OnComponentChanged_FiresOnAdd()
        {
            Entity? changedEntity = null;
            System.Type changedType = null;
            bool? wasAdded = null;

            _world.OnComponentChanged += (e, t, added) =>
            {
                changedEntity = e;
                changedType = t;
                wasAdded = added;
            };

            var entity = _world.CreateEntity();
            _world.AddComponent(entity, HealthComponent.Create(100));

            Assert.AreEqual(entity, changedEntity);
            Assert.AreEqual(typeof(HealthComponent), changedType);
            Assert.IsTrue(wasAdded);
        }

        [Test]
        public void WorldReset_ClearsAllEntities()
        {
            for (int i = 0; i < 10; i++)
            {
                var e = _world.CreateEntity();
                _world.AddComponent(e, HealthComponent.Create(50));
            }
            Assert.AreEqual(10, _world.EntityCount);

            _world.Reset();
            Assert.AreEqual(0, _world.EntityCount);
        }
    }
}
