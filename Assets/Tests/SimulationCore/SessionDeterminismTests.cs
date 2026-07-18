using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionDeterminismTests
    {
        [Test]
        public void DifferentSeed_ChangesSeededInitialStateChecksum()
        {
            using SimBattleSession seed42 = SessionTestDriver.StartedBattle(42);
            using SimBattleSession seed99 = SessionTestDriver.StartedBattle(99);

            Assert.That(seed42.State.Checksum, Is.Not.EqualTo(seed99.State.Checksum));
        }

        [Test]
        public void Checksum_ChangesForTickPhaseAllocatorAndPendingEventDifferences()
        {
            using SimBattleSession beforeTick = SessionTestDriver.StartedDeployingBattle();
            using SimBattleSession afterTick = SessionTestDriver.StartedDeployingBattle();
            afterTick.Tick();
            Assert.That(afterTick.State.Checksum, Is.Not.EqualTo(beforeTick.State.Checksum));

            var cleanWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var advancedAllocator = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity temporary = advancedAllocator.CreateEntity(typeof(SimHealthComponent));
            advancedAllocator.DestroyEntity(temporary);
            Assert.That(advancedAllocator.ComputeChecksum(), Is.Not.EqualTo(cleanWorld.ComputeChecksum()));

            var noEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var pendingEventWorld = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            noEventWorld.CreateEntity(typeof(SimHealthComponent));
            SimEntity attacker = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            SimEntity target = pendingEventWorld.CreateEntity(typeof(SimHealthComponent));
            pendingEventWorld.EnqueueEvent(new SimDamageEvent
            {
                Amount = 5,
                Attacker = attacker,
                Target = target,
            });
            Assert.That(pendingEventWorld.ComputeChecksum(), Is.Not.EqualTo(noEventWorld.ComputeChecksum()));
        }

        [Test]
        public void PendingCommands_DifferentTotalOrderChangesSessionChecksum()
        {
            using SimBattleSession orderA = SessionTestDriver.StartedDeployingBattle();
            using SimBattleSession orderB = SessionTestDriver.StartedDeployingBattle();

            SubmitDeploy(orderA, 1, 3);
            SubmitDeploy(orderA, 2, 4);
            SubmitDeploy(orderB, 1, 4);
            SubmitDeploy(orderB, 2, 3);

            Assert.That(orderA.State.Checksum, Is.Not.EqualTo(orderB.State.Checksum));
        }

        private static void SubmitDeploy(SimBattleSession session, int sequence, int gridX)
        {
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 1,
                PlayerId = 0,
                Sequence = sequence,
                UnitTypeId = 10,
                GridX = gridX,
                GridY = 3,
            }).Accepted, Is.True);
        }
    }
}
