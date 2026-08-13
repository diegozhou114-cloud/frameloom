using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SimWorldHashAllocationTests
    {
        private const int SampleCount = 256;

        [Test]
        public void MultiArchetypeWorld_HashIntoDoesNotAllocateAfterWarmup()
        {
            SimWorld world = CreateMultiArchetypeWorld();
            ulong expected = world.HashInto(DeterministicHash64.OffsetBasis);
            Assert.That(expected, Is.EqualTo(0x4437AFC8E171A03CUL));
            world.HashInto(DeterministicHash64.OffsetBasis);
            Recorder recorder = CreateValidatedRecorder();
            var samples = new long[SampleCount];

            try
            {
                for (int sample = 0; sample < SampleCount; sample++)
                {
                    recorder.enabled = false;
                    recorder.enabled = true;
                    ulong actual = world.HashInto(DeterministicHash64.OffsetBasis);
                    recorder.enabled = false;
                    samples[sample] = recorder.sampleBlockCount;
                    Assert.That(actual, Is.EqualTo(expected));
                }
            }
            finally
            {
                recorder.enabled = false;
            }

            Array.Sort(samples);
            long median = samples[(SampleCount - 1) / 2];
            long p95 = samples[(int)Math.Ceiling(SampleCount * 0.95d) - 1];
            long max = samples[SampleCount - 1];
            Assert.That(new[] { median, p95, max }, Is.EqualTo(new long[] { 0, 0, 0 }),
                $"SimWorld.HashInto allocation events median/P95/max={median}/{p95}/{max}");
        }

        private static SimWorld CreateMultiArchetypeWorld()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 16);
            for (int index = 0; index < 4; index++)
            {
                world.CreateEntity(typeof(SimTransformComponent));
                world.CreateEntity(typeof(SimHealthComponent), typeof(SimTeamComponent));
                world.CreateEntity(typeof(SimTransformComponent),
                    typeof(SimHealthComponent), typeof(SimIdentityComponent));
            }
            Assert.That(world.EntityCount, Is.EqualTo(12));
            return world;
        }

        private static Recorder CreateValidatedRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            recorder.enabled = false;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            recorder.enabled = true;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            Assert.That(recorder.sampleBlockCount, Is.GreaterThanOrEqualTo(1));
            return recorder;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateCalibrationBuffer()
        {
            var buffer = new byte[4096];
            GC.KeepAlive(buffer);
        }
    }
}
