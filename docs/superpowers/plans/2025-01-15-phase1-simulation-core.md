# Phase 1: SimulationCore 基础层 — 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 创建 SimulationCore 纯 C# 目录，包含确定性数学库、无 Unity 依赖的 ECS 核心、外部接口，并通过 Feature Toggle 集成到 AppConfig。

**Architecture:** 从现有 `ECS/` 复制 World/Entity/ComponentPool/SystemBase/SystemGroup，去除 `UnityEngine` 和 `UniTask` 依赖，替换日志为 `ISimLogger` 注入。新增 `LogicInt`（long 存储，Scale=10000）和 `DeterministicRandom`（SplitMix64）作为确定性数学基础。

**Tech Stack:** C# 9.0, .NET Standard 2.1, Unity 2022.3 (宿主), Unity Test Framework (EditMode)

**关键约束:** SimulationCore 目录下所有文件不引用 `UnityEngine`、`Cysharp.Threading.Tasks`、任何 Unity 包。仅依赖 `System` / `System.Collections.Generic`。

---

### Task 1: 创建 SimulationCore 目录和 Assembly Definition

**Files:**
- Create: `Assets/Scripts/SimulationCore/ZWebGLFrameWork.SimulationCore.asmdef`
- Create: `Assets/Scripts/SimulationCore/Core/.gitkeep`
- Create: `Assets/Scripts/SimulationCore/Math/.gitkeep`
- Create: `Assets/Scripts/SimulationCore/Interfaces/.gitkeep`
- Create: `Assets/Scripts/SimulationCore/Orchestrator/.gitkeep`

- [ ] **Step 1: 创建 asmdef 文件（零 UnityEngine 引用）**

`Assets/Scripts/SimulationCore/ZWebGLFrameWork.SimulationCore.asmdef`:
```json
{
    "name": "ZWebGLFrameWork.SimulationCore",
    "rootNamespace": "ZWebGLFrameWork.SimulationCore",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

> **关键：** `"references": []` 和 `"noEngineReferences": true` — 不引用任何 Unity 模块，不引用其他 asmdef。这保证了 SimulationCore 可以独立编译。

- [ ] **Step 2: 创建子目录结构**

```bash
mkdir -p Assets/Scripts/SimulationCore/Core
mkdir -p Assets/Scripts/SimulationCore/Math
mkdir -p Assets/Scripts/SimulationCore/Interfaces
mkdir -p Assets/Scripts/SimulationCore/Orchestrator
```

- [ ] **Step 3: 在 Unity Editor 中打开项目，确认 asmdef 无编译错误**

打开 Unity → Console 无 `ZWebGLFrameWork.SimulationCore` 相关错误。

> 此时 asmdef 是空的（无 .cs 文件），Unity 会警告或自动忽略空 asmdef。无错误即通过。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/SimulationCore/
git commit -m "feat: create SimulationCore asmdef with zero Unity dependencies"
```

---

### Task 2: ISimLogger 和 ISimInitializer 接口

**Files:**
- Create: `Assets/Scripts/SimulationCore/Interfaces/ISimLogger.cs`
- Create: `Assets/Scripts/SimulationCore/Interfaces/ISimInitializer.cs`

- [ ] **Step 1: 创建 ISimLogger.cs**

```csharp
namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真日志接口 — SimulationCore 对宿主环境的唯一日志依赖。
    /// Unity 侧由 DebugLogger 实现（适配 Debug.Log），服务器侧由 ConsoleLogger 实现。
    /// </summary>
    public interface ISimLogger
    {
        void Log(string message);
        void Warn(string message);
        void Error(string message);
    }
}
```

- [ ] **Step 2: 创建 ISimInitializer.cs**

```csharp
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 战斗初始布局描述 — 单个实体的生成参数。
    /// SimulationCore 不依赖 VillageData/BuildingData 等 Unity 侧类型，
    /// 由宿主侧实现此接口完成数据转换。
    /// </summary>
    public readonly struct SimEntitySpawnInfo
    {
        /// <summary>实体原型 ID（决定挂哪些逻辑组件和初始值）</summary>
        public readonly int ArchetypeId;
        /// <summary>初始网格 X 坐标</summary>
        public readonly int GridX;
        /// <summary>初始网格 Y 坐标</summary>
        public readonly int GridY;
        /// <summary>所属队伍（0=Attacker, 1=Defender）</summary>
        public readonly int TeamId;

        public SimEntitySpawnInfo(int archetypeId, int gridX, int gridY, int teamId)
        {
            ArchetypeId = archetypeId;
            GridX = gridX;
            GridY = gridY;
            TeamId = teamId;
        }
    }

    /// <summary>
    /// 战斗初始状态提供者 — 由 DataManager 侧实现。
    /// 返回战斗开始时的所有实体生成信息。
    /// </summary>
    public interface ISimInitializer
    {
        /// <summary>随机种子 — 确保客户端/服务器同 seed</summary>
        int Seed { get; }

        /// <summary>获取所有初始实体的生成信息</summary>
        IEnumerable<SimEntitySpawnInfo> GetInitialEntities();
    }
}
```

- [ ] **Step 3: 确认 Unity 编译通过**

