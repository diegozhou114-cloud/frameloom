using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class SessionCommandTests
    {
        [Test]
        public void SessionSkill_DotNetHostProducesDamageAndEvent()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];

            CommandSubmitResult submitted = session.Submit(new SkillCmd
            {
                Tick = 0,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            });
            TickResult result = session.Tick();

            Assert.That(submitted.Accepted, Is.True);
            Assert.That(result.Presentation.Entities[1].Health, Is.EqualTo(target.Health - 30));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Type, Is.EqualTo(SimBattleEventType.DamageApplied));
        }

        [Test]
        public void InvalidSessionCommand_DotNetHostDoesNotConsumeSequenceOrMutateWorld()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 0,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 99,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 0,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            Assert.That(session.Tick().Presentation.Entities[1].Health, Is.EqualTo(target.Health - 30));
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

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 0,
                PlayerId = 20,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            Assert.That(session.Tick().Events.Count, Is.EqualTo(1));
        }
    }
}
