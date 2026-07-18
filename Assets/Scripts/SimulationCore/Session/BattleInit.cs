using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public enum SimCombatMode : byte
    {
        VillageAttack = 1,
        BuilderBase = 2,
        ClanWar = 3,
        Campaign = 4,
    }

    public readonly struct SimPlayerIdentity
    {
        public int PlayerId { get; }
        public string AccountId { get; }

        public SimPlayerIdentity(int playerId, string accountId)
        {
            PlayerId = playerId;
            AccountId = accountId ?? throw new ArgumentNullException(nameof(accountId));
        }
    }

    public readonly struct InitialEntitySpec
    {
        public int ArchetypeId { get; }
        public int GridX { get; }
        public int GridY { get; }
        public int TeamId { get; }
        public int OwnerPlayerId { get; }

        public InitialEntitySpec(int archetypeId, int gridX, int gridY, int teamId, int ownerPlayerId)
        {
            ArchetypeId = archetypeId;
            GridX = gridX;
            GridY = gridY;
            TeamId = teamId;
            OwnerPlayerId = ownerPlayerId;
        }
    }

    public sealed class BattleInit
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
        public InitialEntitySpec[] InitialEntities => (InitialEntitySpec[])_initialEntities.Clone();
        public int InputDeadlineTicks { get; }

        public BattleInit(int protocolVersion, int schemaVersion, string matchId, string sessionId,
            int seed, int tickRate, SimCombatMode mode, byte[] contentHash,
            SimPlayerIdentity[] players, InitialEntitySpec[] initialEntities, int inputDeadlineTicks)
        {
            ProtocolVersion = protocolVersion;
            SchemaVersion = schemaVersion;
            MatchId = matchId ?? throw new ArgumentNullException(nameof(matchId));
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            Seed = seed;
            TickRate = tickRate;
            Mode = mode;
            _contentHash = (byte[])(contentHash ?? throw new ArgumentNullException(nameof(contentHash))).Clone();
            _players = (SimPlayerIdentity[])(players ?? throw new ArgumentNullException(nameof(players))).Clone();
            _initialEntities = (InitialEntitySpec[])(initialEntities ?? throw new ArgumentNullException(nameof(initialEntities))).Clone();
            InputDeadlineTicks = inputDeadlineTicks;
        }
    }
}
