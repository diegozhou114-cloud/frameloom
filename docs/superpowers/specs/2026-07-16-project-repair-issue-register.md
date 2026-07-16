# ZWebGLFrameWork 审核问题登记表

**基线提交：** `4a6738302ade966ae7a5eae1abc51487b29402ca`

**目标架构：** 方案 A，SimulationCore 成为唯一正式战斗内核；Legacy Combat 仅在迁移门禁通过前保留。

**状态规则：** 本表初始状态全部为 `Open`。只有回归测试先出现预期失败、修复后通过，并且所属验证门禁通过，问题才能改为 `Closed`。删除旧实现只能在替代行为及其测试已经通过时关闭对应问题。

## 严重度

- `P0`：阻止导入、编译、启动、权威校验或核心战斗闭环。
- `P1`：可造成数据损坏、错误结果、断线、不同步、不可交互或稳定运行失败。
- `P2`：边界行为、资源泄漏、测试失真或可维护性问题，会削弱交付证据。

## 编译、资源与构建基线

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| BUILD-001 | P0 | `Assets/Editor/AutoFixProcessor.cs:82,108,132-143` 的 `FindUsingInsertPosition` 方法签名丢失，留下类级裸代码块。 | 恢复合法方法并通过 Editor 程序集编译。 | `EditorAssembly_AutoFixProcessor_Compiles` | G1 |
| BUILD-002 | P0 | `Assets/Scripts/SimulationCore/Systems/SimMovementSystem.cs:12` 与 `SimAttackSystem.cs:14` 使用无参数类型的 `ref` lambda，C# 语法无效。 | 使用与 `ForEachEntities` 委托完全一致的显式参数类型。 | `SimulationSystems_RefDelegates_Compile` | G1 |
| BUILD-003 | P0 | `Assets/Scripts/Gameplay/Economy/ResourceProducer.cs:63,69,75` 使用 `Math.Min` 但未导入 `System`。 | Gameplay 程序集编译通过。 | `GameplayAssembly_ResourceProducer_Compiles` | G1 |
| BUILD-004 | P0 | `Assets/Scripts/Combat/Core/CombatBootstrap.cs:17` 使用 `Debug` 但未导入 `UnityEngine`。 | Combat 程序集编译通过。 | `CombatAssembly_CombatBootstrap_Compiles` | G1 |
| ASSET-001 | P1 | 已跟踪 Assets 中存在大量缺失 `.meta` 的文件/目录，并有 `Assets/Scripts/Core/Async.meta` 孤儿。 | 固定编辑器导入后所有资产/目录有稳定 GUID、零孤儿，Git 保持干净。 | `RepositoryMetaAudit_HasNoMissingOrOrphanedMeta` | G0/G5 |
| ASSET-002 | P0 | 仓库没有单位 Prefab 或 `Config/ViewConfig.asset`，`PoolPrewarmer` 与表现桥无法解析生产资源键。 | 所有必需单位/View 资源存在且 BuildValidator 缺失时直接失败。 | `RequiredRuntimeAssets_AllProductionKeysResolve` | G0/G2/G5 |
| SCENE-001 | P0 | `ProjectSettings/EditorBuildSettings.asset:7` 场景列表为空；BuildScript 又扫描所有场景而非启用顺序。 | `Launcher.unity` 为启用的 scene 0，构建严格使用启用列表。 | `BuildScenes_LauncherIsEnabledSceneZero` | G0/G5 |
| SCENE-002 | P0 | `Assets/Scence/Launcher.unity` 只有 Camera/Light，主城状态也未组装网格、输入、选择、建筑视图。 | Launcher PlayMode 可进入可交互主城，所有必需服务恰好创建一次。 | `LauncherPlayMode_EntersInteractiveVillage` | G2/G5 |
| CI-001 | P1 | CI matrix 写 `2022.3.40f1`，项目锁定 `2022.3.62f3c1`，GameCI actions 没有接收 matrix 版本。 | 所有 Unity action 显式使用项目固定版本。 | `Workflow_AllUnityActionsUsePinnedProjectVersion` | G1/G5 |
| CI-002 | P1 | CI path filters 漏掉 Server、场景、Resources、workflow，push 还漏 Tests。 | 所有会影响 G0-G5 的路径都触发对应 job。 | `Workflow_TriggersCoverAllGateInputs` | G0-G5 |
| CI-003 | P0 | 三个测试 asmdef 均为 Editor-only；PlayMode job 不拒绝 0 tests，可能假绿。 | 增加非 Editor PlayMode 程序集并校验预期非零测试数。 | `PlayModeGate_RequiresExpectedNonZeroTestCount` | G2/G5 |
| CI-004 | P0 | CI 没有 .NET 8 build/test job，ReplayValidator 和 FrameSync server 破损不阻断合并。 | 所有 server project 在 Linux/Windows Release warn-as-error 构建并测试。 | `CI_ServerProjects_BuildAndTestReleaseWarnAsError` | G1/G2/G3 |
| CI-005 | P1 | unity-builder 没有调用仓库 BuildScript，也没有浏览器/Windows 启动 smoke 或 artifact 校验。 | 构建显式走 BuildScript，并验证目标产物和启动行为。 | `CI_BuildJobs_InvokeRepositoryBuildScriptAndSmokeArtifacts` | G5 |

