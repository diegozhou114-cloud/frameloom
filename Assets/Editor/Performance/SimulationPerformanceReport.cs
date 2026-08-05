using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    [Serializable]
    public sealed class SimulationPerformanceStatistics
    {
        public double median;
        public double p95;
        public double max;
    }

    [Serializable]
    public sealed class SimulationPerformanceEnvironment
    {
        public string unityVersion;
        public string operatingSystem;
        public bool isBatchMode;
    }

    [Serializable]
    public sealed class SimulationPerformanceConfiguration
    {
        public int warmupTicks;
        public int measuredTicks;
        public int repetitions;
    }

    [Serializable]
    public sealed class SimulationPerformanceScenario
    {
        public string name;
        public int initialEntityCount;
        public int finalEntityCount;
        public long totalManagedAllocationEvents;
        public SimulationPerformanceStatistics managedAllocationEventsPerTick;
        public SimulationPerformanceStatistics millisecondsPerTick;
        public int generation0Collections;
        public int generation1Collections;
        public int generation2Collections;
        public string finalChecksum;
    }

    [Serializable]
    public sealed class SimulationPerformanceReportDocument
    {
        public int schemaVersion;
        public string generatedAtUtc;
        public SimulationPerformanceEnvironment environment;
        public SimulationPerformanceConfiguration configuration;
        public SimulationPerformanceScenario[] scenarios;
    }

    /// <summary>Validates and writes the SimulationCore Editor performance report.</summary>
    public static class SimulationPerformanceReport
    {
        public const int CurrentSchemaVersion = 2;
        public const string RelativePath = "performance/simulation-baseline.json";

        /// <summary>Writes a validated report below the external evidence root.</summary>
        public static string Write(SimulationPerformanceReportDocument report)
        {
            Validate(report);
            string path = Path.Combine(BuildScript.GetEvidenceRoot(), RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + "\n");
            return path;
        }

        /// <summary>Rejects incomplete reports and invalid numeric values.</summary>
        public static void Validate(SimulationPerformanceReportDocument report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (report.schemaVersion != CurrentSchemaVersion)
                throw new ArgumentException("Unsupported performance report schema.", nameof(report));
            Required(report.generatedAtUtc, "generatedAtUtc");
            if (report.environment == null)
                throw new ArgumentException("environment is required.", nameof(report));
            if (report.environment.unityVersion != "2022.3.62f3c1")
                throw new ArgumentException(
                    "environment.unityVersion must equal 2022.3.62f3c1.", nameof(report));
            Required(report.environment.operatingSystem, "environment.operatingSystem");
            if (report.configuration == null ||
                report.configuration.warmupTicks != SimulationPerformanceProbe.WarmupTicks ||
                report.configuration.measuredTicks != SimulationPerformanceProbe.MeasuredTicks ||
                report.configuration.repetitions != SimulationPerformanceProbe.Repetitions)
                throw new ArgumentException("Performance configuration must be 100/600/5.",
                    nameof(report));
            if (report.scenarios == null || report.scenarios.Length != 2)
                throw new ArgumentException("Exactly two performance scenarios are required.",
                    nameof(report));

            bool hasStandard = false;
            bool hasStress = false;
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
            {
                if (scenario == null)
                    throw new ArgumentException("Performance scenario is required.", nameof(report));
                int expectedCount;
                if (scenario.name == "standard-300")
                {
                    expectedCount = SimulationPerformanceProbe.StandardEntityCount;
                    if (hasStandard) throw Duplicate(scenario.name);
                    hasStandard = true;
                }
                else if (scenario.name == "stress-500")
                {
                    expectedCount = SimulationPerformanceProbe.StressEntityCount;
                    if (hasStress) throw Duplicate(scenario.name);
                    hasStress = true;
                }
                else
                {
                    throw new ArgumentException("Unknown performance scenario.", nameof(report));
                }
                if (scenario.initialEntityCount != expectedCount ||
                    scenario.finalEntityCount != expectedCount)
                    throw new ArgumentException("Entity counts changed during measurement.",
                        nameof(report));
                if (scenario.totalManagedAllocationEvents < 0 ||
                    scenario.generation0Collections < 0 ||
                    scenario.generation1Collections < 0 ||
                    scenario.generation2Collections < 0)
                    throw new ArgumentException("Scenario counters must be non-negative.",
                        nameof(report));
                Statistics(scenario.managedAllocationEventsPerTick,
                    "managedAllocationEventsPerTick");
                Statistics(scenario.millisecondsPerTick, "millisecondsPerTick");
                if (scenario.finalChecksum == null ||
                    !Regex.IsMatch(scenario.finalChecksum, "^[0-9A-F]{16}$"))
                    throw new ArgumentException("Invalid finalChecksum.", nameof(report));
            }
            if (!hasStandard || !hasStress)
                throw new ArgumentException("The 300 and 500 entity scenarios are required.",
                    nameof(report));
        }

        private static void Statistics(SimulationPerformanceStatistics value, string name)
        {
            if (value == null || !Valid(value.median) || !Valid(value.p95) ||
                !Valid(value.max) || value.median > value.p95 || value.p95 > value.max)
                throw new ArgumentException($"Invalid {name} statistics.");
        }

        private static bool Valid(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

        private static void Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"{name} is required.");
        }

        private static ArgumentException Duplicate(string name) =>
            new ArgumentException($"Duplicate performance scenario: {name}.");
    }
}
