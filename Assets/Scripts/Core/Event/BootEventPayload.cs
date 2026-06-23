namespace ZWebGLFrameWork.Core
{
    /// <summary>启动阶段进度事件（LaunchState → UI 面板）</summary>
    public struct BootProgressEvent
    {
        /// <summary>当前阶段描述（如 "引擎初始化中..."）</summary>
        public string Phase;
        /// <summary>进度 0~1</summary>
        public float Progress;
        /// <summary>额外提示（如 下载速度）</summary>
        public string Hint;

        public BootProgressEvent(string phase, float progress, string hint = "")
        {
            Phase = phase;
            Progress = progress;
            Hint = hint;
        }

        public static readonly string EventKey = "BootProgress";
    }

    /// <summary>启动错误事件（任意 Manager Init 失败 → UI 面板）</summary>
    public struct BootErrorEvent
    {
        /// <summary>出错的模块名</summary>
        public string Module;
        /// <summary>错误描述</summary>
        public string Message;
        /// <summary>是否允许跳过继续</summary>
        public bool CanSkip;

        public BootErrorEvent(string module, string message, bool canSkip = true)
        {
            Module = module;
            Message = message;
            CanSkip = canSkip;
        }

        public static readonly string EventKey = "BootError";
    }
}
