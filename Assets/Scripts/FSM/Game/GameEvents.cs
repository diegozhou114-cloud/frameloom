namespace ZWebGLFrameWork.FSM
{
    public struct GameStateChangedEvent
    {
        public GameState OldState;
        public GameState NewState;
        public object Data;
        public override string ToString() => $"{OldState} → {NewState}";
    }

    public struct LoadingPayload
    {
        public GameState TargetState;
        public object TargetData;
        /// <summary>需要加载的场景名（null/empty = 仅过渡动画，不加载场景）</summary>
        public string TargetScene;
    }

    /// <summary>游戏暂停事件</summary>
    public struct GamePauseEvent
    {
        /// <summary>暂停原因：Manual=手动暂停, Background=切后台</summary>
        public string Reason;
        public static GamePauseEvent Manual => new() { Reason = "Manual" };
        public static GamePauseEvent Background => new() { Reason = "Background" };
    }

    /// <summary>游戏恢复事件</summary>
    public struct GameResumeEvent
    {
        /// <summary>已暂停时长（秒）</summary>
        public float PausedDuration;
    }
}
