# ZWebGLFrameWork

ZWebGLFrameWork 是面向策略游戏的 Unity 2022.3 通用框架，以同一套确定性 `SimulationCore` 支持单机战斗、网络传输和权威帧同步参考服务。

## 环境

- Unity `2022.3.62f3c1`
- C# 9 / .NET Standard 2.1
- WebGL（主要目标）、Windows、Android、iOS
- .NET 8（FrameSync、RealtimeHost、ReplayValidator 参考项目）

## 能力

- 离线启动、本地 JSON 多槽位存档和单机战斗
- 确定性 tick、command、snapshot、checksum 和表现帧
- 面向稳态分配控制的 buffered simulation output，以及 Replay、服务端参考消费者和 Combat 的复用式接入
- 表现帧插值与实体 GameObject 池化；`ObjectPool` / `GameObjectPool` 定义有界容量、所有权和生命周期契约
- WebGL WSS 与 Native KCP 传输 adapter
- Replay 录制、回放和 .NET 8 权威校验 CLI

## 开始开发

完整的运行方式、架构边界、代码示例、网络/帧同步接入、扩展流程和验证命令见：

**[开发者手册](docs/DEVELOPMENT_GUIDE.md)**

> `Server/` 提供可构建、可测试的参考组件，并非可直接上线的生产服务。正式 matchmaker/session 分配、客户端帧同步协调器、预测/回滚与 full resync 仍需业务项目集成。
