using System;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.Profiling;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class BufferedSessionAllocationTests
    {
        private const int WarmupTicks = 100;
        private const int MeasuredTicks = 600;

        [Test]
        public void SteadyStateBufferedTick_AllFieldsStayWithinAllocationBudget()
        {
            Recorder recorder = CreateValidatedRecorder();
            try
            {
                ScenarioResult standard = Measure(recorder, 300);
                ScenarioResult stress = Measure(recorder, 500);
                AssertScenario(standard, 300, 0x1109F8052E1B6729UL);
                AssertScenario(stress, 500, 0xACE2247CE82E7B4FUL);
            }
            finally
            {
                recorder.enabled = false;
            }
        }

        private static ScenarioResult Measure(Recorder recorder, int entityCount)
        {
            using ISimBattleSession session = CreateSession(entityCount);
            using var output = new SimTickOutputBuffer(SimTickOutputFields.All,
                entityCount, 16, 8);
            output.EnsureCapacity(entityCount, 16, 8);
            IBufferedSimBattleSession buffered = (IBufferedSimBattleSession)session;
            for (int tick = 0; tick < WarmupTicks; tick++) buffered.TickInto(output);

            var samples = new long[MeasuredTicks];
            for (int tick = 0; tick < MeasuredTicks; tick++)
            {
                recorder.enabled = false;
                recorder.enabled = true;
                buffered.TickInto(output);
                recorder.enabled = false;
                samples[tick] = recorder.sampleBlockCount;
            }

            Array.Sort(samples);
            return new ScenarioResult(entityCount, output.Presentation.Entities.Count,
                output.Checksum, samples[(MeasuredTicks - 1) / 2],
                samples[(int)Math.Ceiling(MeasuredTicks * 0.95d) - 1],
                samples[MeasuredTicks - 1]);
        }

        private static void AssertScenario(
            ScenarioResult scenario, int entityCount, ulong checksum)
        {
            Assert.That(scenario.InitialEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.FinalEntityCount, Is.EqualTo(entityCount));
            Assert.That(scenario.Checksum, Is.EqualTo(checksum));
            Assert.That(scenario.Median, Is.EqualTo(0), scenario.ToString());
            Assert.That(scenario.P95, Is.EqualTo(0), scenario.ToString());
            Assert.That(scenario.Max, Is.EqualTo(0), scenario.ToString());
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

        private static ISimBattleSession CreateSession(int entityCount)
        {
            byte[] manifestBytes = BenchmarkManifestBytes();
            byte[] contentHash = DeterministicContentManifest.Parse(manifestBytes).ContentHash;
            var entities = new InitialEntitySpec[entityCount];
            int attackerCount = entityCount / 2;
            for (int index = 0; index < entityCount; index++)
            {
                bool attacker = index < attackerCount;
                entities[index] = new InitialEntitySpec(attacker ? 10 : 20,
                    index % 32, index / 32,
                    attacker ? SimTeam.Attacker : SimTeam.Defender,
                    attacker ? 0 : 1);
            }
            var init = new BattleInit(1, SimComponentSchema.Version,
                "buffered-performance-" + entityCount,
                "buffered-session-" + entityCount, 42, 20,
                SimCombatMode.VillageAttack, contentHash, new[]
                {
                    new SimPlayerIdentity(0, "benchmark-attacker"),
                    new SimPlayerIdentity(1, "benchmark-defender"),
                }, entities, 6);
            ISimBattleSession session = new SimBattleSessionFactory().Create(manifestBytes);
            StartResult started = session.Start(init);
            if (started.Accepted) return session;
            session.Dispose();
            throw new InvalidOperationException("Buffered benchmark start failed: " + started.Code);
        }

        private static byte[] BenchmarkManifestBytes() =>
            new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "BenchmarkAttacker",
                    int.MaxValue, 0, 1, 1000000, 1),
                new SimArchetypeDefinition(20, "BenchmarkDefender",
                    int.MaxValue, 0, 1, 1000000, 1),
            }, new[]
            {
                Mode(SimCombatMode.VillageAttack), Mode(SimCombatMode.BuilderBase),
                Mode(SimCombatMode.ClanWar), Mode(SimCombatMode.Campaign),
            }).ToCanonicalBytes();

        private static SimModeProfile Mode(SimCombatMode mode) =>
            new SimModeProfile(mode, false, 0, 0, 10000, Array.Empty<int>());

        private readonly struct ScenarioResult
        {
            internal ScenarioResult(int initial, int final, ulong checksum,
                long median, long p95, long max)
            {
                InitialEntityCount = initial;
                FinalEntityCount = final;
                Checksum = checksum;
                Median = median;
                P95 = p95;
                Max = max;
            }

            internal int InitialEntityCount { get; }
            internal int FinalEntityCount { get; }
            internal ulong Checksum { get; }
            internal long Median { get; }
            internal long P95 { get; }
            internal long Max { get; }

            public override string ToString() =>
                $"entities={InitialEntityCount}, events={Median}/{P95}/{Max}";
        }
    }
}
