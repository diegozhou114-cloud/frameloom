using System;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Data
{
    /// <summary>
    /// 应用启动配置 ScriptableObject — 控制帧率、后台运行、加载模式等全局参数。
    /// 通过 Resources.Load 加载，由 GameLauncher 在启动时应用。
    /// </summary>
    [CreateAssetMenu(fileName = "AppConfig", menuName = "ZWebGL/Create AppConfig", order = 0)]
    public class AppConfig : ScriptableObject
    {
        /// <summary>此实例是否为运行时降级创建的默认配置（非 .asset 加载）</summary>
        public bool IsDefaultInstance { get; set; }
        [Header("性能 — 帧率与渲染")]
        /// <summary>启用平台自适应：开启后根据平台自动调整帧率/画质等参数</summary>
        [Tooltip("开启后根据平台自动覆盖帧率和画质（生产环境推荐开启）")]
        [SerializeField] private bool _enablePlatformAutoDetect = false;
        public bool EnablePlatformAutoDetect => _enablePlatformAutoDetect;

        /// <summary>目标帧率：0=平台自适应，-1=不限制，30/60/120=固定刷新率<br/>
        /// 当 AutoDetect 开启时此值被平台默认值覆盖</summary>
        [Tooltip("0=平台自适应，-1=不限制，30/60/120=固定")]
        [SerializeField] private int _targetFrameRate = 0;
        public int TargetFrameRate => _targetFrameRate;

        /// <summary>垂直同步：0=关闭（允许撕裂但延迟最低），1=每帧同步（消除撕裂），2=每两帧同步</summary>
        [Tooltip("垂直同步计数（0=关闭，1=每帧同步，2=每两帧同步）")]
        [SerializeField] private int _vSyncCount = 0;
        public int VSyncCount => _vSyncCount;

        [Header("后台行为 — 窗口失焦时的策略")]
        /// <summary>是否允许游戏在窗口失焦时继续运行（WebGL 平台无视此设置）</summary>
        [Tooltip("是否允许后台运行")]
        [SerializeField] private bool _runInBackground = true;
        public bool RunInBackground => _runInBackground;

        /// <summary>窗口失焦时是否自动降低帧率以节省 CPU/电量</summary>
        [Tooltip("失焦时是否降低帧率")]
        [SerializeField] private bool _throttleInBackground = false;
        public bool ThrottleInBackground => _throttleInBackground;

        /// <summary>窗口失焦时的目标帧率（仅在启用省电模式时生效，建议 5~15）</summary>
        [Tooltip("后台目标帧率")]
        [SerializeField] private int _backgroundFrameRate = 10;
        public int BackgroundFrameRate => _backgroundFrameRate;

        [Header("功能开关 — 模块启停（本地测试可关闭）")]
        /// <summary>保留的热更新开关。TODO(YooAsset)：当前启动流程不会下载或应用资源更新。</summary>
        [Tooltip("TODO(YooAsset)：保留旧配置值，当前启动流程跳过资源更新与分包下载")]
        [SerializeField] private bool _enableHotUpdate = false;
        public bool EnableHotUpdate => _enableHotUpdate;

        /// <summary>是否启用音频：关闭后 AudioManager 不初始化</summary>
        [Tooltip("关闭后静音运行")]
        [SerializeField] private bool _enableAudio = true;
        public bool EnableAudio => _enableAudio;

        /// <summary>是否启用 SDK：关闭后跳过第三方 SDK 初始化（广告/统计/支付等）</summary>
        [Tooltip("关闭后 SDK 不初始化，本地开发推荐关闭")]
        [SerializeField] private bool _enableSDK = false;
        public bool EnableSDK => _enableSDK;

        /// <summary>是否启用多语言：关闭后使用 key 原文，不加载语言表</summary>
        [Tooltip("关闭后所有文本显示 key 原名")]
        [SerializeField] private bool _enableLocalization = false;
        public bool EnableLocalization => _enableLocalization;

        /// <summary>是否启用离线模式：开启后跳过登录和网络连接，直接使用本地存档进主城</summary>
        [Tooltip("开启后完全离线运行，本地存档自给自足")]
        [SerializeField] private bool _enableOfflineMode = true;
        public bool EnableOfflineMode => _enableOfflineMode;

        [Header("网络 - 在线端点")]
        [SerializeField] private string _httpsApiBaseUrl = "https://127.0.0.1:7443";
        public string HttpsApiBaseUrl
        {
            get
            {
                ValidateOnlineUri(_httpsApiBaseUrl, Uri.UriSchemeHttps, null);
                return _httpsApiBaseUrl;
            }
        }

        [SerializeField] private string _wssRealtimeUrl = "wss://127.0.0.1:7443/v1/realtime";
        public string WssRealtimeUrl
        {
            get
            {
                ValidateOnlineUri(_wssRealtimeUrl, "wss", "/v1/realtime");
                return _wssRealtimeUrl;
            }
        }

        [Header("资源 — 加载方式切换")]
        /// <summary>资源加载提供者：当前仅 Resources 生效；旧枚举值为序列化兼容保留。</summary>
        [Tooltip("当前仅 Resources 生效；AssetBundle/Addressables 保留旧配置值，TODO(YooAsset)")]
        [SerializeField] private ResourceProviderType _resourceProvider = ResourceProviderType.Resources;
        public ResourceProviderType ResourceProvider => _resourceProvider;

        /// <summary>游戏启动时的资源加载策略：顺序加载（稳定）/ 并行加载（快速）/ 懒加载（省内存）</summary>
        [Tooltip("资源加载策略")]
        [SerializeField] private LoadingMode _loadingMode = LoadingMode.Sequential;
        public LoadingMode LoadingMode => _loadingMode;

        [Header("画质 — 渲染质量预设")]
        /// <summary>初始画质等级：对应 QualitySettings 数组索引，通常 0=低 / 1=中 / 2=高</summary>
        [Tooltip("初始画质等级（0=低，1=中，2=高）")]
        [SerializeField] private int _qualityLevel = 1;
        public int QualityLevel => _qualityLevel;

        [Header("日志 — 输出等级与过滤")]
        /// <summary>最低日志输出等级：低于此级别的日志会被静默过滤（Verbose=全量，Warning=仅警告+错误，None=关闭）</summary>
        [Tooltip("低于此级别的日志被过滤")]
        [SerializeField] private LogLevel _minLogLevel = LogLevel.Verbose;
        public LogLevel MinLogLevel => _minLogLevel;

        [Header("WebGL 专属 — 仅 WebGL 构建生效")]
        /// <summary>WebGL 请求的内存大小（MB）：影响浏览器分配，建议 256~2048</summary>
        [Tooltip("WebGL 内存大小（MB）")]
        [SerializeField] private int _webGLMemorySize = 256;
        public int WebGLMemorySize => _webGLMemorySize;

        /// <summary>WebGL 异常捕获模式：None=不捕获 / ExplicitlyThrown=仅显式throw / FullWithoutStacktrace=全量无堆栈 / FullWithStacktrace=全量带堆栈</summary>
        [Tooltip("WebGL 异常处理模式")]
        [SerializeField] private WebGLExceptionMode _webGLExceptionMode = WebGLExceptionMode.ExplicitlyThrown;
        public WebGLExceptionMode WebGLExceptionMode => _webGLExceptionMode;

        // ==================== 应用配置 ====================

        // 缓存平台自适应后的运行时值（与 Inspector 原始值区分）
        private int _runtimeFrameRate;
        private int _runtimeQualityLevel;
        private bool _runtimeThrottleInBackground;

        /// <summary>平台自适应后的运行时帧率</summary>
        public int RuntimeFrameRate => _runtimeFrameRate;
        /// <summary>平台自适应后的运行时画质</summary>
        public int RuntimeQualityLevel => _runtimeQualityLevel;

        /// <summary>将配置应用到 Unity 全局设置</summary>
        public void Apply()
        {
            _ = HttpsApiBaseUrl;
            _ = WssRealtimeUrl;

            // -------- 平台自适应 --------
            if (_enablePlatformAutoDetect)
                ApplyPlatformDefaults();
            else
            {
                _runtimeFrameRate = _targetFrameRate;
                _runtimeQualityLevel = _qualityLevel;
                _runtimeThrottleInBackground = _throttleInBackground;
            }

            // -------- 运行时生效：直接设置 Unity 全局参数 --------
            Application.targetFrameRate = _runtimeFrameRate;
            QualitySettings.vSyncCount = _vSyncCount;
            Application.runInBackground = _runInBackground;

            if (_runtimeQualityLevel >= 0 && _runtimeQualityLevel < QualitySettings.names.Length)
                QualitySettings.SetQualityLevel(_runtimeQualityLevel, applyExpensiveChanges: false);

            LogManager.MinLevel = _minLogLevel;

            // -------- 仅构建时生效（需在 PlayerSettings 中配置）--------
#if UNITY_WEBGL
            Debug.Log($"[AppConfig] WebGL 构建参数（需在 PlayerSettings 确认）— "
                      + $"内存:{_webGLMemorySize}MB, 异常模式:{_webGLExceptionMode}");
#endif

            Debug.Log($"[AppConfig] 配置已应用 — 帧率:{_runtimeFrameRate}"
                      + (AutoDetectActive ? $"(平台自适应，原始值:{_targetFrameRate})" : "")
                      + $", VSync:{_vSyncCount}, 后台:{_runInBackground}"
                      + $", 画质:{_runtimeQualityLevel}, 后台省电:{_runtimeThrottleInBackground}");
        }

        private static void ValidateOnlineUri(
            string value, string requiredScheme, string requiredPath)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                !string.Equals(uri.Scheme, requiredScheme, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Online endpoint must use {requiredScheme}.");

            if (requiredPath != null &&
                (!string.Equals(uri.AbsolutePath, requiredPath, StringComparison.Ordinal) ||
                 !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
                throw new InvalidOperationException(
                    $"Realtime endpoint path must be exactly {requiredPath}.");
        }

        /// <summary>平台自适应是否已激活</summary>
        private bool AutoDetectActive => _enablePlatformAutoDetect && _targetFrameRate <= 0;

        /// <summary>根据当前平台自动调整帧率、画质、后台省电策略</summary>
        private void ApplyPlatformDefaults()
        {
#if UNITY_ANDROID || UNITY_IOS
            _runtimeFrameRate = 30;
            _runtimeQualityLevel = 0;
            _runtimeThrottleInBackground = true;
            Debug.Log("[AppConfig] 平台自适应 → 移动端：帧率30, 低画质, 后台省电开启");
#elif UNITY_WEBGL
            _runtimeFrameRate = 30;
            _runtimeQualityLevel = 0;
            _runtimeThrottleInBackground = false;
            Debug.Log("[AppConfig] 平台自适应 → WebGL：帧率30, 低画质（浏览器性能受限）");
#else
            _runtimeFrameRate = 60;
            _runtimeQualityLevel = 1;
            _runtimeThrottleInBackground = false;
            Debug.Log("[AppConfig] 平台自适应 → 桌面端：帧率60, 中画质, 后台省电关闭");
#endif
        }
    }

    // 若此文件超过 200 行，建议将以下枚举移至独立的 Enums.cs 以保持职责单一

    /// <summary>资源加载模式 — 决定游戏启动时如何加载核心模块</summary>
    public enum LoadingMode
    {
        /// <summary>顺序加载：按依赖链逐个加载，初始化顺序可控，内存峰值低但总耗时较长</summary>
        Sequential,

        /// <summary>并行加载：同时启动多个模块的加载，总耗时最短但瞬时内存峰值高</summary>
        Parallel,

        /// <summary>懒加载：启动时仅加载最小核心集，其余模块在首次使用时才加载，内存最友好但首次交互可能卡顿</summary>
        Lazy,
    }

    /// <summary>WebGL 异常处理模式 — 决定浏览器中 C# 异常的捕获与上报策略</summary>
    public enum WebGLExceptionMode
    {
        /// <summary>不捕获：异常会导致整个 WebGL 播放器崩溃，性能最好</summary>
        None,

        /// <summary>仅显式 throw：只捕获主动抛出的异常，兼顾性能与基本错误追踪</summary>
        ExplicitlyThrown,

        /// <summary>全量无堆栈：捕获所有异常但不保留 C# 堆栈，适合线上版本</summary>
        FullWithoutStacktrace,

        /// <summary>全量带堆栈：捕获所有异常并保留完整 C# 调用栈，调试必选但包体/内存开销最大</summary>
        FullWithStacktrace,
    }

    /// <summary>资源加载提供者。AssetBundle/Addressables 的数值为既有序列化数据保留，当前回退至 Resources。</summary>
    public enum ResourceProviderType
    {
        /// <summary>Resources 目录：本地开发模式，无需打包，即改即用</summary>
        Resources,

        /// <summary>保留既有序列化值。TODO(YooAsset)：当前启动时回退至 Resources。</summary>
        AssetBundle,

        /// <summary>保留既有序列化值。TODO(YooAsset)：当前启动时回退至 Resources。</summary>
        Addressables,
    }
}
