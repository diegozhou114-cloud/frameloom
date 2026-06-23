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
}
