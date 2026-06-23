# 战斗帧同步框架设计

## 目标

将现有 ECS 战斗系统改造为**逻辑/表现完全分离**的确定性仿真框架，支持三种运行模式，并为日后服务器验证和实时帧同步预留扩展点。

## 核心原则

- **逻辑层零 Unity 依赖**：SimulationCore 目录可整体复制到 .NET Core 项目编译运行
- **确定性仿真**：相同初始状态 + 相同输入序列 → 逐 Tick 完全相同结果（bit-exact）
- **ECS 组件即能力**：无 ViewComponent 即无渲染，服务器自然无表现
- **渐进改造**：在现有 ECS/Combat 基础上新增 SimulationCore 抽象层，逐步剥离
- **新旧并行**：通过 AppConfig Feature Toggle 切换，改造期间不阻塞其他开发

---

## 一、架构分层

```
┌─ Unity 表现层 ───────────────────────────────────────────┐
│  CombatManager (MonoBehaviour, Update 驱动)               │
│  SimulationSyncBridge (SimWorld → GameObject 同步)         │
│  Interpolator (Tick 间插值渲染)                            │
│  ViewConfig (Entity Archetype → Prefab 映射)               │
│  ViewComponents: UnitView, EffectView                     │
├─ SimulationCore (纯 C#, .NET Standard 2.1) ──────────────┤
│  SimOrchestrator (Deploy→Battle→End 阶段驱动)              │
│  SimWorld, SimEntity, SimComponentPool (SparseSet)         │
│  SimSystemBase → Movement/Attack/Damage/Death              │
│  CommandPipeline: CommandBuffer, CommandRecorder          │
│  Math: LogicInt (long 存储), DeterministicRandom           │
│  AI: IntAStarPathfinding (确定性网格寻路)                  │
│  外部接口: ISimLogger, ISimInitializer                     │
└──────────────────────────────────────────────────────────┘
```

**目录结构**：
```
Assets/Scripts/SimulationCore/
├── Core/         SimWorld, SimEntity, SimComponentPool, SimSystemBase, SystemGroup
├── Math/         LogicInt, DeterministicRandom
├── Systems/      MovementSystem, AttackSystem, DamageSystem, DeathSystem
├── Command/      ISimCommand, CommandBuffer, CommandRecorder, ReplayFile
├── Validation/   StateChecksum, ReplayValidator
├── Orchestrator/ SimOrchestrator
├── AI/           IntAStarPathfinding
├── Interfaces/   ISimLogger, ISimInitializer
└── Perf/         SimPerfCounters（性能计数器）
```

### 外部接口

```csharp
// SimulationCore 对宿主环境的唯一日志依赖
public interface ISimLogger { void Log(string msg); void Warn(string msg); void Error(string msg); }

// 战斗初始布局加载 — 由 DataManager 侧实现
public interface ISimInitializer {
    int Seed { get; }
    IEnumerable<(int archetypeId, int gridX, int gridY, int teamId)> GetInitialEntities(VillageData village);
}
```

---

## 二、确定性数值方案

### 2.1 LogicInt（逻辑整数，替代 float）

```csharp
public readonly struct LogicInt {
    private readonly long _raw;   // 用 long 预留 64 位（日后升级 Q31.32 改运算符即可）
    internal const long Scale = 10000; // 当前：1 世界单位 = 10000 逻辑单位
}
```

- **配置转换**：`LogicInt.FromFloat(1.5f)` → `_raw=15000`，ConfigLoader 加载时一次转换
- **百分比**：`ApplyPercent(value, 3500)` → `value * 3500 / 10000`，配置存整数 basis points
- **升级路径**：日后换 Q31.32 定点数，改 `Scale` 和运算符内部实现，上层逻辑代码零改动
- **乘法溢出保护**：当前 Scale=10000 下 CoC 规模安全；升级后用 `Math.BigMul` 做 128 位中间值

### 2.2 DeterministicRandom

- SplitMix64 算法，给定 seed 即确定序列
- 替代 `UnityEngine.Random` 和 `System.Random`
- 种子来源：`ISimInitializer.Seed`，保证客户端/服务器同 seed

### 2.3 确定性 A* 寻路

- openSet 按 f-cost 排序，f-cost 相同时以 EntityID 为二级键 → 确保相同局面选同一条路
- 网格坐标直接用 `int`（天然确定），不涉及 LogicInt

---

## 三、Command Pipeline（输入→Tick→模拟→校验）

