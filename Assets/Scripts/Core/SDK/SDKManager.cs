using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// SDK 管理器 — 跨场景单例，统一管理第三方 SDK 的初始化与生命周期。
    /// 所有 SDK 实现 ISDKProvider 接口后注册到此管理器，由 Init() 统一初始化。
    ///
    /// 用法：
    ///   SDKManager.Instance.Register(new AdSDK());
    ///   SDKManager.Instance.Register(new AnalyticsSDK());
    ///   SDKManager.Instance.Init();
    /// </summary>
    public class SDKManager : SingletonDontDestroy<SDKManager>
    {
        /// <summary>是否已初始化</summary>
        public bool IsReady { get; private set; }

        private readonly List<ISDKProvider> _sdkProviders = new();

        // ==================== 初始化 ====================

        /// <summary>
        /// 按注册顺序初始化所有 SDK。
        /// 单个 SDK 失败不影响后续初始化。
        /// TODO: 根据平台条件跳过不适用的 SDK（如 WebGL 跳过原生广告 SDK）。
        /// TODO: 接入 AppConfig 控制哪些 SDK 启用。
        /// </summary>
        public void Init()
        {
            Debug.Log($"[SDKManager] 开始初始化 {_sdkProviders.Count} 个 SDK...");

            foreach (var sdk in _sdkProviders)
            {
                try
                {
                    sdk.Initialize();
                    Debug.Log($"[SDKManager] ✅ {sdk.Name} 初始化成功");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SDKManager] ❌ {sdk.Name} 初始化失败: {ex.Message}");
                }
            }

            IsReady = true;
            Debug.Log("[SDKManager] ✅ 全部 SDK 初始化完成");
        }

        // ==================== 注册 ====================

        /// <summary>注册一个 SDK（需在 Init() 之前调用）</summary>
        public void Register(ISDKProvider sdk)
        {
            if (sdk == null) return;
            _sdkProviders.Add(sdk);
            Debug.Log($"[SDKManager] 注册 SDK: {sdk.Name}");
        }

        /// <summary>获取已注册的 SDK（按名称）</summary>
        public ISDKProvider GetSDK(string name)
        {
            return _sdkProviders.Find(s => s.Name == name);
        }
    }

    /// <summary>
    /// SDK 提供者接口 — 所有第三方 SDK 适配器实现此接口。
    /// </summary>
    public interface ISDKProvider
    {
        /// <summary>SDK 名称（用于日志和查找）</summary>
        string Name { get; }

        /// <summary>初始化 SDK</summary>
        void Initialize();
    }
}
