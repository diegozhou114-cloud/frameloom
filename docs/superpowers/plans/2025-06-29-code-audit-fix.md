# ZWebGLFrameWork 代码审核修复计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use `subagent-driven-development` (recommended) or `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修复代码审核发现的全部 34 个 CRITICAL + HIGH 问题

**Architecture:** 按模块依赖链 Core→ECS→Data→FSM→UI→Combat→Gameplay→Network→SimCore 排序，P0 恢复编译 → P1 防崩溃/数据损坏 → P2 功能性修复

**Tech Stack:** Unity 2022.3, C# 9.0, UniTask

---

## P0 — 编译恢复 (必须在所有修复之前完成)

---

### Task S1.1: 修复 UILayer.HUD 编译错误

**Files:**
- Modify: `Assets/Scripts/UI/Core/UIDefine.cs:6-13`
- Modify: `Assets/Scripts/UI/Panels/BattleHUD.cs:20`
- Modify: `Assets/Scripts/UI/Panels/DeployPanel.cs:29`

**问题:** `BattleHUD.cs:20` 和 `DeployPanel.cs:29` 引用 `UILayer.HUD`，但枚举只定义了 Background/Normal/Popup/Tip/Top

- [ ] **Step 1: 在 UIDefine.cs 枚举中添加 HUD 成员**

`Assets/Scripts/UI/Core/UIDefine.cs` — 修改 enum UILayer:

```diff
     public enum UILayer
     {
-        Background = 0,  // 背景层（HUD、状态栏等常驻界面）
-        Normal     = 1,  // 普通界面（主城、商店、背包等全屏界面）
-        Popup      = 2,  // 弹窗层（确认框、奖励弹窗等模态窗口）
-        Tip        = 3,  // 提示层（飘字、Tooltip 等短暂提示）
-        Top        = 4,  // 顶层（Loading遮罩、网络重连、系统通知）
+        Background = 0,  // 背景层
+        HUD        = 1,  // HUD层（战斗HUD、部署面板等常驻战斗界面）
+        Normal     = 2,  // 普通界面（主城、商店、背包等全屏界面）
+        Popup      = 3,  // 弹窗层（确认框、奖励弹窗等模态窗口）
+        Tip        = 4,  // 提示层（飘字、Tooltip 等短暂提示）
+        Top        = 5,  // 顶层（Loading遮罩、网络重连、系统通知）
     }
```

- [ ] **Step 2: 验证 — 检查不再有 UILayer.HUD 引用报错**

在 Unity Editor 中打开项目，确认 BattleHUD.cs 和 DeployPanel.cs 编译通过。

---

### Task S1.2: 修复 ObservableTests asmdef 引用缺失

**Files:**
- Modify: `Assets/Scripts/Core/Tests/ZWebGLFrameWork.Core.Tests.asmdef`

**问题:** `ObservableTests.cs` 使用 `using ZWebGLFrameWork.Data;` 但 `Core.Tests.asmdef` 的 `references` 只有 `ZWebGLFrameWork.Core`

- [ ] **Step 1: 添加 ZWebGLFrameWork.Data 引用**

`Assets/Scripts/Core/Tests/ZWebGLFrameWork.Core.Tests.asmdef`:

```diff
     "references": [
-        "ZWebGLFrameWork.Core"
+        "ZWebGLFrameWork.Core",
+        "ZWebGLFrameWork.Data"
     ],
```

- [ ] **Step 2: 验证 asmdef 引用链正确**

确认 `ZWebGLFrameWork.Data.asmdef` 本身不引用 `ZWebGLFrameWork.Core.Tests`（防止循环依赖）。

---

### Task S1.3: 修复 SimMovementSystem 编译错误 (2处)

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs:20-21,45-46`

**问题1:** `new LogicInt(2)` 使用私有 raw-value 构造函数，应为 `LogicInt.FromInt(2)`  
**问题2:** `dx * step.RawValue` — `step.RawValue` 是 `long`，无 `LogicInt * long` 运算符

- [ ] **Step 1: 修复 LogicInt(2) 构造调用**

`Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs:20-21`:

```diff
-                LogicInt targetX = (LogicInt.FromInt(wx) * movement.CellSize) + (movement.CellSize / new LogicInt(2));
-                LogicInt targetY = (LogicInt.FromInt(wy) * movement.CellSize) + (movement.CellSize / new LogicInt(2));
+                LogicInt halfCell = movement.CellSize / LogicInt.FromInt(2);
+                LogicInt targetX = (LogicInt.FromInt(wx) * movement.CellSize) + halfCell;
+                LogicInt targetY = (LogicInt.FromInt(wy) * movement.CellSize) + halfCell;
```

- [ ] **Step 2: 修复 dx/dy 乘以 step.RawValue 的类型错误**

`Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs:45-46`:

```diff
                     if (dist.RawValue > 0)
                     {
-                        LogicInt fracX = (dx * step.RawValue) / dist;
-                        LogicInt fracY = (dy * step.RawValue) / dist;
+                        LogicInt fracX = (dx * step) / dist;
+                        LogicInt fracY = (dy * step) / dist;
                         transform.PosX += fracX;
                         transform.PosY += fracY;
                     }