```
Player Input (每渲染帧收集)
       │
       ▼
CommandBuilder → MoveCmd / DeployCmd / SkillCmd
       │
       ▼
CommandBuffer (按 Tick 分组缓冲)
       │
       ▼
SimOrchestrator.Tick(int tick):
  1. DrainCommands(tick)
  2. systems.UpdateAll()
  3. checksum = world.ComputeChecksum()   ← FNV-1a 遍历所有组件池
  4. Record(tick, cmds, checksum)
```

### Tick 参数

| 参数 | 值 | 说明 |
|------|-----|------|
| 逻辑帧率 | 20 Tick/s | 每 Tick 50ms，策略游戏够用 |
| 渲染帧率 | 60 fps | 每 3 渲染帧消耗 1 逻辑 Tick |
| RingBuffer 窗口 | 64 Tick | 覆盖 3.2s，远超 WebGL 典型 RTT |

### 三种运行模式

| 模式 | 命令流向 | Checksum 用途 | 服务器 |
|------|----------|---------------|--------|
| **LocalMode** | 直接消费 | 不录 | 无 |
| **AsyncReplay** | 全程录制→战斗结束上传 | 每 Tick 录，服务器逐 Tick 比对定位作弊 | 重放验证 |
| **FrameSync** | 发服务器 + 本地预测 | 每 Tick 与服务器权威 Hash 比对 | 权威仿真 |

---

## 四、异步回放验证（CoC PvP 攻击）

```
客户端攻击 → 录制 ReplayFile { Version, Seed, InitialLayout, Commands[], Checksums[] }
                                ↓ 上传
服务器重放 → 逐 Tick ApplyCommands → ComputeChecksum → 比对
            ✗ 不匹配 → 拒绝结果（精确到作弊 Tick）
```

### 命令定义（纯数据，序列化友好）

```csharp
MoveCmd   { Tick, PlayerId, EntityId, TargetX, TargetY }
DeployCmd { Tick, PlayerId, UnitTypeId, GridX, GridY }
SkillCmd  { Tick, PlayerId, SkillId, TargetX, TargetY }
```

### ReplayFile 结构（含版本号做向前兼容）

```csharp
ReplayFile {
    int Version;        // 当前 = 1，格式迭代时递增
    int Seed;
    int[] InitialLayout;  // 由 ISimInitializer 序列化
    List<TickRecord> Records;
}
TickRecord { int Tick; ISimCommand[] Cmds; ulong Checksum; }
```

---

## 五、实时帧同步（独立项目，预留扩展）

### 预测 + 回滚模型

```
客户端：
  1. 本地立即执行输入 → 预测状态
  2. RingBuffer 保留最近 64 Tick 快照
  3. 收到服务器权威数据 → 比对 Hash
     ├─ 一致 → 释放快照
     └─ 不一致 → 从上次一致快照恢复，重放至当前
```

### FrameSyncClient（概要）

```csharp
RingBuffer<TickSnapshot> _history;  // 64 Tick 窗口

OnServerData(tick, cmds, authorityHash):
  匹配 → _history.FreeUpTo(tick)
  不匹配 → SimWorld.RestoreFrom(_history[tick-1])
             重放 tick..current
```

### 快照/恢复机制

```csharp
// 快照：复制所有组件池的 dense 数组（值类型，直接 Array.Copy）
TickSnapshot SimWorld.ExportSnapshot()

// 恢复：逐 pool 写回 dense 数组，重建 sparse 索引
void SimWorld.RestoreFrom(TickSnapshot snapshot)
```

> **注意**：FrameSync（含预测回滚）工作量等同于 Phase 1-4 总和，建议作为独立项目，不在本次 Phase 1-4 scope。

---

## 六、SimWorld 与现有 World 的差异

| 项目 | 现有 World | SimWorld |
|------|-----------|----------|
| 日志 | `Debug.Log` | `ISimLogger` 注入 |
| EntityID | `Queue<int> freeIds` | 单调递增（确保确定性顺序） |
| 事件 | `Action<T>` 直接委托 | `SimEventQueue<T>`（可序列化） |
| 组件池 | `ComponentPool<T>` (SparseSet) | 完全复用，结构不变 |
| 快照 | 无 | `ComputeChecksum()` / `ExportSnapshot()` / `RestoreFrom()` |
| 性能监控 | 无 | `SimPerfCounters`（Tick 耗时、Checksum 耗时） |

---

## 七、表现层衔接

### 7.1 SimulationSyncBridge

```csharp
// 监听 SimWorld 变化 → 管理 Unity GameObject
OnEntityCreated(entity):
  prefab = ViewConfig.GetPrefab(entity.ArchetypeId);
  无 prefab → 跳过（纯逻辑实体，服务器无渲染）
  有 prefab → ObjectPool.Instantiate + 绑定 UnitView

OnEntityDestroyed(entity):
  回收 GameObject 进对象池
```

