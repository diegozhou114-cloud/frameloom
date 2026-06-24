# Phase 4: 表现层分离 — 实现计划

**Goal:** 创建 Unity 表现层衔接，通过 Feature Toggle 接入 SimulationCore。

**Tasks:**
1. ViewConfig — ScriptableObject (ArchetypeId→Prefab 映射)
2. ISimInitializer 实现 — DataManager→SimEntitySpawnInfo
3. Interpolator — Tick 间平滑插值
4. SimulationSyncBridge — SimWorld 事件→GameObject 管理
5. CombatManager 重构 — Feature Toggle 双路径

**Key:** 这些文件依赖 Unity，放在 `Assets/Scripts/Combat/` 下，引用 SimulationCore asmdef。
