using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationPerformanceAllocationHotspotTests
    {
        [Test]
        public void EntityPresentationKey_CollectionsDoNotAllocatePerLookup()
        {
            const int count = 500;
            var keys = new EntityPresentationKey[count];
            var values = new Dictionary<EntityPresentationKey, int>(count);
            var remaining = new HashSet<EntityPresentationKey>();
            for (int index = 0; index < count; index++)
            {
                keys[index] = new EntityPresentationKey(index, index + 1);
                values.Add(keys[index], index);
                remaining.Add(keys[index]);
            }

            Exercise(keys, values, remaining);
            for (int index = 0; index < count; index++) remaining.Add(keys[index]);

            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            int allocationEvents;
            try
            {
                recorder.enabled = false;
                recorder.enabled = true;
                Exercise(keys, values, remaining);
                recorder.enabled = false;
                allocationEvents = recorder.sampleBlockCount;
            }
            finally
            {
                recorder.enabled = false;
            }

            Assert.That(allocationEvents, Is.EqualTo(0),
                "EntityPresentationKey lookup/remove must not allocate after warmup.");
        }

        [Test]
        public void SteadyStateTick_PresentationWorkingSetsStayWithinAllocationBudget()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            AssertBudget(report, "standard-300", 68d);
            AssertBudget(report, "stress-500", 71d);
        }

        [Test]
        public void SteadyStateTick_ChecksumWorkingSetsStayWithinAllocationBudget()
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
            Assert.That(standardMedian <= 32d && standardP95 <= 32d &&
                stressMedian <= 34d && stressP95 <= 34d, Is.True,
                $"allocation events: standard={standardMedian}/{standardP95}, " +
                $"stress={stressMedian}/{stressP95}");
        }

        [Test]
        public void PresentationFrame_PublicConstructorDoesNotShareEmptyCollections()
        {
            var frame = new PresentationFrame(true, 0, 1, 0,
                null, null, null, null);

            Assert.That(frame.Entities, Is.Not.SameAs(frame.Created));
            Assert.That(frame.Created, Is.Not.SameAs(frame.Updated));
        }

        [Test]
        public void TickResult_PublicConstructorRejectsNullArrays()
        {
            Assert.Throws<NullReferenceException>(() => new TickResult(
                TickCode.Advanced, 0, 1, 0, null,
                Array.Empty<CommandExecutionResult>(), Array.Empty<SimBattleEvent>(),
                null, 0, null));
        }

        private static void AssertBudget(SimulationPerformanceReportDocument report,
            string scenarioName, double maximumEvents)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
            {
                if (scenario.name != scenarioName) continue;
                Assert.That(scenario.managedAllocationEventsPerTick.median,
                    Is.LessThanOrEqualTo(maximumEvents), scenarioName + " median");
                Assert.That(scenario.managedAllocationEventsPerTick.p95,
                    Is.LessThanOrEqualTo(maximumEvents), scenarioName + " p95");
                return;
            }
            Assert.Fail("Missing scenario: " + scenarioName);
        }

        private static SimulationPerformanceScenario FindScenario(
            SimulationPerformanceReportDocument report, string scenarioName)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
                if (scenario.name == scenarioName) return scenario;
            Assert.Fail("Missing scenario: " + scenarioName);
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Exercise(EntityPresentationKey[] keys,
            Dictionary<EntityPresentationKey, int> values,
            HashSet<EntityPresentationKey> remaining)
        {
            for (int index = 0; index < keys.Length; index++)
            {
                if (!values.TryGetValue(keys[index], out int value) || value != index)
                    throw new InvalidOperationException("Dictionary lookup changed.");
                if (!remaining.Remove(keys[index]))
                    throw new InvalidOperationException("HashSet removal changed.");
            }
        }
    }
}
