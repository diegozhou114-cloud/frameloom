using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.UI;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 启动状态 — 按依赖顺序初始化全部核心模块。
    /// 管线：UI → 资源 → 基础服务 → 热更检查 → Login
    /// 各模块受 AppConfig 功能开关控制，失败时通过 ErrorPanel 通知用户。
    /// </summary>
    public class LaunchState : FSMState<GameState>
    {
        public LaunchState() : base(GameState.Launch) { }

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入启动状态");
            EventBus.Publish("LaunchStarted");
            RunInitPipeline().Forget();
        }

        private async UniTaskVoid RunInitPipeline()
        {
            var cfg = GameLauncher.Config;
            if (cfg == null)
            {
                Debug.LogError("[LaunchState] ❌ AppConfig 为空，初始化终止");
                return;
            }

            // ─── 打开 Loading 遮罩 ───
            UIManager.Instance.Open<LoadingPanel>();
            PublishProgress("引擎初始化中...", 0.05f);

            // ─── ① UIManager（确保 Canvas 在 ResourceManager 之前就绪）───
            PublishProgress("UI 系统...", 0.08f);
            // UIManager 是 SingletonDontDestroy，首次访问 .Instance 即自动初始化

            // ─── ② 资源管理器 ───
            PublishProgress("资源管理器...", 0.10f);
            if (!ResourceManager.Instance.Init(
                CreateResourceProvider(cfg.ResourceProvider)))
                PublishError("ResourceManager", "资源管理器初始化失败，将使用默认 Resources 模式");

            // ─── ③ 场景加载器 ───
            PublishProgress("场景加载器...", 0.15f);
            SceneLoader.Instance.Init();

            // ─── ④ 可选模块 ───
            float progress = 0.20f;
            float step = 0.08f;

            if (cfg.EnableAudio)
            {
                PublishProgress("音频系统...", progress);
                if (!AudioManager.Instance.Init())
                    PublishError("AudioManager", "音频初始化失败，游戏将静音运行");
            }
            else Debug.Log("[LaunchState] ⏭ 音频已关闭");
            progress += step;

            if (cfg.EnableLocalization)
            {
                PublishProgress("多语言...", progress);
                if (!LocalizationManager.Instance.Init())
                    PublishError("LocalizationManager", "多语言加载失败，将使用 key 原文");
            }
            else Debug.Log("[LaunchState] ⏭ 本地化已关闭");
            progress += step;

            if (cfg.EnableSDK)
            {
                PublishProgress("SDK 初始化...", progress);
                if (!SDKManager.Instance.Init())
                    PublishError("SDKManager", "SDK 初始化部分失败，可能影响广告/统计功能");
            }
            else Debug.Log("[LaunchState] ⏭ SDK 已关闭");
            progress += step;

            // ─── ⑤ 热更新检查 ───
            if (cfg.EnableHotUpdate)
            {
                PublishProgress("热更新检查...", progress);
                if (!HotUpdateManager.Instance.Init())
                    PublishError("HotUpdateManager", "热更管理器初始化失败，将跳过更新");

                var result = await HotUpdateManager.Instance.CheckVersionAsync();
                PublishProgress("热更新检查...", progress + 0.1f,
                    result == VersionCheckResult.NeedUpdate ? "发现新版本" : "");

                switch (result)
                {
                    case VersionCheckResult.ForceUpdate:
                        UIManager.Instance.Close<LoadingPanel>();
                        UIManager.Instance.Open<ForceUpdatePanel>(
                            HotUpdateManager.Instance.HotUpdateBaseUrl ?? "market://details?id=xxx");
                        return; // 终止初始化，等待用户手动更新
                    case VersionCheckResult.NeedUpdate:
                        await HotUpdateManager.Instance.DownloadUpdatesAsync();
                        break;
                }
            }
            else Debug.Log("[LaunchState] ⏭ 热更已关闭");

            // ─── ⑥ 完成 ───
            PublishProgress("即将进入游戏...", 1f);
            await UniTask.Delay(200); // 让玩家看到 100%

            UIManager.Instance.Close<LoadingPanel>();
            Debug.Log("[LaunchState] ✅ 初始化完成 → Login");
            ChangeState(GameState.Login);
        }

        // ==================== 辅助方法 ====================

        private static IResourceProvider CreateResourceProvider(ResourceProviderType type)
        {
            return type switch
            {
                ResourceProviderType.AssetBundle => new AssetBundleProvider(),
                _ => new ResourcesProvider(),
            };
        }

        private static void PublishProgress(string phase, float progress, string hint = "")
        {
            EventBus.Publish(BootProgressEvent.EventKey,
                new BootProgressEvent(phase, progress, hint));
        }

        private static void PublishError(string module, string message, bool canSkip = true)
        {
            EventBus.Publish(BootErrorEvent.EventKey,
                new BootErrorEvent(module, message, canSkip));
            Debug.LogError($"[LaunchState] ❌ {module}: {message}");
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
