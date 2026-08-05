using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SessionCommandWorkingSetAllocationTests
    {
        [Test]
        public void SteadyStateTick_CommandExecutionWorkingSetsStayWithinAllocationBudget()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            AssertScenario(report, "standard-300", 300, "1109F8052E1B6729");
            AssertScenario(report, "stress-500", 500, "ACE2247CE82E7B4F");
        }

        [Test]
        public void TickResult_CommandCollectionsRemainOwnedAcrossLaterTicks()
        {
            using (ISimBattleSession session = CreatePerformanceSession())
            {
                EntityPresentationState caster = FindPlayerEntity(session, 0);
                EntityPresentationState target = FindPlayerEntity(session, 1);
                var command = new SkillCmd
                {
                    Tick = session.State.NextTick,
                    PlayerId = caster.OwnerPlayerId,
                    Sequence = 1,
                    CasterId = caster.EntityId,
                    SkillId = 1,
                    TargetX = target.GridX,
                    TargetY = target.GridY,
                };
                Assert.That(session.Submit(command).Accepted, Is.True);

                TickResult retained = session.Tick();
                Assert.That(retained.ExecutedCommands.Count, Is.EqualTo(1));
                Assert.That(retained.CommandOutcomes.Count, Is.EqualTo(1));
                ISimCommand retainedCommand = retained.ExecutedCommands[0];
                CommandExecutionResult retainedOutcome = retained.CommandOutcomes[0];
                for (int tick = 0; tick < 3; tick++) session.Tick();

                Assert.That(retained.ExecutedCommands.Count, Is.EqualTo(1));
                Assert.That(retained.ExecutedCommands[0], Is.EqualTo(retainedCommand));
                Assert.That(retained.CommandOutcomes.Count, Is.EqualTo(1));
                Assert.That(retained.CommandOutcomes[0], Is.EqualTo(retainedOutcome));
                Assert.That(retainedOutcome.Disposition, Is.EqualTo(CommandDisposition.Accepted));
            }
        }

        [Test]
        public void SessionTickWorkingSets_AreClearedAfterTickRestoreAndDispose()
        {
            ISimBattleSession session = CreatePerformanceSession();
            try
            {
                session.Tick();
                AssertWorkingSetsCleared(session);

                SimSnapshot snapshot = session.CaptureSnapshot();
                PopulateWorkingSets(session);
                Assert.That(session.RestoreSnapshot(snapshot).Code,
                    Is.EqualTo(SnapshotRestoreCode.Restored));
                AssertWorkingSetsCleared(session);

                PopulateWorkingSets(session);
                session.Dispose();
                AssertWorkingSetsCleared(session);
            }
            finally
            {
                session.Dispose();
            }
        }

        private static void AssertScenario(SimulationPerformanceReportDocument report,
            string name, int entityCount, string checksum)
        {
            SimulationPerformanceScenario scenario = FindScenario(report, name);
            Assert.That(scenario.initialEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.finalEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.finalChecksum, Is.EqualTo(checksum));
            Assert.That(scenario.managedAllocationEventsPerTick.median,
                Is.LessThanOrEqualTo(17d), name + " median");
            Assert.That(scenario.managedAllocationEventsPerTick.p95,
                Is.LessThanOrEqualTo(17d), name + " p95");
            Assert.That(scenario.managedAllocationEventsPerTick.max,
                Is.LessThanOrEqualTo(17d), name + " max");
        }

        private static SimulationPerformanceScenario FindScenario(
            SimulationPerformanceReportDocument report, string name)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
                if (scenario.name == name) return scenario;
            Assert.Fail("Missing scenario: " + name);
            return null;
        }

        private static EntityPresentationState FindPlayerEntity(
            ISimBattleSession session, int playerId)
        {
            foreach (EntityPresentationState entity in session.State.Presentation.Entities)
                if (entity.OwnerPlayerId == playerId) return entity;
            Assert.Fail("Missing player entity: " + playerId);
            return default;
        }

        private static void PopulateWorkingSets(ISimBattleSession session)
        {
            var command = new EndDeploymentCmd { Tick = 0, PlayerId = 0, Sequence = 1 };
            WorkingList<ISimCommand>(session, "_executedCommands").Add(command);
            WorkingList<ISimCommand>(session, "_acceptedCommands").Add(command);
            WorkingList<CommandExecutionResult>(session, "_commandOutcomes").Add(
                new CommandExecutionResult(command, CommandDisposition.Accepted, -1, -1));
        }

        private static void AssertWorkingSetsCleared(ISimBattleSession session)
        {
            Assert.That(WorkingList<ISimCommand>(session, "_executedCommands"), Is.Empty);
            Assert.That(WorkingList<ISimCommand>(session, "_acceptedCommands"), Is.Empty);
            Assert.That(WorkingList<CommandExecutionResult>(session, "_commandOutcomes"), Is.Empty);
        }

        private static List<T> WorkingList<T>(ISimBattleSession session, string name)
        {
            FieldInfo field = typeof(SimBattleSession).GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing working set: " + name);
            return (List<T>)field.GetValue(session);
        }

        private static ISimBattleSession CreatePerformanceSession()
        {
            Type probe = typeof(SimulationPerformanceProbe);
            byte[] manifest = (byte[])probe.GetMethod("BenchmarkManifestBytes",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            return (ISimBattleSession)probe.GetMethod("CreateSession",
                BindingFlags.NonPublic | BindingFlags.Static).Invoke(null,
                new object[] { SimulationPerformanceProbe.StandardEntityCount, 0, manifest });
        }
    }
}
