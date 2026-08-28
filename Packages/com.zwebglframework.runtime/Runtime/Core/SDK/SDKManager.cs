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
        /// WebGL 平台自动跳过原生 SDK。
        /// </summary>
        public bool Init()
        {
            try
            {
                Debug.Log($"[SDKManager] 开始初始化 {_sdkProviders.Count} 个 SDK...");

                int successCount = 0;
                foreach (var sdk in _sdkProviders)
                {
                    try
                    {
#if UNITY_WEBGL
                        // WebGL 平台仅初始化 Web 兼容的 SDK
                        if (sdk.Name.Contains("Ad") || sdk.Name.Contains("Analytics"))
                        {
                            Debug.Log($"[SDKManager] ⏭ WebGL 跳过原生 SDK: {sdk.Name}");
                            continue;
                        }
#endif
                        sdk.Initialize();
                        Debug.Log($"[SDKManager] ✅ {sdk.Name} 初始化成功");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[SDKManager] ❌ {sdk.Name} 初始化失败: {ex.Message}");
                    }
                }

                IsReady = true;
                Debug.Log($"[SDKManager] ✅ 初始化完成 ({successCount}/{_sdkProviders.Count})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SDKManager] ❌ 初始化异常: {ex.Message}");
                return false;
            }
        }

        // ==================== 注册 ====================

        /// <summary>注册一个 SDK（需在 Init() 之前调用）</summary>
        public void Register(ISDKProvider sdk)
        {
            if (sdk == null) return;
            if (_sdkProviders.Exists(s => s.Name == sdk.Name))
            {
                Debug.LogWarning($"[SDKManager] SDK 已注册，跳过重复: {sdk.Name}");
                return;
            }
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
