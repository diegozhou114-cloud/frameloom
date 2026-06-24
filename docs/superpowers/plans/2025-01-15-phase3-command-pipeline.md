# Phase 3: Command Pipeline — 实现计划

> Subagent-driven, 5 tasks

**Goal:** 构建 Command→Buffer→Orchestrator→Recorder 完整管线，支持录制+回放验证。

**Tasks:**
1. ISimCommand + MoveCmd/DeployCmd/SkillCmd - 命令数据结构
2. CommandBuffer - Tick 分组缓冲
3. ReplayFile + CommandRecorder - 录制+序列化
4. SimOrchestrator - Deploy→Battle→End 阶段驱动
5. 往返测试 - export→replay→checksum 一致