打开 Unity → Console 无错误。

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/SimulationCore/Interfaces/
git commit -m "feat: add ISimLogger and ISimInitializer interfaces"
```

---

### Task 3: LogicInt 确定性整数类型

**Files:**
- Create: `Assets/Scripts/SimulationCore/Math/LogicInt.cs`

- [ ] **Step 1: 创建 LogicInt.cs**

```csharp
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

        /// <summary>从 int 创建（1 → 10000）</summary>
        public static LogicInt FromInt(int value)
        {
            return new LogicInt((long)value * Scale);
        }

        /// <summary>从 float 创建 — ConfigLoader 用此接口转换策划配置</summary>
        public static LogicInt FromFloat(float value)
        {
            long raw = value >= 0
                ? (long)(value * Scale + 0.5f)
                : (long)(value * Scale - 0.5f);
            return new LogicInt(raw);
        }

        /// <summary>从原始 long 值直接构造 — 反序列化用</summary>
        public static LogicInt FromRaw(long raw)
        {
            return new LogicInt(raw);
        }

        // ==================== 算术运算 ====================

        public static LogicInt operator +(LogicInt a, LogicInt b)
        {
            return new LogicInt(a.RawValue + b.RawValue);
        }

        public static LogicInt operator -(LogicInt a, LogicInt b)
        {
            return new LogicInt(a.RawValue - b.RawValue);
        }

        /// <summary>乘以 int 标量（无精度损失）</summary>
        public static LogicInt operator *(LogicInt a, int b)
        {
            return new LogicInt(a.RawValue * b);
        }

        /// <summary>LogicInt × LogicInt → 64 位乘 + 除 Scale</summary>
        public static LogicInt operator *(LogicInt a, LogicInt b)
        {
            return new LogicInt((a.RawValue * b.RawValue) / Scale);
        }

        /// <summary>除以 int 标量</summary>
        public static LogicInt operator /(LogicInt a, int b)
        {
            if (b == 0)
                throw new DivideByZeroException("LogicInt division by zero");
            return new LogicInt(a.RawValue / b);
        }

        /// <summary>LogicInt / LogicInt → 分子×Scale 再除分母（避免截断）</summary>
        public static LogicInt operator /(LogicInt a, LogicInt b)
        {
            if (b.RawValue == 0)
                throw new DivideByZeroException("LogicInt division by zero");
            return new LogicInt((a.RawValue * Scale) / b.RawValue);
        }

        public static LogicInt operator -(LogicInt a)
        {
            return new LogicInt(-a.RawValue);
        }

        // ==================== 比较运算 ====================

        public static bool operator ==(LogicInt a, LogicInt b) => a.RawValue == b.RawValue;
        public static bool operator !=(LogicInt a, LogicInt b) => a.RawValue != b.RawValue;
        public static bool operator <(LogicInt a, LogicInt b)  => a.RawValue < b.RawValue;
        public static bool operator >(LogicInt a, LogicInt b)  => a.RawValue > b.RawValue;
        public static bool operator <=(LogicInt a, LogicInt b) => a.RawValue <= b.RawValue;
        public static bool operator >=(LogicInt a, LogicInt b) => a.RawValue >= b.RawValue;

        // ==================== 百分比 ====================

        /// <summary>
        /// 应用百分比加成。basisPoints 以万分之一为单位（3500 = 35.00%）。
        /// 策划配百分比时，ConfigLoader 将 "35%" → 3500。
        /// </summary>
        public static LogicInt ApplyPercent(LogicInt value, int basisPoints)
        {
            return new LogicInt((value.RawValue * basisPoints) / 10000);
        }

        /// <summary>将 LogicInt 转换为 basis points（用于序列化/比较）</summary>
        public int ToBasisPoints()
        {
            return (int)(RawValue * 10000 / Scale);
        }

        // ==================== 转换 ====================

        public int FloorToInt()
        {
            return (int)(RawValue / Scale);
        }

        public int CeilToInt()
        {
            return (int)((RawValue + Scale - 1) / Scale);
        }

        public int RoundToInt()
        {
            return (int)((RawValue + Half) / Scale);
        }

        public float ToFloat()
        {
            return (float)RawValue / Scale;
        }

        // ==================== 工具方法 ====================

        /// <summary>绝对值</summary>
        public static LogicInt Abs(LogicInt value)
        {
            return new LogicInt(value.RawValue >= 0 ? value.RawValue : -value.RawValue);
        }

        /// <summary>取两者中较大值</summary>
        public static LogicInt Max(LogicInt a, LogicInt b)
        {
            return a.RawValue >= b.RawValue ? a : b;
        }

        /// <summary>取两者中较小值</summary>
        public static LogicInt Min(LogicInt a, LogicInt b)
        {
            return a.RawValue <= b.RawValue ? a : b;
        }

        /// <summary>线性插值：a + (b - a) * t，t ∈ [0, 1]（LogicInt 范围）</summary>
        public static LogicInt Lerp(LogicInt a, LogicInt b, LogicInt t)
        {
            return a + (b - a) * t;
        }

        // ==================== 接口实现 ====================

        public bool Equals(LogicInt other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is LogicInt other && Equals(other);
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(LogicInt other) => RawValue.CompareTo(other.RawValue);
        public override string ToString() => $"{ToFloat():F4}";
    }
}
```

- [ ] **Step 2: 确认 Unity 编译通过**

打开 Unity → Console 无错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/SimulationCore/Math/LogicInt.cs
git commit -m "feat: add LogicInt deterministic fixed-point type"
```

---

### Task 4: LogicInt 单元测试

**Files:**
- Create: `Assets/Tests/SimulationCore/LogicIntTests.cs`
- Create: `Assets/Tests/SimulationCore/ZWebGLFrameWork.SimulationCore.Tests.asmdef`

- [ ] **Step 1: 创建测试 asmdef**

`Assets/Tests/SimulationCore/ZWebGLFrameWork.SimulationCore.Tests.asmdef`:
```json
{
    "name": "ZWebGLFrameWork.SimulationCore.Tests",
    "rootNamespace": "ZWebGLFrameWork.SimulationCore.Tests",
    "references": [
        "ZWebGLFrameWork.SimulationCore",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 2: 创建 LogicIntTests.cs — 构造测试**

```csharp
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
}
```

- [ ] **Step 3: 运行构造测试，确认通过**

在 Unity Editor → Test Runner → EditMode → 运行 `LogicIntConstructionTests`
预期：6/6 PASS

- [ ] **Step 4: 追加算术运算测试**

```csharp
public class LogicIntArithmeticTests
{
    [Test]
    public void Add_TwoPositive_ReturnsSum()
    {
        var a = LogicInt.FromInt(2);   // 20000
        var b = LogicInt.FromInt(3);   // 30000
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
        var a = LogicInt.FromFloat(1.5f);  // 15000
        var result = a * 3;
        Assert.That(result.RawValue, Is.EqualTo(45000L));
        Assert.That(result.FloorToInt(), Is.EqualTo(4));
    }

    [Test]
    public void Multiply_LogicIntByLogicInt_ReturnsCorrect()
    {
        var a = LogicInt.FromFloat(1.5f);   // 15000
        var b = LogicInt.FromFloat(2.0f);   // 20000
        var result = a * b;
        // 1.5 * 2.0 = 3.0 → raw = 30000
        Assert.That(result.FloorToInt(), Is.EqualTo(3));
    }

