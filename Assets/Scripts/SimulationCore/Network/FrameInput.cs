using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>
    /// 一帧的玩家输入 — 紧凑位域，便于网络传输。
    /// 最多 32 bits，小到可以在每个 UDP 包中冗余携带。
    /// </summary>
    [Serializable]
    public struct FrameInput
    {
        /// <summary>序列号（= Tick 号）</summary>
        public uint Sequence;
        /// <summary>输入位域
        /// Bit 0-7:   输入类型 (0=无操作, 1=部署, 2=移动, 3=技能)
        /// Bit 8-15:  参数1 (UnitTypeId / EntityId low / SkillId)
        /// Bit 16-23: 参数2 (GridX / TargetX)
        /// Bit 24-31: 参数3 (GridY / TargetY)
        /// </summary>
        public uint Data;

        public bool HasInput => Data != 0;

        public static FrameInput Empty(uint sequence) => new FrameInput { Sequence = sequence, Data = 0 };

        /// <summary>创建部署输入</summary>
        public static FrameInput Deploy(uint sequence, int unitTypeId, int gridX, int gridY)
        {
            unitTypeId = Math.Clamp(unitTypeId, 0, 255);
            gridX = Math.Clamp(gridX, 0, 255);
            gridY = Math.Clamp(gridY, 0, 255);
            return new FrameInput
            {
                Sequence = sequence,
                Data = 1u | ((uint)unitTypeId << 8) | ((uint)gridX << 16) | ((uint)gridY << 24),
            };
        }

        /// <summary>解析为 SimCommand（由 SimulationCore 侧实现）</summary>
        public ISimCommand ToCommand(int playerId)
        {
            uint type = Data & 0xFF;
            int p1 = (int)((Data >> 8) & 0xFF);
            int p2 = (int)((Data >> 16) & 0xFF);
            int p3 = (int)((Data >> 24) & 0xFF);

            switch (type)
            {
                case 1: // Deploy
                    return new DeployCmd { Tick = (int)Sequence, PlayerId = playerId, Sequence = (int)Sequence, UnitTypeId = p1, GridX = p2, GridY = p3 };
                case 2: // Move
                    return new MoveCmd { Tick = (int)Sequence, PlayerId = playerId, Sequence = (int)Sequence, EntityId = p1, TargetX = p2, TargetY = p3 };
                case 3: // Skill
                    return new SkillCmd { Tick = (int)Sequence, PlayerId = playerId, Sequence = (int)Sequence, CasterId = 0, SkillId = p1, TargetX = p2, TargetY = p3 };
                default:
                    return null;
            }
        }

        /// <summary>序列化为 8 字节（显式小端序，跨平台一致）</summary>
        public byte[] ToBytes()
        {
            byte[] bytes = new byte[8];
            // Little-endian: Sequence first
            bytes[0] = (byte)(Sequence & 0xFF);
            bytes[1] = (byte)((Sequence >> 8) & 0xFF);
            bytes[2] = (byte)((Sequence >> 16) & 0xFF);
            bytes[3] = (byte)((Sequence >> 24) & 0xFF);
            bytes[4] = (byte)(Data & 0xFF);
            bytes[5] = (byte)((Data >> 8) & 0xFF);
            bytes[6] = (byte)((Data >> 16) & 0xFF);
            bytes[7] = (byte)((Data >> 24) & 0xFF);
            return bytes;
        }

        /// <summary>从 8 字节反序列化（显式小端序，跨平台一致）</summary>
        public static FrameInput FromBytes(byte[] bytes, int offset = 0)
        {
            return new FrameInput
            {
                Sequence = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)),
                Data = (uint)(bytes[offset + 4] | (bytes[offset + 5] << 8) | (bytes[offset + 6] << 16) | (bytes[offset + 7] << 24)),
            };
        }
    }
}
