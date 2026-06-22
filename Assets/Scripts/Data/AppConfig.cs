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
        /// <summary>目标帧率：0=平台默认，-1=不限制，30/60/120=固定刷新率（移动端建议30，桌面端建议60）</summary>
        [Tooltip("目标帧率（0=默认，-1=不限制，30/60/120=固定）")]
        [SerializeField] private int _targetFrameRate = 60;
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

        [Header("加载 — 资源初始化策略")]
        /// <summary>游戏启动时的资源加载模式：顺序加载（稳定）/ 并行加载（快速）/ 懒加载（省内存）</summary>
        [Tooltip("资源加载模式")]
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

        /// <summary>将配置应用到 Unity 全局设置</summary>
        public void Apply()
        {
            // -------- 运行时生效：直接设置 Unity 全局参数 --------
            Application.targetFrameRate = _targetFrameRate;
            QualitySettings.vSyncCount = _vSyncCount;
            Application.runInBackground = _runInBackground;

            if (_qualityLevel >= 0 && _qualityLevel < QualitySettings.names.Length)
            {
                QualitySettings.SetQualityLevel(_qualityLevel, applyExpensiveChanges: false);
            }

            LogManager.MinLevel = _minLogLevel;

            // -------- 运行时生效：由其他模块读取，不设 Unity API --------
            // _loadingMode       → LaunchState 读取，决定资源加载策略
            // _throttleInBackground  → GameLauncher.OnApplicationFocus 读取
            // _backgroundFrameRate   → GameLauncher.OnApplicationFocus 读取

            // -------- 仅构建时生效（需在 PlayerSettings 中配置）--------
            // _webGLMemorySize       → PlayerSettings.WebGL.memorySize
            // _webGLExceptionMode    → PlayerSettings.WebGL.exceptionSupport
#if UNITY_WEBGL
            Debug.Log($"[AppConfig] WebGL 构建参数（需在 PlayerSettings 确认）— "
                      + $"内存:{_webGLMemorySize}MB, 异常模式:{_webGLExceptionMode}");
#endif

            Debug.Log($"[AppConfig] 配置已应用 — 帧率:{_targetFrameRate}, VSync:{_vSyncCount}, "
                      + $"后台:{_runInBackground}, 加载模式:{_loadingMode}, 画质:{_qualityLevel}");
        }
    }

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
}
