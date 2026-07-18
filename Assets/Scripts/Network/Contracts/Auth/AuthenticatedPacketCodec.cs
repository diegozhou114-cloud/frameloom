using System;
using System.Security.Cryptography;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Contracts.Auth
{
    public readonly struct AuthenticatedPacket
    {
        public Guid SessionId { get; }
        public ulong Sequence { get; }
        public byte[] Payload { get; }

        public AuthenticatedPacket(Guid sessionId, ulong sequence, byte[] payload)
        {
            SessionId = sessionId;
            Sequence = sequence;
            Payload = (byte[])(payload ?? throw new ArgumentNullException(nameof(payload))).Clone();
        }
    }

    public static class AuthenticatedPacketCodec
    {
        private const int HeaderBytes = 2 + 16 + 8 + 4;
        private const int TagBytes = 32;
        private const int KeyBytes = 32;

        public static byte[] Encode(
            Guid sessionId, ulong sequence, byte[] payload, byte[] key)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Length < 1 || payload.Length > RealtimeProtocol.MaxMessageBytes)
                throw new ArgumentOutOfRangeException(nameof(payload));
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length != KeyBytes)
                throw new ArgumentException("MAC key must be exactly 32 bytes.", nameof(key));

            byte[] packet = new byte[HeaderBytes + payload.Length + TagBytes];
            WriteUInt16(packet, 0, RealtimeProtocol.Version);
            byte[] sessionBytes = sessionId.ToByteArray();
            Buffer.BlockCopy(sessionBytes, 0, packet, 2, sessionBytes.Length);
            WriteUInt64(packet, 18, sequence);
            WriteUInt32(packet, 26, (uint)payload.Length);
            Buffer.BlockCopy(payload, 0, packet, HeaderBytes, payload.Length);

            int tagOffset = HeaderBytes + payload.Length;
            byte[] tag;
            using (var hmac = new HMACSHA256(key))
                tag = hmac.ComputeHash(packet, 0, tagOffset);
            Buffer.BlockCopy(tag, 0, packet, tagOffset, TagBytes);
            return packet;
        }

        public static ProtocolError TryDecode(
            byte[] packet,
            Guid expectedSession,
            byte[] key,
            SequenceReplayWindow replayWindow,
            out AuthenticatedPacket decoded)
        {
            decoded = default(AuthenticatedPacket);
            if (packet == null || packet.Length < HeaderBytes + TagBytes)
                return ProtocolError.Truncated;
            if (ReadUInt16(packet, 0) != RealtimeProtocol.Version)
                return ProtocolError.UnsupportedVersion;

            Guid session = ReadGuid(packet, 2);
            if (expectedSession == Guid.Empty || session != expectedSession)
                return ProtocolError.WrongSession;
            if (key == null || key.Length != KeyBytes)
                return ProtocolError.AuthenticationFailed;
            if (replayWindow == null) return ProtocolError.InvalidPayload;

            ulong sequence = ReadUInt64(packet, 18);
            uint payloadLengthValue = ReadUInt32(packet, 26);
            if (payloadLengthValue < 1 ||
                payloadLengthValue > RealtimeProtocol.MaxMessageBytes)
                return ProtocolError.InvalidLength;
            int payloadLength = (int)payloadLengthValue;
            int tagOffset = HeaderBytes + payloadLength;
            if (packet.Length != tagOffset + TagBytes)
                return ProtocolError.InvalidLength;

            byte[] expectedTag;
            using (var hmac = new HMACSHA256(key))
                expectedTag = hmac.ComputeHash(packet, 0, tagOffset);
            if (!ConstantTimeEquals(packet, tagOffset, expectedTag, 0, TagBytes))
                return ProtocolError.AuthenticationFailed;
            if (!replayWindow.TryAccept(sequence))
                return ProtocolError.ReplayedPacket;

            var payload = new byte[payloadLength];
            Buffer.BlockCopy(packet, HeaderBytes, payload, 0, payloadLength);
            decoded = new AuthenticatedPacket(session, sequence, payload);
            return ProtocolError.None;
        }

        private static bool ConstantTimeEquals(
            byte[] left, int leftOffset, byte[] right, int rightOffset, int length)
        {
            int difference = 0;
            for (int index = 0; index < length; index++)
                difference |= left[leftOffset + index] ^ right[rightOffset + index];
            return difference == 0;
        }

        private static Guid ReadGuid(byte[] data, int offset)
        {
            var bytes = new byte[16];
            Buffer.BlockCopy(data, offset, bytes, 0, bytes.Length);
            return new Guid(bytes);
        }

        private static ushort ReadUInt16(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset] | (data[offset + 1] << 8) |
                   (data[offset + 2] << 16) | (data[offset + 3] << 24));

        private static ulong ReadUInt64(byte[] data, int offset) =>
            ReadUInt32(data, offset) | ((ulong)ReadUInt32(data, offset + 4) << 32);

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

        private static void WriteUInt64(byte[] data, int offset, ulong value)
        {
            WriteUInt32(data, offset, (uint)value);
            WriteUInt32(data, offset + 4, (uint)(value >> 32));
        }
    }
}