## 启动、数据、UI 与 Gameplay

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| BOOT-001 | P0 | `FSM.cs:104-109` 在无当前状态时把来源映射为 `default(GameState)=Launch`，首次 `Launch -> Launch` 被拒绝。 | 仅首次无状态进入初始状态时绕过普通转换守卫。 | `GameFSM_InitialTransition_EntersLaunchExactlyOnce` | G2/G5 |
| BOOT-002 | P0 | Pause 只保存前一枚举，恢复 Battle 时把 `GameResumeEvent` 传给只接受 `BattleParams` 的状态。 | 暂停上下文保存目标状态、原始 typed payload 和暂停信息，恢复不重启战斗。 | `GameFSM_PauseResume_PreservesBattleContext` | G2 |
| BOOT-003 | P1 | `LaunchState` 以 `.Forget()` 启动管线，退出状态不取消下载/预加载，晚到任务仍可切状态。 | 状态退出取消全部异步工作，取消后不再发布或转换。 | `LaunchState_Exit_CancelsPipelineAndPreventsLateTransition` | G2 |
| BOOT-004 | P1 | 热更新下载返回 `false` 和 `NeedRestart=true` 都被 LaunchState 忽略。 | 失败进入可重试错误态；需要重启时阻止继续启动。 | `LaunchState_HotUpdateFailureOrRestart_DoesNotContinue` | G2/G5 |
| BOOT-005 | P1 | SceneLoader 在不存在场景/超时时正常返回，LoadingState 仍发布成功并切状态。 | 加载返回明确结果；失败/超时只发布失败。 | `LoadingState_SceneTimeout_PublishesFailureOnly` | G2/G5 |
| DATA-001 | P0 | SaveManager 与 LocalStore 首写直接 `File.Replace`，目标不存在时失败。 | 首写创建，后续原子替换；失败保留旧文件。 | `Persistence_FirstWrite_CreatesDestinationAtomically` | G2/G5 |
| DATA-002 | P1 | Building DTO 缺 Id/State/UpgradeEndTime，加载还丢弃已保存 Level。 | 建筑完整状态和身份往返一致。 | `SaveGame_BuildingRoundTrip_PreservesCompleteState` | G2 |
| DATA-003 | P1 | 自动保存只观察 Gold/Elixir；失败仍推进基线，退出状态不兜底保存。 | 所有持久状态变化可检测；失败不推进基线；退出成功保存。 | `MainVillageAutosave_FailureDoesNotAdvanceBaseline` | G2 |
| DATA-004 | P1 | GameInitializer/Login 忽略创建或加载失败，仍记录成功并进入半初始化主城。 | 仅成功创建/加载后进入主城，失败停留在恢复 UI。 | `OfflineLogin_LoadFailure_DoesNotEnterVillage` | G2/G5 |
| DATA-005 | P0 | CombatEndEvent 奖励只显示，不写入 PlayerData、不钳制、不保存，也无 exactly-once。 | 有效结果奖励恰好应用一次并在返回主城前保存；无效/重复结果不奖励。 | `BattleResult_ValidCompletion_AppliesAndSavesRewardsOnce` | G2/G5 |
| UI-001 | P0 | UIManager/Launcher 均未创建 EventSystem/InputModule，动态 Button 不可点击。 | 全局恰好一个兼容 EventSystem。 | `UIManager_Initialize_CreatesSingleEventSystem` | G2/G5 |
| UI-002 | P1 | 缓存面板每次重开重新 BuildUI；已打开未暂停时又不会重绑新 data。 | 静态层级只建一次，每次 Open 绑定当前数据且监听数回归基线。 | `Panel_Reopen_ReusesHierarchyAndRebindsData` | G2 |
| UI-003 | P1 | BuildingInfoPanel 私有 Awake 隐藏 UIBase 生命周期且不调用 base。 | 派生类显式 override 并完成 CanvasGroup/初始状态初始化。 | `BuildingInfoPanel_Awake_InitializesUIBase` | G2 |
| GAME-001 | P1 | VillageData/GridManager 只查询建筑原点，忽略 SizeX/SizeY，多格建筑可重叠。 | 任一足迹格都映射到所属建筑并阻止重叠。 | `VillageOccupancy_MultiCellFootprintBlocksEveryCell` | G2 |
| GAME-002 | P2 | ResourceProducer 只在 Start 计算产率，建筑增删/升级后不更新。 | 订阅村庄变化并重算，退出解除订阅。 | `ResourceProducer_BuildingChange_RecalculatesRates` | G2 |
| GAME-003 | P1 | 名称匹配先判 `Elixir`，`Dark Elixir Drill` 被归入普通圣水。 | 配置使用明确 ResourceType，暗黑重油只增加对应资源。 | `ResourceProducer_DarkElixirDrill_ProducesDarkElixirOnly` | G2 |
| GAME-004 | P1 | 跨场景 Input/Selection/Placer 只在 Start 缓存 Camera/Grid 引用，换场景后悬空。 | `sceneLoaded` 后重绑，旧引用和订阅被清理。 | `SceneChange_GameplayServices_RebindToNewScene` | G2 |

