using System;
using System.Collections.Generic;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public static class UnityReplayJsonCodec
    {
        public static string Serialize(ReplayFile replay, bool prettyPrint = true)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            return JsonUtility.ToJson(ReplayDto.From(replay), prettyPrint);
        }

        public static ReplayFile Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Replay JSON must not be empty.", nameof(json));

            ReplayDto dto = JsonUtility.FromJson<ReplayDto>(json);
            if (dto == null) throw new FormatException("Replay JSON is invalid.");
            return dto.ToReplay();
        }

        [Serializable]
        private sealed class ReplayDto
        {
            public BattleInitDto BattleInit;
            public ushort FormatVersion;
            public string SimulationSchemaHash;
            public string ContentConfigHash;
            public int TickRate;
            public string MatchId;
            public string InitialStateHash;
            public string SubmissionNonce;
            public int FinalTick = -1;
            public ResultDto FinalResult;
            public ulong FinalChecksum;
            public int Version = 1;
            public int Seed;
            public SpawnDto[] InitialLayout = Array.Empty<SpawnDto>();
            public TickDto[] Records = Array.Empty<TickDto>();

            public static ReplayDto From(ReplayFile replay)
            {
                var dto = new ReplayDto
                {
                    BattleInit = BattleInitDto.From(replay.BattleInit),
                    FormatVersion = replay.FormatVersion,
                    SimulationSchemaHash = replay.SimulationSchemaHash,
                    ContentConfigHash = replay.ContentConfigHash,
                    TickRate = replay.TickRate,
                    MatchId = replay.MatchId,
                    InitialStateHash = replay.InitialStateHash,
                    SubmissionNonce = replay.SubmissionNonce,
                    FinalTick = replay.FinalTick,
                    FinalResult = ResultDto.From(replay.FinalResult),
                    FinalChecksum = replay.FinalChecksum,
                    Version = replay.Version,
                    Seed = replay.Seed,
                    InitialLayout = new SpawnDto[replay.InitialLayout?.Length ?? 0],
                    Records = new TickDto[replay.Records?.Count ?? 0]
                };
                for (int index = 0; index < dto.InitialLayout.Length; index++)
                    dto.InitialLayout[index] = SpawnDto.From(replay.InitialLayout[index]);
                for (int index = 0; index < dto.Records.Length; index++)
                    dto.Records[index] = TickDto.From(replay.Records[index]);
                return dto;
            }

            public ReplayFile ToReplay()
            {
                var replay = new ReplayFile
                {
                    BattleInit = BattleInit?.ToReplayBattleInit(),
                    FormatVersion = FormatVersion,
                    SimulationSchemaHash = SimulationSchemaHash,
                    ContentConfigHash = ContentConfigHash,
                    TickRate = TickRate,
                    MatchId = MatchId,
                    InitialStateHash = InitialStateHash,
                    SubmissionNonce = SubmissionNonce,
                    FinalTick = FinalTick,
                    FinalResult = FinalResult?.ToResult(),
                    FinalChecksum = FinalChecksum,
                    Version = Version,
                    Seed = Seed,
                    InitialLayout = new SimEntitySpawnInfo[InitialLayout?.Length ?? 0],
                    Records = new List<ReplayTickRecord>(Records?.Length ?? 0)
                };
                for (int index = 0; index < replay.InitialLayout.Length; index++)
                    replay.InitialLayout[index] = InitialLayout[index].ToSpawnInfo();
                if (Records != null)
                {
                    foreach (TickDto record in Records)
                        replay.Records.Add(record.ToTickRecord());
                }
                return replay;
            }
        }

        [Serializable]
        private sealed class BattleInitDto
        {
            public int ProtocolVersion;
            public int SchemaVersion;
            public string MatchId;
            public string SessionId;
            public int Seed;
            public int TickRate;
            public SimCombatMode Mode;
            public byte[] ContentHash = Array.Empty<byte>();
            public PlayerDto[] Players = Array.Empty<PlayerDto>();
            public InitialEntityDto[] InitialEntities =
                Array.Empty<InitialEntityDto>();
            public int InputDeadlineTicks;

            public static BattleInitDto From(ReplayBattleInit init)
            {
                if (init == null) return null;
                var dto = new BattleInitDto
                {
                    ProtocolVersion = init.ProtocolVersion,
                    SchemaVersion = init.SchemaVersion,
                    MatchId = init.MatchId,
                    SessionId = init.SessionId,
                    Seed = init.Seed,
                    TickRate = init.TickRate,
                    Mode = init.Mode,
                    ContentHash = init.ContentHash,
                    Players = new PlayerDto[init.Players.Length],
                    InitialEntities =
                        new InitialEntityDto[init.InitialEntities.Length],
                    InputDeadlineTicks = init.InputDeadlineTicks
                };
                SimPlayerIdentity[] players = init.Players;
                for (int index = 0; index < players.Length; index++)
                    dto.Players[index] = PlayerDto.From(players[index]);
                InitialEntitySpec[] entities = init.InitialEntities;
                for (int index = 0; index < entities.Length; index++)
                    dto.InitialEntities[index] = InitialEntityDto.From(entities[index]);
                return dto;
            }

            public ReplayBattleInit ToReplayBattleInit()
            {
                var players = new SimPlayerIdentity[Players?.Length ?? 0];
                for (int index = 0; index < players.Length; index++)
                    players[index] = Players[index].ToPlayer();
                var entities = new InitialEntitySpec[InitialEntities?.Length ?? 0];
                for (int index = 0; index < entities.Length; index++)
                    entities[index] = InitialEntities[index].ToEntity();
                return new ReplayBattleInit(ProtocolVersion, SchemaVersion,
                    MatchId, SessionId, Seed, TickRate, Mode,
                    ContentHash ?? Array.Empty<byte>(), players, entities,
                    InputDeadlineTicks);
            }
        }

        [Serializable]
        private sealed class PlayerDto
        {
            public int PlayerId;
            public string AccountId;

            public static PlayerDto From(SimPlayerIdentity player) => new PlayerDto
            {
                PlayerId = player.PlayerId,
                AccountId = player.AccountId
            };

            public SimPlayerIdentity ToPlayer() =>
                new SimPlayerIdentity(PlayerId, AccountId);
        }

        [Serializable]
        private sealed class InitialEntityDto
        {
            public int ArchetypeId;
            public int GridX;
            public int GridY;
            public int TeamId;
            public int OwnerPlayerId;

            public static InitialEntityDto From(InitialEntitySpec entity) =>
                new InitialEntityDto
                {
                    ArchetypeId = entity.ArchetypeId,
                    GridX = entity.GridX,
                    GridY = entity.GridY,
                    TeamId = entity.TeamId,
                    OwnerPlayerId = entity.OwnerPlayerId
                };

            public InitialEntitySpec ToEntity() => new InitialEntitySpec(
                ArchetypeId, GridX, GridY, TeamId, OwnerPlayerId);
        }

        [Serializable]
        private sealed class ResultDto
        {
            public string MatchId;
            public string ResultId;
            public int WinnerTeamId;
            public int Stars;
            public int LootGold;
            public int LootElixir;
            public int FinalTick;
            public ulong FinalChecksum;

            public static ResultDto From(SimBattleResult result) => result == null
                ? null
                : new ResultDto
                {
                    MatchId = result.MatchId,
                    ResultId = result.ResultId,
                    WinnerTeamId = result.WinnerTeamId,
                    Stars = result.Stars,
                    LootGold = result.LootGold,
                    LootElixir = result.LootElixir,
                    FinalTick = result.FinalTick,
                    FinalChecksum = result.FinalChecksum
                };

            public SimBattleResult ToResult() => new SimBattleResult(
                MatchId,
                ResultId,
                WinnerTeamId,
                Stars,
                LootGold,
                LootElixir,
                FinalTick,
                FinalChecksum);
        }

        [Serializable]
        private sealed class SpawnDto
        {
            public int ArchetypeId;
            public int GridX;
            public int GridY;
            public int TeamId;

            public static SpawnDto From(SimEntitySpawnInfo spawn) => new SpawnDto
            {
                ArchetypeId = spawn.ArchetypeId,
                GridX = spawn.GridX,
                GridY = spawn.GridY,
                TeamId = spawn.TeamId
            };

            public SimEntitySpawnInfo ToSpawnInfo() =>
                new SimEntitySpawnInfo(ArchetypeId, GridX, GridY, TeamId);
        }

        [Serializable]
        private sealed class TickDto
        {
            public int Tick;
            public CommandDto[] Commands = Array.Empty<CommandDto>();
            public ulong Checksum;

            public static TickDto From(ReplayTickRecord record)
            {
                var dto = new TickDto
                {
                    Tick = record.Tick,
                    Checksum = record.Checksum,
                    Commands = new CommandDto[record.Commands?.Count ?? 0]
                };
                for (int index = 0; index < dto.Commands.Length; index++)
                    dto.Commands[index] = CommandDto.From(record.Commands[index]);
                return dto;
            }

            public ReplayTickRecord ToTickRecord()
            {
                var record = new ReplayTickRecord
                {
                    Tick = Tick,
                    Checksum = Checksum,
                    Commands = new List<SerializableCommand>(Commands?.Length ?? 0)
                };
                if (Commands != null)
                {
                    foreach (CommandDto command in Commands)
                        record.Commands.Add(command.ToCommand());
                }
                return record;
            }
        }

        [Serializable]
        private sealed class CommandDto
        {
            public string Type;
            public int Tick;
            public int PlayerId;
            public int Sequence;
            public int UnitTypeId;
            public int EntityId;
            public int CasterId;
            public int SkillId;
            public int GridX;
            public int TargetX;
            public int GridY;
            public int TargetY;

            public static CommandDto From(SerializableCommand command) => new CommandDto
            {
                Type = command.Type,
                Tick = command.Tick,
                PlayerId = command.PlayerId,
                Sequence = command.Sequence,
                UnitTypeId = command.UnitTypeId,
                EntityId = command.EntityId,
                CasterId = command.CasterId,
                SkillId = command.SkillId,
                GridX = command.GridX,
                TargetX = command.TargetX,
                GridY = command.GridY,
                TargetY = command.TargetY
            };

            public SerializableCommand ToCommand() => new SerializableCommand
            {
                Type = Type,
                Tick = Tick,
                PlayerId = PlayerId,
                Sequence = Sequence,
                UnitTypeId = UnitTypeId,
                EntityId = EntityId,
                CasterId = CasterId,
                SkillId = SkillId,
                GridX = GridX,
                TargetX = TargetX,
                GridY = GridY,
                TargetY = TargetY
            };
        }
    }
}
