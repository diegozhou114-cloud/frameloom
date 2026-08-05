using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;
namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationPerformanceReportTests
    {
        private string _evidenceRoot;
        private string _previousEvidenceRoot;
        [SetUp] public void SetUp()
        {
            _previousEvidenceRoot = Environment.GetEnvironmentVariable("ZWEB_EVIDENCE_ROOT");
            _evidenceRoot = Path.Combine(Path.GetTempPath(), "zweb-perf-report-tests", TestContext.CurrentContext.Test.ID);
            Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", _evidenceRoot);
        }
        [TearDown] public void TearDown()
        {
            Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", _previousEvidenceRoot);
            if (Directory.Exists(_evidenceRoot)) Directory.Delete(_evidenceRoot, true);
        }
        [Test] public void SimulationPerformanceReport_WritesRequiredSchema()
        {
            string path = (string)Invoke("Write", ValidReport());
            string json = File.ReadAllText(path);
            var report = JsonUtility.FromJson<ReportDto>(json);
            Assert.That(report.schemaVersion, Is.EqualTo(2));
            Assert.That(report.generatedAtUtc, Is.Not.Empty); Assert.That(report.environment.unityVersion, Is.Not.Empty); Assert.That(report.environment.operatingSystem, Is.Not.Empty);
            Assert.That(report.configuration.warmupTicks, Is.EqualTo(100)); Assert.That(report.configuration.measuredTicks, Is.EqualTo(600)); Assert.That(report.configuration.repetitions, Is.EqualTo(5));
            Assert.That(report.scenarios, Has.Length.EqualTo(2));
            Assert.That(report.scenarios[0].managedAllocationEventsPerTick.p95, Is.GreaterThanOrEqualTo(0));
            Assert.That(report.scenarios[1].millisecondsPerTick.max, Is.GreaterThanOrEqualTo(0));
            Assert.That(report.scenarios[0].finalChecksum, Is.EqualTo("FFFFFFFFFFFFFFFF")); Assert.That(report.scenarios[0].finalChecksum, Does.Match("^[0-9A-F]{16}$"));
            Assert.That(json, Does.Not.Contain("totalManagedAllocatedBytes")); Assert.That(json, Does.Not.Contain("allocatedBytesPerTick"));
        }
        [Test] public void Report_Contains300And500EntityScenarios()
        {
            object report = ValidReport();
            Assert.DoesNotThrow(() => Invoke("Validate", report));
            Array scenarios = (Array)Get(report, "scenarios");
            Assert.That(Get(scenarios.GetValue(0), "initialEntityCount"), Is.EqualTo(300));
            Assert.That(Get(scenarios.GetValue(1), "initialEntityCount"), Is.EqualTo(500));
        }
        [Test] public void ProbeConfiguration_IsFixedAt100_600_5()
        {
            Type probe = RequiredType("SimulationPerformanceProbe");
            Assert.That(probe.GetField("WarmupTicks").GetRawConstantValue(), Is.EqualTo(100)); Assert.That(probe.GetField("MeasuredTicks").GetRawConstantValue(), Is.EqualTo(600)); Assert.That(probe.GetField("Repetitions").GetRawConstantValue(), Is.EqualTo(5));
        }
        [Test] public void ReportOutputPath_MustBeOutsideRepository()
        {
            string path = (string)Invoke("Write", ValidReport());
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Assert.That(Path.GetFullPath(path), Does.StartWith(Path.GetFullPath(_evidenceRoot)));
            Assert.That(Path.GetFullPath(path), Does.Not.StartWith(projectRoot + Path.DirectorySeparatorChar));
            Environment.SetEnvironmentVariable("ZWEB_EVIDENCE_ROOT", projectRoot);
            Assert.That(Capture("Write", ValidReport()), Is.Not.Null);
        }
        [Test] public void Report_RejectsNaNInfinityAndNegativeNumbers()
        {
            foreach (double value in new[] { double.NaN, double.PositiveInfinity, -1d })
            {
                object report = ValidReport();
                Array scenarios = (Array)Get(report, "scenarios");
                object timing = Get(scenarios.GetValue(0), "millisecondsPerTick");
                Set(timing, "median", value);
                Assert.That(Capture("Validate", report), Is.Not.Null, value.ToString());
            }
            object negative = ValidReport();
            Set(((Array)Get(negative, "scenarios")).GetValue(0), "totalManagedAllocationEvents", -1L);
            Assert.That(Capture("Validate", negative), Is.Not.Null);
            object wrongVersion = ValidReport();
            Set(Get(wrongVersion, "environment"), "unityVersion", "2022.3.61f1");
            Assert.That(Capture("Validate", wrongVersion), Is.Not.Null);
        }
        [Test] public void Report_RejectsMissingScenario()
        {
            object report = ValidReport();
            Array scenarios = (Array)Get(report, "scenarios");
            Array oneScenario = Array.CreateInstance(RequiredType("SimulationPerformanceScenario"), 1);
            oneScenario.SetValue(scenarios.GetValue(0), 0);
            Set(report, "scenarios", oneScenario);
            Assert.That(Capture("Validate", report), Is.Not.Null);
        }
        [Test] public void Report_RejectsInvalidChecksumFormat()
        {
            foreach (string checksum in new[] { "123", "abcdef0123456789", "G000000000000000" })
            {
                object report = ValidReport();
                Set(((Array)Get(report, "scenarios")).GetValue(0), "finalChecksum", checksum);
                Assert.That(Capture("Validate", report), Is.Not.Null, checksum);
            }
        }
        [Test] public void AllocationRecorder_DetectsKnownManagedAllocation()
        {
            MethodInfo method = RequiredType("SimulationPerformanceProbe").GetMethod("CalibrateAllocationRecorder", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            Assert.That((int)method.Invoke(null, null), Is.GreaterThanOrEqualTo(1));
        }
        [Test] public void ProbeFixture_UsesBalancedStableEntitiesAndNoWaves()
        {
            Type probe = RequiredType("SimulationPerformanceProbe");
            byte[] manifestBytes = (byte[])probe.GetMethod("BenchmarkManifestBytes", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            foreach (SimModeProfile mode in DeterministicContentManifest.Parse(manifestBytes).Modes)
            {
                Assert.That(mode.ManualDeployment, Is.False);
                Assert.That(mode.WaveTicks, Is.Empty);
                Assert.That(mode.TimeoutTicks, Is.GreaterThan(700));
            }
            MethodInfo createSession = probe.GetMethod("CreateSession", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (int count in new[] { 300, 500 })
            using (var session = (ISimBattleSession)createSession.Invoke(null, new object[] { count, 0, manifestBytes }))
            {
                int attackers = 0;
                int defenders = 0;
                foreach (EntityPresentationState entity in session.State.Presentation.Entities)
                    if (entity.TeamId == SimTeam.Attacker) attackers++; else defenders++;
                Assert.That(attackers, Is.EqualTo(count / 2)); Assert.That(defenders, Is.EqualTo(count / 2));
                for (int tick = 0; tick < 700; tick++) session.Tick();
                Assert.That(session.State.Presentation.Entities.Count, Is.EqualTo(count));
            }
            object measured = probe.GetMethod("Measure", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { "test-2", 2, manifestBytes });
            using (var session = (ISimBattleSession)createSession.Invoke(null, new object[] { 2, 0, manifestBytes }))
            {
                for (int tick = 0; tick < 700; tick++) session.Tick();
                Assert.That(Get(measured, "finalChecksum"), Is.EqualTo(session.State.Checksum.ToString("X16")));
            }
            Assert.That(Get(measured, "totalManagedAllocationEvents"), Is.GreaterThan(0L));
        }
        private static object ValidReport()
        {
            object report = New("SimulationPerformanceReportDocument");
            Set(report, "schemaVersion", 2);
            Set(report, "generatedAtUtc", "2026-08-03T00:00:00.0000000Z");
            object environment = New("SimulationPerformanceEnvironment");
            Set(environment, "unityVersion", "2022.3.62f3c1");
            Set(environment, "operatingSystem", "test-os");
            Set(environment, "isBatchMode", true);
            Set(report, "environment", environment);
            object configuration = New("SimulationPerformanceConfiguration");
            Set(configuration, "warmupTicks", 100);
            Set(configuration, "measuredTicks", 600);
            Set(configuration, "repetitions", 5);
            Set(report, "configuration", configuration);
            Type scenarioType = RequiredType("SimulationPerformanceScenario");
            Array scenarios = Array.CreateInstance(scenarioType, 2);
            scenarios.SetValue(Scenario("standard-300", 300), 0);
            scenarios.SetValue(Scenario("stress-500", 500), 1);
            Set(report, "scenarios", scenarios);
            return report;
        }
        private static object Scenario(string name, int count)
        {
            object scenario = New("SimulationPerformanceScenario");
            Set(scenario, "name", name);
            Set(scenario, "initialEntityCount", count);
            Set(scenario, "finalEntityCount", count);
            Set(scenario, "totalManagedAllocationEvents", 0L);
            Set(scenario, "managedAllocationEventsPerTick", Statistics(0, 0, 0));
            Set(scenario, "millisecondsPerTick", Statistics(0.1, 0.2, 0.4));
            Set(scenario, "generation0Collections", 0);
            Set(scenario, "generation1Collections", 0);
            Set(scenario, "generation2Collections", 0);
            Set(scenario, "finalChecksum", "FFFFFFFFFFFFFFFF");
            return scenario;
        }
        private static object Statistics(double median, double p95, double max)
        {
            object statistics = New("SimulationPerformanceStatistics");
            Set(statistics, "median", median);
            Set(statistics, "p95", p95);
            Set(statistics, "max", max);
            return statistics;
        }
        private static Type RequiredType(string name)
        {
            Type type = Type.GetType($"ZWebGLFrameWork.Editor.{name}, ZWebGLFrameWork.Editor");
            Assert.That(type, Is.Not.Null, $"Expected editor type {name} to exist.");
            return type;
        }
        private static object New(string name) => Activator.CreateInstance(RequiredType(name));
        private static object Get(object target, string field) => target.GetType().GetField(field).GetValue(target);
        private static void Set(object target, string name, object value) { FieldInfo field = target.GetType().GetField(name); Assert.That(field, Is.Not.Null, $"Expected field {name}."); field.SetValue(target, value); }
        private static object Invoke(string method, object argument) => RequiredType("SimulationPerformanceReport")
            .GetMethod(method, BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { argument });
        private static Exception Capture(string method, object argument)
        {
            try { Invoke(method, argument); return null; }
            catch (TargetInvocationException exception) { return exception.InnerException; }
            catch (Exception exception) { return exception; }
        }
        [Serializable] private sealed class ReportDto { public int schemaVersion; public string generatedAtUtc; public EnvironmentDto environment; public ConfigurationDto configuration; public ScenarioDto[] scenarios; }
        [Serializable] private sealed class EnvironmentDto { public string unityVersion; public string operatingSystem; public bool isBatchMode; }
        [Serializable] private sealed class ConfigurationDto { public int warmupTicks; public int measuredTicks; public int repetitions; }
        [Serializable] private sealed class ScenarioDto { public int initialEntityCount; public StatisticsDto managedAllocationEventsPerTick; public StatisticsDto millisecondsPerTick; public string finalChecksum; }
        [Serializable] private sealed class StatisticsDto { public double median; public double p95; public double max; }
    }
}