## Legacy Combat 迁移问题

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| COMBAT-001 | P0 | CombatManager 的启动、阶段、部署、结束部署都绑定 Legacy；StartSimBattle 无调用者，开关开启会解引用未初始化字段。 | 所有生产操作只经 `ISimBattleSession`；完成迁移后删除开关与 Legacy。 | `BattleState_UsesSingleSimulationSession_EndToEnd` | G2/G5 |
| COMBAT-002 | P0 | BattleParams 的双方村庄未消费；Legacy 使用全局村庄，Sim 也使用同一村庄并硬编码 seed 42。 | BattleInit 明确携带并校验模式、seed、双方身份和权威布局。 | `BattleInit_UsesRequestedDefenderAndSeed` | G2/G3 |
| COMBAT-003 | P1 | Legacy A* 对角穿角检查使用 `&&`，单侧墙仍可穿越。 | 迁移期修正行为；最终由确定性寻路替代并保留等价测试。 | `LegacyAStar_DiagonalBesideSingleWall_IsRejected` | G2 |
| COMBAT-004 | P1 | 建筑无 Movement/Target，UnitAI 跳过，AttackSystem 又要求已有 Target，防御永不攻击。 | SimulationCore 索敌与移动能力解耦，防御可稳定攻击。 | `DefenseBuilding_AcquiresAndDamagesAttacker` | G2/G3 |
| COMBAT-005 | P0 | Campaign/BuilderBase 以零攻击方开始，首波前就判防守胜；BuilderBase 又无波次来源。 | 每个支持模式定义零攻击方启动、波次、超时、平局和终局语义。 | `EachSupportedMode_HasDefinedZeroAttackerAndTerminalBehavior` | G2/G3 |
| COMBAT-006 | P1 | 部署 API 直接生成实体，不校验阶段、玩家、坐标、白名单、数量或占用。 | 只接受带 tick/player/sequence 的验证后 DeployCmd；非法命令不改状态。 | `DeployCommand_RejectsOutOfPhaseDisallowedOrOverLimit` | G2/G3 |
| COMBAT-007 | P0 | VillageAttack 结果使用 Unity Random/Time，Unity 与服务端不能复现。 | 结算完全来自确定性 Session tick 状态。 | `CombatResult_UnityAndDotNet_AreIdentical` | G2/G3 |

