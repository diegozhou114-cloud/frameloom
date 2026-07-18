using System;
using System.Collections.Generic;
using System.Text;

namespace ZWebGLFrameWork.Network.Contracts.Protocol
{
    public static class RealtimeCodec
    {
        private const int MaxTextBytes = 1024;

        public static byte[] Encode(AuthorityFrame frame, uint requestId = 0)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            ValidateAuthority(frame);

            var writer = new WireWriter();
            writer.WriteString(frame.MatchId);
            writer.WriteInt32(frame.Tick);
            writer.WriteUInt32(frame.AckPlayer0);
            writer.WriteUInt32(frame.AckPlayer1);
            writer.WriteUInt64(frame.Checksum);
            writer.WriteUInt16((ushort)frame.OrderedInputs.Length);
            for (int index = 0; index < frame.OrderedInputs.Length; index++)
                WriteCommand(writer, frame.OrderedInputs[index], includeMatchId: false);
            writer.WriteByte(frame.ResyncSnapshot == null ? (byte)0 : (byte)1);
            if (frame.ResyncSnapshot != null) WriteSnapshot(writer, frame.ResyncSnapshot);
            return Wrap(RealtimeMessageKind.Authority, writer.ToArray(), requestId);
        }

        public static byte[] Encode(CommandEnvelope command, uint requestId = 0)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            ValidateCommand(command, requireMatchId: true);
            var writer = new WireWriter();
            WriteCommand(writer, command, includeMatchId: true);
            return Wrap(RealtimeMessageKind.Command, writer.ToArray(), requestId);
        }

        public static DecodeResult Decode(byte[] data)
        {
            try
            {
                if (!TryReadHeader(data, out WireHeader header, out ProtocolError error))
                    return Failure(error);

                var reader = new WireReader(data, RealtimeProtocol.HeaderBytes, (int)header.PayloadLength);
                DecodeResult result;
                switch (header.Kind)
                {
                    case RealtimeMessageKind.Command:
                        if (!TryReadCommand(ref reader, includeMatchId: true, out CommandEnvelope command))
                            return Failure(ProtocolError.InvalidPayload, header.Kind);
                        if (!IsValidCommand(command, requireMatchId: true))
                            return Failure(ProtocolError.InvalidPayload, header.Kind);
                        result = new DecodeResult
                        {
                            Error = ProtocolError.None,
                            Kind = header.Kind,
                            Command = command
                        };
                        break;
                    case RealtimeMessageKind.Authority:
                        result = DecodeAuthority(ref reader, header.Kind);
                        break;
                    case RealtimeMessageKind.ResyncSnapshot:
                        if (!TryReadSnapshot(ref reader, out ResyncSnapshotEnvelope snapshot))
                            return Failure(ProtocolError.InvalidPayload, header.Kind);
                        result = new DecodeResult
                        {
                            Error = ProtocolError.None,
                            Kind = header.Kind,
                            ResyncSnapshot = snapshot
                        };
                        break;
                    default:
                        return Failure(ProtocolError.InvalidPayload, header.Kind);
                }

                return reader.Remaining == 0
                    ? result
                    : Failure(ProtocolError.InvalidLength, header.Kind);
            }
            catch
            {
                return Failure(ProtocolError.InvalidPayload);
            }
        }

        private static DecodeResult DecodeAuthority(
            ref WireReader reader, RealtimeMessageKind kind)
        {
            if (!reader.TryReadString(out string matchId) ||
                !reader.TryReadInt32(out int tick) || tick < 0 ||
                !reader.TryReadUInt32(out uint ack0) ||
                !reader.TryReadUInt32(out uint ack1) ||
                !reader.TryReadUInt64(out ulong checksum) ||
                !reader.TryReadUInt16(out ushort count))
                return Failure(ProtocolError.InvalidPayload, kind);
            if (string.IsNullOrWhiteSpace(matchId) ||
                count > RealtimeProtocol.MaxCommandsPerAuthorityFrame)
                return Failure(ProtocolError.InvalidCount, kind);

            var commands = new CommandEnvelope[count];
            var seen = new HashSet<ulong>();
            CommandEnvelope previous = null;
            for (int index = 0; index < count; index++)
            {
                if (!TryReadCommand(ref reader, includeMatchId: false, out CommandEnvelope command) ||
                    !IsValidCommand(command, requireMatchId: false) || command.Tick != tick)
                    return Failure(ProtocolError.InvalidPayload, kind);
                command.MatchId = matchId;
                ulong key = ((ulong)(uint)command.PlayerId << 32) | command.Sequence;
                if (!seen.Add(key) || (previous != null && Compare(previous, command) >= 0))
                    return Failure(ProtocolError.UnorderedCommands, kind);
                commands[index] = command;
                previous = command;
            }

            if (!reader.TryReadByte(out byte hasSnapshot) || hasSnapshot > 1)
                return Failure(ProtocolError.InvalidPayload, kind);
            ResyncSnapshotEnvelope snapshot = null;
            if (hasSnapshot == 1 && !TryReadSnapshot(ref reader, out snapshot))
                return Failure(ProtocolError.InvalidPayload, kind);

            return new DecodeResult
            {
                Error = ProtocolError.None,
                Kind = kind,
                Authority = new AuthorityFrame
                {
                    MatchId = matchId,
                    Tick = tick,
                    AckPlayer0 = ack0,
                    AckPlayer1 = ack1,
                    Checksum = checksum,
                    OrderedInputs = commands,
                    ResyncSnapshot = snapshot
                }
            };
        }

        private static void ValidateAuthority(AuthorityFrame frame)
        {
            if (string.IsNullOrWhiteSpace(frame.MatchId))
                throw new ArgumentException("MatchId is required.", nameof(frame));
            if (frame.Tick < 0) throw new ArgumentOutOfRangeException(nameof(frame));
            CommandEnvelope[] commands = frame.OrderedInputs ?? Array.Empty<CommandEnvelope>();
            if (commands.Length > RealtimeProtocol.MaxCommandsPerAuthorityFrame)
                throw new ArgumentException("Authority frame command count exceeds the limit.", nameof(frame));

            var seen = new HashSet<ulong>();
            CommandEnvelope previous = null;
            for (int index = 0; index < commands.Length; index++)
            {
                CommandEnvelope command = commands[index];
                ValidateCommand(command, requireMatchId: false);
                if (command.Tick != frame.Tick)
                    throw new ArgumentException(
                        "Authority command tick must match the frame tick.", nameof(frame));
                ulong key = ((ulong)(uint)command.PlayerId << 32) | command.Sequence;
                if (!seen.Add(key) || (previous != null && Compare(previous, command) >= 0))
                    throw new ArgumentException("Authority commands must have a unique stable order.", nameof(frame));
                previous = command;
            }
            frame.OrderedInputs = commands;
        }

        private static int Compare(CommandEnvelope left, CommandEnvelope right)
        {
            int value = left.Tick.CompareTo(right.Tick);
            if (value != 0) return value;
            value = left.PlayerId.CompareTo(right.PlayerId);
            return value != 0 ? value : left.Sequence.CompareTo(right.Sequence);
        }

        private static void ValidateCommand(CommandEnvelope command, bool requireMatchId)
        {
            if (!IsValidCommand(command, requireMatchId))
                throw new ArgumentException("Command envelope is invalid.", nameof(command));
        }

        private static bool IsValidCommand(CommandEnvelope command, bool requireMatchId)
        {
            if (command == null || command.Version != RealtimeProtocol.Version ||
                command.Tick < 0 || (command.PlayerId != 0 && command.PlayerId != 1) ||
                command.Payload == null || command.Payload.Length > RealtimeProtocol.MaxMessageBytes)
                return false;
            if (requireMatchId && string.IsNullOrWhiteSpace(command.MatchId)) return false;
            return command.MessageId != 0 || command.Payload.Length == 0;
        }

        private static void WriteCommand(
            WireWriter writer, CommandEnvelope command, bool includeMatchId)
        {
            if (includeMatchId) writer.WriteString(command.MatchId);
            writer.WriteUInt16(command.Version);
            writer.WriteInt32(command.Tick);
            writer.WriteInt32(command.PlayerId);
            writer.WriteUInt32(command.Sequence);
            writer.WriteUInt32(command.MessageId);
            writer.WriteBytes(command.Payload);
        }

        private static bool TryReadCommand(
            ref WireReader reader, bool includeMatchId, out CommandEnvelope command)
        {
            command = null;
            string matchId = null;
            if (includeMatchId && !reader.TryReadString(out matchId)) return false;
            if (!reader.TryReadUInt16(out ushort version) ||
                !reader.TryReadInt32(out int tick) ||
                !reader.TryReadInt32(out int playerId) ||
                !reader.TryReadUInt32(out uint sequence) ||
                !reader.TryReadUInt32(out uint messageId) ||
                !reader.TryReadBytes(out byte[] payload))
                return false;
            command = new CommandEnvelope
            {
                Version = version,
                MatchId = matchId,
                Tick = tick,
                PlayerId = playerId,
                Sequence = sequence,
                MessageId = messageId,
                Payload = payload
            };
            return true;
        }

        private static void WriteSnapshot(WireWriter writer, ResyncSnapshotEnvelope snapshot)
        {
            writer.WriteString(snapshot.MatchId);
            writer.WriteString(snapshot.SessionId);
            writer.WriteInt32(snapshot.SchemaVersion);
            writer.WriteBytes(snapshot.ContentHash);
            writer.WriteInt32(snapshot.CompletedTick);
            writer.WriteInt32(snapshot.NextTick);
            writer.WriteInt64(snapshot.TimelineRevision);
            writer.WriteInt32(snapshot.UncompressedLength);
            writer.WriteUInt64(snapshot.StateChecksum);
            writer.WriteBytes(snapshot.Payload);
            writer.WriteBytes(snapshot.PayloadSha256);
        }

        private static bool TryReadSnapshot(
            ref WireReader reader, out ResyncSnapshotEnvelope snapshot)
        {
            snapshot = null;
            if (!reader.TryReadString(out string matchId) ||
                !reader.TryReadString(out string sessionId) ||
                !reader.TryReadInt32(out int schemaVersion) ||
                !reader.TryReadBytes(out byte[] contentHash) ||
                !reader.TryReadInt32(out int completedTick) ||
                !reader.TryReadInt32(out int nextTick) ||
                !reader.TryReadInt64(out long revision) ||
                !reader.TryReadInt32(out int uncompressedLength) ||
                !reader.TryReadUInt64(out ulong stateChecksum) ||
                !reader.TryReadBytes(out byte[] payload) ||
                !reader.TryReadBytes(out byte[] payloadSha256))
                return false;
            if (string.IsNullOrWhiteSpace(matchId) || string.IsNullOrWhiteSpace(sessionId) ||
                schemaVersion <= 0 || completedTick < -1 || nextTick != completedTick + 1 ||
                revision < 0 || uncompressedLength < 0 || payload.Length > uncompressedLength ||
                contentHash.Length != 32 || payloadSha256.Length != 32)
                return false;
            snapshot = new ResyncSnapshotEnvelope
            {
                MatchId = matchId,
                SessionId = sessionId,
                SchemaVersion = schemaVersion,
                ContentHash = contentHash,
                CompletedTick = completedTick,
                NextTick = nextTick,
                TimelineRevision = revision,
                UncompressedLength = uncompressedLength,
                StateChecksum = stateChecksum,
                Payload = payload,
                PayloadSha256 = payloadSha256
            };
            return true;
        }

        private static byte[] Wrap(
            RealtimeMessageKind kind, byte[] payload, uint requestId)
        {
            if (payload == null || payload.Length == 0 ||
                payload.Length > RealtimeProtocol.MaxMessageBytes)
                throw new ArgumentException("Realtime payload length is invalid.", nameof(payload));
            byte[] data = new byte[RealtimeProtocol.HeaderBytes + payload.Length];
            WriteUInt16(data, 0, RealtimeProtocol.Version);
            data[2] = (byte)kind;
            data[3] = 0;
            WriteUInt32(data, 4, (uint)payload.Length);
            WriteUInt32(data, 8, requestId);
            Buffer.BlockCopy(payload, 0, data, RealtimeProtocol.HeaderBytes, payload.Length);
            return data;
        }

        private static bool TryReadHeader(
            byte[] data, out WireHeader header, out ProtocolError error)
        {
            header = default(WireHeader);
            error = ProtocolError.None;
            if (data == null || data.Length < RealtimeProtocol.HeaderBytes)
            {
                error = ProtocolError.Truncated;
                return false;
            }

            ushort version = ReadUInt16(data, 0);
            byte kindValue = data[2];
            uint payloadLength = ReadUInt32(data, 4);
            uint requestId = ReadUInt32(data, 8);
            if (version != RealtimeProtocol.Version)
            {
                error = ProtocolError.UnsupportedVersion;
                return false;
            }
            if (!Enum.IsDefined(typeof(RealtimeMessageKind), kindValue))
            {
                error = ProtocolError.UnknownMessageKind;
                return false;
            }
            if (payloadLength == 0 || payloadLength > RealtimeProtocol.MaxMessageBytes ||
                payloadLength != data.Length - RealtimeProtocol.HeaderBytes)
            {
                error = ProtocolError.InvalidLength;
                return false;
            }

            header = new WireHeader(
                version, (RealtimeMessageKind)kindValue, data[3], payloadLength, requestId);
            return true;
        }

        private static DecodeResult Failure(
            ProtocolError error, RealtimeMessageKind kind = default(RealtimeMessageKind)) =>
            new DecodeResult { Error = error, Kind = kind };

        private static ushort ReadUInt16(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] | (data[offset + 1] << 8) |
                   (data[offset + 2] << 16) | (data[offset + 3] << 24));

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteUInt32(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
            data[offset + 2] = (byte)(value >> 16);
            data[offset + 3] = (byte)(value >> 24);
        }

        private readonly struct WireHeader
        {
            public ushort Version { get; }
            public RealtimeMessageKind Kind { get; }
            public byte Flags { get; }
            public uint PayloadLength { get; }
            public uint RequestId { get; }

            public WireHeader(
                ushort version, RealtimeMessageKind kind, byte flags,
                uint payloadLength, uint requestId)
            {
                Version = version;
                Kind = kind;
                Flags = flags;
                PayloadLength = payloadLength;
                RequestId = requestId;
            }
        }

        private sealed class WireWriter
        {
            private readonly List<byte> _bytes = new List<byte>();

            public void WriteByte(byte value) => _bytes.Add(value);

            public void WriteUInt16(ushort value)
            {
                _bytes.Add((byte)value);
                _bytes.Add((byte)(value >> 8));
            }

            public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

            public void WriteUInt32(uint value)
            {
                _bytes.Add((byte)value);
                _bytes.Add((byte)(value >> 8));
                _bytes.Add((byte)(value >> 16));
                _bytes.Add((byte)(value >> 24));
            }

            public void WriteInt64(long value) => WriteUInt64(unchecked((ulong)value));

            public void WriteUInt64(ulong value)
            {
                WriteUInt32((uint)value);
                WriteUInt32((uint)(value >> 32));
            }

            public void WriteString(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException("Text value is required.", nameof(value));
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                if (bytes.Length > MaxTextBytes)
                    throw new ArgumentException("Text value is too long.", nameof(value));
                WriteUInt16((ushort)bytes.Length);
                _bytes.AddRange(bytes);
            }

            public void WriteBytes(byte[] value)
            {
                value = value ?? Array.Empty<byte>();
                WriteUInt32((uint)value.Length);
                _bytes.AddRange(value);
            }

            public byte[] ToArray() => _bytes.ToArray();
        }

        private struct WireReader
        {
            private readonly byte[] _data;
            private readonly int _end;
            private int _offset;

            public int Remaining => _end - _offset;

            public WireReader(byte[] data, int offset, int length)
            {
                _data = data;
                _offset = offset;
                _end = offset + length;
            }

            public bool TryReadByte(out byte value)
            {
                value = 0;
                if (Remaining < 1) return false;
                value = _data[_offset++];
                return true;
            }

            public bool TryReadUInt16(out ushort value)
            {
                value = 0;
                if (Remaining < 2) return false;
                value = ReadUInt16(_data, _offset);
                _offset += 2;
                return true;
            }

            public bool TryReadInt32(out int value)
            {
                bool success = TryReadUInt32(out uint unsigned);
                value = unchecked((int)unsigned);
                return success;
            }

            public bool TryReadUInt32(out uint value)
            {
                value = 0;
                if (Remaining < 4) return false;
                value = ReadUInt32(_data, _offset);
                _offset += 4;
                return true;
            }

            public bool TryReadInt64(out long value)
            {
                bool success = TryReadUInt64(out ulong unsigned);
                value = unchecked((long)unsigned);
                return success;
            }

            public bool TryReadUInt64(out ulong value)
            {
                value = 0;
                if (!TryReadUInt32(out uint low) || !TryReadUInt32(out uint high))
                    return false;
                value = low | ((ulong)high << 32);
                return true;
            }

            public bool TryReadString(out string value)
            {
                value = null;
                if (!TryReadUInt16(out ushort length) || length == 0 ||
                    length > MaxTextBytes || Remaining < length)
                    return false;
                value = Encoding.UTF8.GetString(_data, _offset, length);
                _offset += length;
                return true;
            }

            public bool TryReadBytes(out byte[] value)
            {
                value = null;
                if (!TryReadUInt32(out uint length) ||
                    length > RealtimeProtocol.MaxMessageBytes || length > Remaining)
                    return false;
                value = new byte[length];
                if (length > 0) Buffer.BlockCopy(_data, _offset, value, 0, (int)length);
                _offset += (int)length;
                return true;
            }
        }
    }
}
