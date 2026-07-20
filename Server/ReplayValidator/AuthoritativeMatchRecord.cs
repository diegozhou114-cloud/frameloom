using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    public sealed record AuthoritativeMatchRecord
    {
        public string MatchId { get; init; }
        public string InitialStateHash { get; init; }
        public string SubmissionNonce { get; init; }
        public string SimulationSchemaHash { get; init; }
        public string ContentConfigHash { get; init; }
        private readonly byte[] _canonicalManifestBytes;
        private readonly string[] _allowedSubmitterPlayerIds;

        public byte[] CanonicalManifestBytes
        {
            get => Copy(_canonicalManifestBytes);
            init => _canonicalManifestBytes = Copy(value);
        }
        public BattleInit BattleInit { get; init; }
        public SimSnapshot InitialSnapshot { get; init; }
        public SimBattleResult ExpectedResult { get; init; }
        public string[] AllowedSubmitterPlayerIds
        {
            get => Copy(_allowedSubmitterPlayerIds);
            init => _allowedSubmitterPlayerIds = Copy(value);
        }

        public AuthoritativeMatchRecord(
            string matchId,
            string initialStateHash,
            string submissionNonce,
            string simulationSchemaHash,
            string contentConfigHash,
            byte[] canonicalManifestBytes,
            BattleInit battleInit,
            SimSnapshot initialSnapshot,
            SimBattleResult expectedResult,
            string[] allowedSubmitterPlayerIds)
        {
            MatchId = matchId;
            InitialStateHash = initialStateHash;
            SubmissionNonce = submissionNonce;
            SimulationSchemaHash = simulationSchemaHash;
            ContentConfigHash = contentConfigHash;
            _canonicalManifestBytes = Copy(canonicalManifestBytes);
            BattleInit = battleInit;
            InitialSnapshot = initialSnapshot;
            ExpectedResult = expectedResult;
            _allowedSubmitterPlayerIds = Copy(allowedSubmitterPlayerIds);
        }

        private static T[] Copy<T>(T[] source) => source == null
            ? null
            : (T[])source.Clone();
    }
}
