using System;
using System.Collections.Generic;
using System.Text;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 命令录制器 — 记录每 Tick 的命令和校验和，战斗结束后导出 ReplayFile。
    /// </summary>
    public class CommandRecorder
    {
        private readonly ReplayFile _replay = new ReplayFile();
        private readonly CommandBuffer _buffer;

        public CommandRecorder(int seed, IEnumerable<SimEntitySpawnInfo> initialLayout, CommandBuffer buffer)
        {
            _replay.Seed = seed;
            _replay.InitialLayout = new List<SimEntitySpawnInfo>(initialLayout).ToArray();
            _buffer = buffer;
        }

        /// <summary>录制一个 Tick 的数据</summary>
        public void Record(int tick, ulong checksum)
        {
            var cmds = _buffer.Peek(tick);
            var record = new TickRecord
            {
                Tick = tick,
                Checksum = checksum,
                Commands = SerializeCommands(cmds),
            };
            _replay.Records.Add(record);
        }

        /// <summary>导出 ReplayFile（战斗结束后调用）</summary>
        public ReplayFile Export()
        {
            return _replay;
        }

        /// <summary>序列化命令列表为字节数组</summary>
        private static byte[][] SerializeCommands(List<ISimCommand> cmds)
        {
            var result = new byte[cmds.Count][];
            for (int i = 0; i < cmds.Count; i++)
            {
                result[i] = SerializeCommand(cmds[i]);
            }
            return result;
        }

        /// <summary>序列化单条命令为字节数组（简单二进制格式）</summary>
        private static byte[] SerializeCommand(ISimCommand cmd)
        {
            var sb = new StringBuilder();
            sb.Append((byte)cmd.Type);
            sb.Append('|');
            sb.Append(cmd.Tick);
            sb.Append('|');
            sb.Append(cmd.PlayerId);

            switch (cmd)
            {
                case DeployCmd d:
                    sb.Append('|');
                    sb.Append(d.UnitTypeId);
                    sb.Append('|');
                    sb.Append(d.GridX);
                    sb.Append('|');
                    sb.Append(d.GridY);
                    break;
                case MoveCmd m:
                    sb.Append('|');
                    sb.Append(m.EntityId);
                    sb.Append('|');
                    sb.Append(m.TargetX);
                    sb.Append('|');
                    sb.Append(m.TargetY);
                    break;
                case SkillCmd s:
                    sb.Append('|');
                    sb.Append(s.CasterId);
                    sb.Append('|');
                    sb.Append(s.SkillId);
                    sb.Append('|');
                    sb.Append(s.TargetX);
                    sb.Append('|');
                    sb.Append(s.TargetY);
                    break;
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        /// <summary>反序列化命令</summary>
        public static ISimCommand DeserializeCommand(byte[] data)
        {
            var str = Encoding.UTF8.GetString(data);
            var parts = str.Split('|');
            var type = (SimCommandType)byte.Parse(parts[0]);
            int tick = int.Parse(parts[1]);
            int playerId = int.Parse(parts[2]);

            switch (type)
            {
                case SimCommandType.Deploy:
                    return new DeployCmd
                    {
                        Tick = tick,
                        PlayerId = playerId,
                        UnitTypeId = int.Parse(parts[3]),
                        GridX = int.Parse(parts[4]),
                        GridY = int.Parse(parts[5]),
                    };
                case SimCommandType.Move:
                    return new MoveCmd
                    {
                        Tick = tick,
                        PlayerId = playerId,
                        EntityId = int.Parse(parts[3]),
                        TargetX = int.Parse(parts[4]),
                        TargetY = int.Parse(parts[5]),
                    };
                case SimCommandType.Skill:
                    return new SkillCmd
                    {
                        Tick = tick,
                        PlayerId = playerId,
                        CasterId = int.Parse(parts[3]),
                        SkillId = int.Parse(parts[4]),
                        TargetX = int.Parse(parts[5]),
                        TargetY = int.Parse(parts[6]),
                    };
            }

            return null;
        }

        /// <summary>获取录制数据（用于测试）</summary>
        public ReplayFile GetReplay() => _replay;
    }
}
