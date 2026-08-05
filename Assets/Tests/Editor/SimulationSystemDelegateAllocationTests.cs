using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationSystemDelegateAllocationTests
    {
        [Test]
        public void SteadyStateSystems_TraversalDelegatesDoNotAllocateAfterWarmup()
        {
            using (ISimBattleSession session = CreatePerformanceSession())
            {
                for (int tick = 0; tick < SimulationPerformanceProbe.WarmupTicks; tick++)
                    session.Tick();
                var systems = (SimSystemGroup)typeof(SimBattleSession).GetField("_systems",
                    BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
                Recorder recorder = Recorder.Get("GC.Alloc");
                Assert.That(recorder, Is.Not.Null);
                Assert.That(recorder.isValid, Is.True);
                recorder.FilterToCurrentThread();
                int allocationEvents;
                try
                {
                    recorder.enabled = false;
                    recorder.enabled = true;
                    systems.UpdateAll();
                    recorder.enabled = false;
                    allocationEvents = recorder.sampleBlockCount;
                }
                finally
                {
                    recorder.enabled = false;
                }

                Assert.That(allocationEvents, Is.EqualTo(0),
                    $"System traversal delegates allocated {allocationEvents} event(s). ");
            }
        }

        [Test]
        public void SteadyStateTick_SystemDelegateCacheStaysWithinAllocationBudget()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            SimulationPerformanceScenario standard = FindScenario(report, "standard-300");
            SimulationPerformanceScenario stress = FindScenario(report, "stress-500");

            Assert.That(standard.finalChecksum, Is.EqualTo("1109F8052E1B6729"));
            Assert.That(stress.finalChecksum, Is.EqualTo("ACE2247CE82E7B4F"));
            double standardMedian = standard.managedAllocationEventsPerTick.median;
            double standardP95 = standard.managedAllocationEventsPerTick.p95;
            double stressMedian = stress.managedAllocationEventsPerTick.median;
            double stressP95 = stress.managedAllocationEventsPerTick.p95;
            Assert.That(standardMedian <= 22d && standardP95 <= 22d &&
                stressMedian <= 22d && stressP95 <= 22d, Is.True,
                $"allocation events: standard={standardMedian}/{standardP95}, " +
                $"stress={stressMedian}/{stressP95}");
        }

        private static ISimBattleSession CreatePerformanceSession()
        {
            Type probe = typeof(SimulationPerformanceProbe);
            MethodInfo manifestMethod = probe.GetMethod("BenchmarkManifestBytes",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo createSession = probe.GetMethod("CreateSession",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(manifestMethod, Is.Not.Null);
            Assert.That(createSession, Is.Not.Null);
            byte[] manifest = (byte[])manifestMethod.Invoke(null, null);
            return (ISimBattleSession)createSession.Invoke(null,
                new object[] { SimulationPerformanceProbe.StandardEntityCount, 0, manifest });
        }

        private static SimulationPerformanceScenario FindScenario(
            SimulationPerformanceReportDocument report, string name)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
                if (scenario.name == name) return scenario;
            Assert.Fail("Missing scenario: " + name);
            return null;
        }
    }
}
