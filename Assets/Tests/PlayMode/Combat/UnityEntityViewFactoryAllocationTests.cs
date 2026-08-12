using System;
using NUnit.Framework;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class UnityEntityViewFactoryAllocationTests
    {
        [TearDown]
        public void TearDown() => CombatPoolTestSupport.CleanupScene();

        [Test]
        public void SteadyStateFactoryCreateDestroy_DoesNotAllocateAfterWarmup()
        {
            long configuredFirst = MeasureFirstCreate(1);
            long fallbackFirst = MeasureFirstCreate(9001);
            long configured = Measure(1);
            long fallback = Measure(9001);
            TestContext.WriteLine(
                $"configured first create events={configuredFirst}; " +
                $"fallback first create events={fallbackFirst}");
            TestContext.WriteLine(
                $"configured events/600={configured}; fallback events/600={fallback}");
            Assert.That(configured, Is.Zero,
                $"configured steady-state allocated {configured} events/600 cycles");
            Assert.That(fallback, Is.Zero,
                $"fallback steady-state allocated {fallback} events/600 cycles");
        }

        private static long MeasureFirstCreate(int archetypeId)
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            Recorder recorder = CombatPoolTestSupport.ValidatedRecorder();
            string marker = archetypeId == 1
                ? "POOL002.ConfiguredFirstCreate"
                : "POOL002.FallbackFirstCreate";
            recorder.enabled = true;
            Profiler.BeginSample(marker);
            IEntityView view;
            try { view = factory.Create(new EntityPresentationKey(1, 1), archetypeId); }
            finally { Profiler.EndSample(); }
            recorder.enabled = false;
            long events = recorder.sampleBlockCount;
            factory.Destroy(view);
            CombatPoolTestSupport.DisposeIfSupported(factory);
            return events;
        }

        private static long Measure(int archetypeId)
        {
            var factory = new UnityEntityViewFactory(CombatPoolTestSupport.LoadConfig());
            for (int index = 0; index < 100; index++)
            {
                IEntityView view = factory.Create(
                    new EntityPresentationKey(index, 1), archetypeId);
                factory.Destroy(view);
            }
            Recorder recorder = CombatPoolTestSupport.ValidatedRecorder();
            recorder.enabled = true;
            for (int index = 0; index < 600; index++)
            {
                IEntityView view = factory.Create(
                    new EntityPresentationKey(index, 2), archetypeId);
                factory.Destroy(view);
            }
            recorder.enabled = false;
            long events = recorder.sampleBlockCount;
            CombatPoolTestSupport.DisposeIfSupported(factory);
            return events;
        }
    }
}
