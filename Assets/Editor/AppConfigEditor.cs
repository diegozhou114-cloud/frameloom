using UnityEditor;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Editor
{
    /// <summary>
    /// AppConfig 自定义 Inspector — 在每个字段旁显示中文标签与使用说明，
    /// 降低策划/非程序人员填写配置的理解成本。
    /// </summary>
    [CustomEditor(typeof(AppConfig))]
    public class AppConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var cfg = (AppConfig)target;
            serializedObject.Update();

            // ===== 性能 =====
            EditorGUILayout.LabelField("━ 性能 · 帧率与渲染 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_enablePlatformAutoDetect"),
                new GUIContent("平台自适应", "开启后根据平台自动调整帧率/画质，覆盖下方手动配置"));
            // NOTE: HelpBox reads from `cfg` (cast target) while PropertyField writes to
            // `serializedObject`. Until ApplyModifiedProperties runs at the bottom of
            // OnInspectorGUI, `cfg` holds the *previous* frame's value — so the HelpBox
            // text is one-frame-late when the user toggles a checkbox. Fix by reading
            // serializedObject.FindProperty("_enablePlatformAutoDetect").boolValue instead.
            EditorGUILayout.HelpBox(cfg.EnablePlatformAutoDetect
                ? "✅ 已开启 — 移动端→30FPS+低画质  WebGL→30FPS+低画质  桌面端→60FPS+中画质"
                : "⏭ 未开启 — 使用下方手动配置的值", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_targetFrameRate"),
                new GUIContent("目标帧率", "0=平台自适应，-1=不限制，30/60/120=固定"));
            string frameHint = cfg.EnablePlatformAutoDetect
                ? "⚠ 平台自适应已开启，此值被覆盖。运行时帧率由平台决定"
                : "0=平台自适应，-1=不限制，30/60/120=固定刷新率";
            EditorGUILayout.HelpBox(frameHint, cfg.EnablePlatformAutoDetect ? MessageType.Warning : MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_vSyncCount"),
                new GUIContent("垂直同步", "0=关闭（低延迟，允许撕裂）  1=每帧同步（无撕裂）  2=每两帧同步"));
            EditorGUILayout.HelpBox("0=关闭，1=每帧同步消除撕裂，2=每两帧同步", MessageType.None);

            EditorGUILayout.Space(8);

            // ===== 后台行为 =====
            EditorGUILayout.LabelField("━ 后台行为 · 窗口失焦策略 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_runInBackground"),
                new GUIContent("允许后台运行", "窗口失焦时游戏是否继续运行（WebGL 无视此设置）"));
            EditorGUILayout.HelpBox("开启后窗口失焦游戏仍运行；WebGL 平台此设置无效", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_throttleInBackground"),
                new GUIContent("后台省电模式", "失焦时自动降低帧率以节省 CPU/电量"));
            EditorGUILayout.HelpBox("仅在上方「允许后台运行」开启时生效", MessageType.None);

            if (cfg.ThrottleInBackground)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("_backgroundFrameRate"),
                    new GUIContent("后台帧率", "失焦时的目标帧率，建议 5~15"));
                EditorGUILayout.HelpBox($"当前设置：失焦后帧率降至 {cfg.BackgroundFrameRate} FPS", MessageType.Info);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8);

            // ===== 功能开关 =====
            EditorGUILayout.LabelField("━ 功能开关 · 模块启停（本地测试可关闭）━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_enableHotUpdate"),
                new GUIContent("启用热更新", "关闭后跳过版本检查与资源下载，直接进入登录"));
            EditorGUILayout.HelpBox(cfg.EnableHotUpdate
                ? "✅ 线上模式 — 启动时检查版本并下载热更包"
                : "⏭ 本地模式 — 跳过热更，直接进入登录", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_enableAudio"),
                new GUIContent("启用音频", "关闭后 AudioManager 不初始化，静音运行"));
            EditorGUILayout.HelpBox(cfg.EnableAudio ? "✅ 正常播放音效/BGM" : "⏭ 静音模式", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_enableSDK"),
                new GUIContent("启用 SDK", "关闭后跳过第三方 SDK 初始化（广告/统计/支付）"));
            EditorGUILayout.HelpBox(cfg.EnableSDK ? "✅ 初始化全部已注册 SDK" : "⏭ 本地开发模式，SDK 不加载", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_enableLocalization"),
                new GUIContent("启用多语言", "关闭后文本使用 key 原名，不加载语言表"));
            EditorGUILayout.HelpBox(cfg.EnableLocalization ? "✅ 加载语言表，按 key 翻译" : "⏭ 直接显示 key 原名", MessageType.None);

            EditorGUILayout.Space(8);

            // ===== 资源 =====
            EditorGUILayout.LabelField("━ 资源 · 加载方式与策略 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_resourceProvider"),
                new GUIContent("资源提供者", "底层从何处加载资源"));
            string providerHint = cfg.ResourceProvider switch
            {
                ResourceProviderType.Resources    => "📁 Resources 目录 — 本地开发，无需打包，即改即用",
                ResourceProviderType.AssetBundle  => "TODO(YooAsset)：保留旧序列化值；当前启动时回退到 Resources",
                ResourceProviderType.Addressables => "TODO(YooAsset)：保留旧序列化值；当前启动时回退到 Resources",
                _                                 => "",
            };
            EditorGUILayout.HelpBox(providerHint, MessageType.Info);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_loadingMode"),
                new GUIContent("加载策略", "游戏启动时的异步加载策略"));
            string loadingHint = cfg.LoadingMode switch
            {
                LoadingMode.Sequential => "顺序加载：按依赖链逐个加载 → 启动慢但内存稳，适合低端设备",
                LoadingMode.Parallel   => "并行加载：多模块同时加载 → 启动最快但瞬时内存高，适合高端设备",
                LoadingMode.Lazy       => "懒加载：最小启动 + 按需加载 → 内存最优但首次交互可能卡顿",
                _                      => "",
            };
            EditorGUILayout.HelpBox(loadingHint, MessageType.Info);

            EditorGUILayout.Space(8);

            // ===== 画质 =====
            EditorGUILayout.LabelField("━ 画质 · 渲染质量预设 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_qualityLevel"),
                new GUIContent("初始画质等级", "对应 QualitySettings 数组索引"));
            EditorGUILayout.HelpBox("通常 0=低画质  1=中画质  2=高画质", MessageType.None);

            EditorGUILayout.Space(8);

            // ===== 日志 =====
            EditorGUILayout.LabelField("━ 日志 · 输出等级与过滤 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_minLogLevel"),
                new GUIContent("最低日志等级", "低于此级别的日志会被静默过滤"));
            string logHint = (LogLevel)serializedObject.FindProperty("_minLogLevel").enumValueIndex switch
            {
                LogLevel.Verbose => "Verbose：全量输出（开发期推荐）",
                LogLevel.Debug   => "Debug：调试信息 + 以上",
                LogLevel.Info    => "Info：普通信息 + 以上",
                LogLevel.Warning => "Warning：仅警告 + 错误（线上推荐）",
                LogLevel.Error   => "Error：仅错误",
                LogLevel.Fatal   => "Fatal：仅致命错误",
                LogLevel.None    => "None：关闭所有日志",
                _                => "",
            };
            EditorGUILayout.HelpBox(logHint, MessageType.Info);

            EditorGUILayout.Space(8);

            // ===== WebGL =====
            EditorGUILayout.LabelField("━ WebGL 专属 · 仅 WebGL 构建生效 ━", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_webGLMemorySize"),
                new GUIContent("内存大小（MB）", "浏览器端请求的内存大小，建议 256~2048"));
            EditorGUILayout.HelpBox("建议 256~2048 MB；过小→资源加载失败，过大→浏览器拒绝", MessageType.None);

            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_webGLExceptionMode"),
                new GUIContent("异常捕获模式", "C# 异常在浏览器中的捕获策略"));
            string excHint = cfg.WebGLExceptionMode switch
            {
                WebGLExceptionMode.None                  => "不捕获 → 异常导致播放器崩溃，性能最优",
                WebGLExceptionMode.ExplicitlyThrown     => "仅显式 throw → 兼顾性能与基本错误追踪（推荐）",
                WebGLExceptionMode.FullWithoutStacktrace => "全量无堆栈 → 捕获所有异常，无 C# 堆栈，适合线上",
                WebGLExceptionMode.FullWithStacktrace    => "全量带堆栈 → 完整 C# 调用栈，调试必选但包体/内存开销大",
                _                                        => "",
            };
            EditorGUILayout.HelpBox(excHint, MessageType.Info);

            EditorGUILayout.Space(8);

            Undo.RecordObject(target, "Modify AppConfig");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