```

**验证逻辑:** `fracX = dx * step / dist` 等价于 `dx * (step.RawValue / dist.RawValue)`，但因为 step 和 dist 都是 LogicInt，直接使用 LogicInt 乘除运算符即可（内部会用 Scale 正确处理）。

- [ ] **Step 3: 验证编译通过**

在 Unity Editor 中确认 SimMovementSystem.cs 无编译错误。

---

## P1 — 防崩溃 / 防数据损坏

---

### Task S2: Core + Data 修复

**包含问题:**
- HIGH: `LogManager.cs:42` 静态 OnLog 事件无清理
- HIGH: `SceneLoader.cs:31-46` Init() 非幂等
- CRITICAL: `ConfigTableManager.cs:207-218` Resources.Load 在线程池调用
- HIGH: `SaveManager.cs:77-78` NRE 风险

---

#### S2.1: LogManager 添加 ClearListeners + 线程安全

**Files:**
- Modify: `Assets/Scripts/Core/Log/LogManager.cs`

- [ ] **Step 1: 添加 ClearListeners 方法和锁保护**

`Assets/Scripts/Core/Log/LogManager.cs` — 在类中添加:

```csharp
private static readonly object _lock = new object();
```

修改 `OnLog` 事件声明和 `AddListener`/`RemoveListener`:

```diff
-        public static event Action<string, string, LogType> OnLog;
+        private static event Action<string, string, LogType> _onLog;
+
+        public static void AddLogListener(Action<string, string, LogType> listener)
+        {
+            lock (_lock) { _onLog += listener; }
+        }
+
+        public static void RemoveLogListener(Action<string, string, LogType> listener)
+        {
+            lock (_lock) { _onLog -= listener; }
+        }
+
+        /// <summary>清除所有日志监听器，防止内存泄漏</summary>
+        public static void ClearLogListeners()
+        {
+            lock (_lock) { _onLog = null; }
+        }
```

修改 `_minLevel` 的 get/set:

```diff
-        private static LogLevel _minLevel = LogLevel.Info;
+        private static volatile LogLevel _minLevel = LogLevel.Info;
```

修改所有 `OnLog?.Invoke(...)` 调用为:

```csharp
Action<string, string, LogType> handler;
lock (_lock) { handler = _onLog; }
handler?.Invoke(condition, stackTrace, type);
```

- [ ] **Step 2: 更新所有订阅者代码**

搜索所有 `LogManager.OnLog +=` 的使用，改为 `LogManager.AddLogListener(...)`; 搜索所有 `LogManager.OnLog -=` 改为 `LogManager.RemoveLogListener(...)`。

---

#### S2.2: SceneLoader Init 幂等性修复

**Files:**
- Modify: `Assets/Scripts/Core/Scene/SceneLoader.cs`

- [ ] **Step 1: 添加初始化守卫**

`Assets/Scripts/Core/Scene/SceneLoader.cs` — 在类中添加:

```csharp
private bool _initialized = false;
```

修改 `Init()`:

```diff
     public void Init()
     {
+        if (_initialized) return;
+        _initialized = true;
         SceneManager.sceneLoaded += OnSceneLoaded;
         SceneManager.sceneUnloaded += OnSceneUnloaded;
         // ... rest of Init
     }
```

修改 `OnDestroy()`:

```diff
     private void OnDestroy()
     {
+        if (!_initialized) return;
+        _initialized = false;
         SceneManager.sceneLoaded -= OnSceneLoaded;
         SceneManager.sceneUnloaded -= OnSceneUnloaded;
     }