    [Test]
    public void Multiply_LogicIntByLogicInt_FractionalResult()
    {
        var a = LogicInt.FromFloat(0.5f);   // 5000
        var b = LogicInt.FromFloat(0.5f);   // 5000
        var result = a * b;
        // 0.5 * 0.5 = 0.25 → raw = 2500
        Assert.That(result.RawValue, Is.EqualTo(2500L));
        Assert.That(result.ToFloat(), Is.EqualTo(0.25f).Within(0.001f));
    }

    [Test]
    public void Divide_ByIntScalar_ReturnsCorrect()
    {
        var a = LogicInt.FromInt(10);  // 100000
        var result = a / 3;
        Assert.That(result.FloorToInt(), Is.EqualTo(3));  // 33333 / 10000 = 3
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
```

- [ ] **Step 5: 运行算术测试**

Editor Test Runner → 预期：全部 PASS

- [ ] **Step 6: 追加百分比测试**

```csharp
public class LogicIntPercentTests
{
    [Test]
    public void ApplyPercent_35Percent_ReturnsCorrect()
    {
        var value = LogicInt.FromInt(100);       // raw=1,000,000
        int basisPoints = 3500;                   // 35.00%
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
        var value = LogicInt.FromInt(100);       // raw=1,000,000
        var result = LogicInt.ApplyPercent(value, 1);  // 0.01%
        Assert.That(result.RawValue, Is.EqualTo(100L));
    }
}
```

- [ ] **Step 7: 运行百分比测试**

Editor Test Runner → 预期：全部 PASS

- [ ] **Step 8: 追加溢出/边界测试**

```csharp
public class LogicIntOverflowTests
{
    [Test]
    public void FromFloat_MaxReasonableSpeed_DoesNotOverflow()
    {
        // CoC 级地图：最大速度 10 格/Tick, Scale=10000 → raw=100000
        var speed = LogicInt.FromFloat(10f);
        Assert.That(speed.RawValue, Is.EqualTo(100000L));
    }

    [Test]
    public void Multiply_MaxSpeedTimesMaxTick_DoesNotOverflow()
    {
        // 最坏情形：10 格/Tick × 1000 Tick = 10000 格
        var speed = LogicInt.FromInt(10);            // 100,000
        var ticks = 1000;
        var result = speed * ticks;                  // 100,000,000
        Assert.That(result.RawValue, Is.LessThan(long.MaxValue / 2));
        Assert.That(result.FloorToInt(), Is.EqualTo(10000));
    }

    [Test]
    public void Multiply_MaxGridSizeSquared_DoesNotOverflowLong()
    {
        // 地图 1000×1000 格，距离计算 dx² + dy²
        long rawDx = 1000L * LogicInt.Scale;  // 10,000,000
        long rawDxSq = rawDx * rawDx;          // 100,000,000,000,000
        Assert.That(rawDxSq, Is.LessThan(long.MaxValue));          // ≈ 9.2e18
        Assert.That(rawDxSq, Is.LessThan(long.MaxValue / 4));       // 安全余量
    }

    [Test]
    public void Multiply_QScaledValues_DoubledAndDivided()
    {
        // 模拟 Q31.32 场景下 128 位乘法需求：
        // 两个 Scale=2^32 的值相乘 → 中间值 > long.MaxValue
        // 当前 Scale=10000 不会触发，但验证 Math.BigMul 可用
        long a = (long)int.MaxValue;
        long b = (long)int.MaxValue;
        // 仅验证 BigMul API 存在
        var hi = System.Math.BigMul(a, b, out long lo);
        Assert.That(hi, Is.Not.EqualTo(0)); // 高 64 位非零，说明确实越过了 long 范围
    }
}
```

- [ ] **Step 9: 运行溢出测试**

Editor Test Runner → 预期：全部 PASS

- [ ] **Step 10: 追加比较运算测试**

```csharp
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
```

- [ ] **Step 11: 运行比较测试**

Editor Test Runner → 预期：全部 PASS

- [ ] **Step 12: Commit**

```bash
git add Assets/Tests/SimulationCore/
git commit -m "test: add LogicInt unit tests (construction, arithmetic, percent, overflow, comparison)"
```

---

### Task 5: DeterministicRandom 确定性随机数

**Files:**
- Create: `Assets/Scripts/SimulationCore/Math/DeterministicRandom.cs`

- [ ] **Step 1: 创建 DeterministicRandom.cs**

```csharp
using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性随机数生成器 — SplitMix64 算法。
    /// 给定相同 seed，序列完全可复现，跨平台一致。
    /// </summary>
    public class DeterministicRandom
    {
        private ulong _state;

        /// <param name="seed">初始种子。相同 seed 产生相同序列。</param>
        public DeterministicRandom(int seed)
        {
            _state = (ulong)seed;
            // 避免 seed=0 导致的零序列问题：初始搅动一次
            NextULong();
        }

        /// <summary>返回 [0, int.MaxValue) 范围的随机整数</summary>
        public int Next()
        {
            return (int)(NextULong() & 0x7FFFFFFF);
        }

        /// <summary>返回 [0, max) 范围的随机整数</summary>
        public int Next(int max)
        {
            if (max <= 0) return 0;
            return (int)(NextULong() % (ulong)max);
        }

        /// <summary>返回 [min, max) 范围的随机整数</summary>
        public int Next(int min, int max)
        {
            if (min >= max) return min;
            return min + Next(max - min);
        }

        /// <summary>返回 [0, 1) 范围的概率值（以 basis points 表示，0~9999）</summary>
        public int NextProbability()
        {
            return Next(10000);
        }

        /// <summary>概率判定：basisPoints 概率返回 true（如 3000 = 30%）</summary>
        public bool RollChance(int basisPoints)
        {
            return NextProbability() < basisPoints;
        }

        /// <summary>将内部状态重置为指定 seed</summary>
        public void Reset(int seed)
        {
            _state = (ulong)seed;
            NextULong(); // 初始搅动
        }

        // ==================== SplitMix64 核心 ====================

        private ulong NextULong()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
```

- [ ] **Step 2: 确认 Unity 编译通过**

打开 Unity → Console 无错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/SimulationCore/Math/DeterministicRandom.cs
git commit -m "feat: add DeterministicRandom (SplitMix64)"
```

---

### Task 6: DeterministicRandom 单元测试

**Files:**
- Create: `Assets/Tests/SimulationCore/DeterministicRandomTests.cs`

- [ ] **Step 1: 创建 DeterministicRandomTests.cs**

```csharp
using NUnit.Framework;
using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var rng1 = new DeterministicRandom(42);
            var rng2 = new DeterministicRandom(42);

            var seq1 = new List<int>();
            var seq2 = new List<int>();

            for (int i = 0; i < 100; i++)
            {
                seq1.Add(rng1.Next(100));
                seq2.Add(rng2.Next(100));
            }

            Assert.That(seq1, Is.EqualTo(seq2));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var rng1 = new DeterministicRandom(42);
            var rng2 = new DeterministicRandom(99);

            bool allSame = true;
            for (int i = 0; i < 100; i++)
            {
                if (rng1.Next(100) != rng2.Next(100))
                {
                    allSame = false;
                    break;
                }
            }

            Assert.That(allSame, Is.False,
                "不同 seed 应产生不同序列（极端低概率下可能相同，但不应常态化）");
        }

        [Test]
        public void SequenceOf10000_AcrossThreeSeeds_EachSelfReproducible()
        {
            int[] seeds = { 1, 42, int.MaxValue };
            foreach (int seed in seeds)
            {
                var rng1 = new DeterministicRandom(seed);
                var rng2 = new DeterministicRandom(seed);

                for (int i = 0; i < 10000; i++)
                {
                    Assert.That(rng1.Next(), Is.EqualTo(rng2.Next()),
                        $"seed={seed}, index={i}: 序列不一致");
                }
            }
        }

        [Test]
        public void Next_RangeLimits_Respected()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int val = rng.Next(10);
                Assert.That(val, Is.InRange(0, 9));
            }
        }

        [Test]
        public void Next_MinMax_Respected()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int val = rng.Next(5, 15);
                Assert.That(val, Is.InRange(5, 14));
            }
        }

        [Test]
        public void NextProbability_Range0To9999()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int prob = rng.NextProbability();
                Assert.That(prob, Is.InRange(0, 9999));
            }
        }

        [Test]
        public void RollChance_AlwaysTrue_When100Percent()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(rng.RollChance(10000), Is.True);
            }
        }

        [Test]
        public void RollChance_AlwaysFalse_When0Percent()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(rng.RollChance(0), Is.False);
            }
        }

        [Test]
        public void Reset_SameSeed_ReproducesOriginalSequence()
        {
            var rng = new DeterministicRandom(42);
            var first = new int[10];
            for (int i = 0; i < 10; i++) first[i] = rng.Next();

            rng.Reset(42);
            for (int i = 0; i < 10; i++)
                Assert.That(rng.Next(), Is.EqualTo(first[i]));
        }
    }
}
```

- [ ] **Step 2: 运行全部测试**

Editor Test Runner → 预期：全部 PASS

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/SimulationCore/DeterministicRandomTests.cs
git commit -m "test: add DeterministicRandom unit tests (reproducibility, range, reset)"
```

---

### Task 7: SimEntity 和 ISimComponent

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SimEntity.cs`
- Create: `Assets/Scripts/SimulationCore/Core/ISimComponent.cs`

- [ ] **Step 1: 创建 SimEntity.cs（复制 Entity.cs 结构，改命名空间）**

```csharp
using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真实体标识符 — 轻量值类型，由 SimWorld 分配。
    /// 完全等价于现有 ECS 的 Entity，但属于 SimulationCore 命名空间。
    /// </summary>
    public readonly struct SimEntity : IEquatable<SimEntity>
    {
        public readonly int Id;
        public readonly int Generation;
        public static readonly SimEntity Invalid = new SimEntity(-1, -1);

        internal SimEntity(int id, int generation)
        {
            Id = id;
            Generation = generation;
        }

        public bool IsValid => Id >= 0;

        public bool Equals(SimEntity other) => Id == other.Id && Generation == other.Generation;
        public override bool Equals(object obj) => obj is SimEntity e && Equals(e);
        public override int GetHashCode() => Id;
        public static bool operator ==(SimEntity a, SimEntity b) => a.Equals(b);
        public static bool operator !=(SimEntity a, SimEntity b) => !a.Equals(b);
        public override string ToString() => IsValid ? $"SimEntity({Id}:{Generation})" : "SimEntity(Invalid)";
    }
}
```

- [ ] **Step 2: 创建 ISimComponent.cs（复制 IComponent.cs 结构）**

```csharp
namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>仿真组件标记接口 — 所有逻辑组件必须实现</summary>
    public interface ISimComponent { }

    /// <summary>
    /// 组件池内部接口 — 提供非泛型 Remove/Clear/AccumulateHash，
    /// 消除 SimWorld.DestroyEntity 和 ComputeChecksum 的反射调用。
    /// </summary>
    internal interface ISimComponentPool
    {
        void Remove(SimEntity entity);
        void Clear();
        ulong AccumulateHash(ulong hash);
    }
}
```

- [ ] **Step 3: 确认 Unity 编译通过**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/SimulationCore/Core/SimEntity.cs Assets/Scripts/SimulationCore/Core/ISimComponent.cs
git commit -m "feat: add SimEntity and ISimComponent (zero Unity deps)"
```

---

### Task 8: SimComponentPool (SparseSet)

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SimComponentPool.cs`

> **说明**：这段代码从现有 `World.cs` 中的 `ComponentPool<T>` + `IComponentPool`（在 `IComponent.cs` 中）复制，做最小改动：命名空间、类型名、ISimComponent/ISimComponentPool、AccumulateHash。

- [ ] **Step 1: 创建 SimComponentPool.cs**

```csharp
using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// SparseSet 组件池 — 确定性遍历顺序（= 创建顺序），O(1) 增删查。
    /// 与现有 ComponentPool&lt;T&gt; 完全一致，但 AccumulateHash 用于状态校验。
    /// </summary>
    internal class SimComponentPool<T> : ISimComponentPool where T : struct, ISimComponent
    {
        private int[] _sparse;
        private SimEntity[] _denseEntities;
        private T[] _denseComponents;
        private int _count;

        public int Count => _count;

        public SimComponentPool(int initialCapacity)
        {
            _sparse = new int[initialCapacity];
            for (int i = 0; i < initialCapacity; i++) _sparse[i] = -1;
            _denseEntities = new SimEntity[initialCapacity];
            _denseComponents = new T[initialCapacity];
        }

        public void Add(SimEntity entity, T component)
        {
            if (Has(entity)) return;
            int id = entity.Id;
            if (id >= _sparse.Length) ResizeSparse(id + 1);
            if (_count >= _denseEntities.Length) ResizeDense(_count * 2);
            _sparse[id] = _count;
            _denseEntities[_count] = entity;
            _denseComponents[_count] = component;
            _count++;
        }

        public void Set(SimEntity entity, T component)
        {
            int id = entity.Id;
            if (id < _sparse.Length)
            {
                int idx = _sparse[id];
                if (idx >= 0 && idx < _count && _denseEntities[idx] == entity)
                {
                    _denseComponents[idx] = component;
                    return;
                }
            }
            Add(entity, component);
        }

        public ref T Get(SimEntity entity)
        {
            int idx = _sparse[entity.Id];
            if (idx < 0 || idx >= _count || _denseEntities[idx] != entity)
                throw new InvalidOperationException($"SimEntity {entity} does not have component {typeof(T).Name}");
            return ref _denseComponents[idx];
        }

        public bool TryGet(SimEntity entity, out T component)
        {
            int id = entity.Id;
            if (id < _sparse.Length)
            {
                int idx = _sparse[id];
                if (idx >= 0 && idx < _count && _denseEntities[idx] == entity)
                {
                    component = _denseComponents[idx];
                    return true;
                }
            }
            component = default;
            return false;
        }

        public bool Has(SimEntity entity)
        {
            int id = entity.Id;
            return id < _sparse.Length && _sparse[id] >= 0
                && _sparse[id] < _count && _denseEntities[_sparse[id]] == entity;
        }

        public void Remove(SimEntity entity)
        {
            if (!Has(entity)) return;
            int idx = _sparse[entity.Id];
            int lastIdx = _count - 1;
            if (idx != lastIdx)
            {
                _denseEntities[idx] = _denseEntities[lastIdx];
                _denseComponents[idx] = _denseComponents[lastIdx];
                _sparse[_denseEntities[lastIdx].Id] = idx;
            }
            _sparse[entity.Id] = -1;
            _count--;
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
                _sparse[_denseEntities[i].Id] = -1;
            _count = 0;
        }

        // ==================== 遍历 ====================

        public IEnumerable<SimEntity> GetEntities()
        {
            for (int i = 0; i < _count; i++)
                yield return _denseEntities[i];
        }

        public void ForEach(Action<SimEntity> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i]);
        }

        public delegate void ValAction<TA, TB>(TA arg1, TB arg2);
        public delegate void RefAction<TA, TB>(TA arg1, ref TB arg2);
        public delegate void RefPairAction<TA, TB, TC>(TA arg1, ref TB arg2, ref TC arg3);

        public void ForEach(ValAction<SimEntity, T> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i], _denseComponents[i]);
        }

        public void ForEach(RefAction<SimEntity, T> action)
        {
            for (int i = 0; i < _count; i++)
                action(_denseEntities[i], ref _denseComponents[i]);
        }

        /// <summary>
        /// 联合遍历：遍历本池所有实体，对同时存在于 otherPool 的实体执行 action。
        /// 用于双组件系统（如 MovementSystem 需要 Position + Velocity）。
        /// </summary>
        internal void ForEachRefPair<TOther>(SimComponentPool<TOther> otherPool,
            RefPairAction<SimEntity, T, TOther> action) where TOther : struct, ISimComponent
        {
            for (int i = 0; i < _count; i++)
            {
                var entity = _denseEntities[i];
                int id = entity.Id;
                if (id < otherPool._sparse.Length)
                {
                    int otherIdx = otherPool._sparse[id];
                    if (otherIdx >= 0 && otherIdx < otherPool._count
                        && otherPool._denseEntities[otherIdx] == entity)
                    {
                        action(entity, ref _denseComponents[i],
                            ref otherPool._denseComponents[otherIdx]);
                    }
                }
            }
        }

        // ==================== 状态哈希 ====================

        public ulong AccumulateHash(ulong hash)
        {
            // FNV-1a: 逐字节 XOR + 乘法扩散
            const ulong FNVPrime = 1099511628211UL;
            for (int i = 0; i < _count; i++)
            {
                // 对每个 Entity + Component 做哈希
                hash = HashEntity(hash, _denseEntities[i], _denseComponents[i]);
            }
            return hash;
        }

        private static ulong HashEntity(ulong hash, SimEntity entity, T component)
        {
            // 混合 Entity Id, Generation, 和组件的所有字节
            hash ^= (ulong)entity.Id;
            hash *= FNVPrime;
            hash ^= (ulong)entity.Generation;
            hash *= FNVPrime;

            // 将 struct 转为字节数组参与哈希
            byte[] bytes = StructToBytes(component);
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= FNVPrime;
            }
            return hash;
        }

        private static byte[] StructToBytes(T value)
        {
            // 使用 Buffer.BlockCopy 或 Unsafe.As 将值类型转为字节
            // 对不同大小的 struct 通用处理
            byte[] bytes = new byte[System.Runtime.InteropServices.Marshal.SizeOf<T>()];
            System.IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
            System.Runtime.InteropServices.Marshal.StructureToPtr(value, ptr, false);
            System.Runtime.InteropServices.Marshal.Copy(ptr, bytes, 0, bytes.Length);
            System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr);
            return bytes;
        }

        // ==================== 内部 ====================

        private void ResizeSparse(int newSize)
        {
            int old = _sparse.Length;
            Array.Resize(ref _sparse, newSize);
            for (int i = old; i < newSize; i++) _sparse[i] = -1;
        }

        private void ResizeDense(int newSize)
        {
            Array.Resize(ref _denseEntities, newSize);
            Array.Resize(ref _denseComponents, newSize);
        }
    }
}
```

> **注意**：`StructToBytes` 使用了 `Marshal`，但 `Marshal` 在 IL2CPP (WebGL) 和 .NET Core 上均可用。如果遇到 IL2CPP 兼容问题，后续可替换为 `Unsafe.As` + `Span<byte>`。当前可编译即可。

- [ ] **Step 2: 确认 Unity 编译通过**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/SimulationCore/Core/SimComponentPool.cs
git commit -m "feat: add SimComponentPool (SparseSet with FNV-1a hash)"
```

---

### Task 9: SimWorld

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SimWorld.cs`

> **说明**：从现有 `World.cs` 复制，关键改动：① Entity → SimEntity ② IComponent → ISimComponent ③ IComponentPool → ISimComponentPool ④ 新增 ComputeChecksum/ExportSnapshot/RestoreFrom ⑤ 新增事件回调 ⑥ EntityID 单调递增（不用 Queue）⑦ ISimLogger 注入。

- [ ] **Step 1: 创建 SimWorld.cs**

```csharp
using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 确定性仿真世界 — 管理所有 SimEntity 和 SimComponentPool。
    /// 零 UnityEngine 依赖，可在 .NET Core 服务器上运行。
    /// </summary>
    public class SimWorld
    {
        private readonly ISimLogger _logger;

        public int EntityCount => _entities.Count;

        /// <summary>实体创建/销毁事件 — 表现层同步用</summary>
        public event Action<SimEntity> OnEntityCreated;
        public event Action<SimEntity> OnEntityDestroyed;

        private readonly List<SimEntity> _entities = new List<SimEntity>();
        private int _nextId;
        private int[] _generations;
        private readonly Dictionary<Type, ISimComponentPool> _componentPools = new Dictionary<Type, ISimComponentPool>();

        public SimWorld(int initialCapacity = 256, ISimLogger logger = null)
        {
            _logger = logger;
            _generations = new int[initialCapacity];
        }

        // ==================== Entity 生命周期 ====================

        /// <summary>
        /// 创建实体。ID 单调递增，确保确定性顺序。
        /// （不复用 freeIds — Queue 的出队顺序取决于销毁顺序，破坏确定性。）
        /// </summary>
        public SimEntity CreateEntity()
        {
            int id = _nextId++;
            if (id >= _generations.Length)
                Array.Resize(ref _generations, _generations.Length * 2);
            _generations[id] = _generations[id] + 1;

            var entity = new SimEntity(id, _generations[id]);
            _entities.Add(entity);
            OnEntityCreated?.Invoke(entity);
            return entity;
        }

        public void DestroyEntity(SimEntity entity)
        {
            if (!entity.IsValid) return;
            foreach (var pool in _componentPools.Values)
                pool.Remove(entity);
            _entities.RemoveAll(e => e == entity);
            OnEntityDestroyed?.Invoke(entity);
        }

        // ==================== 组件操作 ====================

        public void AddComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
            => GetPool<T>().Add(entity, component);

        public ref T GetComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => ref GetPool<T>().Get(entity);

        public bool TryGetComponent<T>(SimEntity entity, out T component) where T : struct, ISimComponent
            => GetPool<T>().TryGet(entity, out component);

        public bool HasComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => GetPool<T>().Has(entity);

        public void RemoveComponent<T>(SimEntity entity) where T : struct, ISimComponent
            => GetPool<T>().Remove(entity);

        public void SetComponent<T>(SimEntity entity, T component) where T : struct, ISimComponent
            => GetPool<T>().Set(entity, component);

        // ==================== 遍历 ====================

        public IEnumerable<SimEntity> GetEntitiesWith<T>() where T : struct, ISimComponent
            => GetPool<T>().GetEntities();

        // ==================== 状态校验 ====================

        /// <summary>
        /// 计算当前世界状态的全量哈希 — FNV-1a 遍历所有组件池。
        /// 用于帧同步 Checksum 比对。
        /// </summary>
        public ulong ComputeChecksum()
        {
            ulong hash = 14695981039346656037UL; // FNV-1a offset basis
            hash ^= (ulong)_entities.Count;
            hash *= 1099511628211UL;
            foreach (var kv in _componentPools)
            {
                hash = kv.Value.AccumulateHash(hash);
            }
            return hash;
        }

        // ==================== 快照（预留 FrameSync 回滚） ====================

        /// <summary>导出当前全部组件池的字节快照</summary>
        public SimWorldSnapshot ExportSnapshot()
        {
            return new SimWorldSnapshot(_nextId, (int[])_generations.Clone(), _entities.ToArray());
        }

        /// <summary>从快照恢复</summary>
        public void RestoreFrom(SimWorldSnapshot snapshot)
        {
            ClearPools();
            _nextId = snapshot.NextId;
            _generations = (int[])snapshot.Generations.Clone();
            _entities.Clear();
            _entities.AddRange(snapshot.Entities);
            // 组件数据在 Pool 中，TickSnapshot 需要额外包含组件数据
            // 完整实现见 Phase 5c (FrameSync)
        }

        // ==================== 清理 ====================

        public void ClearPools()
        {
            foreach (var pool in _componentPools.Values)
                pool.Clear();
        }

        // ==================== 内部 ====================

        private SimComponentPool<T> GetPool<T>() where T : struct, ISimComponent
        {
            var type = typeof(T);
            if (!_componentPools.TryGetValue(type, out var pool))
            {
                pool = new SimComponentPool<T>(_generations.Length);
                _componentPools[type] = pool;
            }
            return (SimComponentPool<T>)pool;
        }
    }

    /// <summary>仿真世界快照 — 用于回滚恢复（Phase 5c 完善）</summary>
    public struct SimWorldSnapshot
    {
        public int NextId;
        public int[] Generations;
        public SimEntity[] Entities;

        public SimWorldSnapshot(int nextId, int[] generations, SimEntity[] entities)
        {
            NextId = nextId;
            Generations = generations;
            Entities = entities;
        }
    }
}
```

- [ ] **Step 2: 确认 Unity 编译通过**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/SimulationCore/Core/SimWorld.cs
git commit -m "feat: add SimWorld (deterministic ECS world with checksum)"
```

---

### Task 10: SimWorld 确定性测试

**Files:**
- Create: `Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs`

- [ ] **Step 1: 创建 SimWorldDeterminismTests.cs**

```csharp
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    /// <summary>测试组件 — 纯数据，用于验证确定性</summary>
    public struct TestPosition : ISimComponent
    {
        public int X, Y;
    }

    public struct TestHealth : ISimComponent
    {
        public int Current, Max;
    }

    public class SimWorldDeterminismTests
    {
        private SimWorld CreateWorld()
        {
            return new SimWorld(64);
        }

        [Test]
        public void SameOperations_SameChecksum()
        {
            var w1 = CreateWorld();
            var w2 = CreateWorld();

            // 相同操作序列
            foreach (var world in new[] { w1, w2 })
            {
                var e1 = world.CreateEntity();
                world.AddComponent(e1, new TestPosition { X = 10, Y = 20 });
                world.AddComponent(e1, new TestHealth { Current = 100, Max = 100 });

                var e2 = world.CreateEntity();
                world.AddComponent(e2, new TestPosition { X = 30, Y = 40 });
                world.AddComponent(e2, new TestHealth { Current = 50, Max = 80 });
            }

            Assert.That(w1.ComputeChecksum(), Is.EqualTo(w2.ComputeChecksum()));
        }

        [Test]
        public void DifferentPosition_DifferentChecksum()
        {
            var w1 = CreateWorld();
            var e1 = w1.CreateEntity();
            w1.AddComponent(e1, new TestPosition { X = 10, Y = 20 });
            var cs1 = w1.ComputeChecksum();

            var w2 = CreateWorld();
            var e2 = w2.CreateEntity();
            w2.AddComponent(e2, new TestPosition { X = 99, Y = 99 }); // 不同值
            var cs2 = w2.ComputeChecksum();

            Assert.That(cs1, Is.Not.EqualTo(cs2),
                "不同组件值应产生不同 checksum");
        }

        [Test]
        public void DestroyEntity_ChangesChecksum()
        {
            var w1 = CreateWorld();
            var e = w1.CreateEntity();
            w1.AddComponent(e, new TestHealth { Current = 50, Max = 100 });
            var csBefore = w1.ComputeChecksum();

            w1.DestroyEntity(e);
            var csAfter = w1.ComputeChecksum();

            Assert.That(csBefore, Is.Not.EqualTo(csAfter),
                "销毁实体后 checksum 应改变");
        }

        [Test]
        public void EntityID_MonotonicIncrease()
        {
            var world = CreateWorld();
            var e1 = world.CreateEntity();
            var e2 = world.CreateEntity();
            var e3 = world.CreateEntity();

            Assert.That(e1.Id, Is.EqualTo(0));
            Assert.That(e2.Id, Is.EqualTo(1));
            Assert.That(e3.Id, Is.EqualTo(2));
        }

        [Test]
        public void DestroyThenCreate_NewEntityHasNewId()
        {
            var world = CreateWorld();
            var e1 = world.CreateEntity(); // Id=0
            world.DestroyEntity(e1);
            var e2 = world.CreateEntity();
            // 不复用 Id — 单调递增
            Assert.That(e2.Id, Is.EqualTo(1));
        }

        [Test]
        public void TenRuns_SameOps_SameChecksum()
        {
            for (int run = 0; run < 10; run++)
            {
                var world = CreateWorld();

                for (int i = 0; i < 10; i++)
                {
                    var e = world.CreateEntity();
                    world.AddComponent(e, new TestPosition { X = i, Y = i * 2 });
                    world.AddComponent(e, new TestHealth { Current = 100 - i * 3, Max = 100 });
                }

                // 所有 run 的 checksum 应与第 0 次相同
                ulong expected = 0;
                if (run == 0)
                    expected = world.ComputeChecksum();
                else
                    Assert.That(world.ComputeChecksum(), Is.EqualTo(expected),
                        $"run={run}: checksum 不一致");
            }
        }
    }
}
```

- [ ] **Step 2: 运行全部测试**

Editor Test Runner → 预期：全部 PASS（特别注意 `TenRuns_SameOps_SameChecksum`）

- [ ] **Step 3: Commit**

```bash
git add Assets/Tests/SimulationCore/SimWorldDeterminismTests.cs
git commit -m "test: add SimWorld determinism tests (checksum, entity ID, reproducibility)"
```

---

### Task 11: SimSystemBase 和 SimSystemGroup

**Files:**
- Create: `Assets/Scripts/SimulationCore/Core/SimSystemBase.cs`
- Create: `Assets/Scripts/SimulationCore/Core/SimSystemGroup.cs`

- [ ] **Step 1: 创建 SimSystemBase.cs（去除 UniTask）**

```csharp
namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真系统基类 — 纯同步管线，去掉 UniTask 异步。
    /// Order 决定 UpdateAll 执行顺序。
    /// </summary>
    public abstract class SimSystemBase
    {
        public string SystemName => GetType().Name;
        public bool Enabled { get; set; } = true;
        public int Order { get; set; }

        protected SimWorld World { get; private set; }

        internal void SetWorld(SimWorld world) => World = world;

        /// <summary>初始化（同步）</summary>
        public virtual void Init() { }

        /// <summary>每 Tick 执行</summary>
        public virtual void Update() { }

        /// <summary>清理（同步）</summary>
        public virtual void Cleanup() { }
    }
}
```

- [ ] **Step 2: 创建 SimSystemGroup.cs（去除 UniTask）**

```csharp
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真系统组 — 管理多个 SimSystemBase，按 Order 排序执行。
    /// </summary>
    public class SimSystemGroup
    {
        private readonly List<SimSystemBase> _systems = new List<SimSystemBase>();
        private bool _isSorted = true;