### 7.2 ViewConfig（Archetype → Prefab 映射）

```csharp
// ScriptableObject，策划配置
public class ViewConfig : ScriptableObject {
    [Serializable]
    public struct ArchetypeEntry {
        public int ArchetypeId;        // 对应 SimEntity.ArchetypeId
        public GameObject Prefab;      // Unity 预制体
    }
    public ArchetypeEntry[] Entries;

    public GameObject GetPrefab(int archetypeId);
}
```

### 7.3 Interpolator（Tick 间插值）

```csharp
// 渲染帧计算：float alpha = (当前渲染时间 - 上次Tick时间) / LogicDeltaTime
// pos_render = pos_lastTick + (pos_currentTick - pos_lastTick) * alpha
// 插值对象：位置（LogicInt→Vector3）、朝向
public class Interpolator {
    float _alpha;                    // [0, 1)
    void Update(float alpha) { ... }
}
```

### 7.4 Feature Toggle

```csharp
// AppConfig 新增
public bool UseSimulationCore = false; // false=走老 CombatManager，true=走新 SimCore
```

---

## 八、风险清单

| # | 风险 | 等级 | 影响 | 缓解措施 |
|---|------|------|------|----------|
| R1 | **确定性未被验证** | 🔴 高 | Phase 2 做完发现不 bit-exact，大量返工 | Phase 2 明确要求：≥3 个不同 seed × 100 Tick 自动对比测试 |
| R2 | **Checksum 性能不达标** | 🟡 中 | 每 Tick 遍历全组件池 FNV-1a，百级 Entity 时掉帧 | Phase 2 前做 Benchmark：100/500/1000 Entity 的 Checksum 耗时 |
| R3 | **LogicInt 溢出边界 case** | 🟡 中 | 乘法溢出 → 结果不一致但难以复现 | Phase 1 要求溢出测试用例覆盖（最大值平方、快速移动×大Tick数） |
| R4 | **Phase 4 改造阻塞其他开发** | 🔴 高 | CombatManager 重构时主城/战斗不可用 | Feature Toggle 并行，Toggle 关闭时 100% 走老代码 |
| R5 | **FrameSync 需求反噬设计** | 🟡 中 | Phase 5c 实际需求可能推翻 Phase 1-4 抽象 | Phase 5c 独立项目，Phase 1 完成时先做一次纸面验证 |
| R6 | **A* 非确定性寻路** | 🟡 中 | openSet 同 f-cost 排序不稳定 → 相同输入走不同路径 | Phase 2 要求 EntityID 二级比较键，单元测试覆盖等 cost 场景 |
| R7 | **ReplayFile 格式迭代兼容** | 🟢 低 | Command 结构变更后旧 ReplayFile 无法重放 | Version 字段 + 服务器侧按版本号分支处理 |

---

## 九、改造步骤

### Phase 1: 基础层

| 内容 | 产出 |
|------|------|
| LogicInt（四则运算 + 百分比 + FromFloat/FromInt） | 确定性数学库 |
| DeterministicRandom（SplitMix64） | 可复现随机 |
| ISimLogger / ISimInitializer 接口定义 | 外部依赖契约 |
| SimWorld, SimEntity, SimComponentPool（复制现有 ECS，去 UnityEngine 引用） | 无 Unity 依赖核心 |
| SimSystemBase, SystemGroup | 系统管线基类 |
| SimPerfCounters | 性能埋点 |

**验收标准**：
- ✅ `SimulationCore/` 目录可用 `dotnet build` 编译，无 UnityEngine 引用
- ✅ LogicInt 四则运算 + 百分比精度测试通过
- ✅ DeterministicRandom 10000 次序列，3 个不同 seed 各自可复现
- ✅ LogicInt 溢出测试：最大值平方、极端速度×大 Tick 数不炸
- ✅ Feature Toggle `UseSimulationCore` 加入 AppConfig，默认 `false`

### Phase 2: ECS 系统迁移

| 内容 | 产出 |
|------|------|
| MovementSystem 迁移 float→LogicInt | 确定性移动 |
| AttackSystem 迁移 Random→DeterministicRandom | 确定性攻击 |
| DamageSystem 复用（纯 int，基本不动） | 确定性伤害 |
| DeathSystem 复用（纯 int，基本不动） | 确定性死亡 |
| IntAStarPathfinding（openSet 二级比较键） | 确定性寻路 |
| **Checksum Benchmark** | 性能基准报告 |

