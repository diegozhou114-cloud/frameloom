using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public sealed class ReplayValidationResult
    {
        public ReplayValidationCode Code { get; }
        public bool IsValid => Code == ReplayValidationCode.Valid;
        public int FailedTick { get; }
        public ulong ExpectedChecksum { get; }
        public ulong ActualChecksum { get; }
        public SimBattleResult FinalResult { get; }

        public ReplayValidationResult(
            ReplayValidationCode code,
            int failedTick = -1,
            ulong expectedChecksum = 0,
            ulong actualChecksum = 0,
            SimBattleResult finalResult = null)
        {
            Code = code;
            FailedTick = failedTick;
            ExpectedChecksum = expectedChecksum;
            ActualChecksum = actualChecksum;
            FinalResult = finalResult;
        }

        public static ReplayValidationResult StructurallyValid() =>
            new ReplayValidationResult(ReplayValidationCode.Valid);

        public static ReplayValidationResult Valid(
            int finalTick, ulong checksum, SimBattleResult result) =>
            new ReplayValidationResult(
                ReplayValidationCode.Valid,
                finalTick,
                checksum,
                checksum,
                result);
    }
}