        public void Add(SimSystemBase system)
        {
            _systems.Add(system);
            _isSorted = false;
        }

        public void Add(params SimSystemBase[] systems)
        {
            _systems.AddRange(systems);
            _isSorted = false;
        }

        /// <summary>初始化所有系统</summary>
        public void InitAll(SimWorld world)
        {
            Sort();
            foreach (var sys in _systems)
            {
                if (!sys.Enabled) continue;
                sys.SetWorld(world);
                sys.Init();
            }
        }

        /// <summary>按 Order 更新所有系统</summary>
        public void UpdateAll()
        {
            foreach (var sys in _systems)
            {
                if (sys.Enabled)
                    sys.Update();
            }
        }

        /// <summary>清理所有系统</summary>
        public void CleanupAll()
        {
            foreach (var sys in _systems)
            {
                if (sys.Enabled)
                    sys.Cleanup();
            }
        }

        /// <summary>查找指定类型的系统</summary>
        public T Find<T>() where T : SimSystemBase
        {
            foreach (var sys in _systems)
                if (sys is T t) return t;
            return null;
        }

        private void Sort()
        {
            if (_isSorted) return;
            _systems.Sort((a, b) => a.Order.CompareTo(b.Order));
            _isSorted = true;
        }
    }
}
```

- [ ] **Step 3: 确认 Unity 编译通过**

- [ ] **Step 4: Commit**

```bash
git add Assets/Scripts/SimulationCore/Core/SimSystemBase.cs Assets/Scripts/SimulationCore/Core/SimSystemGroup.cs
git commit -m "feat: add SimSystemBase and SimSystemGroup (sync-only, no UniTask)"
```

---

### Task 12: SimPerfCounters 性能计数器

**Files:**
- Create: `Assets/Scripts/SimulationCore/Orchestrator/SimPerfCounters.cs`

- [ ] **Step 1: 创建 SimPerfCounters.cs**

```csharp
namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 仿真性能计数器 — 记录每 Tick 的关键耗时。
    /// 通过 ISimLogger 输出，用于 WebGL/服务器性能调优。
    /// </summary>
    public class SimPerfCounters
    {
        /// <summary>最近一次 Tick 的 Checksum 计算耗时（毫秒）</summary>
        public float LastChecksumMs { get; private set; }

        /// <summary>最近一次 Tick 的 DrainCommands 耗时（毫秒）</summary>
        public float LastDrainCommandsMs { get; private set; }

        /// <summary>最近一次 Tick 的 Systems Update 耗时（毫秒）</summary>
        public float LastSystemsUpdateMs { get; private set; }

        /// <summary>最近一次 Tick 总耗时（毫秒）</summary>
        public float LastTickTotalMs => LastDrainCommandsMs + LastSystemsUpdateMs + LastChecksumMs;

        /// <summary>当前活跃 Entity 数量</summary>
        public int EntityCount { get; private set; }

        /// <summary>当前 Tick 编号</summary>
        public int TickNumber { get; private set; }

        /// <summary>最近 60 Tick 的平均总耗时（用于性能劣化检测）</summary>
        private float[] _recentTickTimes = new float[60];
        private int _recentIndex;

        /// <summary>记录一次 Tick 的性能数据</summary>
        public void RecordTick(int tickNumber, int entityCount,
            float drainCommandsMs, float systemsUpdateMs, float checksumMs)
        {
            TickNumber = tickNumber;
            EntityCount = entityCount;
            LastDrainCommandsMs = drainCommandsMs;
            LastSystemsUpdateMs = systemsUpdateMs;
            LastChecksumMs = checksumMs;

            _recentTickTimes[_recentIndex % 60] = LastTickTotalMs;
            _recentIndex++;
        }

        /// <summary>获取最近 60 Tick 的平均总耗时</summary>
        public float AverageTickMs()
        {
            int count = _recentIndex < 60 ? _recentIndex : 60;
            float sum = 0f;
            for (int i = 0; i < count; i++)
                sum += _recentTickTimes[i];
            return count > 0 ? sum / count : 0f;
        }

        /// <summary>以单行日志输出最近性能数据</summary>
        public string GetSummary()
        {
            return $"[SimPerf] Tick#{TickNumber} Entities={EntityCount} "
                 + $"Total={LastTickTotalMs:F2}ms "
                 + $"(Drain={LastDrainCommandsMs:F2} Update={LastSystemsUpdateMs:F2} Hash={LastChecksumMs:F2}) "
                 + $"Avg60={AverageTickMs():F2}ms";
        }
    }
}
```

- [ ] **Step 2: 确认 Unity 编译通过**

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/SimulationCore/Orchestrator/SimPerfCounters.cs
git commit -m "feat: add SimPerfCounters for tick-level performance monitoring"
```

