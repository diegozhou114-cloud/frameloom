namespace ZWebGLFrameWork.SimulationCore
{
    public enum SessionLifecycle
    {
        Created,
        Running,
        Ended,
        Disposed,
    }

    public enum StartCode
    {
        Accepted,
        AlreadyStarted,
        UnsupportedProtocol,
        UnsupportedSchema,
        InvalidMatch,
        InvalidContent,
        InvalidPlayer,
        InvalidEntity,
        InvalidMode,
    }

    public enum TickCode
    {
        Advanced,
        NotRunning,
        Ended,
        Disposed,
    }

    public enum CommandDisposition
    {
        Accepted,
        Duplicate,
        NonMonotonicSequence,
        LateTick,
        FutureWindowExceeded,
        SealedTick,
        UnknownPlayer,
        WrongPhase,
        InvalidPayload,
        UnauthorizedEntity,
        ClientNoOpForbidden,
    }

    public enum SnapshotRestoreCode
    {
        Restored,
        SnapshotUnavailable,
        Disposed,
        ForeignMatch,
        ForeignSession,
        UnsupportedSchema,
        ContentMismatch,
        StaleRevision,
        InvalidTick,
        Oversized,
        CorruptPayload,
    }

    public readonly struct StartResult
    {
        public StartCode Code { get; }
        public PresentationFrame InitialFrame { get; }
        public bool Accepted => Code == StartCode.Accepted;

        public StartResult(StartCode code, PresentationFrame initialFrame)
        {
            Code = code;
            InitialFrame = initialFrame;
        }
    }

    public readonly struct CommandSubmitResult
    {
        public CommandDisposition Disposition { get; }
        public bool Accepted => Disposition == CommandDisposition.Accepted;

        public CommandSubmitResult(CommandDisposition disposition)
        {
            Disposition = disposition;
        }
    }

    public readonly struct SnapshotRestoreResult
    {
        public SnapshotRestoreCode Code { get; }
        public PresentationFrame FullFrame { get; }
        public bool Restored => Code == SnapshotRestoreCode.Restored;

        public SnapshotRestoreResult(SnapshotRestoreCode code, PresentationFrame fullFrame)
        {
            Code = code;
            FullFrame = fullFrame;
        }
    }
}
