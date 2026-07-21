using System;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class FrameworkBattleDefaultsTests
    {
        [Test]
        public void Authority_CreatesCompleteDistinctReplayInputs()
        {
            string[] ids = { "match-token", "session-token" };
            int index = 0;
            var authority = new FrameworkBattleAuthority(() => ids[index++]);

            BattleParams result = authority.CreateVillageAttack(
                new PlayerData(), new VillageData(), DefenderVillage());

            Assert.That(result.MatchId, Is.EqualTo("framework-match-match-token"));
            Assert.That(result.SessionId, Is.EqualTo("framework-session-session-token"));
            Assert.That(result.Seed, Is.Not.Zero);
            Assert.That(result.AttackerPlayerId, Is.Not.EqualTo(result.DefenderPlayerId));
            Assert.That(result.AttackerAccountId, Is.Not.Empty);
            Assert.That(result.DefenderAccountId, Is.Not.Empty);
            Assert.That(result.AttackerVillage, Is.Not.Null);
            Assert.That(result.DefenderVillage, Is.Not.Null);
        }

        [Test]
        public void Authority_SameGeneratedIds_ProduceSameReplaySeed()
        {
            BattleParams left = CreateWithIds("same-match", "same-session");
            BattleParams right = CreateWithIds("same-match", "same-session");
            Assert.That(right.Seed, Is.EqualTo(left.Seed));
        }

        [Test]
        public void Authority_DifferentGeneratedIds_ProduceDistinctBattleIdentity()
        {
            BattleParams left = CreateWithIds("left-match", "left-session");
            BattleParams right = CreateWithIds("right-match", "right-session");
            Assert.That(right.MatchId, Is.Not.EqualTo(left.MatchId));
            Assert.That(right.SessionId, Is.Not.EqualTo(left.SessionId));
            Assert.That(right.Seed, Is.Not.EqualTo(left.Seed));
        }

        [Test]
        public void Content_IsCanonicalCompleteAndMutationSafe()
        {
            byte[] first = FrameworkBattleContent.CanonicalBytes();
            first[0] ^= 0xFF;
            byte[] second = FrameworkBattleContent.CanonicalBytes();
            var manifest = DeterministicContentManifest.Parse(second);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(manifest.Archetypes.Select(row => row.ArchetypeId),
                Is.EquivalentTo(new[] { 1, 2, 10 }));
            Assert.That(manifest.Modes.Select(row => row.Mode), Is.EquivalentTo(new[]
            {
                SimCombatMode.VillageAttack, SimCombatMode.BuilderBase,
                SimCombatMode.ClanWar, SimCombatMode.Campaign
            }));
            Assert.That(FrameworkBattleContent.TryGetBuildingArchetypeId(5, out int cannon), Is.True);
            Assert.That(cannon, Is.EqualTo(2));
            Assert.That(FrameworkBattleContent.TryGetBuildingArchetypeId(2, out _), Is.False);
            Assert.That(FrameworkBattleContent.TryGetUnitArchetypeId(1, out int barbarian), Is.True);
            Assert.That(barbarian, Is.EqualTo(10));
        }

        private static BattleParams CreateWithIds(string match, string session)
        {
            string[] ids = { match, session };
            int index = 0;
            return new FrameworkBattleAuthority(() => ids[index++]).CreateVillageAttack(
                new PlayerData(), new VillageData(), DefenderVillage());
        }

        private static VillageData DefenderVillage()
        {
            var village = new VillageData();
            village.AddBuilding(new BuildingData
            {
                Id = "cannon", TypeId = 5, GridX = { Value = 8 }, GridY = { Value = 2 }
            });
            return village;
        }
    }
}