## SimulationCore、命令、快照与表现

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| SIM-001 | P0 | noEngineReferences=true，但 SimOrchestrator、FrameInput、KcpTransport 直接调用 UnityEngine.Debug。 | Core 零 Unity 引用，日志经 ISimLogger/结果返回，传输移到宿主程序集。 | `SimulationCore_HasNoUnityEngineReferences` | G1/G3 |
| SIM-002 | P0 | ReplayValidator 调用已不存在的 `SimWorld(int,logger)` 构造。 | Unity/.NET 8 通过同一 Session factory 构造 Core。 | `ReplayValidator_DotNet8_BuildsAgainstCurrentSimulationCore` | G1 |
| SIM-003 | P0 | 没有统一 Session/Init/TickResult；Unity 在 Tick 外直接改 World，Validator 不执行相同 AI。 | Session 独占状态；所有宿主只提交命令和消费 TickResult。 | `UnityAndValidator_SameBattleInitAndCommands_SameTickResults` | G2/G3 |
| SIM-004 | P0 | ComponentTypeRegistry 按调用顺序分配 ID，SimChunkHelper 却假定固定 0..5 映射。 | 组件 schema ID 稳定、版本化且与注册顺序无关。 | `ComponentRegistry_PermutedRegistration_PreservesTypeMapping` | G2/G3 |
| SIM-005 | P0 | Get/Try/Has/Set 不校验 generation；回滚和 ID 重用可让旧句柄指向新实体。 | 所有访问验证 ID+generation，allocator/generation 完整进入快照。 | `RollbackThenReuseId_StaleEntityHandleIsRejected` | G3 |
| SIM-006 | P1 | 系统遍历和 checksum 仍依赖 Dictionary/chunk/swap-remove 物理顺序。 | 系统、事件和 hash 按显式稳定实体键执行。 | `EquivalentWorlds_DifferentChunkHistories_SameOrderAndChecksum` | G3 |
| SIM-007 | P0 | checksum 漏路径内容、allocator、tick、phase、计时器、winner、RNG、系统时钟、事件和命令顺序。 | hash 覆盖所有会影响未来状态的数据。 | `Checksum_ChangesForPathPhaseAllocatorAndPendingEventDifferences` | G3 |
| SIM-008 | P0 | 含 List Path 的 struct 仅 Array.Copy，历史快照被后续修改污染；编排器/系统状态也未保存。 | 使用确定性深拷贝值容器并快照全部确定性状态。 | `Snapshot_PathMutationAndSystemClock_AreIsolatedAndRestored` | G3 |
| SIM-009 | P0 | 保存 after-T 快照却恢复 CurrentTick=T，重复执行；tick0 不重置 allocator；缺快照静默返回。 | 恢复后从 T+1 继续；精确恢复；超窗返回 resync 结果。 | `Rollback_AfterTickT_ResumesAtTPlusOneWithOriginalChecksumSequence` | G3 |
| SIM-010 | P1 | StartBattle 不清世界、命令、LastChecksum 或系统时钟，重复启动叠加状态。 | Session Start/Reset 与全新实例得到完全相同初态。 | `SessionRestart_MatchesFreshSessionState` | G2/G3 |
| SIM-011 | P1 | LogicInt 乘除/百分比存在中间值溢出和 long.MinValue 精度丢失，跨运行时语义不明确。 | 定义并实现位级一致的宽中间值或 checked/saturating 规则。 | `LogicInt_ExtremeMultiplyDividePercent_HasDefinedExactResult` | G2/G3 |
| SIM-012 | P0 | Deploy/Move/Skill handler 仅日志占位且无严格验证。 | 三类命令实现真实确定性行为，非法命令明确拒绝且状态不变。 | `DeployMoveSkill_ValidCommandsMutateState_InvalidCommandsDoNot` | G2/G3 |
| SIM-013 | P0 | 命令无 sequence；同 Tick 同玩家只按 PlayerId 排序，依赖到达顺序。 | 总序为 `(tick,playerId,sequence)`，重复/倒退 sequence 拒绝。 | `Commands_SameTickSamePlayer_AreOrderedBySequence` | G3/G4 |
| SIM-014 | P0 | Orchestrator 先 Drain，Recorder 再 Peek，因此 Replay 记录不到实际命令。 | Recorder 只记录 TickResult.ExecutedCommands。 | `Recorder_RecordsExactlyTheCommandsExecutedThisTick` | G3 |
| SIM-015 | P0 | AI/寻路在 Unity CombatManager 外置直接改 World，服务器无同等逻辑；Core 无完整索敌。 | AI、波次、索敌和防御全部进入 Session/Core。 | `DefenseTargetingAndAI_UnityAndDotNet_ProduceSameChecksums` | G2/G3 |
| SIM-016 | P1 | Core 无模式规则；双方为零、波次、超时和平局行为不完整。 | BattleInit.Mode 选择完整且可测试的规则集。 | `EachSupportedMode_HasDefinedZeroAttackerAndTerminalBehavior` | G2/G3 |
| SIM-017 | P0 | Bridge 在初始实体创建后订阅，固定使用 Entries[0]，缺 ViewConfig 时可能 null。 | TickResult 提供带 archetype 的初始/增量生命周期，按 ID 映射 prefab。 | `PresentationBridge_CreatesInitialViewsUsingCorrectArchetype` | G2/G5 |
| SIM-018 | P1 | 插值每渲染帧覆盖 previous；回滚不重建视图；退出不 Cleanup。 | 表现维护相邻逻辑 Tick，回滚校正，退出零视图/订阅泄漏。 | `Presentation_RendersBetweenTicks_AndResyncsAfterRollbackWithoutLeaks` | G2/G3/G5 |

