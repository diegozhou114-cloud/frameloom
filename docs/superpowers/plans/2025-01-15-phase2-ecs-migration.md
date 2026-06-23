# Phase 2: ECS 系统迁移 — 实现计划

> **For agentic workers:** Use superpowers:subagent-driven-development

**Goal:** 将现有 Combat ECS 系统迁移到 SimulationCore，所有 float→LogicInt，Vector3→LogicInt 对，UnityEngine 依赖→零依赖，Time.time→Tick 计数。

---

### Task 1: 创建 SimComponents（6 个）

**Files:**
- `SimulationCore/Components/SimTransformComponent.cs`
- `SimulationCore/Components/SimMovementComponent.cs`
- `SimulationCore/Components/SimAttackComponent.cs`
- `SimulationCore/Components/SimHealthComponent.cs`
- `SimulationCore/Components/SimTargetComponent.cs`
- `SimulationCore/Components/SimTeamComponent.cs`

关键改动：
- TransformComponent: Vector3 Position→LogicInt PosX,PosY; Vector2Int GridPosition→int GridX,GridY; 去掉 Rotation/Scale
- MovementComponent: float Speed→LogicInt Speed; float CellSize→LogicInt CellSize; Path→List<(int,int)>
- AttackComponent: float Range→LogicInt Range; float Cooldown→int CooldownTicks; float LastAttackTime→int LastAttackTick
- HealthComponent: 纯int，基本不动
- TargetComponent: Entity→SimEntity
- TeamComponent: Team enum→int TeamId

### Task 2: 创建 SimMovementSystem

Order=10. float dt→固定 LogicDeltaTime, Vector3→LogicInt, GridToWorld→纯整数.

### Task 3: 创建 SimAttackSystem

Order=20. Time.time→tickCount, TargetEvaluator 也需迁移或简化.

### Task 4: 创建 SimDamageSystem

Order=30. 纯int，复制改名，Debug.Log→ISimLogger.

### Task 5: 创建 SimDeathSystem

Order=90. Team→int, Debug.Log→ISimLogger.

### Task 6: 创建 IntAStarPathfinding

复制 AStarPathfinding，去 GridManager 依赖，BinaryHeap Compare 加 EntityID tiebreaker.

### Task 7: 创建 SimCombatBootstrap

复制 CombatBootstrap，去 UniTask，用 SimSystemBase/SimSystemGroup.

### Task 8: 确定性验证测试

Same seed+input → 10 runs same checksum. Different seed → different checksum. Benchmark 100/500/1000 Entity.

### Task 9: 验证

全部文件 zero UnityEngine/Cysharp refs.
