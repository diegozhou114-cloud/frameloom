using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class CommandBufferComparerAllocationTests
    {
        [Test]
        public void SteadyStateCommandBuffer_SortComparisonsAllocateOnlyDrainResult()
        {
            var buffer = new CommandBuffer();
            buffer.HashInto(DeterministicHash64.OffsetBasis);
            buffer.Drain(0);

            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            int allocationEvents;
            List<ISimCommand> drained;
            try
            {
                recorder.enabled = false;
                recorder.enabled = true;
                buffer.HashInto(DeterministicHash64.OffsetBasis);
                drained = buffer.Drain(0);
                recorder.enabled = false;
                allocationEvents = recorder.sampleBlockCount;
            }
            finally
            {
                recorder.enabled = false;
            }

            GC.KeepAlive(drained);
            Assert.That(allocationEvents, Is.EqualTo(1),
                $"CommandBuffer allocated {allocationEvents} event(s). ");
        }

        [Test]
        public void SteadyStateTick_CommandComparersStayWithinAllocationBudget()
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
            Assert.That(standardMedian <= 20d && standardP95 <= 20d &&
                stressMedian <= 20d && stressP95 <= 20d, Is.True,
                $"allocation events: standard={standardMedian}/{standardP95}, " +
                $"stress={stressMedian}/{stressP95}");
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
