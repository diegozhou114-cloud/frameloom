using System;
using System.Diagnostics;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>Measures steady-state SimulationCore ticks in the Unity Editor.</summary>
    public static class SimulationPerformanceProbe
    {
        public const int WarmupTicks = 100;
        public const int MeasuredTicks = 600;
        public const int Repetitions = 5;
        public const int StandardEntityCount = 300;
        public const int StressEntityCount = 500;
        private const int TimeoutTicks = 10000;

        /// <summary>Runs the fixed 300 and 500 entity benchmark scenarios.</summary>
        public static SimulationPerformanceReportDocument Run()
        {
            byte[] manifestBytes = BenchmarkManifestBytes();
            var report = new SimulationPerformanceReportDocument
            {
                schemaVersion = SimulationPerformanceReport.CurrentSchemaVersion,
                generatedAtUtc = DateTime.UtcNow.ToString("O"),
                environment = new SimulationPerformanceEnvironment
                {
                    unityVersion = Application.unityVersion,
                    operatingSystem = SystemInfo.operatingSystem,
                    isBatchMode = Application.isBatchMode,
                },
                configuration = new SimulationPerformanceConfiguration
                {
                    warmupTicks = WarmupTicks,
                    measuredTicks = MeasuredTicks,
                    repetitions = Repetitions,
                },
                scenarios = new[]
                {
                    Measure("standard-300", StandardEntityCount, manifestBytes),
                    Measure("stress-500", StressEntityCount, manifestBytes),
                },
            };
            SimulationPerformanceReport.Validate(report);
            return report;
        }

        private static SimulationPerformanceScenario Measure(
            string name, int entityCount, byte[] manifestBytes)
        {
            int sampleCount = MeasuredTicks * Repetitions;
            var allocationEvents = new long[sampleCount];
            var timestamps = new long[sampleCount];
            var sortedAllocationEvents = new long[sampleCount];
            var sortedTimestamps = new long[sampleCount];
            int generation0 = 0;
            int generation1 = 0;
            int generation2 = 0;
            ulong finalChecksum = 0;
            SimulationAllocationRecorder allocationRecorder =
                SimulationAllocationRecorder.CreateValidated();

            try
            {
                for (int repetition = 0; repetition < Repetitions; repetition++)
                {
                    using (ISimBattleSession session = CreateSession(
                        entityCount, repetition, manifestBytes))
                    {
                        int initialCount = session.State.Presentation.Entities.Count;
                        if (initialCount != entityCount)
                            throw new InvalidOperationException("Benchmark initial entity count changed.");
                        for (int tick = 0; tick < WarmupTicks; tick++) session.Tick();

                        int gc0 = GC.CollectionCount(0);
                        int gc1 = GC.CollectionCount(1);
                        int gc2 = GC.CollectionCount(2);
                        int offset = repetition * MeasuredTicks;
                        for (int tick = 0; tick < MeasuredTicks; tick++)
                        {
                            allocationRecorder.BeginSample();
                            long started = Stopwatch.GetTimestamp();
                            session.Tick();
                            long finished = Stopwatch.GetTimestamp();
                            allocationEvents[offset + tick] = allocationRecorder.EndSample();
                            timestamps[offset + tick] = finished - started;
                        }
                        generation0 += GC.CollectionCount(0) - gc0;
                        generation1 += GC.CollectionCount(1) - gc1;
                        generation2 += GC.CollectionCount(2) - gc2;

                        int finalCount = session.State.Presentation.Entities.Count;
                        if (finalCount != initialCount)
                            throw new InvalidOperationException("Benchmark entity count changed.");
                        ulong checksum = session.State.Checksum;
                        if (repetition > 0 && checksum != finalChecksum)
                            throw new InvalidOperationException("Benchmark checksum is not repeatable.");
                        finalChecksum = checksum;
                    }
                }

                long totalAllocationEvents = 0;
                for (int index = 0; index < allocationEvents.Length; index++)
                {
                    if (allocationEvents[index] < 0 || timestamps[index] < 0)
                        throw new InvalidOperationException("Benchmark sample is negative.");
                    totalAllocationEvents += allocationEvents[index];
                }
                return new SimulationPerformanceScenario
                {
                    name = name,
                    initialEntityCount = entityCount,
                    finalEntityCount = entityCount,
                    totalManagedAllocationEvents = totalAllocationEvents,
                    managedAllocationEventsPerTick = Summarize(
                        allocationEvents, sortedAllocationEvents, 1d),
                    millisecondsPerTick = Summarize(timestamps, sortedTimestamps,
                        1000d / Stopwatch.Frequency),
                    generation0Collections = generation0,
                    generation1Collections = generation1,
                    generation2Collections = generation2,
                    finalChecksum = finalChecksum.ToString("X16"),
                };
            }
            finally
            {
                allocationRecorder.Close();
            }
        }

        private static int CalibrateAllocationRecorder() =>
            SimulationAllocationRecorder.Calibrate();

        private static SimulationPerformanceStatistics Summarize(
            long[] samples, long[] sorted, double scale)
        {
            Array.Copy(samples, sorted, samples.Length);
            Array.Sort(sorted);
            int middle = sorted.Length / 2;
            double median = sorted.Length % 2 == 0
                ? (sorted[middle - 1] + (double)sorted[middle]) / 2d
                : sorted[middle];
            int p95Index = (int)Math.Ceiling(sorted.Length * 0.95d) - 1;
            return new SimulationPerformanceStatistics
            {
                median = median * scale,
                p95 = sorted[p95Index] * scale,
                max = sorted[sorted.Length - 1] * scale,
            };
        }

        private static ISimBattleSession CreateSession(
            int entityCount, int repetition, byte[] manifestBytes)
        {
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
                $"performance-{entityCount}-{repetition}",
                $"performance-session-{entityCount}-{repetition}", 42, 20,
                SimCombatMode.VillageAttack, contentHash, new[]
                {
                    new SimPlayerIdentity(0, "benchmark-attacker"),
                    new SimPlayerIdentity(1, "benchmark-defender"),
                }, entities, 6);
            ISimBattleSession session = new SimBattleSessionFactory().Create(manifestBytes);
            StartResult started = session.Start(init);
            if (started.Accepted) return session;
            session.Dispose();
            throw new InvalidOperationException($"Benchmark session rejected: {started.Code}.");
        }

        private static byte[] BenchmarkManifestBytes()
        {
            var modes = new[]
            {
                Mode(SimCombatMode.VillageAttack), Mode(SimCombatMode.BuilderBase),
                Mode(SimCombatMode.ClanWar), Mode(SimCombatMode.Campaign),
            };
            return new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(10, "BenchmarkAttacker",
                    int.MaxValue, 0, 1, 1000000, 1),
                new SimArchetypeDefinition(20, "BenchmarkDefender",
                    int.MaxValue, 0, 1, 1000000, 1),
            }, modes).ToCanonicalBytes();
        }

        private static SimModeProfile Mode(SimCombatMode mode) =>
            new SimModeProfile(mode, false, 0, 0, TimeoutTicks, Array.Empty<int>());
    }
}
