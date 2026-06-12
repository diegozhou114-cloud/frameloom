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
    }
}
