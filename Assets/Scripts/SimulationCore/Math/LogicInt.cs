using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 逻辑整数 — 确定性定点数类型，替代 float。
    /// 内部用 long 存储，当前精度 Scale=10000（1 世界单位 = 10000 逻辑单位）。
    /// 升级 Q31.32 时只需改 Scale 和运算符内部实现，调用方零改动。
    /// </summary>
    public readonly struct LogicInt : IEquatable<LogicInt>, IComparable<LogicInt>
    {
        internal const long Scale = 10000;
        internal const long Half = Scale / 2;

        public readonly long RawValue;

        private LogicInt(long raw)
        {
            RawValue = raw;
        }

        // ==================== 构造 ====================

        public static LogicInt FromInt(int value)
        {
            return new LogicInt((long)value * Scale);
        }

        public static LogicInt FromFloat(float value)
        {
            long raw = value >= 0
                ? (long)(value * Scale + 0.5f)
                : (long)(value * Scale - 0.5f);
            return new LogicInt(raw);
        }

        public static LogicInt FromRaw(long raw)
        {
            return new LogicInt(raw);
        }

        // ==================== 算术运算 ====================

        public static LogicInt operator +(LogicInt a, LogicInt b)
            => new LogicInt(a.RawValue + b.RawValue);

        public static LogicInt operator -(LogicInt a, LogicInt b)
            => new LogicInt(a.RawValue - b.RawValue);

        public static LogicInt operator *(LogicInt a, int b)
            => new LogicInt(a.RawValue * b);

        public static LogicInt operator *(LogicInt a, LogicInt b)
        {
            // 128-bit multiply → 64-bit divide. Safe against overflow.
            long hi = Math.BigMul(a.RawValue, b.RawValue, out long lo);
            if (hi == 0 && lo >= 0)
                return new LogicInt(lo / Scale);
            // Large values: divide the 128-bit number by Scale
            // (hi*2^64 + lo) / Scale = hi*(2^64/Scale) + (hi*(2^64%Scale) + lo)/Scale
            const ulong TwoPow64DivScale = 1844674407370955UL; // 2^64 / 10000
            const ulong TwoPow64ModScale = 1616UL;              // 2^64 % 10000
            ulong result = (ulong)hi * TwoPow64DivScale;
            ulong remainder = (ulong)hi * TwoPow64ModScale + (ulong)lo;
            result += remainder / Scale;
            return new LogicInt((long)result);
        }

        public static LogicInt operator /(LogicInt a, int b)
        {
            if (b == 0) throw new DivideByZeroException("LogicInt division by zero");
            return new LogicInt(a.RawValue / b);
        }

        public static LogicInt operator /(LogicInt a, LogicInt b)
        {
            if (b.RawValue == 0) throw new DivideByZeroException("LogicInt division by zero");
            return new LogicInt((a.RawValue * Scale) / b.RawValue);
        }

        public static LogicInt operator -(LogicInt a)
            => new LogicInt(-a.RawValue);

        // ==================== 比较运算 ====================

        public static bool operator ==(LogicInt a, LogicInt b) => a.RawValue == b.RawValue;
        public static bool operator !=(LogicInt a, LogicInt b) => a.RawValue != b.RawValue;
        public static bool operator <(LogicInt a, LogicInt b)  => a.RawValue < b.RawValue;
        public static bool operator >(LogicInt a, LogicInt b)  => a.RawValue > b.RawValue;
        public static bool operator <=(LogicInt a, LogicInt b) => a.RawValue <= b.RawValue;
        public static bool operator >=(LogicInt a, LogicInt b) => a.RawValue >= b.RawValue;

        // ==================== 百分比 ====================

        public static LogicInt ApplyPercent(LogicInt value, int basisPoints)
        {
            return new LogicInt((value.RawValue * basisPoints) / 10000);
        }

        public int ToBasisPoints()
        {
            return (int)(RawValue * 10000 / Scale);
        }

        // ==================== 转换 ====================

        public int FloorToInt() => (int)(RawValue / Scale);
        public int CeilToInt()
        {
            if (RawValue >= 0)
                return (int)((RawValue + Scale - 1) / Scale);
            else
                return (int)(RawValue / Scale);
        }
        public int RoundToInt() => (int)((RawValue + Half) / Scale);
        public float ToFloat() => (float)RawValue / Scale;

        // ==================== 工具方法 ====================

        public static LogicInt Abs(LogicInt value)
        {
            if (value.RawValue == long.MinValue)
                return new LogicInt(long.MaxValue); // Clamp to prevent overflow
            return new LogicInt(value.RawValue >= 0 ? value.RawValue : -value.RawValue);
        }

        public static LogicInt Max(LogicInt a, LogicInt b)
            => a.RawValue >= b.RawValue ? a : b;

        public static LogicInt Min(LogicInt a, LogicInt b)
            => a.RawValue <= b.RawValue ? a : b;

        public static LogicInt Lerp(LogicInt a, LogicInt b, LogicInt t)
            => a + (b - a) * t;

        // ==================== 接口实现 ====================

        public bool Equals(LogicInt other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is LogicInt other && Equals(other);
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(LogicInt other) => RawValue.CompareTo(other.RawValue);
        public override string ToString() => $"{ToFloat():F4}";
    }
}
