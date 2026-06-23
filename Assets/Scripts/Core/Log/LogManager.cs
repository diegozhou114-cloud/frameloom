using System;
using System.Text;
using UnityEngine;

namespace ZWebGLFrameWork.Core
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        Verbose = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        None = 99,
    }

    /// <summary>
    /// 统一日志管理器 — 提供分级/分类/格式化日志。
    /// WebGL 平台自动适配，支持运行时切换日志级别。
    /// 用法：LogManager.Info("Network", "Connected to {0}:{1}", host, port);
    /// </summary>
    public static class LogManager
    {
        /// <summary>当前日志级别（低于此级别的日志被过滤）</summary>
        public static LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }
        private static LogLevel _minLevel =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            LogLevel.Verbose;
#else
            LogLevel.Warning;
#endif

        /// <summary>日志输出回调（可注入自定义收集器，如文件/网络上传）</summary>
        public static event Action<LogLevel, string, string> OnLog;

        // ==================== 公开 API ====================

        public static void Verbose(string tag, string message, params object[] args)
            => Log(LogLevel.Verbose, tag, message, args);

        public static void Debug(string tag, string message, params object[] args)
            => Log(LogLevel.Debug, tag, message, args);

        public static void Info(string tag, string message, params object[] args)
            => Log(LogLevel.Info, tag, message, args);

        public static void Warning(string tag, string message, params object[] args)
            => Log(LogLevel.Warning, tag, message, args);

        public static void Error(string tag, string message, params object[] args)
            => Log(LogLevel.Error, tag, message, args);

        public static void Fatal(string tag, string message, params object[] args)
            => Log(LogLevel.Fatal, tag, message, args);

        /// <summary>异常日志</summary>
        public static void Exception(string tag, Exception ex, string context = null)
        {
            var sb = new StringBuilder();
            sb.Append(context != null ? $"[{context}] " : "");
            sb.Append(ex.GetType().Name);
            sb.Append(": ");
            sb.Append(ex.Message);

            Log(LogLevel.Error, tag, sb.ToString());

            if (MinLevel <= LogLevel.Debug)
            {
                UnityEngine.Debug.LogException(ex);
            }
        }

        // ==================== 内部 ====================

        private static void Log(LogLevel level, string tag, string message, object[] args = null)
        {
            if (level < MinLevel) return;

            string formatted = args != null && args.Length > 0
                ? string.Format(message, args)
                : message;

            string line = $"[{level.ToString().ToUpperInvariant()}] [{tag}] {formatted}";

            switch (level)
            {
                case LogLevel.Verbose:
                case LogLevel.Debug:
                case LogLevel.Info:
                    UnityEngine.Debug.Log(line);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(line);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    UnityEngine.Debug.LogError(line);
                    break;
            }

            OnLog?.Invoke(level, tag, formatted);
        }
    }
}
