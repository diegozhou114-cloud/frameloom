# ZWebGLFrameWork

Unity 2022.3 WebGL 框架 — Clash-of-Clans 风格的策略游戏，自研轻量 ECS + FSM + 响应式数据绑定。

## Project

- **引擎**: Unity 2022.3.62f3c1, C# 9.0, .NET Standard 2.1
- **入口**: `Assets/Scripts/FSM/Game/GameFSM.cs` (全局状态机: Launch→Login→MainVillage→Battle)
- **目标平台**: WebGL (主), Windows, Android, iOS
- **8 个 Assembly Definitions** (`Assets/Scripts/*/ZWebGLFrameWork.*.asmdef`)

## Commands

| 命令 | 用途 |
|------|------|
| `./ci_build.sh Validate` | 仅编译检查 (不打包) |
| `./ci_build.sh WebGL` | 编译 + WebGL 打包 |
| `./ci_build.sh Windows` | 编译 + Windows 打包 |
| Unity Editor → ZWebGL → Re-run Compile Hook | 手动触发编译后验证 |
| 测试 | **当前无测试 — 需要创建 Test Assembly** |

## Architecture

```
Assets/Scripts/
├── Core/          # 基础层: Singleton, EventBus, ObjectPool, AsyncManager
├── ECS/           # 自研轻量ECS: World, Entity, ComponentPool<SparseSet>, SystemBase
├── FSM/           # 状态机: FSM<T>, GameFSM(5状态+转换规则表+Loading过渡)
├── Data/          # 数据层: Observable<T>, ObservableModel, DataManager, SaveManager(5槽位JSON), ConfigLoader
├── Network/       # 网络层: TCP/UDP/HTTP/KCP四协议, Happy Eyeballs, 消息分发, 心跳
├── UI/            # UI层: UIManager(5层), UIStack(暂停恢复), UIAnimation, UIScreenAdapter
├── Combat/        # 战斗层: CombatMode(4模式), Orchestrator(Update-driven), ECS Systems, AI, Spawner
└── Gameplay/      # 玩法层: GridManager, CameraController, InputManager, BuildingPlacer
Assets/Editor/     # 编辑器: CompileHook, BuildScript, BuildValidator
```

**模块依赖链**: Core ← ECS ← FSM / Data / Network / UI ← Combat / Gameplay

## Conventions

- **命名空间**: 每个 asmdef 对应一个 `ZWebGLFrameWork.{Module}` 命名空间
- **单例**: 普通场景用 `Singleton<T>`, 跨场景用 `SingletonDontDestroy<T>`
- **事件**: 模块间通信用 `EventBus.Publish/Subscribe`，类型安全键用 `EventKey<T>`
- **数据绑定**: 数据模型继承 `ObservableModel`，字段用 `Observable<T>`，UI 订阅 `OnChanged`
- **ECS**: 组件实现 `IComponent` 接口，系统继承 `SystemBase`，通过 `World.ForEachEntities` 遍历
- **FSM**: 状态继承 `FSMState<T>`，状态标识用 enum，转换规则在 GameFSM 定义
- **战斗**: 新增模式继承 `CombatMode`，实现 `CheckWinCondition`/`CalculateResult`/`SpawnDefenders`/`SpawnAttackerWave`
- **文件限制**: 单文件 ≤ 200 行（Ruler 准则）
- **注释**: XML doc 注释 (`/// <summary>`)

## Notes

- 当前项目 **零测试覆盖** — 需要建立 EditMode + PlayMode test assembly
- Core/Coroutine 目录已创建但为空 — 待实现统一协程管理器
- 缺少: Audio, Scene, Logging, Localization, Resource/Asset 管理模块
- Reasonix 工作流定义在 `.reasonix/workflow.md`，任务状态机在 `.reasonix/task-router.md`