---

### Task 13: AppConfig Feature Toggle

**Files:**
- Modify: `Assets/Scripts/Data/AppConfig.cs`

- [ ] **Step 1: 在 AppConfig.cs 的"功能开关"区域添加 UseSimulationCore 字段**

在 `_enableOfflineMode` 和 `_defaultServerHost` 之间插入：

```csharp
/// <summary>是否启用 SimulationCore 战斗引擎：关闭=走老 CombatManager，开启=走新 SimCore</summary>
[Tooltip("开启后战斗走 SimulationCore 确定性仿真引擎")]
[SerializeField] private bool _useSimulationCore = false;
public bool UseSimulationCore => _useSimulationCore;
```

- [ ] **Step 2: 确认 Unity 编译通过，Inspector 中 AppConfig.asset 显示新开关**

打开 Unity → 选中 AppConfig.asset → Inspector 中应在"功能开关"区看到 "Use Simulation Core" 复选框，默认未勾选。

- [ ] **Step 3: Commit**

```bash
git add Assets/Scripts/Data/AppConfig.cs
git commit -m "feat: add UseSimulationCore feature toggle to AppConfig"
```

---

### Task 14: 验证 — dotnet build 独立性检查

**Files:**
- Create: `verify_simcore_independence.csx` (临时脚本)
- 或手动检查

