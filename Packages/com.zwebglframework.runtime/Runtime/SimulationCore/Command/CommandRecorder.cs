using System.Collections.Generic;
using System.Linq;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 命令录制器 — 记录每 Tick 的命令和校验和，战斗结束后导出 ReplayFile。
    /// </summary>
    public class CommandRecorder
    {
        private readonly ReplayFile _replay = new ReplayFile
        {
            FormatVersion = ReplayFormat.CurrentVersion,
            TickRate = ReplayFormat.TickRate
        };
        private readonly CommandBuffer _buffer;

        public CommandRecorder(int seed, IEnumerable<SimEntitySpawnInfo> initialLayout, CommandBuffer buffer)
        {
            _replay.Seed = seed;
            _replay.InitialLayout = initialLayout.ToArray();
            _buffer = buffer;
        }

        public CommandRecorder(BattleInit init)
        {
            if (init == null) throw new System.ArgumentNullException(nameof(init));
            _replay.BattleInit = ReplayBattleInit.From(init);
            _replay.MatchId = init.MatchId;
            _replay.TickRate = init.TickRate;
            _replay.Seed = init.Seed;
            _replay.InitialLayout = init.InitialEntities.Select(entity =>
                new SimEntitySpawnInfo(entity.ArchetypeId, entity.GridX,
                    entity.GridY, entity.TeamId)).ToArray();
        }

        public CommandRecorder(BattleInit init,
            ReplayAuthorityMetadata authority) : this(init)
        {
            if (authority == null)
                throw new System.ArgumentNullException(nameof(authority));
            _replay.SimulationSchemaHash = authority.SimulationSchemaHash;
            _replay.ContentConfigHash = authority.ContentConfigHash;
            _replay.InitialStateHash = authority.InitialStateHash;
            _replay.SubmissionNonce = authority.SubmissionNonce;
        }

        /// <summary>录制一个 Tick 的数据</summary>
        public void Record(int tick, ulong checksum)
        {
            Record(tick, checksum, _buffer.Peek(tick));
        }

        /// <summary>记录本 Tick 实际从缓冲区 Drain 并执行的命令。</summary>
        public void Record(int tick, ulong checksum, List<ISimCommand> executedCommands)
        {
            var record = new ReplayTickRecord
            {
                Tick = tick,
                Checksum = checksum,
                Commands = SerializeCommands(executedCommands),
            };
            _replay.Records.Add(record);
        }

        public void Record(TickResult result)
        {
            if (result == null)
                throw new System.ArgumentNullException(nameof(result));
            var commands = new List<ISimCommand>(result.ExecutedCommands.Count);
            foreach (ISimCommand command in result.ExecutedCommands)
                commands.Add(command);
            Record(result.CompletedTick, result.Checksum, commands);
            _replay.FinalTick = result.CompletedTick;
            _replay.FinalChecksum = result.Checksum;
            if (result.Result != null) _replay.FinalResult = result.Result;
        }

        /// <summary>Records a published caller-owned tick output.</summary>
        /// <remarks>
        /// Borrowed commands are synchronously copied into Replay-owned serializable values before
        /// return, so the caller may then clear, reuse, or dispose the parent output. Outputs that
        /// omit executed commands or do not contain a published tick are rejected.
        /// </remarks>
        /// <param name="output">The caller-owned output borrowed for this call.</param>
        /// <exception cref="System.ArgumentNullException"><paramref name="output"/> is null.</exception>
        /// <exception cref="System.ArgumentException"><paramref name="output"/> has invalid state.</exception>
        public void Record(SimTickOutputBuffer output)
        {
            if (output == null)
                throw new System.ArgumentNullException(nameof(output));
            if ((output.Fields & SimTickOutputFields.ExecutedCommands) == 0)
                throw new System.ArgumentException(
                    "Output must include executed commands.", nameof(output));
            if (output.Tick < 0)
                throw new System.ArgumentException(
                    "Output must contain a published tick.", nameof(output));

            var record = new ReplayTickRecord
            {
                Tick = output.Tick,
                Checksum = output.Checksum,
                Commands = SerializeCommands(output.ExecutedCommands),
            };
            _replay.Records.Add(record);
            _replay.FinalTick = output.Tick;
            _replay.FinalChecksum = output.Checksum;
            if (output.Result != null) _replay.FinalResult = output.Result;
        }

        /// <summary>导出 ReplayFile（战斗结束后调用）</summary>
        public ReplayFile Export() => _replay;

        /// <summary>序列化命令列表为 SerializableCommand 列表</summary>
        private static List<SerializableCommand> SerializeCommands(IReadOnlyList<ISimCommand> cmds)
        {
            var result = new List<SerializableCommand>(cmds.Count);
            foreach (var cmd in cmds)
            {
                var sc = new SerializableCommand
                {
                    Type = cmd.Type.ToString(),
                    Tick = cmd.Tick,
                    PlayerId = cmd.PlayerId,
                    Sequence = cmd.Sequence,
                };
                switch (cmd)
                {
                    case DeployCmd d:
                        sc.UnitTypeId = d.UnitTypeId;
                        sc.GridX = d.GridX;
                        sc.GridY = d.GridY;
                        break;
                    case MoveCmd m:
                        sc.EntityId = m.EntityId;
                        sc.TargetX = m.TargetX;
                        sc.TargetY = m.TargetY;
                        break;
                    case SkillCmd s:
                        sc.CasterId = s.CasterId;
                        sc.SkillId = s.SkillId;
                        sc.TargetX = s.TargetX;
                        sc.TargetY = s.TargetY;
                        break;
                }
                result.Add(sc);
            }
            return result;
        }

        /// <summary>从 SerializableCommand 还原为 ISimCommand</summary>
        public static ISimCommand DeserializeCommand(SerializableCommand sc)
        {
            if (sc == null || !System.Enum.TryParse(sc.Type, false, out SimCommandType type) ||
                !System.Enum.IsDefined(typeof(SimCommandType), type) ||
                !string.Equals(sc.Type, type.ToString(), System.StringComparison.Ordinal))
                return null;

            return type switch
            {
                SimCommandType.Deploy => new DeployCmd
                {
                    Tick = sc.Tick, PlayerId = sc.PlayerId, Sequence = sc.Sequence,
                    UnitTypeId = sc.UnitTypeId, GridX = sc.GridX, GridY = sc.GridY,
                },
                SimCommandType.Move => new MoveCmd
                {
                    Tick = sc.Tick, PlayerId = sc.PlayerId, Sequence = sc.Sequence,
                    EntityId = sc.EntityId, TargetX = sc.TargetX, TargetY = sc.TargetY,
                },
                SimCommandType.Skill => new SkillCmd
                {
                    Tick = sc.Tick, PlayerId = sc.PlayerId, Sequence = sc.Sequence,
                    CasterId = sc.CasterId, SkillId = sc.SkillId,
                    TargetX = sc.TargetX, TargetY = sc.TargetY,
                },
                SimCommandType.EndDeployment => new EndDeploymentCmd
                {
                    Tick = sc.Tick, PlayerId = sc.PlayerId, Sequence = sc.Sequence,
                },
                _ => null,
            };
        }

        public ReplayFile GetReplay() => _replay;
    }
}
