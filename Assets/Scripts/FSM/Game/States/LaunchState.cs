using Cysharp.Threading.Tasks;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 启动状态 — 按依赖顺序初始化全部核心模块。
    /// 管线：资源配置 → 基础服务 → 热更检查 → 预热 → Login
    /// 各模块受 AppConfig 功能开关控制，本地测试可按需关闭。
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

            Debug.Log($"[LaunchState] 开始初始化管线 — "
                      + $"资源:{cfg.ResourceProvider}, 热更:{cfg.EnableHotUpdate}, "
                      + $"音频:{cfg.EnableAudio}, SDK:{cfg.EnableSDK}, 本地化:{cfg.EnableLocalization}");

            // ===== ① 资源管理器（必须最先初始化）=====
            ResourceManager.Instance.Init(CreateResourceProvider(cfg.ResourceProvider));

            // ===== ② 场景加载器（始终启用）=====
            SceneLoader.Instance.Init();

            // ===== ③ 功能开关控制的可选模块 =====
            if (cfg.EnableAudio)
                AudioManager.Instance.Init();
            else
                Debug.Log("[LaunchState] ⏭ 音频已关闭，跳过 AudioManager");

            if (cfg.EnableLocalization)
                LocalizationManager.Instance.Init();
            else
                Debug.Log("[LaunchState] ⏭ 本地化已关闭，跳过 LocalizationManager");

            if (cfg.EnableSDK)
                SDKManager.Instance.Init();
            else
                Debug.Log("[LaunchState] ⏭ SDK 已关闭，跳过 SDKManager");

            // ===== ④ 热更新检查 =====
            if (cfg.EnableHotUpdate)
            {
                HotUpdateManager.Instance.Init();
                var result = await HotUpdateManager.Instance.CheckVersionAsync();

                switch (result)
                {
                    case VersionCheckResult.ForceUpdate:
                        Debug.Log("[LaunchState] 需要强制更新 → 跳转商店");
                        Application.OpenURL("market://details?id=xxx");
                        return;
                    case VersionCheckResult.NeedUpdate:
                        Debug.Log("[LaunchState] 发现热更新，开始下载...");
                        await HotUpdateManager.Instance.DownloadUpdatesAsync();
                        break;
                    case VersionCheckResult.UpToDate:
                        Debug.Log("[LaunchState] 已是最新版本");
                        break;
                }
            }
            else
            {
                Debug.Log("[LaunchState] ⏭ 热更已关闭，跳过版本检查");
            }

            // ===== ⑤ 预热 + 登录 =====
            // TODO: ObjectPool 预热
            // TODO: 配置表预加载

            Debug.Log("[LaunchState] ✅ 初始化完成 → Login");
            ChangeState(GameState.Login);
        }

        /// <summary>根据配置创建对应的资源加载器</summary>
        private static IResourceProvider CreateResourceProvider(ResourceProviderType type)
        {
            return type switch
            {
                ResourceProviderType.AssetBundle => new AssetBundleProvider(),
                // TODO: ResourceProviderType.Addressables => new AddressablesProvider(),
                _ => new ResourcesProvider(),
            };
        }

        public override void OnExit()
        {
            base.OnExit();
            Debug.Log("[GameFSM] 退出启动状态");
        }
    }
}
