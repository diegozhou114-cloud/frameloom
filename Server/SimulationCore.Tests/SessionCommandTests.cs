using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class SessionCommandTests
    {
        [Test]
        public void Start_RejectsInitialEntitiesThatOccupyTheSameCell()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "overlap-match", "overlap-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 2, 2, SimTeam.Defender, 1),
                }, 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidEntity));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void Start_RejectsBattleWithoutPlayers()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "empty-players-match", "empty-players-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                System.Array.Empty<SimPlayerIdentity>(),
                System.Array.Empty<InitialEntitySpec>(), 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidPlayer));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void SessionSkill_DotNetHostProducesDamageAndEvent()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();

            CommandSubmitResult submitted = session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            });
            TickResult result = session.Tick();

            Assert.That(submitted.Accepted, Is.True);
            Assert.That(session.State.Presentation.Entities[1].Health,
                Is.EqualTo(target.Health - 30));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Type, Is.EqualTo(SimBattleEventType.DamageApplied));
        }

        [Test]
        public void InvalidSessionCommand_DotNetHostDoesNotConsumeSequenceOrMutateWorld()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 99,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            session.Tick();
            Assert.That(session.State.Presentation.Entities[1].Health,
                Is.EqualTo(target.Health - 30));
        }

        [Test]
        public void SkillTargeting_UsesEntityTeamInsteadOfAssumingPlayerIdsZeroAndOne()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "custom-player-match", "custom-player-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 10),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            EntityPresentationState caster = session.State.Presentation.Entities[1];
            EntityPresentationState target = session.State.Presentation.Entities[0];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 10, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            target = session.State.Presentation.Entities.Single(entity =>
                entity.OwnerPlayerId == target.OwnerPlayerId);

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = 20,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            Assert.That(session.Tick().Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void Deploy_UsesSecondBattlePlayerAsDefenderWithCustomPlayerIds()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "custom-deploy-match", "custom-deploy-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 20, Sequence = 1, UnitTypeId = 10, GridX = 7, GridY = 2,
            }).Accepted, Is.True);

            Assert.That(session.Tick().CommandOutcomes[0].Disposition,
                Is.EqualTo(CommandDisposition.Accepted));
            EntityPresentationState deployed = session.State.Presentation.Entities.Single(entity =>
                entity.OwnerPlayerId == 20 && entity.GridX == 7 && entity.GridY == 2);

            Assert.That(deployed.TeamId, Is.EqualTo(SimTeam.Defender));
        }
    }
}
