using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    internal sealed class SessionSnapshotData
    {
        public SessionLifecycle Lifecycle;
        public SimCombatPhase Phase;
        public int CompletedTick;
        public int NextTick;
        public int AttackSystemTick;
        public int AttackerAlive;
        public int DefenderAlive;
        public int AttackerDead;
        public int DefenderDead;
        public int? Winner;
        public int DeployedCount;
        public SimTerminalOutcome TerminalOutcome;
        public int TerminalFinalTick;
        public Dictionary<int, int> LastSequenceByPlayer;
        public ISimCommand[] PendingCommands;
        public TickSnapshot WorldSnapshot;
    }

    internal static class SessionSnapshotCodec
    {
        private static readonly byte[] Magic = { 0x5A, 0x53, 0x4E, 0x50 };
        private const int Version = 2;
        private const int MaxGenerations = 1_000_000;
        private const int MaxChunks = 100_000;
        private const int MaxComponentsPerChunk = 16;
        private const int MaxPathLength = 1_000_000;
        private const int MaxCommands = 100_000;
        private const int MaxPlayers = 4096;
        private const int MaxEvents = 100_000;

        public static byte[] Encode(SessionSnapshotData data)
        {
            if (data == null || data.WorldSnapshot == null)
                throw new ArgumentNullException(nameof(data));
            var writer = new DeterministicBinaryWriter();
            writer.WriteBytes(Magic);
            writer.WriteInt32(Version);
            writer.WriteInt32((int)data.Lifecycle);
            writer.WriteInt32((int)data.Phase);
            writer.WriteInt32(data.CompletedTick);
            writer.WriteInt32(data.NextTick);
            writer.WriteInt32(data.AttackSystemTick);
            writer.WriteInt32(data.AttackerAlive);
            writer.WriteInt32(data.DefenderAlive);
            writer.WriteInt32(data.AttackerDead);
            writer.WriteInt32(data.DefenderDead);
            writer.WriteByte(data.Winner.HasValue ? (byte)1 : (byte)0);
            if (data.Winner.HasValue) writer.WriteInt32(data.Winner.Value);
            writer.WriteInt32(data.DeployedCount);
            writer.WriteByte(data.TerminalOutcome != null ? (byte)1 : (byte)0);
            if (data.TerminalOutcome != null)
            {
                writer.WriteInt32(data.TerminalOutcome.WinnerTeamId);
                writer.WriteInt32(data.TerminalOutcome.Stars);
                writer.WriteInt32(data.TerminalOutcome.LootGold);
                writer.WriteInt32(data.TerminalOutcome.LootElixir);
                writer.WriteInt32(data.TerminalFinalTick);
            }

            var players = new List<KeyValuePair<int, int>>(data.LastSequenceByPlayer ??
                new Dictionary<int, int>());
            players.Sort((left, right) => left.Key.CompareTo(right.Key));
            writer.WriteInt32(players.Count);
            foreach (var player in players)
            {
                writer.WriteInt32(player.Key);
                writer.WriteInt32(player.Value);
            }

            ISimCommand[] commands = data.PendingCommands ?? Array.Empty<ISimCommand>();
            writer.WriteInt32(commands.Length);
            foreach (ISimCommand command in commands) WriteCommand(writer, command);

            WriteWorld(writer, data.WorldSnapshot);
            return writer.ToArray();
        }

        public static SessionSnapshotData Decode(byte[] payload)
        {
            var reader = new DeterministicBinaryReader(payload ?? throw new ArgumentNullException(nameof(payload)));
            foreach (byte expected in Magic)
                if (reader.ReadByte() != expected) throw new FormatException("Invalid snapshot magic.");
            if (reader.ReadInt32() != Version) throw new FormatException("Unsupported snapshot payload version.");

            int lifecycleValue = reader.ReadInt32();
            int phaseValue = reader.ReadInt32();
            if (!Enum.IsDefined(typeof(SessionLifecycle), lifecycleValue) ||
                !Enum.IsDefined(typeof(SimCombatPhase), phaseValue))
                throw new FormatException("Invalid snapshot lifecycle state.");
            var data = new SessionSnapshotData
            {
                Lifecycle = (SessionLifecycle)lifecycleValue,
                Phase = (SimCombatPhase)phaseValue,
                CompletedTick = reader.ReadInt32(),
                NextTick = reader.ReadInt32(),
                AttackSystemTick = ReadNonNegative(reader, "attack tick"),
                AttackerAlive = ReadNonNegative(reader, "attacker alive"),
                DefenderAlive = ReadNonNegative(reader, "defender alive"),
                AttackerDead = ReadNonNegative(reader, "attacker dead"),
                DefenderDead = ReadNonNegative(reader, "defender dead"),
            };
            byte hasWinner = reader.ReadByte();
            if (hasWinner > 1) throw new FormatException("Invalid winner marker.");
            data.Winner = hasWinner == 1 ? reader.ReadInt32() : (int?)null;
            data.DeployedCount = ReadNonNegative(reader, "deployed unit");
            byte hasTerminalOutcome = reader.ReadByte();
            if (hasTerminalOutcome > 1) throw new FormatException("Invalid terminal outcome marker.");
            if (hasTerminalOutcome == 1)
            {
                try
                {
                    data.TerminalOutcome = new SimTerminalOutcome(
                        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
                    data.TerminalFinalTick = ReadNonNegative(reader, "terminal final tick");
                }
                catch (ArgumentException exception)
                {
                    throw new FormatException("Invalid terminal outcome.", exception);
                }
            }
            else
            {
                data.TerminalFinalTick = -1;
            }

            int playerCount = ReadCount(reader, MaxPlayers, "player sequence");
            data.LastSequenceByPlayer = new Dictionary<int, int>(playerCount);
            for (int index = 0; index < playerCount; index++)
            {
                int playerId = reader.ReadInt32();
                int sequence = reader.ReadInt32();
                if (!data.LastSequenceByPlayer.TryAdd(playerId, sequence))
                    throw new FormatException("Duplicate player sequence state.");
            }

            int commandCount = ReadCount(reader, MaxCommands, "pending command");
            data.PendingCommands = new ISimCommand[commandCount];
            for (int index = 0; index < commandCount; index++)
                data.PendingCommands[index] = ReadCommand(reader);

            data.WorldSnapshot = ReadWorld(reader);
            if (!reader.AtEnd) throw new FormatException("Trailing snapshot payload bytes.");
            return data;
        }

        private static void WriteWorld(DeterministicBinaryWriter writer, TickSnapshot world)
        {
            writer.WriteInt32(world.NextId);
            int[] generations = world.Generations ?? Array.Empty<int>();
            writer.WriteInt32(generations.Length);
            foreach (int generation in generations) writer.WriteInt32(generation);
            writer.WriteInt32(world.Tick);
            writer.WriteUInt64(world.Checksum);

            List<ChunkSnapshot> chunks = world.ChunkSnapshots ?? new List<ChunkSnapshot>();
            writer.WriteInt32(chunks.Count);
            foreach (ChunkSnapshot chunk in chunks)
            {
                writer.WriteInt32(chunk.ComponentTypeIds.Length);
                foreach (int typeId in chunk.ComponentTypeIds) writer.WriteInt32(typeId);
                writer.WriteInt32(chunk.Count);
                for (int index = 0; index < chunk.Count; index++)
                {
                    writer.WriteInt32(chunk.EntityIds[index]);
                    writer.WriteInt32(chunk.Generations[index]);
                }
                for (int column = 0; column < chunk.ComponentTypeIds.Length; column++)
                    WriteColumn(writer, chunk.ComponentTypeIds[column], chunk.Columns[column], chunk.Count);
            }

            List<SimDamageEvent> events = world.PendingDamageEvents ?? new List<SimDamageEvent>();
            writer.WriteInt32(events.Count);
            foreach (SimDamageEvent damage in events)
            {
                writer.WriteInt32(damage.Amount);
                WriteEntity(writer, damage.Attacker);
                WriteEntity(writer, damage.Target);
            }
        }

        private static TickSnapshot ReadWorld(DeterministicBinaryReader reader)
        {
            int nextId = ReadNonNegative(reader, "next entity ID");
            int generationCount = ReadCount(reader, MaxGenerations, "generation");
            if (nextId > generationCount) throw new FormatException("Allocator exceeds generation table.");
            if (generationCount > reader.RemainingBytes / sizeof(int))
                throw new FormatException("Generation count exceeds the snapshot payload.");
            var generations = new int[generationCount];
            for (int index = 0; index < generationCount; index++)
                generations[index] = reader.ReadInt32();

            var snapshot = new TickSnapshot
            {
                NextId = nextId,
                Generations = generations,
                Tick = reader.ReadInt32(),
                Checksum = reader.ReadUInt64(),
                ChunkSnapshots = new List<ChunkSnapshot>(),
                PendingDamageEvents = new List<SimDamageEvent>(),
            };
            int chunkCount = ReadCount(reader, MaxChunks, "chunk");
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                int componentCount = ReadCount(reader, MaxComponentsPerChunk, "component type");
                if (componentCount == 0) throw new FormatException("Chunk has no component types.");
                var typeIds = new int[componentCount];
                int previousType = 0;
                for (int index = 0; index < componentCount; index++)
                {
                    int typeId = reader.ReadInt32();
                    if (typeId <= previousType || SimChunkHelper.CreateColumn(typeId, 0) == null)
                        throw new FormatException("Invalid component schema in snapshot.");
                    previousType = typeId;
                    typeIds[index] = typeId;
                }
                int count = ReadCount(reader, SimChunk.Capacity, "chunk entity");
                var chunk = new ChunkSnapshot
                {
                    ComponentTypeIds = typeIds,
                    EntityIds = new int[count],
                    Generations = new int[count],
                    Count = count,
                    Columns = new Array[componentCount],
                };
                for (int index = 0; index < count; index++)
                {
                    chunk.EntityIds[index] = reader.ReadInt32();
                    chunk.Generations[index] = reader.ReadInt32();
                }
                for (int column = 0; column < componentCount; column++)
                    chunk.Columns[column] = ReadColumn(reader, typeIds[column], count);
                snapshot.ChunkSnapshots.Add(chunk);
            }

            int eventCount = ReadCount(reader, MaxEvents, "damage event");
            for (int index = 0; index < eventCount; index++)
            {
                snapshot.PendingDamageEvents.Add(new SimDamageEvent
                {
                    Amount = reader.ReadInt32(),
                    Attacker = ReadEntity(reader),
                    Target = ReadEntity(reader),
                });
            }
            return snapshot;
        }

        private static void WriteColumn(DeterministicBinaryWriter writer, int typeId, Array column, int count)
        {
            if (column == null || column.Length < count) throw new ArgumentException("Invalid snapshot column.");
            for (int index = 0; index < count; index++)
            {
                switch (typeId)
                {
                    case SimComponentTypeId.Transform:
                        SimTransformComponent transform = ((SimTransformComponent[])column)[index];
                        writer.WriteInt64(transform.PosX.RawValue); writer.WriteInt64(transform.PosY.RawValue);
                        writer.WriteInt32(transform.GridX); writer.WriteInt32(transform.GridY);
                        break;
                    case SimComponentTypeId.Health:
                        SimHealthComponent health = ((SimHealthComponent[])column)[index];
                        writer.WriteInt32(health.MaxHP); writer.WriteInt32(health.CurrentHP);
                        break;
                    case SimComponentTypeId.Team:
                        writer.WriteInt32(((SimTeamComponent[])column)[index].TeamId);
                        break;
                    case SimComponentTypeId.Movement:
                        SimMovementComponent movement = ((SimMovementComponent[])column)[index];
                        writer.WriteInt64(movement.Speed.RawValue); writer.WriteInt64(movement.CellSize.RawValue);
                        writer.WriteInt32(movement.PathIndex); writer.WriteInt32(movement.TargetGridX);
                        writer.WriteInt32(movement.TargetGridY);
                        writer.WriteInt32(movement.Path?.Count ?? -1);
                        if (movement.Path != null)
                            foreach (int waypoint in movement.Path) writer.WriteInt32(waypoint);
                        break;
                    case SimComponentTypeId.Attack:
                        SimAttackComponent attack = ((SimAttackComponent[])column)[index];
                        writer.WriteInt32(attack.Damage); writer.WriteInt64(attack.Range.RawValue);
                        writer.WriteInt32(attack.CooldownTicks); writer.WriteInt32(attack.LastAttackTick);
                        break;
                    case SimComponentTypeId.Target:
                        SimTargetComponent target = ((SimTargetComponent[])column)[index];
                        WriteEntity(writer, target.TargetEntity);
                        writer.WriteInt64(target.TargetPosX.RawValue); writer.WriteInt64(target.TargetPosY.RawValue);
                        break;
                    case SimComponentTypeId.Identity:
                        SimIdentityComponent identity = ((SimIdentityComponent[])column)[index];
                        writer.WriteInt32(identity.ArchetypeId); writer.WriteInt32(identity.OwnerPlayerId);
                        break;
                    default:
                        throw new FormatException("Unsupported snapshot component type.");
                }
            }
        }

        private static Array ReadColumn(DeterministicBinaryReader reader, int typeId, int count)
        {
            Array column = SimChunkHelper.CreateColumn(typeId, count);
            for (int index = 0; index < count; index++)
            {
                switch (typeId)
                {
                    case SimComponentTypeId.Transform:
                        ((SimTransformComponent[])column)[index] = new SimTransformComponent
                        {
                            PosX = LogicInt.FromRaw(reader.ReadInt64()), PosY = LogicInt.FromRaw(reader.ReadInt64()),
                            GridX = reader.ReadInt32(), GridY = reader.ReadInt32(),
                        };
                        break;
                    case SimComponentTypeId.Health:
                        ((SimHealthComponent[])column)[index] = new SimHealthComponent
                        {
                            MaxHP = reader.ReadInt32(), CurrentHP = reader.ReadInt32(),
                        };
                        break;
                    case SimComponentTypeId.Team:
                        ((SimTeamComponent[])column)[index] = SimTeamComponent.Create(reader.ReadInt32());
                        break;
                    case SimComponentTypeId.Movement:
                        long speed = reader.ReadInt64(); long cellSize = reader.ReadInt64();
                        int pathIndex = reader.ReadInt32(); int targetX = reader.ReadInt32();
                        int targetY = reader.ReadInt32(); int pathLength = reader.ReadInt32();
                        if (pathLength < -1 || pathLength > MaxPathLength)
                            throw new FormatException("Invalid movement path length.");
                        List<int> path = null;
                        if (pathLength >= 0)
                        {
                            path = new List<int>(pathLength);
                            for (int waypoint = 0; waypoint < pathLength; waypoint++) path.Add(reader.ReadInt32());
                        }
                        if (pathIndex < 0 || (path != null && pathIndex > path.Count))
                            throw new FormatException("Invalid movement path index.");
                        ((SimMovementComponent[])column)[index] = new SimMovementComponent
                        {
                            Speed = LogicInt.FromRaw(speed), CellSize = LogicInt.FromRaw(cellSize),
                            Path = path, PathIndex = pathIndex, TargetGridX = targetX, TargetGridY = targetY,
                        };
                        break;
                    case SimComponentTypeId.Attack:
                        ((SimAttackComponent[])column)[index] = new SimAttackComponent
                        {
                            Damage = reader.ReadInt32(), Range = LogicInt.FromRaw(reader.ReadInt64()),
                            CooldownTicks = reader.ReadInt32(), LastAttackTick = reader.ReadInt32(),
                        };
                        break;
                    case SimComponentTypeId.Target:
                        ((SimTargetComponent[])column)[index] = new SimTargetComponent
                        {
                            TargetEntity = ReadEntity(reader),
                            TargetPosX = LogicInt.FromRaw(reader.ReadInt64()),
                            TargetPosY = LogicInt.FromRaw(reader.ReadInt64()),
                        };
                        break;
                    case SimComponentTypeId.Identity:
                        ((SimIdentityComponent[])column)[index] = new SimIdentityComponent
                        {
                            ArchetypeId = reader.ReadInt32(), OwnerPlayerId = reader.ReadInt32(),
                        };
                        break;
                }
            }
            return column;
        }

        private static void WriteCommand(DeterministicBinaryWriter writer, ISimCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            writer.WriteByte((byte)command.Type);
            writer.WriteInt32(command.Tick); writer.WriteInt32(command.PlayerId); writer.WriteInt32(command.Sequence);
            switch (command)
            {
                case DeployCmd deploy:
                    writer.WriteInt32(deploy.UnitTypeId); writer.WriteInt32(deploy.GridX); writer.WriteInt32(deploy.GridY);
                    break;
                case MoveCmd move:
                    writer.WriteInt32(move.EntityId); writer.WriteInt32(move.TargetX); writer.WriteInt32(move.TargetY);
                    break;
                case SkillCmd skill:
                    writer.WriteInt32(skill.CasterId); writer.WriteInt32(skill.SkillId);
                    writer.WriteInt32(skill.TargetX); writer.WriteInt32(skill.TargetY);
                    break;
                case EndDeploymentCmd:
                    break;
                default:
                    throw new FormatException("Unsupported snapshot command type.");
            }
        }

        private static ISimCommand ReadCommand(DeterministicBinaryReader reader)
        {
            SimCommandType type = (SimCommandType)reader.ReadByte();
            int tick = reader.ReadInt32(); int playerId = reader.ReadInt32(); int sequence = reader.ReadInt32();
            switch (type)
            {
                case SimCommandType.Deploy:
                    return new DeployCmd { Tick = tick, PlayerId = playerId, Sequence = sequence,
                        UnitTypeId = reader.ReadInt32(), GridX = reader.ReadInt32(), GridY = reader.ReadInt32() };
                case SimCommandType.Move:
                    return new MoveCmd { Tick = tick, PlayerId = playerId, Sequence = sequence,
                        EntityId = reader.ReadInt32(), TargetX = reader.ReadInt32(), TargetY = reader.ReadInt32() };
                case SimCommandType.Skill:
                    return new SkillCmd { Tick = tick, PlayerId = playerId, Sequence = sequence,
                        CasterId = reader.ReadInt32(), SkillId = reader.ReadInt32(),
                        TargetX = reader.ReadInt32(), TargetY = reader.ReadInt32() };
                case SimCommandType.EndDeployment:
                    return new EndDeploymentCmd { Tick = tick, PlayerId = playerId, Sequence = sequence };
                default:
                    throw new FormatException("Unknown snapshot command type.");
            }
        }

        private static void WriteEntity(DeterministicBinaryWriter writer, SimEntity entity)
        {
            writer.WriteInt32(entity.Id); writer.WriteInt32(entity.Generation);
        }

        private static SimEntity ReadEntity(DeterministicBinaryReader reader)
        {
            int id = reader.ReadInt32(); int generation = reader.ReadInt32();
            return id < 0 ? SimEntity.Invalid : new SimEntity(id, generation, 0);
        }

        private static int ReadCount(DeterministicBinaryReader reader, int maximum, string label)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new FormatException($"Invalid {label} count.");
            return value;
        }

        private static int ReadNonNegative(DeterministicBinaryReader reader, string label)
        {
            int value = reader.ReadInt32();
            if (value < 0) throw new FormatException($"Invalid {label}.");
            return value;
        }
    }
}
