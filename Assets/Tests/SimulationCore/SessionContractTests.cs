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

            Assert.That(submitted.Disposition, Is.EqualTo(CommandDisposition.Accepted));
            TickResult result = session.Tick();
            Assert.That(result.CommandOutcomes[0].Disposition, Is.EqualTo(CommandDisposition.UnauthorizedEntity));
            Assert.That(result.Checksum, Is.Not.EqualTo(0UL));

            using SimBattleSession baseline = SessionTestDriver.StartedBattle();
            TickResult baselineResult = baseline.Tick();
            Assert.That(result.Presentation.Entities[0].GridX,
                Is.EqualTo(baselineResult.Presentation.Entities[0].GridX));
            Assert.That(result.Checksum, Is.EqualTo(baselineResult.Checksum));
            Assert.That(before, Is.Not.EqualTo(result.Checksum));
        }
    }
}
