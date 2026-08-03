# 开源开发使用文档设计

日期：2026-08-03  
基线：`origin/master` / `1ea98c563a79ee2201d860fa4108fd44001bd6e5`

## 目标

为 ZWebGLFrameWork 增加一份面向开源使用者的中文开发手册，使 Unity 开发者能够先跑通单机模式，再理解和接入确定性模拟、网络传输与权威帧同步参考服务端；同时为维护者提供准确的模块边界、扩展路径和验证入口。

文档必须明确区分已可直接使用的能力、仅供参考的实现和必须由业务项目补齐的集成点，不能把尚未闭环的联机链路描述为生产就绪。

## 目标读者

文档采用分层阅读路径，同时服务两类读者：

1. 首次接触项目的 Unity 开发者：需要环境准备、快速启动、最小代码示例和常见失败排查。
2. 框架维护者与集成者：需要架构边界、确定性约束、网络协议、服务端参考实现、扩展规则和测试门禁。

## 交付物

- 新建 `docs/DEVELOPMENT_GUIDE.md`，作为唯一开发使用正文。
- 修改根目录 `README.md`，增加能力摘要、环境要求和开发手册入口，不复制正文。
- 不拆分网络、帧同步或单机模式专题文档；后续能力扩展继续更新同一手册。

## 成熟度标记

每个主要能力在首次出现时使用下列标记：

| 标记 | 含义 |
|---|---|
| `Ready` | 仓库中已有可执行实现和验证入口，可用于框架开发。 |
| `Reference` | 仓库提供可构建、可测试的参考实现，但生产部署仍需业务配置与运维能力。 |
| `Integration Required` | 只提供契约、扩展点或 fallback，业务项目必须补齐实现。 |

初始分类如下：

- `Ready`：离线启动、单机战斗、确定性 `SimulationCore`、snapshot、checksum、Replay 基础流程。
- `Reference`：WebGL WSS、Native KCP、认证 envelope、`.NET 8` FrameSync/RealtimeHost、ReplayValidator CLI。
- `Integration Required`：正式 matchmaker/session 分配、客户端逐 tick 发送与 ack 消费、客户端预测/回滚协调器、生产内容热更新。

## 文档结构

`docs/DEVELOPMENT_GUIDE.md` 按以下顺序组织：

1. **框架定位与成熟度**：目标平台、三种运行模式、成熟度标记和非目标。
2. **快速开始**：Unity 版本、依赖恢复、打开项目、入口场景、离线启动、编译验证。
3. **统一运行模型**：用 Mermaid 图说明单机、帧同步客户端和参考服务端共享确定性 `SimulationCore`。
4. **项目结构与依赖边界**：说明 Core、Data、FSM、SimulationCore、Combat、Network、UI、Gameplay、Server 的职责和依赖方向。
5. **单机模式开发**：说明 `GameLauncher -> GameFSM -> MainVillageState -> FrameworkBattleAuthority -> CombatManager -> SimBattleSession` 的真实调用路径及 fallback 限制。
6. **SimulationCore 使用方法**：展示 session 创建、`Start`、`Submit`、`Tick`、`PresentationFrame`、snapshot、checksum 和 Replay 的最小闭环。
7. **网络层接入**：说明 `IRealtimeChannel`、WSS、KCP、认证凭据、连接生命周期和平台选择。
8. **帧同步参考服务端**：说明 `RealtimeSessionRouter -> FrameSyncMatchActor -> AuthorityFrameBroadcaster`、本地启动、NoOp/deadline/ack/checksum 和业务接入点。
9. **扩展框架**：给出新增命令、确定性内容、战斗模式、UI 面板、transport/provider 的最短路径和约束。
10. **Replay 与问题定位**：说明录制、回放、权威校验 CLI、退出码和 checksum 不一致的排查顺序。
11. **测试与构建门禁**：列出 Unity EditMode/PlayMode、`.NET 8` 测试、`ci_build.sh` 和提交前检查清单。
12. **已知边界与贡献规范**：集中说明未闭环能力、文档更新契约和贡献要求。

## 内容规则

- 中文为主，保留英文类型名、协议名、文件路径和命令。
- 代码示例必须来自当前公共 API，并形成可复制的最小闭环；不使用与源码不一致的伪 API。
- 首次引用类型时链接到真实源码路径；路径统一相对仓库根目录。
- 使用一张总览 Mermaid 图和一张权威帧同步数据流图，避免依赖 Unity Editor 截图。
- 不重复 `docs/superpowers` 中的审计历史和修复过程，只链接仍有参考价值的详细设计或问题登记。
- 不承诺仓库当前没有提供的生产服务、部署方案或自动化能力。

## 数据流与失败路径

手册必须同时说明正常路径和可观察失败：

- 启动：配置缺失、业务内容未配置、离线存档失败。
- Session：`StartResult`、`CommandSubmitResult`、`TickResult` 和 `SnapshotRestoreResult` 的拒绝语义。
- 网络：凭据过期、认证失败、错误 session、限流、迟到或未来输入、断线恢复。
- 帧同步：deadline 后 NoOp、ack 推进、checksum mismatch、超出 rollback window 后的 full resync 接入责任。
- Replay：结构拒绝、权威身份/初态不匹配、资源上限和稳定 CLI 退出码。

问题定位顺序固定为：先确认协议和身份，再确认 tick/sequence，再比较权威输入，最后比较 snapshot/checksum；不建议直接修改模拟状态掩盖分歧。

## README 边界

根 README 保持简短，只包含：

- 一段项目定位；
- Unity、C#、目标平台和 `.NET 8` 参考服务端的环境摘要；
- 单机、确定性模拟、WSS/KCP 和 Replay 能力列表；
- 指向 `docs/DEVELOPMENT_GUIDE.md` 的显著入口；
- 对参考服务端非生产就绪的简短提示。

## 维护契约

以下变化必须在同一提交中更新开发手册：

- 公共 API、启动流程或状态机路径；
- 新增、删除或重命名运行模式；
- 协议版本、消息 envelope、transport 或认证方式；
- SimulationCore 命令、snapshot、checksum 或 Replay contract；
- 框架扩展点、构建命令、最低环境版本；
- 任一能力从 `Integration Required` 到 `Reference` 或 `Ready` 的成熟度变化。

代码评审应把开发手册视为公共接口的一部分。README 只在入口、环境要求或顶层能力摘要变化时同步更新。

## 验证

实施完成后执行：

1. 检查文档中引用的文件、目录和命令在当前仓库中存在。
2. 检查示例使用的类型、方法、枚举和参数与源码一致。
3. 检查所有相对 Markdown 链接可解析，Mermaid 代码块语法闭合。
4. 运行 `dotnet test Server/ZWebGLFrameWork.Server.sln --nologo --verbosity minimal`。
5. 在 Unity 2022.3.62f3c1 可用时运行 `./ci_build.sh Validate`；若环境无法运行，交付说明必须明确标记。
6. 复查 `git diff --check` 和最终变更范围，确保只有 README、开发手册及其设计/计划文档发生预期变化。

## 非目标

- 不在本任务中实现客户端帧同步协调器、matchmaker、生产 session 服务或 YooAsset 热更新。
- 不修改现有协议、SimulationCore、网络传输或服务端行为。
- 不新增文档站点生成器、第三方文档依赖、截图资产或英文翻译版本。
- 不把历史审计文档重写成面向使用者的教程。
