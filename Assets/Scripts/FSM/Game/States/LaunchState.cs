using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>启动状态 — 初始化SDK、检查更新、加载基础资源</summary>
    public class LaunchState : FSMState<GameState>
    {
        public LaunchState() : base(GameState.Launch) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入启动状态");

            EventBus.Publish("LaunchStarted");

            var config = GameLauncher.Config;
            var mode = config?.LoadingMode ?? LoadingMode.Sequential;

            Debug.Log($"[LaunchState] 加载模式: {mode}");

            // TODO: 根据加载模式执行不同的资源初始化策略
            // Sequential → 顺序加载核心模块
            // Parallel   → 并行加载（配合 AsyncManager）
            // Lazy       → 仅加载最小集，其余按需

            // 当前版本：0.5s 后进入 Login
            CoroutineManager.Delay(0.5f, () => ChangeState(GameState.Login));
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
