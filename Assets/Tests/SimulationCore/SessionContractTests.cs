using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionContractTests
    {
        [Test]
        public void SessionContract_UsesTypedLifecycleResultsAndImmutableOutputs()
        {
            Assert.That(typeof(ISimBattleSession).GetMethod(nameof(ISimBattleSession.Start)).ReturnType,
                Is.EqualTo(typeof(StartResult)));
            Assert.That(typeof(ISimBattleSession).GetMethod(nameof(ISimBattleSession.RestoreSnapshot)).ReturnType,
                Is.EqualTo(typeof(SnapshotRestoreResult)));
            Assert.That(typeof(TickResult).GetProperty(nameof(TickResult.Presentation)).CanWrite, Is.False);
            Assert.That(typeof(SimBattleState).GetProperty(nameof(SimBattleState.NextTick)).CanWrite, Is.False);
        }

        [Test]
        public void BattleInit_UsesRequestedDefenderAndSeed()
        {
            var entity = new InitialEntitySpec(7, 21, 9, SimTeam.Defender, 1);
            var init = new BattleInit(
                1, 1, "match-001", "session-001", 8675309, 20,
                SimCombatMode.VillageAttack, new byte[32],
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                new[] { entity }, 6);

            Assert.That(init.Seed, Is.EqualTo(8675309));
            Assert.That(init.InitialEntities[0].OwnerPlayerId, Is.EqualTo(1));
            Assert.That(init.InitialEntities[0].GridX, Is.EqualTo(21));
        }

        [Test]
        public void SessionFactory_StartsCanonicalBattleWithInitialFullFrame()
        {
            using ISimBattleSession session =
                new SimBattleSessionFactory().Create(SessionTestDriver.ManifestBytes());

            StartResult started = session.Start(SessionTestDriver.BattleInit(42));

            Assert.That(started.Accepted, Is.True);
            Assert.That(started.InitialFrame.IsFull, Is.True);
            Assert.That(started.InitialFrame.CompletedTick, Is.EqualTo(-1));
            Assert.That(started.InitialFrame.NextTick, Is.EqualTo(0));
            Assert.That(started.InitialFrame.Entities.Count, Is.EqualTo(2));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Running));
        }

        [Test]
        public void SessionTick_RunsCoreSystemsAndReturnsAdvancedResult()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();

            TickResult result = session.Tick();

            Assert.That(result.Code, Is.EqualTo(TickCode.Advanced));
            Assert.That(result.CompletedTick, Is.EqualTo(0));
            Assert.That(result.NextTick, Is.EqualTo(1));
            Assert.That(result.Checksum, Is.Not.EqualTo(0UL));
            Assert.That(session.LastChecksum, Is.EqualTo(result.Checksum));
            Assert.That(result.Presentation.Entities[0].GridX, Is.GreaterThan(2));
        }

        [Test]
        public void CommandsSameTickSamePlayer_AreOrderedBySequenceAndRejectRetrogradeSequence()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            int entityId = session.State.Presentation.Entities[0].EntityId;

            var later = new MoveCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 2,
                EntityId = entityId, TargetX = 5, TargetY = 2,
            };
            var earlier = new MoveCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                EntityId = entityId, TargetX = 4, TargetY = 2,
            };

            Assert.That(session.Submit(later).Disposition, Is.EqualTo(CommandDisposition.Accepted));
            Assert.That(session.Submit(earlier).Disposition, Is.EqualTo(CommandDisposition.NonMonotonicSequence));
            Assert.That(session.Submit(later).Disposition, Is.EqualTo(CommandDisposition.Duplicate));

            TickResult result = session.Tick();
            Assert.That(result.ExecutedCommands.Count, Is.EqualTo(1));
            Assert.That(((MoveCmd)result.ExecutedCommands[0]).Sequence, Is.EqualTo(2));
        }

        [Test]
        public void MoveCommand_FromForeignPlayer_IsRejectedWithoutStateMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            int entityId = session.State.Presentation.Entities[0].EntityId;
            ulong before = session.LastChecksum;

            CommandSubmitResult submitted = session.Submit(new MoveCmd
            {
                Tick = 0, PlayerId = 1, Sequence = 1,
                EntityId = entityId, TargetX = 12, TargetY = 2,
            });

            Assert.That(submitted.Disposition, Is.EqualTo(CommandDisposition.UnauthorizedEntity));
            TickResult result = session.Tick();
            Assert.That(result.Checksum, Is.Not.EqualTo(0UL));

            using SimBattleSession baseline = SessionTestDriver.StartedBattle();
            TickResult baselineResult = baseline.Tick();
            Assert.That(result.Presentation.Entities[0].GridX,
                Is.EqualTo(baselineResult.Presentation.Entities[0].GridX));
            Assert.That(result.Checksum, Is.EqualTo(baselineResult.Checksum));
            Assert.That(before, Is.Not.EqualTo(result.Checksum));
        }

        [Test]
        public void SkillCommand_ValidSkillMutatesTargetHealthDeterministically()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];

            CommandSubmitResult submitted = session.Submit(new SkillCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
                CasterId = caster.EntityId, SkillId = 1,
                TargetX = target.GridX, TargetY = target.GridY,
            });

            Assert.That(submitted.Disposition, Is.EqualTo(CommandDisposition.Accepted));
            TickResult result = session.Tick();
            Assert.That(result.CommandOutcomes[0].Disposition, Is.EqualTo(CommandDisposition.Accepted));
            Assert.That(result.Presentation.Entities[1].Health, Is.EqualTo(target.Health - 30));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Type, Is.EqualTo(SimBattleEventType.DamageApplied));
            Assert.That(result.Events[0].Amount, Is.EqualTo(30));
        }

        [Test]
        public void SkillCommand_UnknownSkillIsRejectedBeforeMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];

            CommandSubmitResult rejected = session.Submit(new SkillCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
                CasterId = caster.EntityId, SkillId = 99,
                TargetX = target.GridX, TargetY = target.GridY,
            });

            Assert.That(rejected.Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            TickResult result = session.Tick();
            Assert.That(result.CommandOutcomes, Is.Empty);
            Assert.That(result.Presentation.Entities[1].Health, Is.EqualTo(target.Health));
        }

        [Test]
        public void DeployCommand_UnknownArchetypeOrOccupiedCellIsRejectedBeforeMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedDeployingBattle();

            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 999, GridX = 4, GridY = 4,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 8, GridY = 2,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            Assert.That(session.Tick().CommandOutcomes, Is.Empty);
            Assert.That(session.State.Presentation.Entities.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExecutedCommands_ContainsOnlyCommandsAcceptedAtExecution()
        {
            using SimBattleSession session = SessionTestDriver.StartedDeployingBattle();

            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 2,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);

            TickResult result = session.Tick();

            Assert.That(result.CommandOutcomes.Count, Is.EqualTo(2));
            Assert.That(result.CommandOutcomes[0].Disposition, Is.EqualTo(CommandDisposition.Accepted));
            Assert.That(result.CommandOutcomes[1].Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            Assert.That(result.ExecutedCommands.Count, Is.EqualTo(1));
            Assert.That(((DeployCmd)result.ExecutedCommands[0]).Sequence, Is.EqualTo(1));
        }
    }
}
