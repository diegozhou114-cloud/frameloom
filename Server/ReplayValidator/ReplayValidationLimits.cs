namespace ZWebGLFrameWork.Server
{
    public sealed class ReplayValidationLimits
    {
        public static readonly ReplayValidationLimits Approved =
            new ReplayValidationLimits(
                16777216,
                12000,
                64,
                100000,
                4096,
                8388608);

        public int MaxFileBytes { get; }
        public int MaxTicks { get; }
        public int MaxCommandsPerTick { get; }
        public int MaxTotalCommands { get; }
        public int MaxInitialEntities { get; }
        public int MaxDecompressedSnapshotBytes { get; }

        public ReplayValidationLimits(
            int maxFileBytes,
            int maxTicks,
            int maxCommandsPerTick,
            int maxTotalCommands,
            int maxInitialEntities,
            int maxDecompressedSnapshotBytes)
        {
            if (maxFileBytes <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxFileBytes));
            if (maxTicks <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxTicks));
            if (maxCommandsPerTick <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxCommandsPerTick));
            if (maxTotalCommands < maxCommandsPerTick)
                throw new System.ArgumentOutOfRangeException(nameof(maxTotalCommands));
            if (maxInitialEntities <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxInitialEntities));
            if (maxDecompressedSnapshotBytes <= 0)
                throw new System.ArgumentOutOfRangeException(
                    nameof(maxDecompressedSnapshotBytes));
            MaxFileBytes = maxFileBytes;
            MaxTicks = maxTicks;
            MaxCommandsPerTick = maxCommandsPerTick;
            MaxTotalCommands = maxTotalCommands;
            MaxInitialEntities = maxInitialEntities;
            MaxDecompressedSnapshotBytes = maxDecompressedSnapshotBytes;
        }
    }
}
