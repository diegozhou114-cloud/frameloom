using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationQueryWorkingSetAllocationTests
    {
        [Test]
        public void SteadyStateTick_QueryWorkingSetsStayWithinAllocationBudget()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            AssertScenario(report, "standard-300", 300, "1109F8052E1B6729");
            AssertScenario(report, "stress-500", 500, "ACE2247CE82E7B4F");
        }

        [Test]
        public void ModeProfile_WaveQueryMatchesDefensivePublicSnapshot()
        {
            var profile = new SimModeProfile(SimCombatMode.Campaign, false,
                0, 0, 100, new[] { 0, 3, 7, 11 });
            int[] exposed = profile.WaveTicks;
            exposed[0] = 99;
            Assert.That(profile.WaveTicks, Is.EqualTo(new[] { 0, 3, 7, 11 }));

            MethodInfo countWaves = typeof(SimModeProfile).GetMethod("CountWavesAfter",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(countWaves, Is.Not.Null, "Missing allocation-free wave query.");
            int[] completedTicks = { -1, 0, 2, 3, 7, 10, 11, 12 };
            foreach (int completedTick in completedTicks)
            {
                int expected = CountGreaterThan(profile.WaveTicks, completedTick);
                int actual = (int)countWaves.Invoke(profile, new object[] { completedTick });
                Assert.That(actual, Is.EqualTo(expected), "completedTick=" + completedTick);
            }
        }

        [Test]
        public void AcceptedSequenceHashWorkingSet_IsClearedAfterChecksumRestoreAndDispose()
        {
            ISimBattleSession session = CreatePerformanceSession();
            try
            {
                List<KeyValuePair<int, int>> workingSet = AcceptedSequenceWorkingSet(session);
                workingSet.Add(new KeyValuePair<int, int>(9, 9));
                InvokeChecksum(session);
                Assert.That(workingSet, Is.Empty, "checksum");

                session.Tick();
                SimSnapshot snapshot = session.CaptureSnapshot();
                workingSet.Add(new KeyValuePair<int, int>(8, 8));
                Assert.That(session.RestoreSnapshot(snapshot).Code,
                    Is.EqualTo(SnapshotRestoreCode.Restored));
                Assert.That(workingSet, Is.Empty, "restore");

                workingSet.Add(new KeyValuePair<int, int>(7, 7));
                session.Dispose();
                Assert.That(workingSet, Is.Empty, "dispose");
            }
            finally
            {
                session.Dispose();
            }
        }

        private static int CountGreaterThan(int[] values, int completedTick)
        {
            int count = 0;
            foreach (int value in values)
                if (value > completedTick) count++;
            return count;
        }

        private static void AssertScenario(SimulationPerformanceReportDocument report,
            string name, int entityCount, string checksum)
        {
            SimulationPerformanceScenario scenario = FindScenario(report, name);
            Assert.That(scenario.initialEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.finalEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.finalChecksum, Is.EqualTo(checksum));
            Assert.That(scenario.managedAllocationEventsPerTick.median,
                Is.LessThanOrEqualTo(13d), name + " median");
            Assert.That(scenario.managedAllocationEventsPerTick.p95,
                Is.LessThanOrEqualTo(13d), name + " p95");
            Assert.That(scenario.managedAllocationEventsPerTick.max,
                Is.LessThanOrEqualTo(13d), name + " max");
        }

        private static SimulationPerformanceScenario FindScenario(
            SimulationPerformanceReportDocument report, string name)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
                if (scenario.name == name) return scenario;
            Assert.Fail("Missing scenario: " + name);
            return null;
        }

        private static List<KeyValuePair<int, int>> AcceptedSequenceWorkingSet(
            ISimBattleSession session)
        {
            FieldInfo field = typeof(SimBattleSession).GetField("_acceptedSequenceHashEntries",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Missing accepted-sequence hash working set.");
            return (List<KeyValuePair<int, int>>)field.GetValue(session);
        }

        private static void InvokeChecksum(ISimBattleSession session)
        {
            MethodInfo checksum = typeof(SimBattleSession).GetMethod("ComputeChecksum",
                BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            Assert.That(checksum, Is.Not.Null);
            checksum.Invoke(session, null);
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
