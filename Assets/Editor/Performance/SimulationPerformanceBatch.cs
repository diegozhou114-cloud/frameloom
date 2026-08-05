using UnityEngine;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>Command-line entry point for the SimulationCore performance baseline.</summary>
    public static class SimulationPerformanceBatch
    {
        /// <summary>Runs the fixed probe and writes its external evidence report.</summary>
        public static void Run()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            string path = SimulationPerformanceReport.Write(report);
            Debug.Log($"[SimulationPerformanceBatch] Report: {path}");
            Debug.Log("[SimulationPerformanceBatch] Editor data does not represent WebGL IL2CPP performance.");
        }
    }
}
