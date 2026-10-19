# 迷你攻城：同机双客户端帧同步验证

目标是用两名玩家在同一台电脑上进行一场真实网络对战。客户端连接独立 .NET 8 WebSocket 服务，每个逻辑帧由服务器广播双方操作，客户端独立计算战斗结果；界面显示帧号与校验状态，便于观察是否同步。

## 构建和启动

先退出打开此项目的 Unity Editor，避免命令行构建与编辑器争用项目。

```bash
./scripts/minisiege/build.sh macos
./scripts/minisiege/run.sh macos
```

默认生成 `~/Desktop/MiniSiegeDemo/MiniSiege.app` 和 `Server/`。也可双击输出目录中的 `启动双人对战.command`，不依赖工程目录；保持打开的终端窗口以维持服务。运行脚本启动服务并打开两个独立游戏窗口；两个窗口自动连接，第二个连接后开始对局。服务默认地址为 `ws://127.0.0.1:8787/siege`，日志在输出目录的 `server.log`。关闭游戏后，在终端按 Ctrl+C 停止服务。

构建日志为 `build-macos.log`。脚本使用本机 Unity 2022.3.62f3c1 ARM Editor；可通过 `UNITY_EDITOR` 指定其他兼容 Editor，通过 `MINISIEGE_OUTPUT` 指定输出目录。

```bash
MINISIEGE_OUTPUT=/tmp/MiniSiegeDemo ./scripts/minisiege/build.sh macos
MINISIEGE_OUTPUT=/tmp/MiniSiegeDemo ./scripts/minisiege/run.sh macos
```

## 对战规则和验证

双方目标是摧毁对方基地；3 分钟结束时基地血量较高的一方获胜。Q / E 选择上、下路线，1 部署近战护卫，2 部署远程弓手，3 释放有冷却的范围冲击。资源自动恢复，后半场双倍恢复。让护卫保护弓手，用冲击清理中场敌军，在两条路线之间分配兵力；具体费用显示在按钮上。点击“自动出兵”可以让一个窗口自动出兵，便于一个人测试。

1. 两个窗口连接同一个地址并加入一局。
2. 在两个窗口交替出兵，检查双方看到相同的基地血量、单位和胜负。
3. 切换窗口时，未聚焦的客户端仍持续运行。两端帧号可能短暂相差一帧，校验结果应持续匹配。
4. 一方基地被摧毁后检查双方结果一致；两个窗口点击“重新连接”，加入下一局。

这个演示用于本地验证帧同步概念，不包含账号、匹配服务或正式部署流程。局域网测试时，一台电脑运行服务，两台客户端填写该电脑内网 IP，端口 8787。

## 自动验证与记录

关闭手动对战服务后，可运行 `./scripts/minisiege/verify.sh`（桌面产物目录中直接运行 `./verify.sh`）。它会启动独立服务器和两个真实 macOS 客户端，开启自动出兵，打到终局后逐帧比较双方记录，并检查每端对服务器的校验；成功写入 `Verification/macos-verification.json`，自动结束测试程序和服务器。通常一局约一分钟，最多三分钟。

`Verification/` 内包含双方逐帧 CSV、程序日志和游戏截图。校验覆盖逻辑状态、资源、技能冷却、输入序列与 ECS 世界；客户端只在收到有序操作帧后推进逻辑，Unity 的帧时间仅影响显示插值。断线会停止操作并提示重连，不进行回滚或中途加入。

本次已验证：macOS 通用包构建成功（零构建错误）；两个打包客户端完整对局各 1,398 帧全部一致，蓝方获胜，基地血量 22:0；最终包另跑 3,600 帧完整限时对局全部一致，红方以 26:10 获胜；模拟测试 158 项通过。WebGL 已提供传输桥接与备用构建入口，但本机缺少 Unity 2022 WebGL 模块，未验证 WebGL 包。Jev Review 工具未加载，质量检查由编译、测试、实际双客户端对战及两名独立 QA 完成。

## WebGL 备用

Unity 2022.3.62f3c1 必须安装 WebGL Build Support；当前机器该版本仅安装了 macOS 支持，因此优先使用 macOS。

```bash
./scripts/minisiege/build.sh webgl
./scripts/minisiege/run.sh webgl
```

备用脚本通过 Python 的本地 HTTP 服务在 `http://127.0.0.1:8790` 打开两个页面。确保浏览器没有暂停后台页面，最好使用两个并排窗口。WebGL 构建关闭压缩，便于本地 HTTP 测试。

构建入口为 `ZWebGLFrameWork.Editor.MiniSiegeBuild.BuildMacOS` / `BuildWebGL`。仅构建临时小游戏场景，完成后恢复项目场景和相关 PlayerSettings，不修改现有框架启动场景列表。