## 网络、Replay 与服务端

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| NET-001 | P0 | NetworkManager 用无参 lambda 订阅 Action<string>，且解除的是从未订阅的方法组。 | 类型正确的存储 handler 可确定解绑，旧连接回调不泄漏。 | `NetworkManager_Reconnect_DoesNotLeakOrDispatchStaleCallbacks` | G1/G2/G4 |
| NET-002 | P0 | Socket/Thread/TCP/UDP/KCP 全进入无平台限制程序集，WebGL 无 WSS adapter。 | Contracts、WebGL、Native asmdef 分离，WebGL 零 native socket 引用。 | `WebGLAssembly_HasNoSocketThreadOrNativeKcpReferences` | G1/G4/G5 |
| NET-003 | P1 | HttpClient 强制 `http://`，HTTPS 页面产生 mixed content。 | 线上 endpoint 必须 HTTPS，WebGL 使用 IHttpApi。 | `WebGlHttpApi_RejectsInsecureHttpEndpoint` | G4/G5 |
| NET-004 | P0 | Happy Eyeballs 首个失败、第二个成功时会把实际 winner 作为 loser 关闭。 | winner 唯一转移所有权，失败/cancel/loser socket 全部处置。 | `HappyEyeballs_FirstFails_SecondWinnerRemainsOpen` | G2/G4 |
| NET-005 | P1 | TcpClient 忽略 EndSend 实际字节数，短写截断 frame。 | stream transport 循环发送完整 frame 或明确失败。 | `TcpTransport_PartialSend_CompletesWholeFrame` | G2/G4 |
| NET-006 | P0 | async void Connect 无取消，迟到连接可覆盖新 socket/state。 | 每次连接绑定 lifetime token，迟到 socket 立即销毁。 | `TcpClient_DisconnectDuringConnect_DiscardsLateSocket` | G2/G4 |
| NET-007 | P0 | 自研 KCP 随机 conv、本地 bind 即 Connected，无认证握手或来源/session 绑定。 | pinned kcp2k adapter 通过认证握手获得 session/conv 并拒绝异源包。 | `NativeRealtime_Handshake_AgreesSessionAndRejectsForeignConv` | G4 |
| NET-008 | P0 | KcpCore 对首段 Frg>0 的分片消息永久返回不完整。 | 删除自研实现；kcp2k 多分片 golden/loopback 通过。 | `NativeKcp_FragmentedMessage_RoundTrips` | G3/G4 |
| NET-009 | P0 | KcpCore 用 selective maxAck 覆盖 cumulative una，可误删缺口段。 | 删除自研实现；丢包/乱序/重复测试不丢未确认段。 | `NativeKcp_GappedAck_DoesNotDropMissingSegment` | G3/G4 |
| NET-010 | P0 | FrameSyncClient 从未使用 sender，所谓发送函数只消费远端输入。 | 每 tick 发送本地 versioned envelope 并处理 ack。 | `FrameSyncClient_EachTickSendsLocalInputAndProcessesAck` | G2/G4 |
| NET-011 | P0 | PendingCount 被当作权威 tick 就绪；未来命令可连续空跑，无双方输入/deadline/NoOp。 | AuthorityFrame 每次只推进一 tick，六 tick deadline 后显式 NoOp。 | `FrameSync_EmptyInput_AdvancesExactlyOneTickViaNoOp` | G3/G4 |
| NET-012 | P0 | prediction 只改计数；缺 checksum 静默接受；mismatch 只 Restore 不重放，超窗不 resync。 | 真实预测和权威重放；超窗请求 full resync。 | `FrameSync_OutOfWindowMismatch_RequestsFullResync` | G3/G4 |
| NET-013 | P1 | FrameInput/InputPacket 无版本/session/auth，非法值截断，短包越界，ack 未接线。 | Versioned envelope 严格验证长度、范围、来源、session 和 rate。 | `AuthorityEnvelope_RejectsShortOversizedWrongVersionAndForeignSession` | G2/G3/G4 |
| NET-014 | P1 | MessageDispatcher 4 字节 ID 最终截为 ushort；接收队列无界且每帧 drain 到空。 | Codec 使用真实 uint ID；队列有容量与每帧预算，溢出可观测。 | `MessageDispatcher_UIntIdAndBackpressure_ArePreserved` | G2/G4 |
| REPLAY-001 | P0 | 空/null Records 零次循环后返回 valid。 | 空、null、缺初态结构化拒绝。 | `ReplayValidator_EmptyReplay_IsRejected` | G2/G3 |
| REPLAY-002 | P0 | Validator 直接信任客户端 seed/layout。 | 服务端按 matchId 解析权威 BattleInit，客户端声明必须匹配。 | `ReplayValidator_TamperedSeedOrLayout_IsRejectedAgainstMatch` | G3 |
| REPLAY-003 | P1 | Version 未检查，且缺 schema/config hash、tick rate、match/final metadata。 | 完整版本化 Replay contract，未知/不匹配元数据拒绝。 | `ReplayValidator_UnknownVersionOrConfigHash_IsRejected` | G2/G3 |
| REPLAY-004 | P0 | 以 Records.Count 猜 tick 并 Find；缺失、重复、乱序、截断、尾随无预检。 | tick 0..final 连续、唯一、有序并与命令 tick 一致。 | `ReplayValidator_GapDuplicateReorderOrTruncation_IsRejected` | G3 |
| REPLAY-005 | P0 | Ended 后先 break，终局 checksum 未校验，尾随记录被忽略。 | 校验终局 tick、final result/checksum 并拒绝尾随记录。 | `ReplayValidator_TerminalChecksumAndTrailingRecords_AreValidated` | G3 |
| REPLAY-006 | P1 | unknown command 可能 Parse 抛异常；玩家/坐标/archetype/payload 未验证。 | 所有非法输入返回确定性 rejection reason。 | `ReplayValidator_UnknownOrInvalidCommand_IsRejected` | G2/G3 |
| REPLAY-007 | P1 | CLI ReadAllText 无文件、tick、command、布局或耗时上限。 | 入口 size/count/time/rate 上限，超限非零退出。 | `ReplayCli_OversizedOrOverLimitReplay_IsRejected` | G3 |
| SERVER-001 | P0 | FrameSync server 只有 Markdown，无可构建项目或测试。 | .NET 8 match actor、20Hz authority session、WSS/native ingress 可构建测试。 | `FrameSyncServer_TwoClients_ReachEqualChecksumAndResult` | G1/G3/G4 |
| SERVER-002 | P0 | 服务端伪码无限等双方输入，一方丢包即可永久停 tick。 | InputDeadlineTicks=6 后插入显式 NoOp 并广播权威 frame。 | `FrameSyncServer_MissingInputAfterDeadline_InsertsNoOp` | G3/G4 |
| SERVER-003 | P0 | 设计仍为 custom KCP-only，无 WSS、统一 AuthorityFrame、认证/version/source/rate checks。 | WSS/native 解码同一契约并进入同一 match actor。 | `FrameSyncServer_WssAndNativeDecodeSameAuthorityVector` | G3/G4 |

