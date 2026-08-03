# 延期的权威服务与联机验证工作（16 项）

日期：2026-08-03
基线：`master` / `d7d9a261e8b5a6ac8b91cae0dc556e4795509918`

## 目的与范围

当前仓库只搭建 Unity 客户端框架，不包含服务端实现。下列 16 项依赖未来正式项目提供的权威会话、帧同步或回放数据来源，因此继续保持 `Open/TODO`。它们是有意延期，不代表已经修复、关闭或放弃；在依赖和证据齐备前，不得降低原关闭条件或以客户端占位实现替代权威实现。

当前阶段明确不实现：

- 服务端可执行项目或部署代码；
- 认证、会话签发和密钥服务；
- 权威 match actor、tick scheduler 或输入截止策略；
- 权威回放记录 provider；
- 正式项目的生产协议、endpoint 和运维配置。

客户端可以保留必要的抽象接口和 TODO，但不得伪造服务端成功路径，也不得把未来协议细节固化为生产实现。

## 网络与帧同步（5 项）

| ID | 优先级 | 当前缺陷 | 未来完成条件 | 验收测试 | 门禁 |
|---|---|---|---|---|---|
| NET-007 | P0 | 自研 KCP 随机 conv、本地 bind 即 Connected，无认证握手或来源/session 绑定。 | pinned kcp2k 仅承担可靠传输；HTTPS 签发 session/key，datagram 以 sequence+HMAC 验证、1024 replay window 和受控 endpoint rebinding。 | `NativeRealtime_Handshake_AgreesSessionAndRejectsForeignConv` | G4 |
| NET-010 | P0 | FrameSyncClient 从未使用 sender，所谓发送函数只消费远端输入。 | 每 tick 发送本地 versioned envelope 并处理 ack。 | `FrameSyncClient_EachTickSendsLocalInputAndProcessesAck` | G2/G4 |
| NET-011 | P0 | PendingCount 被当作权威 tick 就绪；未来命令可连续空跑，无双方输入/deadline/NoOp。 | Match actor 按 monotonic 20Hz 计划 tick；计划时间后六个 50ms epoch 封存并插 NoOp，迟到输入明确拒绝且每帧只推进一 tick。 | `FrameSync_EmptyInput_AdvancesExactlyOneTickViaNoOp` | G3/G4 |
| NET-012 | P0 | prediction 只改计数；缺 checksum 静默接受；mismatch 只 Restore 不重放，超窗不 resync。 | 真实预测和权威重放；超窗请求带身份/版本/checksum 的原子 full resync。 | `FrameSync_OutOfWindowMismatch_RequestsFullResync` | G3/G4 |
| NET-013 | P1 | FrameInput/InputPacket 无版本/session/auth，非法值截断，短包越界，ack 未接线。 | Versioned envelope 严格验证长度、范围、来源、session 和 rate。 | `AuthorityEnvelope_RejectsShortOversizedWrongVersionAndForeignSession` | G2/G3/G4 |

## Replay（7 项）

