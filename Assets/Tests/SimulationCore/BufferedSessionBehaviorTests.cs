using System;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class BufferedSessionBehaviorTests
    {
        [Test]
        public void BufferedTick_MatchesOwningTwinForCreateCommandsAndDamage()
        {
            using ISimBattleSession bufferedSession = SessionTestDriver.StartedDeployingBattle();
            using ISimBattleSession owningSession = SessionTestDriver.StartedDeployingBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 8,
                initialEventCapacity: 8, initialCommandCapacity: 8);
            var firstDeploy = new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 7, GridY = 2,
            };
            var secondDeploy = new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 7, GridY = 2,
            };
            BufferedSessionTestSupport.SubmitBoth(
                bufferedSession, owningSession, firstDeploy, secondDeploy);

            BufferedSessionTestSupport.Buffered(bufferedSession).TickInto(output);
            TickResult owning = owningSession.Tick();

            BufferedSessionTestSupport.AssertMatches(owningSession, owning, output);
            Assert.That(output.Presentation.Created, Has.Count.EqualTo(1));
            Assert.That(output.CommandOutcomes[0].Disposition,
                Is.EqualTo(CommandDisposition.Accepted));
        }

        [Test]
        public void BufferedTick_MatchesOwningTwinForMultiChunkWorld()
        {
            InitialEntitySpec[] entities = BufferedSessionTestSupport.MultiChunkEntities();
            byte[] manifest = SessionTestDriver.ManifestBytes();
            using ISimBattleSession bufferedSession =
                new SimBattleSessionFactory().Create(manifest);
            using ISimBattleSession owningSession =
                new SimBattleSessionFactory().Create(manifest);
            Assert.That(bufferedSession.Start(SessionTestDriver.BattleInit(42, entities)).Accepted,
                Is.True);
            Assert.That(owningSession.Start(SessionTestDriver.BattleInit(42, entities)).Accepted,
                Is.True);
            BufferedSessionTestSupport.SubmitBoth(bufferedSession, owningSession,
                EndDeployment(0, 1), EndDeployment(0, 1));
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 130);

            BufferedSessionTestSupport.Buffered(bufferedSession).TickInto(output);
            TickResult owning = owningSession.Tick();

            BufferedSessionTestSupport.AssertMatches(owningSession, owning, output);
            Assert.That(output.Presentation.Entities, Has.Count.EqualTo(130));
        }

        [Test]
        public void BufferedTick_MatchesOwningTwinForDamageDestroyAndTerminalResult()
        {
            using ISimBattleSession bufferedSession = SessionTestDriver.StartedOneHitBattle();
            using ISimBattleSession owningSession = SessionTestDriver.StartedOneHitBattle();
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 4,
                initialEventCapacity: 8, initialCommandCapacity: 2);
            bool ended = false;
            for (int index = 0; index < 20 && !ended; index++)
            {
                BufferedSessionTestSupport.Buffered(bufferedSession).TickInto(output);
                TickResult owning = owningSession.Tick();
                BufferedSessionTestSupport.AssertMatches(owningSession, owning, output);
                ended = output.Code == TickCode.Ended;
            }

            Assert.That(ended, Is.True);
            Assert.That(output.Events, Has.Some.Property("Type")
                .EqualTo(SimBattleEventType.DamageApplied));
            Assert.That(output.Events, Has.Some.Property("Type")
                .EqualTo(SimBattleEventType.EntityDestroyed));
            Assert.That(output.Result.FinalChecksum, Is.EqualTo(output.Checksum));
        }

        [Test]
        public void BufferedTick_StateIsCachedPerVersionAndOwningResultsStayStable()
        {
            using ISimBattleSession session = SessionTestDriver.StartedBattle();
            SimBattleState oldState = session.State;
            EntityPresentationState oldEntity = oldState.Presentation.Entities[0];
            using var output = new SimTickOutputBuffer(initialEntityCapacity: 4);

            BufferedSessionTestSupport.Buffered(session).TickInto(output);
            Assert.That(((SimBattleSession)session).LastChecksum, Is.EqualTo(output.Checksum));
            SimBattleState current = session.State;
            Assert.That(session.State, Is.SameAs(current));
            Assert.That(current, Is.Not.SameAs(oldState));
            Assert.That(oldState.CompletedTick, Is.EqualTo(-1));
            Assert.That(oldState.Presentation.Entities[0].EntityId,
                Is.EqualTo(oldEntity.EntityId));

            TickResult owned = session.Tick();
            int ownedTick = owned.CompletedTick;
            ulong ownedChecksum = owned.Checksum;
            Assert.That(output.Presentation.Entities, Is.Empty);
            BufferedSessionTestSupport.Buffered(session).TickInto(output);
            Assert.That(owned.CompletedTick, Is.EqualTo(ownedTick));
            Assert.That(owned.Checksum, Is.EqualTo(ownedChecksum));
            Assert.That(owned.Presentation.Updated.Count, Is.GreaterThanOrEqualTo(0));
        }

        private static EndDeploymentCmd EndDeployment(int tick, int sequence) =>
            new EndDeploymentCmd { Tick = tick, PlayerId = 0, Sequence = sequence };
    }
}
