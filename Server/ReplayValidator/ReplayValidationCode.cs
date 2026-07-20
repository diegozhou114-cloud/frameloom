namespace ZWebGLFrameWork.Server
{
    public enum ReplayValidationCode
    {
        Valid = 0,
        NullReplay,
        FileTooLarge,
        MalformedJson,
        Empty,
        UnsupportedVersion,
        TooManyTicks,
        TooManyInitialEntities,
        SnapshotTooLarge,
        FinalTickMismatch,
        TickGap,
        DuplicateOrUnorderedTick,
        TooManyCommands,
        UnknownCommand,
        InvalidCommand,
        MatchMismatch,
        UnauthorizedSubmitter,
        InitialStateMismatch,
        SchemaMismatch,
        ConfigMismatch,
        TickRateMismatch,
        ValidationBudgetExceeded,
        CommandRejected,
        ChecksumMismatch,
        TrailingRecord,
        Truncated,
        FinalChecksumMismatch,
        ResultMismatch
    }
}
