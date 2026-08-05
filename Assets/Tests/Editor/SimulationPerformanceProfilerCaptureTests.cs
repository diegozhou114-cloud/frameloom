using System;
using System.Reflection;
using NUnit.Framework;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationPerformanceProfilerCaptureTests
    {
        [Test]
        public void ProfilerAttribution_ReconcilesEveryTickAllocationEvent()
        {
            Type capture = Type.GetType(
                "ZWebGLFrameWork.Editor.SimulationPerformanceProfilerCapture, " +
                "ZWebGLFrameWork.Editor");
            Assert.That(capture, Is.Not.Null,
                "The auditable profiler capture entry point must exist.");

            MethodInfo validate = capture.GetMethod("ValidateEventTotal",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(validate, Is.Not.Null);
            Assert.DoesNotThrow(() => validate.Invoke(null, new object[] { 29, 29 }));

            var mismatch = Assert.Throws<TargetInvocationException>(() =>
                validate.Invoke(null, new object[] { 29, 28 }));
            Assert.That(mismatch.InnerException, Is.TypeOf<InvalidOperationException>());

            MethodInfo run = capture.GetMethod("Run",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(run, Is.Not.Null);
        }
    }
}
