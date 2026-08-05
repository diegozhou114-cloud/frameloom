using System;
using System.IO;
using System.Reflection;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>Captures auditable GC.Alloc call paths for one steady-state tick.</summary>
    public static class SimulationPerformanceProfilerCapture
    {
        internal const string MarkerName = "PERF005A.SimBattleSession.Tick";

        /// <summary>Captures the fixed 300 and 500 entity scenarios.</summary>
        public static void Run()
        {
            if (!ProfilerDriver.deepProfiling)
                throw new InvalidOperationException(
                    "Deep allocation capture requires the Unity -deepprofiling argument.");
            string directory = Path.Combine(BuildScript.GetEvidenceRoot(), "performance",
                "perf-005a-profiler");
            Directory.CreateDirectory(directory);
            Type probe = typeof(SimulationPerformanceProbe);
            MethodInfo manifestMethod = probe.GetMethod("BenchmarkManifestBytes",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo createSession = probe.GetMethod("CreateSession",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (manifestMethod == null || createSession == null)
                throw new MissingMethodException("Simulation benchmark fixture is unavailable.");
            byte[] manifest = (byte[])manifestMethod.Invoke(null, null);
            Capture(createSession, manifest, 300, directory);
            Capture(createSession, manifest, 500, directory);
            Debug.Log("[SimulationPerformanceProfilerCapture] Evidence: " + directory);
        }

        private static void Capture(MethodInfo createSession, byte[] manifest,
            int entityCount, string directory)
        {
            using (var session = (ISimBattleSession)createSession.Invoke(null,
                new object[] { entityCount, 0, manifest }))
            {
                for (int tick = 0; tick < SimulationPerformanceProbe.WarmupTicks; tick++)
                    session.Tick();
                int measuredEvents = MeasureAllocationEvents(session);
                string rawPath = Path.Combine(directory,
                    "simulation-" + entityCount + ".raw");
                RecordProfile(session, rawPath);
                if (!ProfilerDriver.LoadProfile(rawPath, false))
                    throw new InvalidOperationException("Unable to load GC.Alloc profile: " +
                        rawPath);
                SimulationProfilerEvidence evidence =
                    SimulationPerformanceProfilerEvidence.Read(entityCount, rawPath,
                        measuredEvents);
                ValidateEventTotal(measuredEvents, evidence.profiledAllocationEvents);
                File.WriteAllText(Path.Combine(directory,
                    "simulation-" + entityCount + ".json"),
                    JsonUtility.ToJson(evidence, true));
            }
        }

        private static int MeasureAllocationEvents(ISimBattleSession session)
        {
            SimulationAllocationRecorder recorder =
                SimulationAllocationRecorder.CreateValidated();
            try
            {
                recorder.BeginSample();
                session.Tick();
                return recorder.EndSample();
            }
            finally
            {
                recorder.Close();
            }
        }

        private static void RecordProfile(ISimBattleSession session, string rawPath)
        {
            if (File.Exists(rawPath)) File.Delete(rawPath);
            bool oldEnabled = Profiler.enabled;
            bool oldCallstacks = Profiler.enableAllocationCallstacks;
            bool oldBinaryLog = Profiler.enableBinaryLog;
            string oldLogFile = Profiler.logFile;
            bool oldDeepProfiling = ProfilerDriver.deepProfiling;
            try
            {
                Profiler.enabled = false;
                ProfilerDriver.ClearAllFrames();
                ProfilerDriver.deepProfiling = true;
                Profiler.enableAllocationCallstacks = true;
                Profiler.logFile = rawPath;
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
                Profiler.BeginSample(MarkerName);
                try { session.Tick(); }
                finally { Profiler.EndSample(); }
                Profiler.enabled = false;
                Profiler.logFile = string.Empty;
            }
            finally
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = oldBinaryLog;
                Profiler.logFile = oldLogFile;
                Profiler.enableAllocationCallstacks = oldCallstacks;
                ProfilerDriver.deepProfiling = oldDeepProfiling;
                Profiler.enabled = oldEnabled;
            }
        }

        private static void ValidateEventTotal(int measuredEvents, int profiledEvents)
        {
            if (measuredEvents != profiledEvents)
                throw new InvalidOperationException("GC.Alloc profile did not reconcile: " +
                    measuredEvents + " measured, " + profiledEvents + " profiled.");
        }
    }
}
