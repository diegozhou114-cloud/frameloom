using System;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class BattleInitFactoryTests
    {
        [Test]
        public void Create_MapsIdentityContentAndSupportedDefenders()
        {
            byte[] bytes = FrameworkBattleContent.CanonicalBytes();
            BattleParams parameters = CompleteParams();
            parameters.DefenderVillage.AddBuilding(Building("gold", 2, 3, 3));
            parameters.DefenderVillage.AddBuilding(Building("cannon", 5, 12, 2));

            BattleInit init = new BattleInitFactory(bytes).Create(parameters);

            Assert.That(init.ProtocolVersion, Is.EqualTo(1));
            Assert.That(init.SchemaVersion, Is.EqualTo(SimComponentSchema.Version));
            Assert.That(init.TickRate, Is.EqualTo(20));
            Assert.That(init.MatchId, Is.EqualTo(parameters.MatchId));
            Assert.That(init.SessionId, Is.EqualTo(parameters.SessionId));
            Assert.That(init.Seed, Is.EqualTo(parameters.Seed));
            Assert.That(init.ContentHash,
                Is.EqualTo(DeterministicContentManifest.Parse(bytes).ContentHash));
            Assert.That(init.Players.Select(player => player.PlayerId),
                Is.EquivalentTo(new[] { 1000, 2000 }));
            Assert.That(init.InitialEntities.Length, Is.EqualTo(2));
            Assert.That(init.InitialEntities.Select(entity => entity.ArchetypeId),
                Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(init.InitialEntities.All(entity =>
                entity.OwnerPlayerId == 2000 && entity.TeamId == SimTeam.Defender), Is.True);
            InitialEntitySpec cannon = init.InitialEntities.Single(entity => entity.ArchetypeId == 2);
            Assert.That(cannon.GridX, Is.EqualTo(12));
            Assert.That(cannon.GridY, Is.EqualTo(2));
        }

        [TestCase(nameof(BattleParams.ModeId))]
        [TestCase(nameof(BattleParams.MatchId))]
        [TestCase(nameof(BattleParams.SessionId))]
        [TestCase(nameof(BattleParams.AttackerAccountId))]
        [TestCase(nameof(BattleParams.DefenderAccountId))]
        public void Create_RejectsMissingRequiredString(string field)
        {
            BattleParams parameters = CompleteParams();
            typeof(BattleParams).GetField(field).SetValue(parameters, null);

            Assert.Throws<ArgumentException>(() =>
                new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
        }

        [Test]
        public void Create_RejectsAliasedPlayers()
        {
            BattleParams parameters = CompleteParams();
            parameters.DefenderPlayerId = parameters.AttackerPlayerId;

            Assert.Throws<ArgumentException>(() =>
                new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
        }

        [Test]
        public void Create_RejectsNullVillages()
        {
            BattleParams missingAttacker = CompleteParams();
            missingAttacker.AttackerVillage = null;
            BattleParams missingDefender = CompleteParams();
            missingDefender.DefenderVillage = null;
            var factory = new BattleInitFactory(FrameworkBattleContent.CanonicalBytes());

            Assert.Throws<ArgumentException>(() => factory.Create(missingAttacker));
            Assert.Throws<ArgumentException>(() => factory.Create(missingDefender));
        }

        [Test]
        public void Create_RejectsUnsupportedMode()
        {
            BattleParams parameters = CompleteParams();
            parameters.ModeId = "unsupported";

            Assert.Throws<ArgumentException>(() =>
                new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
        }

        [Test]
        public void Create_RejectsNoSupportedDefenderEntity()
        {
            BattleParams parameters = CompleteParams();
            parameters.DefenderVillage.Buildings.Clear();
            parameters.DefenderVillage.AddBuilding(Building("gold", 2, 3, 3));

            Assert.Throws<ArgumentException>(() =>
                new BattleInitFactory(FrameworkBattleContent.CanonicalBytes()).Create(parameters));
        }

        [Test]
        public void Constructor_RejectsInvalidManifest()
        {
            Assert.Throws<FormatException>(() => new BattleInitFactory(new byte[] { 1 }));
        }

        private static BattleParams CompleteParams()
        {
            string[] ids = { "factory-match", "factory-session" };
            int index = 0;
            var defender = new VillageData();
            defender.AddBuilding(Building("town-hall", 1, 8, 2));
            return new FrameworkBattleAuthority(() => ids[index++]).CreateVillageAttack(
                new PlayerData(), new VillageData(), defender);
        }

        private static BuildingData Building(string id, int typeId, int x, int y) =>
            new BuildingData
            {
                Id = id, TypeId = typeId, GridX = { Value = x }, GridY = { Value = y }
            };
    }
}
