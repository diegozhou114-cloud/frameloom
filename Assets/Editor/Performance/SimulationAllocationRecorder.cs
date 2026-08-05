using System;
using System.Runtime.CompilerServices;
using UnityEngine.Profiling;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>Counts current-thread managed allocation events for benchmark ticks.</summary>
    internal sealed class SimulationAllocationRecorder
    {
        private readonly Recorder _recorder;

        private SimulationAllocationRecorder(Recorder recorder) => _recorder = recorder;

        public static SimulationAllocationRecorder CreateValidated()
        {
            Recorder recorder = CreateRecorder();
            try
            {
                Calibrate(recorder);
                return new SimulationAllocationRecorder(recorder);
            }
            catch
            {
                recorder.enabled = false;
                throw;
            }
        }

        public static int Calibrate()
        {
            Recorder recorder = CreateRecorder();
            try
            {
                return Calibrate(recorder);
            }
            finally
            {
                recorder.enabled = false;
            }
        }

        public void BeginSample()
        {
            _recorder.enabled = false;
            _recorder.enabled = true;
        }

        public int EndSample()
        {
            _recorder.enabled = false;
            return _recorder.sampleBlockCount;
        }

        public void Close() => _recorder.enabled = false;

        private static Recorder CreateRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            if (recorder == null || !recorder.isValid)
                throw new InvalidOperationException(
                    "GC.Alloc recorder is unavailable; managed allocation events cannot be measured.");
            recorder.FilterToCurrentThread();
            return recorder;
        }

        private static int Calibrate(Recorder recorder)
        {
            recorder.enabled = false;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            recorder.enabled = true;
            AllocateCalibrationBuffer();
            recorder.enabled = false;
            int eventCount = recorder.sampleBlockCount;
            if (eventCount < 1)
                throw new InvalidOperationException(
                    "GC.Alloc recorder self-calibration detected no managed allocation events.");
            return eventCount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AllocateCalibrationBuffer()
        {
            var buffer = new byte[4096];
            GC.KeepAlive(buffer);
        }
    }
}