**验收标准**：
- ✅ 相同 seed + 相同 input → 10 次独立运行 checksum 完全一致（逐 Tick 比对）
- ✅ 不同 seed → checksum 不同（排除 hash 碰撞，确认真随机性）
- ✅ 100 Tick 模拟耗时 < 预算（待 Benchmark 确定）
- ✅ A* 等 cost 多路径场景 → 10 次运行选同一条路
- ✅ Benchmark 报告：100 / 500 / 1000 Entity 单 Tick Checksum 耗时（ms）

### Phase 3: Command 管线

| 内容 | 产出 |
|------|------|
| ISimCommand 接口 + MoveCmd/DeployCmd/SkillCmd 定义 | 命令数据结构 |
| CommandBuffer（按 Tick 分组缓冲） | 命令队列 |
| CommandRecorder（每 Tick 录制 + 导出 ReplayFile） | 命令录制器 |
| ReplayFile 序列化/反序列化（含 Version 字段） | 重放文件格式 |
| SimOrchestrator（Deploy→Battle→End 阶段驱动，纯 Tick 同步） | 阶段编排器 |
| StateChecksum（FNV-1a 遍历组件池） | 状态哈希 |
| ReplayValidator（导出→重放→比对 checksum） | 验证工具 |

**验收标准**：
- ✅ CommandRecorder 导出 → 重放 → checksum 逐 Tick 一致（往返测试）
- ✅ SimOrchestrator 阶段流转正确（Idle→Deploy→Battle→End→Idle）
- ✅ ReplayFile 不同 Version 可被检测（兼容性 guard）
- ✅ CommandBuffer 正确处理无操作 Tick（空命令列表 + checksum 正常产生）

### Phase 4: 表现层分离

| 内容 | 产出 |
|------|------|
| ViewConfig（Archetype → Prefab 映射 ScriptableObject） | 表现配置 |
| SimulationSyncBridge（SimWorld 事件 → GameObject 管理） | 逻辑↔表现同步 |
| Interpolator（Tick 间位置/朝向插值） | 平滑渲染 |
| CombatManager 重构（Feature Toggle 双路径） | 新旧并行门面 |
| ISimInitializer 实现（DataManager → SimEntity 加载） | 初始布局注入 |

**验收标准**：
- ✅ Feature Toggle `false` → 现有战斗功能不受影响（回归测试）
- ✅ Feature Toggle `true` → LocalMode 完整战斗可运行（部署→战斗→结束）
- ✅ SimulationSyncBridge 实体创建/销毁与 SimWorld 同步，无泄漏
- ✅ Interpolator 渲染平滑，无跳变
- ✅ ViewConfig 未配置 Prefab 的 Archetype → 不创建 GameObject（纯逻辑实体）

### Phase 5a: LocalMode 交付

| 内容 | 产出 |
|------|------|
| LocalMode 完整战斗流程验证 | 可用的离线战斗 |
| 性能 Profile（WebGL 平台） | 性能数据 |

**验收标准**：
- ✅ WebGL 平台 60fps 无掉帧（100 Entity 规模）

### Phase 5b: AsyncReplay（独立交付）

| 内容 | 产出 |
|------|------|
| ReplayFile 上传接口定义（HTTP/gRPC 契约，不实现服务端） | API 契约 |
| 服务器重放验证流程文档 | 对接指南 |

### Phase 5c: FrameSync（独立项目，不在本次 scope）

| 内容 | 产出 |
|------|------|
| FrameSyncClient | 预测+回滚客户端 |
| 网络协议设计 | 实时同步协议 |
| 服务器权威仿真 | 服务端 SimCore 集成 |

---

## 十、已确认的技术决策

- ✅ 方案 A：渐进式重构，在现有 ECS 基础上新增 SimulationCore
- ✅ 数值方案：整数化（LogicInt, long 存储），预留 Q31.32 升级接口
- ✅ 配置转换：ConfigLoader 加载时 float→LogicInt，策划无感知
- ✅ Tick 模型：固定步长（20 Tick/s, 50ms）+ 渲染插值（60fps）
- ✅ 校验频率：每 Tick 录 Checksum（状态帧）
- ✅ 快照：CoC 异步模式不需要，实时模式用 RingBuffer（64 Tick 窗口）
- ✅ 服务器：先做剥离设计，暂不实现服务端代码
- ✅ Feature Toggle：AppConfig.UseSimulationCore 控制新旧切换
- ✅ FrameSync：拆分为独立项目（Phase 5c），不阻塞 Phase 1-4 交付
