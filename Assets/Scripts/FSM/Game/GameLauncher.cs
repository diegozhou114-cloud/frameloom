using UnityEngine;
using UnityEngine.SceneManagement;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.FSM
{
    /// <summary>
    /// 游戏启动器 — 程序启动时自动创建，无需手动挂载到场景。
    /// 职责：
    /// 1. 自动创建 GameObject 并挂载自身，标记 DontDestroyOnLoad
    /// 2. 从 Resources 加载 AppConfig 并应用全局设置
    /// 3. 触发 GameFSM 初始化并启动游戏循环
    /// 4. 处理失焦/恢复时的帧率切换（省电模式）
    /// </summary>
    [DisallowMultipleComponent]
    public class GameLauncher : MonoBehaviour
    {
        /// <summary>已加载的应用配置（全局访问入口）</summary>
        public static AppConfig Config { get; private set; }

        /// <summary>GameLauncher 是否已就绪</summary>
        public static bool IsReady { get; private set; }

        [Header("配置路径")]
        [Tooltip("AppConfig 在 Resources 下的相对路径（不含扩展名）")]
        [SerializeField] private string _configPath = "Config/AppConfig";

        private AppConfig _config;
        private static GameLauncher _instance;

        // ==================== 自动创建 ====================

        /// <summary>
        /// 首个场景加载完成后自动创建 GameLauncher。
        /// 仅在启动场景为 "Launcher" 时生效，避免子场景调试时误创建。
        /// 如果场景中已手动放置了 GameLauncher 则跳过。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != "Launcher")
            {
                Debug.Log($"[GameLauncher] 当前场景为「{sceneName}」，非启动场景，跳过自动创建");
                return;
            }

            // 避免重复创建（场景中可能已手动放置）
            if (FindObjectOfType<GameLauncher>() != null)
            {
                Debug.Log("[GameLauncher] 已存在，跳过自动创建");
                return;
            }

            var go = new GameObject("GameLauncher");
            go.AddComponent<GameLauncher>();
            // DontDestroyOnLoad 由 Awake() 统一处理，此处无需重复调用

            Debug.Log("[GameLauncher] ✅ 自动创建完成 — Launcher 场景已就绪");
        }

        // ==================== Unity 生命周期 ====================

        private void Awake()
        {
            // 防止重复实例（场景中手动放置 + 自动创建并发保护）
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            LoadAndApplyConfig();
            VerifyConfigApplied();
        }

        private void Start()
        {
            // 触发 GameFSM 实例化：SingletonDontDestroy 在首次访问 Instance 时自动创建，
            // 其 Awake 中注册所有状态，Start 中触发 Launch 状态
            var fsm = GameFSM.Instance;
            IsReady = true;
            Debug.Log($"[GameLauncher] GameFSM 已就绪 → 当前状态: {fsm.CurrentState}");
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (Config == null || !Config.ThrottleInBackground) return;

            if (hasFocus)
            {
                Application.targetFrameRate = Config.RuntimeFrameRate;
                Debug.Log($"[GameLauncher] 恢复帧率: {Config.RuntimeFrameRate}");
            }
            else
            {
                Application.targetFrameRate = Config.BackgroundFrameRate;
                Debug.Log($"[GameLauncher] 降低帧率: {Config.BackgroundFrameRate}");
            }
        }

        // ==================== 配置加载 ====================

        private void LoadAndApplyConfig()
        {
            _config = Resources.Load<AppConfig>(_configPath);

            if (_config == null)
            {
                Debug.LogError(
                    $"[GameLauncher] ❌ 未找到 AppConfig.asset！\n"
                    + $"  期望路径: Assets/Resources/{_configPath}.asset\n"
                    + $"  已降级为硬编码默认值 — 策划配置未生效，请立即修复！");
                _config = ScriptableObject.CreateInstance<AppConfig>();
                _config.IsDefaultInstance = true;
            }

            Config = _config;
            _config.Apply();
        }

        /// <summary>验证全部配置字段 — 10/10</summary>
        private void VerifyConfigApplied()
        {
            var status = Config.IsDefaultInstance
                ? "⚠ 降级默认值（.asset 未加载）"
                : "✅ 策划配置";

            var platformInfo = Config.EnablePlatformAutoDetect
                ? $"平台自适应已激活 → 帧率:{Config.RuntimeFrameRate} 画质:{Config.RuntimeQualityLevel}"
                : "平台自适应未启用（使用原始配置值）";

            Debug.Log(
                "\n══════════════════════════════════════════\n"
                + $"         AppConfig 运行时验证  [{status}]\n"
                + "──────────────────────────────────────────\n"
                + $"  ⓪ 平台自适应       : {platformInfo}\n"
                + $"  ① 目标帧率         : {Application.targetFrameRate}"
                + $"  (Config原始:{Config.TargetFrameRate})\n"
                + $"  ② 垂直同步         : {QualitySettings.vSyncCount}"
                + $"  ← Config: {Config.VSyncCount}\n"
                + $"  ③ 允许后台运行     : {Application.runInBackground}"
                + $"  ← Config: {Config.RunInBackground}\n"
                + $"  ④ 画质等级         : {QualitySettings.GetQualityLevel()}"
                + $"  (Config原始:{Config.QualityLevel})\n"
                + $"  ⑤ 日志最低等级     : {LogManager.MinLevel}"
                + $"  ← Config: {Config.MinLogLevel}\n"
                + $"  ⑥ 加载模式         : {Config.LoadingMode}"
                + "  （LaunchState 读取）\n"
                + $"  ⑦ 后台省电         : {Config.ThrottleInBackground}"
                + $"  → {Config.BackgroundFrameRate} FPS"
                + "  （OnApplicationFocus 读取）\n"
                + $"  ⑧ WebGL 内存       : {Config.WebGLMemorySize} MB"
                + "  （构建时生效）\n"
                + $"  ⑨ WebGL 异常模式   : {Config.WebGLExceptionMode}"
                + "  （构建时生效）\n"
                + $"  ⑩ 配置来源         : {(Config.IsDefaultInstance ? "CreateInstance 默认值" : "Resources/.asset")}\n"
                + "══════════════════════════════════════════");
        }
    }
}
