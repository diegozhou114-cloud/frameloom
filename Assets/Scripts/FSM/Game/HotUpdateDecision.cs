namespace ZWebGLFrameWork.FSM
{
    public enum HotUpdateAction
    {
        Continue,
        Retry,
        ForceUpdate,
        RestartRequired,
    }

    public static class HotUpdateDecision
    {
        public static HotUpdateAction Evaluate(
            VersionCheckResult check,
            bool downloadSucceeded,
            bool needRestart)
        {
            if (check == VersionCheckResult.ForceUpdate)
                return HotUpdateAction.ForceUpdate;
            if (check == VersionCheckResult.Error)
                return HotUpdateAction.Retry;
            if (check == VersionCheckResult.NeedUpdate && !downloadSucceeded)
                return HotUpdateAction.Retry;
            if (needRestart)
                return HotUpdateAction.RestartRequired;
            return HotUpdateAction.Continue;
        }
    }
}
