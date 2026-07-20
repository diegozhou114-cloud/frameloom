namespace ZWebGLFrameWork.Server
{
    public static class ReplayCliExitCodes
    {
        public static int For(ReplayValidationCode code)
        {
            switch (code)
            {
                case ReplayValidationCode.Valid:
                    return 0;
                case ReplayValidationCode.UnsupportedVersion:
                case ReplayValidationCode.SchemaMismatch:
                    return 3;
                case ReplayValidationCode.ConfigMismatch:
                case ReplayValidationCode.InitialStateMismatch:
                case ReplayValidationCode.TickRateMismatch:
                case ReplayValidationCode.MatchMismatch:
                case ReplayValidationCode.UnauthorizedSubmitter:
                    return 4;
                case ReplayValidationCode.FileTooLarge:
                case ReplayValidationCode.TooManyTicks:
                case ReplayValidationCode.TooManyInitialEntities:
                case ReplayValidationCode.SnapshotTooLarge:
                case ReplayValidationCode.TooManyCommands:
                case ReplayValidationCode.ValidationBudgetExceeded:
                    return 5;
                default:
                    return 2;
            }
        }
    }
}
