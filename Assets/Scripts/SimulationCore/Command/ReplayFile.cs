using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    public static class ReplayFormat
    {
        public const ushort CurrentVersion = 1;
        public const int TickRate = 20;
    }

    /// <summary>可序列化的命令包装</summary>
    [Serializable]
    public class SerializableCommand
    {
        public string Type { get; set; }
        public int Tick { get; set; }
        public int PlayerId { get; set; }
        public int Sequence { get; set; }
        public int UnitTypeId { get; set; }
        public int EntityId { get; set; }
        public int CasterId { get; set; }
        public int SkillId { get; set; }
        public int GridX { get; set; }
        public int TargetX { get; set; }
        public int GridY { get; set; }
        public int TargetY { get; set; }
    }

    /// <summary>单 Tick 的录制数据</summary>
    [Serializable]
    public class ReplayTickRecord
    {
        public int Tick { get; set; }
        public List<SerializableCommand> Commands { get; set; } = new List<SerializableCommand>();
        public ulong Checksum { get; set; }
    }

    /// <summary>
    /// 战斗回放文件 — 包含完整的命令序列和校验数据。
    /// 用于异步攻击验证：客户端上传，服务器重放比对。
    /// </summary>
    [Serializable]
    public class ReplayFile
    {
        public ReplayBattleInit BattleInit { get; set; }
        public ushort FormatVersion { get; set; }
        public string SimulationSchemaHash { get; set; }
        public string ContentConfigHash { get; set; }
        public int TickRate { get; set; }
        public string MatchId { get; set; }
        public string InitialStateHash { get; set; }
        public string SubmissionNonce { get; set; }
        public int FinalTick { get; set; } = -1;
        public SimBattleResult FinalResult { get; set; }
        public ulong FinalChecksum { get; set; }

        // Retained until authoritative match migration removes client-owned initial state.
        public int Version { get; set; } = 1;
        public int Seed { get; set; }
        public SimEntitySpawnInfo[] InitialLayout { get; set; }
        public List<ReplayTickRecord> Records { get; set; } =
            new List<ReplayTickRecord>();

        public int TotalTicks => Records.Count;

        public ulong GetChecksum(int tick)
        {
            // TODO: Use Dictionary<int, ReplayTickRecord> for O(1) lookup in large replays
            foreach (ReplayTickRecord r in Records)
                if (r.Tick == tick) return r.Checksum;
            return 0;
        }
    }

    /// <summary>
    /// 由权威匹配创建响应提供的回放上传身份字段。
    /// 录制器不能从 BattleInit 推导这些值，必须原样保留到上传回放中。
    /// </summary>
    public sealed class ReplayAuthorityMetadata
    {
        public string SimulationSchemaHash { get; }
        public string ContentConfigHash { get; }
        public string InitialStateHash { get; }
        public string SubmissionNonce { get; }

        public ReplayAuthorityMetadata(string simulationSchemaHash,
            string contentConfigHash, string initialStateHash,
            string submissionNonce)
        {
            SimulationSchemaHash = RequireValue(simulationSchemaHash,
                nameof(simulationSchemaHash));
            ContentConfigHash = RequireValue(contentConfigHash,
                nameof(contentConfigHash));
            InitialStateHash = RequireValue(initialStateHash,
                nameof(initialStateHash));
            SubmissionNonce = RequireValue(submissionNonce,
                nameof(submissionNonce));
        }

        private static string RequireValue(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", name);
            return value;
        }
    }

    [Serializable]
    public sealed class ReplayBattleInit
    {
        private readonly byte[] _contentHash;
        private readonly SimPlayerIdentity[] _players;
        private readonly InitialEntitySpec[] _initialEntities;

        public int ProtocolVersion { get; }
        public int SchemaVersion { get; }
        public string MatchId { get; }
        public string SessionId { get; }
        public int Seed { get; }
        public int TickRate { get; }
        public SimCombatMode Mode { get; }
        public byte[] ContentHash => (byte[])_contentHash.Clone();
        public SimPlayerIdentity[] Players => (SimPlayerIdentity[])_players.Clone();
        public InitialEntitySpec[] InitialEntities =>
            (InitialEntitySpec[])_initialEntities.Clone();
        public int InputDeadlineTicks { get; }

        public ReplayBattleInit(int protocolVersion, int schemaVersion,
            string matchId, string sessionId, int seed, int tickRate,
            SimCombatMode mode, byte[] contentHash, SimPlayerIdentity[] players,
            InitialEntitySpec[] initialEntities, int inputDeadlineTicks)
        {
            ProtocolVersion = protocolVersion;
            SchemaVersion = schemaVersion;
            MatchId = matchId ?? throw new ArgumentNullException(nameof(matchId));
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Seed = seed;
            TickRate = tickRate;
            Mode = mode;
            _contentHash = (byte[])(contentHash ??
                throw new ArgumentNullException(nameof(contentHash))).Clone();
            _players = (SimPlayerIdentity[])(players ??
                throw new ArgumentNullException(nameof(players))).Clone();
            _initialEntities = (InitialEntitySpec[])(initialEntities ??
                throw new ArgumentNullException(nameof(initialEntities))).Clone();
            InputDeadlineTicks = inputDeadlineTicks;
        }

        public static ReplayBattleInit From(BattleInit init)
        {
            if (init == null) throw new ArgumentNullException(nameof(init));
            return new ReplayBattleInit(init.ProtocolVersion, init.SchemaVersion,
                init.MatchId, init.SessionId, init.Seed, init.TickRate, init.Mode,
                init.ContentHash, init.Players, init.InitialEntities,
                init.InputDeadlineTicks);
        }

        public BattleInit ToBattleInit() => new BattleInit(
            ProtocolVersion, SchemaVersion, MatchId, SessionId, Seed, TickRate,
            Mode, _contentHash, _players, _initialEntities, InputDeadlineTicks);
    }
}
