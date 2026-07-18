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
    }
}
