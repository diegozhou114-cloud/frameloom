using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class LogicIntCompatibilityTests
    {
        [Test]
        public void LogicInt_Extremes_UsePublicOperatorsAndGoldenVectors()
        {
            LogicInt max = LogicInt.FromRaw(long.MaxValue);
            LogicInt min = LogicInt.FromRaw(long.MinValue);

            Assert.That((max * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((min * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MinValue));
            Assert.That((max / LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue / 2));
            Assert.That(LogicInt.ApplyPercent(max, 5000).RawValue, Is.EqualTo(long.MaxValue / 2));
        }

        [Test]
        public void LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult()
        {
            LogicInt max = LogicInt.FromRaw(long.MaxValue);
            LogicInt min = LogicInt.FromRaw(long.MinValue);

            Assert.That((max * LogicInt.FromInt(2)).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((min / LogicInt.FromInt(-1)).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That(LogicInt.ApplyPercent(min, 20000).RawValue, Is.EqualTo(long.MinValue));
        }
    }
}