## 现有测试可信度问题

| ID | 级别 | 已确认问题与证据 | 关闭条件 | 最小回归测试 | 门禁 |
|---|---|---|---|---|---|
| TEST-001 | P1 | DifferentSeed 测试传入 RNG 但 SetupBattle 完全不使用，断言与实现矛盾。 | seed 进入 BattleInit/确定性状态后再测试不同 seed。 | `DifferentSeed_ChangesSeededInitialStateChecksum` | G2/G3 |
| TEST-002 | P1 | RoundTrip 测试没有序列化/读取 Replay，只把同一外部 AI 跑两遍。 | 真实 Recorder 导出、解析并由 Validator 重放。 | `ReplayExportParseValidate_RoundTripsExecutedCommands` | G3 |
| TEST-003 | P1 | rollback 重演只断言 checksum 非零，未与原始逐 tick 序列比较。 | 对比恢复点至终局的每一 tick checksum。 | `RollbackReplay_MatchesOriginalChecksumSequence` | G3 |
| TEST-004 | P1 | FrameSync 测试不使用 csAt10，只检查 rollback 计数/AuthorityTick。 | 验证恢复、权威输入重放和最终完整 world checksum。 | `FrameSyncMismatch_ReplaysToMatchingWorldChecksum` | G3/G4 |
| TEST-005 | P1 | BattleE2E 手工消费 DamageEvent/扣血，不运行真实 DamageSystem；胜负测试多为常量断言。 | 测试真实系统组和完整 Session 流程。 | `BattleSession_AttackDamageDeathResult_EndToEnd` | G2/G3 |
| TEST-006 | P2 | Benchmark 测试实体数不符、无断言，且被说明为永远通过。 | Benchmark 移出正确性门禁并校验准确 workload。 | `BenchmarkFixture_CreatesDeclaredEntityCount` | G2 |
| TEST-007 | P1 | LogicInt 的 QScaled 测试只调用 Math.BigMul，没有调用 LogicInt。 | 极值用例直接覆盖 LogicInt 公共运算符及跨运行时 golden。 | `LogicInt_Extremes_UsePublicOperatorsAndGoldenVectors` | G2/G3 |

## 关闭顺序约束

1. `BUILD/ASSET/SCENE/CI` 是所有行为修复的前置门禁。
2. `BOOT/DATA/UI/GAME` 必须先形成可重复的离线启动和存档闭环。
3. `SIM-003/012/013/014` 的统一 Session 与真实命令流是 Replay、FrameSync 和 Legacy 删除的前置条件。
4. `SIM-005..011` 的完整状态/确定性契约通过后，才能信任 checksum、rollback 或服务端验证。
5. `COMBAT-*` 只有在对应 SimulationCore 替代测试通过后才能随 Legacy 删除关闭。
6. `NET-007..009` 只有 pinned `kcp2k` 替代及网络损伤测试通过后才能随自研 KCP 删除关闭。
7. `REPLAY-*` 和 `SERVER-*` 必须在同一 SimulationCore Session 上通过，不能维护第二套仿真循环。
