using System;

namespace ZWebGLFrameWork.Network.Contracts.Protocol
{
    // TODO(PRODUCTION-CONTENT): Keep these framework IDs until project-specific commands are approved.
    public enum FrameSyncCommandType : uint
    {
        Deploy = 1,
        Move = 2,
        Skill = 3,
        EndDeployment = 4
    }

    public sealed class FrameSyncCommandPayload
    {
        public FrameSyncCommandType Type { get; }
        public int First { get; }
        public int Second { get; }
        public int Third { get; }
        public int Fourth { get; }

        private FrameSyncCommandPayload(FrameSyncCommandType type,
            int first, int second, int third, int fourth)
        {
            Type = type;
            First = first;
            Second = second;
            Third = third;
            Fourth = fourth;
        }

        public static bool TryDecode(uint messageId, byte[] payload,
            out FrameSyncCommandPayload command)
        {
            command = null;
            if (!Enum.IsDefined(typeof(FrameSyncCommandType), messageId)) return false;
            var type = (FrameSyncCommandType)messageId;
            int length = payload?.Length ?? -1;
            int expectedLength = type switch
            {
                FrameSyncCommandType.Deploy => 12,
                FrameSyncCommandType.Move => 12,
                FrameSyncCommandType.Skill => 16,
                FrameSyncCommandType.EndDeployment => 0,
                _ => -1
            };
            if (length != expectedLength) return false;

            command = type switch
            {
                FrameSyncCommandType.Deploy => new FrameSyncCommandPayload(type,
                    ReadInt32(payload, 0), ReadInt32(payload, 4),
                    ReadInt32(payload, 8), 0),
                FrameSyncCommandType.Move => new FrameSyncCommandPayload(type,
                    ReadInt32(payload, 0), ReadInt32(payload, 4),
                    ReadInt32(payload, 8), 0),
                FrameSyncCommandType.Skill => new FrameSyncCommandPayload(type,
                    ReadInt32(payload, 0), ReadInt32(payload, 4),
                    ReadInt32(payload, 8), ReadInt32(payload, 12)),
                FrameSyncCommandType.EndDeployment => new FrameSyncCommandPayload(
                    type, 0, 0, 0, 0),
                _ => null
            };
            return command != null;
        }

        private static int ReadInt32(byte[] payload, int offset) =>
            payload[offset] |
            (payload[offset + 1] << 8) |
            (payload[offset + 2] << 16) |
            (payload[offset + 3] << 24);
    }
}
