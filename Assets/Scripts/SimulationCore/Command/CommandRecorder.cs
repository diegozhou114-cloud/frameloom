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

        /// <summary>导出 ReplayFile（战斗结束后调用）</summary>
        public ReplayFile Export() => _replay;

        /// <summary>序列化命令列表为 SerializableCommand 列表</summary>
        private static List<SerializableCommand> SerializeCommands(List<ISimCommand> cmds)
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
            var type = System.Enum.Parse<SimCommandType>(sc.Type);
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
