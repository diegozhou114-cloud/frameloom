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
            EventBus.Subscribe("Boot_Retry", OnBootRetry);
            EventBus.Subscribe("Boot_SkipToOffline", OnBootSkip);
            RunInitPipeline().Forget();
        }

        private async UniTaskVoid RunInitPipeline()
        {
            try
            {
                var cfg = GameLauncher.Config;
            if (cfg == null)
            {
                Debug.LogError("[LaunchState] ❌ AppConfig 为空，初始化终止");
                UIManager.Instance.Close<LoadingPanel>();
                PublishError("AppConfig", "配置文件加载失败，请重新安装游戏", false);
                return;
            }

            // ─── 打开 Loading 遮罩 ───
            UIManager.Instance.Open<LoadingPanel>();
            BootProfiler.BeginPhase("Phase0_Bootstrap");
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
            BootProfiler.EndPhase("Phase1_EngineInit");
            BootProfiler.BeginPhase("Phase2_HotUpdate");
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
                        // 发布错误事件通知用户
                        PublishError("HotUpdate", "检测到强制更新，请前往商店下载最新版本", false);
                        return; // 终止初始化，等待用户手动更新
                    case VersionCheckResult.NeedUpdate:
                        await HotUpdateManager.Instance.DownloadUpdatesAsync();
                        break;
                }
            }
            else Debug.Log("[LaunchState] ⏭ 热更已关闭");

            // ─── ⑥ 数据预加载 (Phase 3) ───
            BootProfiler.EndPhase("Phase2_HotUpdate");
            BootProfiler.BeginPhase("Phase3_DataPreload");
            PublishProgress("正在准备游戏数据...", 0.55f);
            var configOk = await ConfigTableManager.Instance.PreloadAllAsync();
            if (!configOk)
                PublishError("ConfigTableManager", "部分配置表加载失败，可能影响游戏功能");

            // 对象池预热（异步）
            PublishProgress("预热对象池...", 0.70f);
            await PoolPrewarmer.PrewarmFromConfigAsync(ConfigTableManager.Instance);

            // ─── ⑦ 完成 ───
            BootProfiler.EndPhase("Phase3_DataPreload");
            PublishProgress("即将进入游戏...", 1f);
            await UniTask.Delay(200);

            UIManager.Instance.Close<LoadingPanel>();
            BootProfiler.EndPhase("Phase4_Transition");
            Debug.Log(BootProfiler.GetReport());

            // ─── C.4 离线模式：无网络时跳过登录 (Phase 5) ───
            if (cfg.EnableOfflineMode
                && Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.Log("[LaunchState] 🔌 离线模式 + 无网络 → 跳过登录，直接进主城");
                GameInitializer.EnsureSaveExists();
                if (SaveManager.Exists(SaveManager.CurrentSlot))
                {
                    SaveManager.LoadToDataManager(SaveManager.CurrentSlot);
                }
                ChangeState(GameState.MainVillage);
                return;
            }

            Debug.Log("[LaunchState] ✅ 初始化完成 → Login");
            ChangeState(GameState.Login);
            }
            catch (System.Exception ex)
            {
                PublishError("LaunchPipeline", $"启动管线崩溃: {ex.Message}", false);
                UIManager.Instance.Close<LoadingPanel>();
                Debug.LogException(ex);
            }
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
            UIManager.Instance.Open<ErrorPanel>();
            EventBus.Publish(BootErrorEvent.EventKey,
                new BootErrorEvent(module, message, canSkip));
            Debug.LogError($"[LaunchState] ❌ {module}: {message}");
        }

        private void OnBootRetry()
        {
            Debug.Log("[LaunchState] 重试启动流程");
            UIManager.Instance.Close<ErrorPanel>();
            // 重新运行启动管线
            RunInitPipeline().Forget();
        }

        private void OnBootSkip()
        {
            Debug.Log("[LaunchState] 跳过错误 → 离线模式");
            UIManager.Instance.Close<ErrorPanel>();
            ChangeState(GameState.Login);
        }

        public override void OnExit()
        {
            UIManager.Instance.Close<LoadingPanel>();
            EventBus.Unsubscribe("Boot_Retry", OnBootRetry);
            EventBus.Unsubscribe("Boot_SkipToOffline", OnBootSkip);
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