| ID | 优先级 | 当前缺陷 | 未来完成条件 | 验收测试 | 门禁 |
|---|---|---|---|---|---|
| REPLAY-001 | P0 | 空/null Records 零次循环后返回 valid。 | 空、null、缺初态结构化拒绝。 | `ReplayValidator_EmptyReplay_IsRejected` | G2/G3 |
| REPLAY-002 | P0 | Validator 直接信任客户端 seed/layout，单独 Replay 文件无法识别伪造初态。 | `Validate(replay, AuthoritativeMatchRecord)` 按 matchId/ownership/nonce 解析权威 BattleInit，客户端声明必须匹配；CLI 接收固定 fixture/provider。 | `ReplayValidator_TamperedSeedOrLayout_IsRejectedAgainstMatch` | G3 |
| REPLAY-003 | P1 | Version 未检查，且缺 schema/config hash、tick rate、match/final metadata。 | 完整版本化 Replay contract，未知/不匹配元数据拒绝。 | `ReplayValidator_UnknownVersionOrConfigHash_IsRejected` | G2/G3 |
| REPLAY-004 | P0 | 以 Records.Count 猜 tick 并 Find；缺失、重复、乱序、截断、尾随无预检。 | tick 0..final 连续、唯一、有序并与命令 tick 一致。 | `ReplayValidator_GapDuplicateReorderOrTruncation_IsRejected` | G3 |
| REPLAY-005 | P0 | Ended 后先 break，终局 checksum 未校验，尾随记录被忽略。 | 校验终局 tick、final result/checksum 并拒绝尾随记录。 | `ReplayValidator_TerminalChecksumAndTrailingRecords_AreValidated` | G3 |
| REPLAY-006 | P1 | unknown command 可能 Parse 抛异常；玩家/坐标/archetype/payload 未验证。 | 所有非法输入返回确定性 rejection reason。 | `ReplayValidator_UnknownOrInvalidCommand_IsRejected` | G2/G3 |
| REPLAY-007 | P1 | CLI ReadAllText 无文件、tick、command、布局或耗时上限。 | 强制 16MiB/12000 ticks/64 commands-per-tick/100000 total/4096 entities/8MiB snapshot 上限，超限 exit 5；wall-clock 只作安全取消。 | `ReplayCli_OversizedOrOverLimitReplay_IsRejected` | G3 |

## 服务端（3 项）

| ID | 优先级 | 当前缺陷 | 未来完成条件 | 验收测试 | 门禁 |
|---|---|---|---|---|---|
| SERVER-001 | P0 | FrameSync server 只有 Markdown，无可构建项目或测试。 | .NET 8 单线程 match actor、monotonic 20Hz scheduler、WSS/native ingress 和 session 生命周期可构建测试，覆盖 WSS-WSS/native-native/WSS-native。 | `FrameSyncServer_TwoClients_ReachEqualChecksumAndResult` | G1/G3/G4 |
| SERVER-002 | P0 | 服务端伪码无限等双方输入，一方丢包即可永久停 tick。 | InputDeadlineTicks=6 后插入显式 NoOp 并广播权威 frame。 | `FrameSyncServer_MissingInputAfterDeadline_InsertsNoOp` | G3/G4 |
| SERVER-003 | P0 | 设计仍为 custom KCP-only，无 WSS、统一 AuthorityFrame、认证/version/source/rate checks。 | WSS/native 解码同一契约并进入同一 match actor。 | `FrameSyncServer_WssAndNativeDecodeSameAuthorityVector` | G3/G4 |

## 测试可信度（1 项）

| ID | 优先级 | 当前缺陷 | 未来完成条件 | 验收测试 | 门禁 |
|---|---|---|---|---|---|
| TEST-004 | P1 | FrameSync 测试不使用 csAt10，只检查 rollback 计数/AuthorityTick。 | 验证恢复、权威输入重放和最终完整 world checksum。 | `FrameSyncMismatch_ReplaysToMatchingWorldChecksum` | G3/G4 |

## 未来实施顺序

1. 定义服务端、match、认证和 session 生命周期契约，并提供可测试的权威数据来源。
2. 固定 WSS/native 共用的 versioned authority envelope、身份验证、ack 和重放防护。
3. 实现权威 tick 调度、输入截止、NoOp、预测、rollback/replay 和 full resync。
4. 接入权威 match record，完成 Replay contract、限制校验和 CLI provider。
5. 运行 WSS-WSS、native-native、WSS-native 端到端测试，并以逐 tick/final checksum 收口。

每一项仍须按 red/fix/green/gate 证据流程独立关闭；仅有接口、TODO、mock、Markdown 或客户端自测不得作为关闭证据。若正式项目改变协议或权威来源，应先更新问题登记表和本文件，再开始实现。

## 依据

- [项目修复问题登记表](./2026-07-16-project-repair-issue-register.md)
- [项目修复证据账本](./2026-07-16-project-repair-evidence.json)
