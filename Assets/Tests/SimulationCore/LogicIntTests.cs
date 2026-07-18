using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class LogicIntConstructionTests
    {
        [Test]
        public void FromInt_One_ReturnsScale()
        {
            var result = LogicInt.FromInt(1);
            Assert.That(result.RawValue, Is.EqualTo(10000L));
            Assert.That(result.FloorToInt(), Is.EqualTo(1));
        }

        [Test]
        public void FromInt_NegativeOne_ReturnsNegativeScale()
        {
            var result = LogicInt.FromInt(-1);
            Assert.That(result.RawValue, Is.EqualTo(-10000L));
            Assert.That(result.FloorToInt(), Is.EqualTo(-1));
        }

        [Test]
        public void FromFloat_OnePointFive_ReturnsCorrectRaw()
        {
            var result = LogicInt.FromFloat(1.5f);
            Assert.That(result.RawValue, Is.EqualTo(15000L));
            Assert.That(result.ToFloat(), Is.EqualTo(1.5f).Within(0.001f));
        }

        [Test]
        public void FromFloat_NegativeOnePointFive_ReturnsCorrectRaw()
        {
            var result = LogicInt.FromFloat(-1.5f);
            Assert.That(result.RawValue, Is.EqualTo(-15000L));
        }

        [Test]
        public void FromFloat_Zero_ReturnsZero()
        {
            var result = LogicInt.FromFloat(0f);
            Assert.That(result.RawValue, Is.EqualTo(0L));
        }

        [Test]
        public void FromRaw_RoundTrips()
        {
            var original = LogicInt.FromRaw(1234567L);
            Assert.That(original.RawValue, Is.EqualTo(1234567L));
        }
    }

    public class LogicIntArithmeticTests
    {
        [Test]
        public void Add_TwoPositive_ReturnsSum()
        {
            var a = LogicInt.FromInt(2);
            var b = LogicInt.FromInt(3);
            var result = a + b;
            Assert.That(result.RawValue, Is.EqualTo(50000L));
            Assert.That(result.FloorToInt(), Is.EqualTo(5));
        }

        [Test]
        public void Subtract_PositiveMinusPositive_ReturnsDiff()
        {
            var a = LogicInt.FromInt(5);
            var b = LogicInt.FromInt(2);
            var result = a - b;
            Assert.That(result.FloorToInt(), Is.EqualTo(3));
        }

        [Test]
        public void Multiply_ByIntScalar_ReturnsCorrect()
        {
            var a = LogicInt.FromFloat(1.5f);
            var result = a * 3;
            Assert.That(result.RawValue, Is.EqualTo(45000L));
            Assert.That(result.FloorToInt(), Is.EqualTo(4));
        }

        [Test]
        public void Multiply_LogicIntByLogicInt_ReturnsCorrect()
        {
            var a = LogicInt.FromFloat(1.5f);
            var b = LogicInt.FromFloat(2.0f);
            var result = a * b;
            Assert.That(result.FloorToInt(), Is.EqualTo(3));
        }

        [Test]
        public void Multiply_LogicIntByLogicInt_FractionalResult()
        {
            var a = LogicInt.FromFloat(0.5f);
            var b = LogicInt.FromFloat(0.5f);
            var result = a * b;
            Assert.That(result.RawValue, Is.EqualTo(2500L));
            Assert.That(result.ToFloat(), Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void Divide_ByIntScalar_ReturnsCorrect()
        {
            var a = LogicInt.FromInt(10);
            var result = a / 3;
            Assert.That(result.FloorToInt(), Is.EqualTo(3));
        }

        [Test]
        public void Divide_LogicIntByLogicInt_ReturnsCorrect()
        {
            var a = LogicInt.FromInt(6);
            var b = LogicInt.FromInt(2);
            var result = a / b;
            Assert.That(result.FloorToInt(), Is.EqualTo(3));
        }

        [Test]
        public void Negate_Positive_ReturnsNegative()
        {
            var a = LogicInt.FromInt(5);
            var result = -a;
            Assert.That(result.RawValue, Is.EqualTo(-50000L));
        }
    }

    public class LogicIntPercentTests
    {
        [Test]
        public void ApplyPercent_35Percent_ReturnsCorrect()
        {
            var value = LogicInt.FromInt(100);
            int basisPoints = 3500;
            var result = LogicInt.ApplyPercent(value, basisPoints);
            Assert.That(result.RawValue, Is.EqualTo(350000L));
            Assert.That(result.FloorToInt(), Is.EqualTo(35));
        }

        [Test]
        public void ApplyPercent_100Percent_ReturnsSame()
        {
            var value = LogicInt.FromInt(50);
            var result = LogicInt.ApplyPercent(value, 10000);
            Assert.That(result, Is.EqualTo(value));
        }

        [Test]
        public void ApplyPercent_ZeroPercent_ReturnsZero()
        {
            var value = LogicInt.FromInt(100);
            var result = LogicInt.ApplyPercent(value, 0);
            Assert.That(result.RawValue, Is.EqualTo(0L));
        }

        [Test]
        public void ApplyPercent_OneBasisPoint_ReturnsSmallFraction()
        {
            var value = LogicInt.FromInt(100);
            var result = LogicInt.ApplyPercent(value, 1);
            Assert.That(result.RawValue, Is.EqualTo(100L));
        }
    }

    public class LogicIntOverflowTests
    {
        [Test]
        public void FromFloat_MaxReasonableSpeed_DoesNotOverflow()
        {
            var speed = LogicInt.FromFloat(10f);
            Assert.That(speed.RawValue, Is.EqualTo(100000L));
        }

        [Test]
        public void Multiply_MaxSpeedTimesMaxTick_DoesNotOverflow()
        {
            var speed = LogicInt.FromInt(10);
            var ticks = 1000;
            var result = speed * ticks;
            Assert.That(result.RawValue, Is.LessThan(long.MaxValue / 2));
            Assert.That(result.FloorToInt(), Is.EqualTo(10000));
        }

        [Test]
        public void Multiply_MaxGridSizeSquared_DoesNotOverflowLong()
        {
            long rawDx = 1000L * LogicInt.Scale;
            long rawDxSq = rawDx * rawDx;
            Assert.That(rawDxSq, Is.LessThan(long.MaxValue));
            Assert.That(rawDxSq, Is.LessThan(long.MaxValue / 4));
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
    }

    public class LogicIntComparisonTests
    {
        [Test]
        public void LessThan_ReturnsTrueWhenLeftSmaller()
        {
            var a = LogicInt.FromInt(1);
            var b = LogicInt.FromInt(2);
            Assert.That(a < b, Is.True);
            Assert.That(b < a, Is.False);
        }

        [Test]
        public void Equality_SameRaw_ReturnsTrue()
        {
            var a = LogicInt.FromRaw(12345);
            var b = LogicInt.FromRaw(12345);
            Assert.That(a == b, Is.True);
            Assert.That(a.Equals(b), Is.True);
        }

        [Test]
        public void Abs_Negative_ReturnsPositive()
        {
            var a = LogicInt.FromInt(-5);
            var result = LogicInt.Abs(a);
            Assert.That(result.FloorToInt(), Is.EqualTo(5));
        }

        [Test]
        public void Max_ReturnsLarger()
        {
            var a = LogicInt.FromInt(1);
            var b = LogicInt.FromInt(2);
            Assert.That(LogicInt.Max(a, b), Is.EqualTo(b));
        }

        [Test]
        public void Min_ReturnsSmaller()
        {
            var a = LogicInt.FromInt(1);
            var b = LogicInt.FromInt(2);
            Assert.That(LogicInt.Min(a, b), Is.EqualTo(a));
        }

        [Test]
        public void Lerp_Halfway_ReturnsMidpoint()
        {
            var a = LogicInt.FromInt(0);
            var b = LogicInt.FromInt(10);
            var t = LogicInt.FromFloat(0.5f);
            var result = LogicInt.Lerp(a, b, t);
            Assert.That(result.FloorToInt(), Is.EqualTo(5));
        }
    }
}