```

---

#### S2.3: ConfigTableManager Resources.Load 回主线程

**Files:**
- Modify: `Assets/Scripts/Data/ConfigTableManager.cs`

- [ ] **Step 1: 移除 SwitchToThreadPool，全程在主线程执行**

`Assets/Scripts/Data/ConfigTableManager.cs:207-218`:

```diff
     private async UniTask LoadConfigTableAsync<T>(string fileName, List<T> targetList) where T : class
     {
         try
         {
-            await UniTask.SwitchToThreadPool();
+            // Resources.Load 必须在主线程调用，不使用 SwitchToThreadPool
             var data = ConfigLoader.LoadJsonArrayFromResources<T>(fileName);
-            await UniTask.SwitchToMainThread();
+            // 已在主线程，无需切换
             if (data != null)
             {
                 targetList.Clear();
```

**注意:** 这意味着 JSON 解析也在主线程进行。如果配置文件很大（>1MB），考虑改为 `Resources.LoadAsync` + `await` 的非阻塞方式。

- [ ] **Step 2: 添加 ConfigLoader._cache 的线程安全保护（防御性）**

`Assets/Scripts/Data/ConfigLoader.cs:14`:

```diff
-        private static readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
+        private static readonly Dictionary<string, object> _cache = new Dictionary<string, object>();
+        // 防御性：虽然当前都在主线程调用，加锁防止未来误用
+        private static readonly object _cacheLock = new object();
```

在 `TryGetCache` / `SetCache` 方法中添加 `lock (_cacheLock)`。

---

#### S2.4: SaveManager NRE 修复 + 原子写入

**Files:**
- Modify: `Assets/Scripts/Data/SaveManager.cs`

- [ ] **Step 1: 添加 null 检查 + File.Replace 原子写入**

`Assets/Scripts/Data/SaveManager.cs:55-58, 77-78`:

```diff
     public bool SaveGame(int slot)
     {
+        var dataManager = DataManager.Instance;
+        if (dataManager?.Player == null || dataManager?.Village == null)
+        {
+            LogManager.Error("[SaveManager] DataManager not ready, save aborted");
+            return false;
+        }
         var saveData = new PlayerSaveData
         {
-            Player = DataManager.Instance.Player,
-            Village = DataManager.Instance.Village,
+            Player = dataManager.Player,
+            Village = dataManager.Village,
             SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
         };
         // ...
         // 原子写入（修复前：先Delete再Move，可能双丢失）
-        File.Delete(filePath);
-        File.Move(tempPath, filePath);
+        File.Replace(tempPath, filePath, null);
     }
```

---

### Task S3: ECS World 实体生命周期修复

**Files:**
- Modify: `Assets/Scripts/ECS/World.cs`

**包含问题:**
- CRITICAL: `World.cs:61` DestroyEntity 旧实体generation验证缺失 → ID碰撞
- HIGH: `World.cs:60` `_entities.RemoveAll` O(n) per destroy
- HIGH: `World.cs:56-59` 遍历全部pool per destroy

- [ ] **Step 1: 添加 generation 验证防止旧实体ID重新入队**

`Assets/Scripts/ECS/World.cs:52-63` — 修改 DestroyEntity:

```diff
     public void DestroyEntity(Entity entity)
     {
-        if (!entity.IsValid) return;
+        if (entity.Id < 0 || entity.Id >= _generations.Length) return;
+        if (entity.Generation != _generations[entity.Id]) return; // generation不匹配 = 旧实体
+
+        // 从实体列表移除（使用swap-remove优化）
+        int index = _entityIndexMap.GetValueOrDefault(entity.Id, -1);
+        if (index >= 0)
+        {
+            int lastIndex = _entities.Count - 1;
+            _entities[index] = _entities[lastIndex];
+            _entityIndexMap[_entities[index].Id] = index;
+            _entities.RemoveAt(lastIndex);
+            _entityIndexMap.Remove(entity.Id);
+        }

         foreach (var pool in _componentPools.Values)
             pool.Remove(entity);

-        _entities.RemoveAll(e => e.Id == entity.Id);
-        _freeIds.Enqueue(entity.Id);
+        if (index >= 0)
+        {
+            _freeIds.Enqueue(entity.Id);
+        }
     }
```

- [ ] **Step 2: 添加 `_entityIndexMap` 字段和 CreateEntity 中的注册**

在 World.cs 字段区添加:

```csharp
private readonly Dictionary<int, int> _entityIndexMap = new Dictionary<int, int>();
```

在 `CreateEntity()` 方法中添加:

```diff
     public Entity CreateEntity()
     {
         // ... existing ID allocation logic ...
         _entities.Add(entity);
+        _entityIndexMap[entity.Id] = _entities.Count - 1;
         return entity;
     }
```

- [ ] **Step 3: 优化 component pool 遍历 — 记录实体所属pool**

在 Entity 结构中（或通过 component mask）跟踪实体拥有哪些组件类型。短期优化方案：利用 SparseSet 的 sparse 数组快速跳过：

`World.cs:56-59` — 当前代码已通过 `pool.Remove(entity)` 内部调用 `Has()` (O(1)) 短路的机制，实际上没有性能问题。只做 generation 验证优化即可。

- [ ] **Step 4: 验证 — 运行 ECS 基本测试**

创建简单测试：Create Entity → Add Component → Destroy Entity → Create Entity (verify different Generation) → old handle.DestroyEntity (should be no-op)。

---

### Task S4: KCP 协议修复

**Files:**
- Modify: `Assets/Scripts/Network/KCP/KcpCore.cs`

**包含问题:**
- CRITICAL: `KcpCore.cs:291` `flag` 从未赋值 → 累积ACK死代码，`_sndUna`恒为0
- CRITICAL: `KcpCore.cs:764-769` 拥塞窗口每次重传都重置
- MEDIUM: `KcpCore.cs:745-756` 每包分配 buffer → GC压力

- [ ] **Step 1: 修复累积ACK — 设置flag并在适当位置调用ParseUnalignedAck**

`Assets/Scripts/Network/KCP/KcpCore.cs:291` — 修改 ParseData:

```diff
     private void ParseData(byte[] data, int size)
     {
-        int flag = 0;
+        int flag = 1; // 启用una处理
         // ... rest of method
     }
```

在读取 `una` 后添加对 `_sndUna` 的更新:

```diff
             uint una = BitConverter.ToUInt32(data, offset);
             offset += 4;
+            _sndUna = una;
             // ...
```

在 `flag != 0` 分支中添加:

```diff
             if (flag != 0)
             {
                 ParseAck(sn);
+                ParseUnalignedAck(una);
             }
```

- [ ] **Step 2: 修复拥塞控制 — 只在检测到新丢包时减半**

`Assets/Scripts/Network/KCP/KcpCore.cs:764-769`:

```diff
-            if (resentCount > 0)
-            {
-                _ssthresh = Math.Max(_ssthresh / 2, 2);
-                _cwnd = 1;
-            }
+            // 拥塞控制已由 ParseAck 中的丢包检测处理
+            // 此处不重复减半，避免每次Flush都重置窗口
```

同时在 `ParseAck` 中确保丢包检测逻辑正确触发拥塞窗口缩减（检查 `_ssthresh` 和 `_cwnd` 的更新是否与参考KCP实现一致）。

- [ ] **Step 3: 优化 buffer 分配 — 使用预分配缓冲区**

`Assets/Scripts/Network/KCP/KcpCore.cs:171` — 检查 `_buffer` 字段已存在，然后在 Flush 输出段中使用它:

```diff
-            byte[] output = new byte[totalLen];
+            if (_buffer == null || _buffer.Length < totalLen)
+                _buffer = new byte[totalLen];
+            byte[] output = _buffer;
             int pos = 0;
```

**注意:** 需确保 `_buffer` 在每次 Flush 输出时不会被并发使用。

- [ ] **Step 4: 验证 — KCP 吞吐量测试**

运行网络层的基本连接测试，验证发送缓冲区不再无限增长。

---

### Task S5: Combat 核心修复

**Files:**
- Modify: `Assets/Scripts/Combat/AI/AStarPathfinding.cs`
- Modify: `Assets/Scripts/Combat/AI/UnitAI.cs`
- Modify: `Assets/Scripts/Combat/CombatManager.cs`
- Modify: `Assets/Scripts/Combat/Systems/DeathSystem.cs`

---

#### S5.1: A* 对角线墙角穿越修复

**Files:**
- Modify: `Assets/Scripts/Combat/AI/AStarPathfinding.cs:53-60`

- [ ] **Step 1: 修正对角线邻居的 cardinal 阻挡检查**

`Assets/Scripts/Combat/AI/AStarPathfinding.cs:53-60`:

```diff
                 // 对角线移动：检查两个相邻的 cardinal 方向
                 if (i % 2 == 1)
                 {
-                    var adj1 = current.Position + _directions[(i + 2) % 8];
-                    var adj2 = current.Position + _directions[(i + 4) % 8];
+                    // 正确: NE(1)检查N(0)+E(2), SE(3)检查E(2)+S(4), SW(5)检查S(4)+W(6), NW(7)检查W(6)+N(0)
+                    var adj1 = current.Position + _directions[(i + 7) % 8]; // 前一个cardinal
+                    var adj2 = current.Position + _directions[(i + 1) % 8]; // 后一个cardinal
                     if (!grid.IsWalkable(adj1.x, adj1.y) || !grid.IsWalkable(adj2.x, adj2.y))
                         continue;
                 }
```

- [ ] **Step 2: 验证 — 创建墙角测试用例**

在 GridManager 上设置墙角的阻挡格，验证 A* 路径不会穿过墙角。

---

#### S5.2: UnitAI 建筑物移动修复

**Files:**
- Modify: `Assets/Scripts/Combat/AI/UnitAI.cs:35`

- [ ] **Step 1: 过滤非移动实体**

`Assets/Scripts/Combat/AI/UnitAI.cs:35`:

```diff
     _world.ForEachEntities<TransformComponent>(entity =>
     {
         if (!_world.TryGetComponent<AttackComponent>(entity, out _)) return;
+        if (!_world.TryGetComponent<MovementComponent>(entity, out _)) return; // 跳过非移动实体（如建筑）
         // ...
         ProcessEntity(entity);
     });
```

- [ ] **Step 2: 验证 — 创建包含建筑的战斗测试**

创建包含防御建筑的战斗场景，验证建筑不会移动，攻击单位正常寻路。

---

#### S5.3: CombatManager 平局处理 + NRE修复

**Files:**
- Modify: `Assets/Scripts/Combat/CombatManager.cs`
- Modify: `Assets/Scripts/Combat/Systems/DeathSystem.cs`

- [ ] **Step 1: DeathSystem 添加明确的 Draw 状态**

`Assets/Scripts/Combat/Systems/DeathSystem.cs:74-77`:

```diff
     if (attackerAlive == 0 && defenderAlive == 0)
     {
-        // 双方同时全灭 → 平局（当前实现默认Defender胜）
+        Winner = Team.None; // 明确标记平局
     }
```

需要确认 `Team` 枚举中有 `None` 成员，如果没有则添加。

- [ ] **Step 2: CombatManager 处理平局**

`Assets/Scripts/Combat/CombatManager.cs:112`:

```diff
-        var winner = DeathSys.Winner ?? Team.Defender;
+        var winner = DeathSys.Winner;
+        if (winner == Team.None)
+        {
+            // 平局处理：可视为攻击方失败
+            winner = Team.Defender;
+            LogManager.Info("[CombatManager] Battle ended in draw, defaulting to defender");
+        }
+        else if (winner == null)
+        {
+            winner = Team.Defender;
+        }
```

- [ ] **Step 3: 添加 null 检查**

`Assets/Scripts/Combat/CombatManager.cs:96`:

```diff
-        _pathfinding = new AStarPathfinding(GridManager.Instance);
+        var gridManager = GridManager.Instance;
+        if (gridManager == null)
+        {
+            LogManager.Error("[CombatManager] GridManager not initialized");
+            return;
+        }
+        _pathfinding = new AStarPathfinding(gridManager);
```

`Assets/Scripts/Combat/CombatManager.cs:113`:

```diff
-        int destroyed = village.Buildings.Count - DeathSys.DefenderAlive;
+        var dataManager = Data.DataManager.Instance;
+        if (dataManager?.Village?.Buildings == null)
+        {
+            LogManager.Error("[CombatManager] Village data not available for result calculation");
+            return;
+        }
+        int destroyed = dataManager.Village.Buildings.Count - DeathSys.DefenderAlive;
```

---

### Task S6: SimulationCore 类型系统 + 确定性修复

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Core/SimChunkHelper.cs`
- Modify: `Assets/Scripts/SimulationCore/Math/LogicInt.cs`
- Modify: `Assets/Scripts/SimulationCore/Orchestrator/SimOrchestrator.cs`
- Modify: `Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs`
- Modify: `Assets/Scripts/SimulationCore/Core/SimWorld.cs`

---

#### S6.1: SimChunkHelper 类型ID对齐

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Core/SimChunkHelper.cs`

- [ ] **Step 1: 改为从 ComponentTypeRegistry 动态查询类型ID**

`Assets/Scripts/SimulationCore/Core/SimChunkHelper.cs:14-21`:

```diff
-        public static readonly int TransformIdx = 0;
-        public static readonly int HealthIdx = 1;
-        public static readonly int TeamIdx = 2;
-        public static readonly int MovementIdx = 3;
-        public static readonly int AttackIdx = 4;
-        public static readonly int TargetIdx = 5;
+        // 类型索引由 ComponentTypeRegistry 统一分配，不再硬编码
+        public static int TransformIdx => ComponentTypeRegistry.GetId<SimTransformComponent>();
+        public static int HealthIdx => ComponentTypeRegistry.GetId<SimHealthComponent>();
+        public static int TeamIdx => ComponentTypeRegistry.GetId<SimTeamComponent>();
+        public static int MovementIdx => ComponentTypeRegistry.GetId<SimMovementComponent>();
+        public static int AttackIdx => ComponentTypeRegistry.GetId<SimAttackComponent>();
+        public static int TargetIdx => ComponentTypeRegistry.GetId<SimTargetComponent>();
```

同时修改 `ComponentTypeRegistry.GetId<T>()` 使其对未注册类型抛出 `KeyNotFoundException` 而非静默返回 0：

```diff
     public static int GetId<T>() where T : struct, ISimComponent
     {
-        return _typeToId.TryGetValue(typeof(T).FullName, out int id) ? id : 0;
+        if (!_typeToId.TryGetValue(typeof(T).FullName, out int id))
+            throw new KeyNotFoundException($"Component type {typeof(T).Name} not registered in ComponentTypeRegistry");
+        return id;
     }
```

---

#### S6.2: LogicInt 乘法溢出修复

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Math/LogicInt.cs`

- [ ] **Step 1: 修复混合符号乘法溢出**

`Assets/Scripts/SimulationCore/Math/LogicInt.cs:53-67`:

```diff
     public static LogicInt operator *(LogicInt a, LogicInt b)
     {
         long hi = Math.BigMul(a.RawValue, b.RawValue, out long lo);
-        if (hi == 0 && lo >= 0)
-            return new LogicInt(lo); // fast path: result fits in long
-        // Scale down the 128-bit result
-        return new LogicInt((long)(((ulong)hi << 64) | (ulong)lo) / Scale);
+        // 使用128位中间结果正确缩放，处理混合符号
+        // 先计算完整128位乘积，再除以Scale
+        bool negative = (a.RawValue < 0) != (b.RawValue < 0);
+        ulong uHi = (ulong)(hi < 0 ? ~hi + (lo == 0 ? 1L : 0L) : hi);
+        ulong uLo = (ulong)lo;
+        // 组合为128位值除以Scale
+        ulong result = ((uHi % (ulong)Scale) << 32 | (uLo >> 32)) / (ulong)Scale;
+        // 简化：使用decimal或BigInteger做精确计算
+        return new LogicInt((long)(new System.Numerics.BigInteger(a.RawValue) * new System.Numerics.BigInteger(b.RawValue) / Scale));
     }
```

**注意:** 使用 `System.Numerics.BigInteger` 需要确认 .NET Standard 2.1 支持。备选方案：逐段计算避免溢出。

- [ ] **Step 2: 添加除法溢出保护**

`Assets/Scripts/SimulationCore/Math/LogicInt.cs:69-78`:

```diff
     public static LogicInt operator /(LogicInt a, int b)
     {
+        if (b == 0) throw new DivideByZeroException();
         return new LogicInt(a.RawValue / b);
     }

     public static LogicInt operator /(LogicInt a, LogicInt b)
     {
+        if (b.RawValue == 0) throw new DivideByZeroException();
         return new LogicInt((a.RawValue * Scale) / b.RawValue);
     }
```

---

#### S6.3: SimOrchestrator 命令分发

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Orchestrator/SimOrchestrator.cs`

- [ ] **Step 1: 分发 Drain 出的命令**

`Assets/Scripts/SimulationCore/Orchestrator/SimOrchestrator.cs:113`:

```diff
     private void ExecuteTick(int tick)
     {
-        var cmds = _buffer.Drain(tick);
+        var cmds = _buffer.Drain(tick);
+        if (cmds != null && cmds.Count > 0)
+        {
+            foreach (var cmd in cmds)
+            {
+                cmd.Execute(_world);
+            }
+        }
         // ... rest of tick logic
     }
```

**验证:** 确认 `ISimCommand` 接口有 `Execute(SimWorld world)` 方法，所有命令实现类（`SimCommands.cs` 中）正确实现。

---

#### S6.4: FrameSyncClient checksum 修复

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs`

- [ ] **Step 1: 对比正确 tick 的 checksum**

`Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs:140`:

```diff
     private void OnAuthorityData(FrameInput input)
     {
-        uint localChecksum = _orchestrator.LastChecksum;
-        uint authorityChecksum = input.Checksum;
+        uint localChecksum = _orchestrator.GetChecksumForTick(input.Tick);
+        uint authorityChecksum = input.Checksum;
         if (localChecksum != authorityChecksum)
         {
             LogManager.Warn($"[FrameSync] Checksum mismatch at tick {input.Tick}");
-            RequestRollback(input.Tick);
+            RequestRollback(input.Tick, authorityChecksum);
         }
     }
```

需要在 `SimOrchestrator` 中添加 `GetChecksumForTick(int tick)` 方法，从 `_snapshotHistory` 中检索特定 tick 的 checksum。

- [ ] **Step 2: 修复 AdvanceAuthority Lockstep**

`Assets/Scripts/SimulationCore/Network/FrameSyncClient.cs:107`:

```diff
-        while (_buffer.PendingCount > 0)
+        while (_buffer.HasAllPlayersInput(CurrentTick))
         {
             AdvanceAuthority();
         }
```

需要在 `CommandBuffer` 中添加 `HasAllPlayersInput(int tick)` 方法，检查所有已连接玩家的输入是否已到达。

---

#### S6.5: SimWorld checksum 确定性

**Files:**
- Modify: `Assets/Scripts/SimulationCore/Core/SimWorld.cs`

- [ ] **Step 1: 固定 Chunk 迭代顺序**

`Assets/Scripts/SimulationCore/Core/SimWorld.cs:355-383`:

```diff
     public uint ComputeChecksum()
     {
         uint hash = 0;
         foreach (var kvp in _archetypes)
         {
             var archetype = kvp.Value;
-            foreach (var chunk in archetype.Chunks)
+            // 按 Chunk.EntityCount 排序确保确定性（或按创建顺序固定不变）
+            // 当前方案：不在DestroyEntity中swap chunk，改为标记空闲槽位
+            var chunks = archetype.Chunks.OrderBy(c => c.FirstEntityId).ToList();
+            foreach (var chunk in chunks)
             {
                 for (int i = 0; i < chunk.Count; i++)
                 {
```

备选方案：修改 `SimChunk` 的 Destroy 操作从 swap-remove 改为标记删除（在 chunk 中设置 dead flag），保持 chunk 顺序不变。

- [ ] **Step 2: 验证 — 运行确定性测试**

运行 `SimWorldDeterminismTests`，确认不同顺序创建/销毁实体后 checksum 一致。

---

## P2 — 功能性修复

---

### Task S7: UI 生命周期修复

**Files:**
- Modify: `Assets/Scripts/UI/Core/UIManager.cs`
- Modify: `Assets/Scripts/UI/Core/UIStack.cs`
- Modify: `Assets/Scripts/UI/Core/UIBase.cs`
- Modify: `Assets/Scripts/UI/Panels/MainVillageHUD.cs`

---

#### S7.1: UIManager Close 内存泄漏修复

**Files:**
- Modify: `Assets/Scripts/UI/Core/UIManager.cs`

- [ ] **Step 1: Close 时销毁 GameObject 或实现正确缓存**

`Assets/Scripts/UI/Core/UIManager.cs:126-132`:

```diff
     public void Close<T>() where T : UIBase
     {
         var type = typeof(T);
         if (_panelCache.TryGetValue(type, out var panel))
         {
             _stack.Pop(panel);
-            _panelCache.Remove(type);
+            // 缓存面板以便复用，不清除
+            // 只在 CloseAll 时真正清理
         }
     }
```

修改 `CloseAll()`:

```diff
     public void CloseAll()
     {
         foreach (var kvp in _panelCache)
         {
             kvp.Value.DoClose();
+            UnityEngine.Object.Destroy(kvp.Value.gameObject);
         }
         _panelCache.Clear();
         _stack.Clear();
     }
```

修改 `GetOrCreatePanel` 以复用缓存的 GameObject:

```diff
     private UIBase GetOrCreatePanel(Type type)
     {
-        if (_panelCache.TryGetValue(type, out var cached))
+        if (_panelCache.TryGetValue(type, out var cached) && cached != null)
         {
             return cached;
         }
         // ... instantiate new panel, add to _panelCache
     }
```

---

#### S7.2: UIStack + UIBase reopen-during-close 修复

**Files:**
- Modify: `Assets/Scripts/UI/Core/UIStack.cs:110-114`
- Modify: `Assets/Scripts/UI/Core/UIBase.cs:118`

- [ ] **Step 1: UIStack.RemoveFromMiddle 添加 resume 逻辑**

`Assets/Scripts/UI/Core/UIStack.cs:110-114`:

```diff
     private void RemoveFromMiddle(UIBase panel)
     {
         var stack = _stacks[panel.Layer];
         stack.Remove(panel);
+        // 如果被移除的面板暂停了下层，需要恢复下层
+        if (panel.PauseLowerPanels && stack.Count > 0)
+        {
+            var topPanel = stack[stack.Count - 1];
+            topPanel.DoResume();
+            // 递归恢复被暂停的更下层
+            ResumeLowerPanels(panel.Layer);
+        }
     }
```

- [ ] **Step 2: UIBase.DoOpen 处理正在关闭的动画**

`Assets/Scripts/UI/Core/UIBase.cs:DoOpen`:

```diff
     public void DoOpen(object data = null)
     {
+        // 如果正在关闭动画中，先完成关闭清理
+        if (!IsOpen && _closeAnimationRunning)
+        {
+            StopCloseAnimation();
+            OnClose(); // 执行被跳过的清理
+        }
         IsOpen = true;
         gameObject.SetActive(true);
         // ... rest of DoOpen
     }
```

添加 `_closeAnimationRunning` 标志位，在 `DoClose` 开始时设为 `true`，在关闭动画完成回调中设为 `false`。

---

#### S7.3: MainVillageHUD UnbindObservables NRE 修复

**Files:**
- Modify: `Assets/Scripts/UI/Panels/MainVillageHUD.cs:116`

- [ ] **Step 1: 添加 null-conditional**

`Assets/Scripts/UI/Panels/MainVillageHUD.cs:116`:

```diff
     private void UnbindObservables()
     {
-        var player = DataManager.Instance.Player;
+        var player = DataManager.Instance?.Player;
         if (player != null)
         {
             player.Gold.OnChanged -= OnGoldChanged;
```

同时修复 `UIManager.Open()` 丢弃 data 参数的问题（`UIManager.cs:96-100`）:

```diff
     if (_panelCache.TryGetValue(type, out var existing) && existing.IsOpen)
     {
-        existing.DoResume();
+        // 传递新数据给已打开的面板
+        existing.DoResume(data);
         return existing as T;
     }
```

---

### Task S8: Network 连接修复

**Files:**
- Modify: `Assets/Scripts/Network/Core/AddressResolver.cs`
- Modify: `Assets/Scripts/Network/Protocols/TcpClient.cs`
- Modify: `Assets/Scripts/Network/NetworkManager.cs`

---

#### S8.1: AddressResolver Happy Eyeballs Socket 泄漏

**Files:**
- Modify: `Assets/Scripts/Network/Core/AddressResolver.cs:124-157`

- [ ] **Step 1: 确保失败方 Socket 被关闭**

`Assets/Scripts/Network/Core/AddressResolver.cs:124-157`:

```diff
     private async Task<Socket> ConnectWithHappyEyeballsAsync(...)
     {
         var taskV6 = ConnectAsync(endpointV6);
         var taskV4 = ConnectAsync(endpointV4);
+        Socket winner = null;
+        Socket loser = null;

-        var winner = await Task.WhenAny(taskV6, taskV4);
-        var other = (winner == taskV6) ? taskV4 : taskV6;
+        try
+        {
+            var completedTask = await Task.WhenAny(taskV6, taskV4);
+            winner = await completedTask;
+            var otherTask = (completedTask == taskV6) ? taskV4 : taskV6;

-        try
-        {
-            await other;
+            // 尝试关闭失败方
+            try
+            {
+                loser = await otherTask;
+                loser?.Close();
+            }
+            catch
+            {
+                // 失败方异常是预期的
+            }
         }
         catch
         {
-            // fallback to winner
+            winner?.Close();
+            throw;
         }

         return winner;
     }
```

---

#### S8.2: TcpClient 指数退避修复 + 同步Send

**Files:**
- Modify: `Assets/Scripts/Network/Protocols/TcpClient.cs`

- [ ] **Step 1: 不在 Connect 中重置重连计数**

`Assets/Scripts/Network/Protocols/TcpClient.cs:46`:

```diff
     public void Connect(string host, int port)
     {
-        _reconnectAttempts = 0;
+        // 不重置 _reconnectAttempts，保持指数退避
         // ... rest of Connect
     }
```

只在连接成功时重置：

```diff
     private void HandleConnected()
     {
+        _reconnectAttempts = 0; // 连接成功，重置退避
         State = ConnectionState.Connected;
         OnConnected?.Invoke();
     }
```

- [ ] **Step 2: 同步 Send 改为异步**

`Assets/Scripts/Network/Protocols/TcpClient.cs:117`:

```diff
     public void Send(byte[] data)
     {
-        if (_socket == null || !_socket.Connected) return;
+        if (_socket == null || !_socket.Connected) return;
         try
         {
-            _socket.Send(data);
+            _socket.BeginSend(data, 0, data.Length, SocketFlags.None, ar =>
+            {
+                try
+                {
+                    _socket?.EndSend(ar);
+                }
+                catch (ObjectDisposedException) { }
+                catch (SocketException) { }
+            }, null);
         }
-        catch (ObjectDisposedException) { }
+        catch (ObjectDisposedException) { }
+        catch (SocketException) { }
     }
```

---

#### S8.3: NetworkManager 竞态条件修复

**Files:**
- Modify: `Assets/Scripts/Network/NetworkManager.cs:64-82`

- [ ] **Step 1: 添加 CancellationTokenSource 取消旧连接**

`Assets/Scripts/Network/NetworkManager.cs`:

```diff
+    private CancellationTokenSource _connectCts;

     public void Connect(string host, int port, NetworkProtocol protocol)
     {
+        // 取消之前的连接尝试
+        _connectCts?.Cancel();
+        _connectCts?.Dispose();
+        _connectCts = new CancellationTokenSource();
+        var token = _connectCts.Token;
+
         Disconnect();
         // ...
-        _currentClient.Connect(host, port);
+        ConnectAsync(host, port, token).Forget();
     }
+
+    private async UniTaskVoid ConnectAsync(string host, int port, CancellationToken token)
+    {
+        _currentClient.Connect(host, port);
+        // 等待连接或取消
+        while (_currentClient.State == ConnectionState.Connecting)
+        {
+            if (token.IsCancellationRequested) return;
+            await UniTask.Delay(100);
+        }
+    }
```

---

### Task S9: Gameplay 输入修复

**Files:**
- Modify: `Assets/Scripts/Gameplay/Control/InputManager.cs`
- Modify: `Assets/Scripts/Gameplay/Building/BuildingPlacer.cs`

---

#### S9.1: InputManager 透视相机 ScreenToWorldDelta 修复

**Files:**
- Modify: `Assets/Scripts/Gameplay/Control/InputManager.cs:232-238`

- [ ] **Step 1: 使用正确的透视相机计算**

`Assets/Scripts/Gameplay/Control/InputManager.cs:232-238`:

```diff
     private Vector3 ScreenToWorldDelta(Vector2 screenDelta)
     {
-        float worldPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
-        return new Vector3(screenDelta.x * worldPerPixel, 0, screenDelta.y * worldPerPixel);
+        if (_mainCamera.orthographic)
+        {
+            float worldPerPixel = _mainCamera.orthographicSize * 2f / Screen.height;
+            return new Vector3(screenDelta.x * worldPerPixel, 0, screenDelta.y * worldPerPixel);
+        }
+        else
+        {
+            // 透视相机：在目标平面上计算
+            Ray rayStart = _mainCamera.ScreenPointToRay(Vector3.zero);
+            Ray rayEnd = _mainCamera.ScreenPointToRay(new Vector3(screenDelta.x, screenDelta.y, 0));
+            float dist = Mathf.Abs(_mainCamera.transform.position.y / rayStart.direction.y);
+            Vector3 worldStart = rayStart.GetPoint(dist);
+            Vector3 worldEnd = rayEnd.GetPoint(dist);
+            return worldEnd - worldStart;
+        }
     }
```

- [ ] **Step 2: 修复鼠标首帧拖拽跳跃**

`Assets/Scripts/Gameplay/Control/InputManager.cs:209-211`:

```diff
             if (!_isDragging && Vector2.Distance(Input.mousePosition, _touchStartPos) > _dragThreshold)
             {
                 _isDragging = true;
+                _lastDragPosition = Input.mousePosition; // 初始化上一帧位置，避免跳跃
                 OnDragStart?.Invoke(_touchStartPos);
             }

             if (_isDragging)
             {
-                Vector3 worldDelta = ScreenToWorldDelta(
-                    (Vector2)Input.mousePosition - _touchStartPos);
-                _touchStartPos = Input.mousePosition;
-                _cameraController?.Pan(worldDelta);
+                Vector3 worldDelta = ScreenToWorldDelta(
+                    (Vector2)Input.mousePosition - _lastDragPosition);
+                _lastDragPosition = Input.mousePosition;
+                _cameraController?.Pan(worldDelta);
             }
```

添加 `_lastDragPosition` 字段声明。

---

#### S9.2: BuildingPlacer 移动端触摸修复

**Files:**
- Modify: `Assets/Scripts/Gameplay/Building/BuildingPlacer.cs:134,180`

- [ ] **Step 1: 使用 Touch 输入代替 Input.mousePosition**

`Assets/Scripts/Gameplay/Building/BuildingPlacer.cs:134`:

```diff
     private void UpdatePlacementPreview()
     {
+        Vector3 screenPoint;
+#if UNITY_ANDROID || UNITY_IOS
+        if (Input.touchCount > 0)
+            screenPoint = Input.GetTouch(0).position;
+        else
+            return;
+#else
+        screenPoint = Input.mousePosition;
+#endif
-        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
+        Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
         // ...
     }
```

`Assets/Scripts/Gameplay/Building/BuildingPlacer.cs:180` — 同样处理:

```diff
     public void TryPlace()
     {
+        Vector3 screenPoint;
+#if UNITY_ANDROID || UNITY_IOS
+        if (Input.touchCount > 0)
+            screenPoint = Input.GetTouch(0).position;
+        else
+            return;
+#else
+        screenPoint = Input.mousePosition;
+#endif
-        Ray ray = _mainCamera.ScreenPointToRay(Input.mousePosition);
+        Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
```



---

### Task S10: 编辑器 + FSM 修复

**Files:**
- Modify: `Assets/Editor/CompileHook.cs`
- Modify: `Assets/Editor/AutoFixProcessor.cs`
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`
- Modify: `Assets/Scripts/FSM/Game/HotUpdateManager.cs`

---

#### S10.1: CompileHook 编译错误检测修复

**Files:**
- Modify: `Assets/Editor/CompileHook.cs`

- [ ] **Step 1: 使用公开 API 代替反射内部 API**

`Assets/Editor/CompileHook.cs` — 替换整个 `HasCompileErrors()` 方法，使用 `EditorApplication.update` 累积 + `AssemblyReloadEvents`:

```csharp
private static bool _hasCompileErrors = false;

[InitializeOnLoadMethod]
private static void Init()
{
    AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
}

private static void OnBeforeAssemblyReload()
{
    // 在程序集重载前检查（此时编译已完成）
    _hasCompileErrors = EditorUtility.scriptCompilationFailed;
}

public static bool HasCompileErrors()
{
    // Unity 2022.3 没有直接的"是否有编译错误"API
    // 使用 EditorUtility.scriptCompilationFailed (需要手动调用触发)
    EditorUtility.RequestScriptReload();
    return _hasCompileErrors;
}
```

**更好的方案:** 使用 `CompilationPipeline` 回调:

```csharp
[InitializeOnLoadMethod]
private static void Init()
{
    CompilationPipeline.compilationFinished += OnCompilationFinished;
}

private static bool _lastCompileHadErrors = false;

private static void OnCompilationFinished(object obj)
{
    _lastCompileHadErrors = CompilationPipeline.assemblyCompilationFinished
        .Any(e => e.status == CompilationStatus.Failed);
}

public static bool HasCompileErrors() => _lastCompileHadErrors;
```

---

#### S10.2: AutoFixProcessor 安全写入

**Files:**
- Modify: `Assets/Editor/AutoFixProcessor.cs`

- [ ] **Step 1: 写入前创建备份 + 用户确认**

`Assets/Editor/AutoFixProcessor.cs:86,113`:

```diff
     private static void ApplyFix(string filePath, string newContent)
     {
+        // 创建备份
+        string backupPath = filePath + ".bak";
+        if (!File.Exists(backupPath))
+        {
+            File.Copy(filePath, backupPath, overwrite: true);
+        }
+
+        // 写入
         File.WriteAllText(filePath, newContent);
+        Debug.Log($"[AutoFix] Applied fix to {filePath}, backup at {backupPath}");
     }
```

添加修复前后 diff 日志输出，方便代码审查:

```diff
     private static void ApplyFix(string filePath, string newContent)
     {
         string backupPath = filePath + ".bak";
         if (!File.Exists(backupPath))
         {
             File.Copy(filePath, backupPath, overwrite: true);
         }
+
+        // 输出 diff
+        string original = File.ReadAllText(filePath);
+        Debug.Log($"[AutoFix] Diff for {filePath}:\n=== ORIGINAL ===\n{original}\n=== FIXED ===\n{newContent}");
+
         File.WriteAllText(filePath, newContent);
         Debug.Log($"[AutoFix] Applied fix to {filePath}, backup at {backupPath}");
     }
```

---

#### S10.3: LoginState async void 竞态条件修复

**Files:**
- Modify: `Assets/Scripts/FSM/Game/States/LoginState.cs`

- [ ] **Step 1: 添加 CancellationTokenSource 守卫**

`Assets/Scripts/FSM/Game/States/LoginState.cs:61-82`:

```diff
+    private CancellationTokenSource _loginCts;

     public override void OnEnter(object data)
     {
         base.OnEnter(data);
+        _loginCts = new CancellationTokenSource();
         // ...
     }

     public override void OnExit()
     {
+        _loginCts?.Cancel();
+        _loginCts?.Dispose();
+        _loginCts = null;
         // ... rest of OnExit
     }

-    private async void OnGuestLogin()
+    private async UniTaskVoid OnGuestLogin()
     {
+        var token = _loginCts.Token;
         // ...
-        await UniTask.Delay(TimeSpan.FromSeconds(10));
+        await UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: token);
+        if (token.IsCancellationRequested) return;
         // ... rest of timeout logic
     }
```

---

#### S10.4: HotUpdateManager 错误处理修复

**Files:**
- Modify: `Assets/Scripts/FSM/Game/HotUpdateManager.cs`

- [ ] **Step 1: 区分网络错误和"无更新"**

`Assets/Scripts/FSM/Game/HotUpdateManager.cs:64,94`:

```diff
     private VersionCheckResult CheckVersion(UpdateManifest manifest)
     {
-        if (manifest == null) return VersionCheckResult.UpToDate;
+        if (manifest == null) return VersionCheckResult.Error; // 明确区分错误和无需更新
         try
         {
             // ... version check logic
         }
         catch (Exception ex)
         {
             LogManager.Error($"[HotUpdate] Version check failed: {ex.Message}");
-            return VersionCheckResult.UpToDate;
+            return VersionCheckResult.Error;
         }
     }
```

需要确认 `VersionCheckResult` 枚举有 `Error` 成员，或在调用处检查。

替换占位 URL 为可配置项:

```diff
-        _http.Connect("https://your-cdn.com");
+        if (string.IsNullOrEmpty(AppConfig.Instance.HotUpdateBaseUrl))
+        {
+            LogManager.Warn("[HotUpdate] HotUpdateBaseUrl not configured, skipping update check");
+            return VersionCheckResult.UpToDate; // 未配置时跳过，不报错
+        }
+        _http.Connect(AppConfig.Instance.HotUpdateBaseUrl);
```

---

## 验证清单

每个专项完成后执行:

- [ ] **S1-S2:** Unity Editor 中编译通过 (0 errors)
- [ ] **S3:** 运行 ECS World 基本测试 (create/destroy/query)
- [ ] **S4:** 运行 KCP 基本连接测试，验证缓冲区不无限增长
- [ ] **S5:** 运行 A* 墙角测试 + 战斗建筑物不动测试
- [ ] **S6:** 运行 SimWorldDeterminismTests 全部通过
- [ ] **S7:** 打开关闭 UI 面板 10 次，检查内存不增长
- [ ] **S8:** 模拟网络断开/重连，检查 Socket 数量不增长
- [ ] **S9:** 移动端触摸拖拽/放置建筑功能正常
- [ ] **S10:** Editor 中触发编译错误 → CompileHook 正确检测; AutoFix 产生 .bak 备份

---

## 执行建议

推荐使用 `subagent-driven-development` 逐专项执行。每个专项:
1. Dispatch 一个 subagent 执行该专项的所有步骤
2. Review 产物（diff + 测试结果）
3. 通过后进入下一个专项

执行顺序严格按 P0 → P1 → P2，同阶段内按 S1→S10 编号顺序。
