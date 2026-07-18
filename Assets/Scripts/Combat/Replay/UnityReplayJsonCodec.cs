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
            public int Version = 1;
            public int Seed;
            public SpawnDto[] InitialLayout = Array.Empty<SpawnDto>();
            public TickDto[] Records = Array.Empty<TickDto>();

            public static ReplayDto From(ReplayFile replay)
            {
                var dto = new ReplayDto
                {
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
                    Version = Version,
                    Seed = Seed,
                    InitialLayout = new SimEntitySpawnInfo[InitialLayout?.Length ?? 0],
                    Records = new List<TickRecord>(Records?.Length ?? 0)
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

            public static TickDto From(TickRecord record)
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

            public TickRecord ToTickRecord()
            {
                var record = new TickRecord
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