- [ ] **Step 1: 验证 SimulationCore asmdef 没有 UnityEngine 引用**

```bash
# 检查 asmdef 文件内容
cat Assets/Scripts/SimulationCore/ZWebGLFrameWork.SimulationCore.asmdef
```
确认 `"references"` 为空数组，`"noEngineReferences"` 为 `true`。

- [ ] **Step 2: grep 检查所有 .cs 文件无 UnityEngine 引用**

```bash
grep -rn "using UnityEngine" Assets/Scripts/SimulationCore/
```
预期：**无任何输出**（零 UnityEngine 引用）

```bash
grep -rn "using Cysharp" Assets/Scripts/SimulationCore/
```
预期：**无任何输出**（零 UniTask 引用）

- [ ] **Step 3: 在 Unity Editor 中运行全部 SimulationCore 测试**

Unity → Test Runner → EditMode → 运行 `ZWebGLFrameWork.SimulationCore.Tests`
预期：全部 PASS

- [ ] **Step 4: Commit（确认全部文件已提交）**

```bash
git status
git add -A
git commit -m "chore: verify SimulationCore zero-Unity-deps, all tests green"
```

---

## 完成检查清单

完成 Task 14 后确认：

- [ ] `Assets/Scripts/SimulationCore/` 目录结构完整
- [ ] 所有 .cs 文件无 `using UnityEngine`
- [ ] 所有 .cs 文件无 `using Cysharp.Threading.Tasks`
- [ ] asmdef `references` 为空，`noEngineReferences` 为 true
- [ ] Unity Editor 无编译错误
- [ ] 全部 EditMode 测试通过（LogicInt + DeterministicRandom + SimWorld）
- [ ] AppConfig 出现 `Use Simulation Core` 开关，默认 false
- [ ] 现有老代码（ECS/Combat）不受影响
