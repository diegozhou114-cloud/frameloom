using System.Threading;
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
        private readonly IBootPipeline _pipelineOverride;
        private readonly OfflineSaveFlow _offlineSaveFlow =
            new OfflineSaveFlow(new DefaultOfflineSaveGateway());

        public LaunchState() : this(null) { }

        public LaunchState(IBootPipeline pipelineOverride) : base(GameState.Launch)
        {
            _pipelineOverride = pipelineOverride;
        }

        private bool _pipelineRunning;

        public override void OnEnter(object data = null)
        {
            base.OnEnter(data);
            Debug.Log("[GameFSM] 进入启动状态");
            EventBus.Publish("LaunchStarted");
            EventBus.Subscribe("Boot_Retry", OnBootRetry);
            EventBus.Subscribe("Boot_SkipToOffline", OnBootSkip);
            RunInitPipeline(LifetimeToken).Forget();
        }

        private async UniTaskVoid RunInitPipeline(CancellationToken token)
        {
            _pipelineRunning = true;
            try
            {
                if (_pipelineOverride != null)
                {
                    GameState? target = await _pipelineOverride.RunAsync(token);
                    token.ThrowIfCancellationRequested();
                    if (target.HasValue) ChangeState(target.Value);
                    return;
                }

                var cfg = GameLauncher.Config;
                if (cfg == null)
                {
                    Debug.LogError("[LaunchState] ❌ AppConfig 为空，初始化终止");
                    UIManager.Instance.Close<LoadingPanel>();
                    PublishError("AppConfig", "配置文件加载失败，请重新安装游戏", false);
                    return;
                }

                token.ThrowIfCancellationRequested();
                float progress = InitCoreModules(cfg);
                if (!await HandleHotUpdate(cfg, progress, token)) return;
                await PreloadAndTransition(cfg, token);
            }
            catch (System.OperationCanceledException)
            {
            }
            catch (System.Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    PublishError("LaunchPipeline", $"启动管线崩溃: {ex.Message}", false);
                    UIManager.Instance.Close<LoadingPanel>();
                    Debug.LogException(ex);
                }
            }
            finally { _pipelineRunning = false; }
        }

        // ─── 管线步骤 ①~④：核心模块初始化 ───
        private float InitCoreModules(AppConfig cfg)
        {
            // ─── 打开 Loading 遮罩 ───
            UIManager.Instance.Open<LoadingPanel>();
            BootProfiler.BeginPhase("Phase0_Bootstrap");
            PublishProgress("引擎初始化中...", 0.05f);

            // ─── ① UIManager（确保 Canvas 在 ResourceManager 之前就绪）───
            PublishProgress("UI 系统...", 0.08f);
            // UIManager 是 SingletonDontDestroy，首次访问 .Instance 即自动初始化

            // ─── ② 资源管理器 ───
            PublishProgress("资源管理器...", 0.10f);
            WarnOnUnsupportedResourceProvider(cfg.ResourceProvider);
            if (!ResourceManager.Instance.Init())
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

            BootProfiler.EndPhase("Phase1_EngineInit");
            return progress;
        }

        // ─── 管线步骤 ⑤：热更新检查 ───
        private async UniTask<bool> HandleHotUpdate(
            AppConfig cfg, float progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            BootProfiler.BeginPhase("Phase2_HotUpdate");
            if (cfg.EnableHotUpdate)
            {
                BusinessContentUpdateResult result =
                    await ResourceManager.Instance.PrepareBusinessContentAsync(token);
                if (!CanContinueAfterBusinessUpdate(result))
                {
                    PublishError("BusinessHotUpdate", result.Message, true);
                    return false;
                }

                if (result.Status == BusinessContentUpdateStatus.NotConfigured)
                {
                    Debug.LogWarning("[LaunchState] TODO(YooAsset) TODO(Huatuo): 业务热更未接入，使用本地业务内容。");
                }
            }
            else Debug.Log("[LaunchState] ⏭ 热更已关闭");

            return true;
        }

        private static bool CanContinueAfterBusinessUpdate(BusinessContentUpdateResult result)
        {
            return result.Status != BusinessContentUpdateStatus.Failed;
        }

        // ─── 管线步骤 ⑥~⑦：数据预加载与场景跳转 ───
        private async UniTask PreloadAndTransition(AppConfig cfg, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // ─── ⑥ 数据预加载 (Phase 3) ───
            BootProfiler.EndPhase("Phase2_HotUpdate");
            BootProfiler.BeginPhase("Phase3_DataPreload");
            PublishProgress("正在准备游戏数据...", 0.55f);
            var configOk = await ConfigTableManager.Instance.PreloadAllAsync()
                .AttachExternalCancellation(token);
            if (!configOk)
            {
                PublishError("ConfigTableManager", "部分配置表加载失败，可能影响游戏功能");
                return;
            }

            // ─── ⑦ 完成 ───
            BootProfiler.EndPhase("Phase3_DataPreload");
            PublishProgress("即将进入游戏...", 1f);
            await UniTask.Delay(200, cancellationToken: token);

            UIManager.Instance.Close<LoadingPanel>();
            BootProfiler.EndPhase("Phase4_Transition");
            Debug.Log(BootProfiler.GetReport());

            // ─── C.4 离线模式：无网络时跳过登录 (Phase 5) ───
            if (cfg.EnableOfflineMode
                && Application.internetReachability == NetworkReachability.NotReachable)
            {
                Debug.Log("[LaunchState] 🔌 离线模式 + 无网络 → 跳过登录，直接进主城");
                if (_offlineSaveFlow.TryPrepareSlot(SaveManager.CurrentSlot))
                {
                    ChangeState(GameState.MainVillage);
                    return;
                }

                PublishError("OfflineSave", "本地存档创建或加载失败，请重试", true);
                return;
            }

            Debug.Log("[LaunchState] ✅ 初始化完成 → Login");
            ChangeState(GameState.Login);
        }

        // ==================== 辅助方法 ====================

        private static void WarnOnUnsupportedResourceProvider(ResourceProviderType type)
        {
            if (type != ResourceProviderType.Resources)
                Debug.LogWarning($"[LaunchState] {type} is reserved for TODO(YooAsset); using ResourcesProvider.");
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
            if (_pipelineRunning)
            {
                Debug.LogWarning("[LaunchState] 启动管线已在运行中，忽略重试");
                return;
            }
            Debug.Log("[LaunchState] 重试启动流程");
            UIManager.Instance.Close<ErrorPanel>();
            RunInitPipeline(LifetimeToken).Forget();
        }

        private void OnBootSkip()
        {
            Debug.Log("[LaunchState] 跳过错误 → 离线模式");
            UIManager.Instance.Close<ErrorPanel>();
            ChangeState(GameState.Login);
        }

        public override void OnExit()
        {
            base.OnExit();
            UIManager.Instance.Close<LoadingPanel>();
            EventBus.Unsubscribe("Boot_Retry", OnBootRetry);
            EventBus.Unsubscribe("Boot_SkipToOffline", OnBootSkip);
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
