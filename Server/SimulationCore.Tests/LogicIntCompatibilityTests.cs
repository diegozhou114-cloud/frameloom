using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class LogicIntCompatibilityTests
    {
        [Test]
        public void FloorToInt_NegativeFraction_RoundsTowardNegativeInfinity()
        {
            var result = LogicInt.FromFloat(-1.5f);
            Assert.That(result.FloorToInt(), Is.EqualTo(-2));
        }

        [Test]
        public void FloorToInt_OutsideIntRange_Saturates()
        {
            Assert.That(LogicInt.FromRaw(long.MaxValue).FloorToInt(), Is.EqualTo(int.MaxValue));
            Assert.That(LogicInt.FromRaw(long.MinValue).FloorToInt(), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void CeilToInt_FractionsAndExtremes_UseMathematicalCeilingAndSaturation()
        {
            Assert.That(LogicInt.FromFloat(1.5f).CeilToInt(), Is.EqualTo(2));
            Assert.That(LogicInt.FromFloat(-1.5f).CeilToInt(), Is.EqualTo(-1));
            Assert.That(LogicInt.FromRaw(long.MaxValue).CeilToInt(), Is.EqualTo(int.MaxValue));
            Assert.That(LogicInt.FromRaw(long.MinValue).CeilToInt(), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void RoundToInt_FractionsAndExtremes_PreserveRoundingAndSaturate()
        {
            Assert.That(LogicInt.FromFloat(1.5f).RoundToInt(), Is.EqualTo(2));
            Assert.That(LogicInt.FromFloat(-1.5f).RoundToInt(), Is.EqualTo(-1));
            Assert.That(LogicInt.FromRaw(long.MaxValue).RoundToInt(), Is.EqualTo(int.MaxValue));
            Assert.That(LogicInt.FromRaw(long.MinValue).RoundToInt(), Is.EqualTo(int.MinValue));
        }

        [Test]
        public void ToBasisPoints_Extremes_Saturates()
        {
            Assert.That(LogicInt.FromFloat(1.5f).ToBasisPoints(), Is.EqualTo(15000));
            Assert.That(LogicInt.FromRaw(long.MaxValue).ToBasisPoints(), Is.EqualTo(int.MaxValue));
            Assert.That(LogicInt.FromRaw(long.MinValue).ToBasisPoints(), Is.EqualTo(int.MinValue));
        }

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

        [Test]
        public void LogicInt_ExtremeBasicArithmetic_SaturatesInsteadOfWrapping()
        {
            LogicInt max = LogicInt.FromRaw(long.MaxValue);
            LogicInt min = LogicInt.FromRaw(long.MinValue);

            Assert.That((max + LogicInt.FromRaw(1)).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((min - LogicInt.FromRaw(1)).RawValue, Is.EqualTo(long.MinValue));
            Assert.That((max * 2).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((-min).RawValue, Is.EqualTo(long.MaxValue));
            Assert.That((min / -1).RawValue, Is.EqualTo(long.MaxValue));
        }
    }
}
