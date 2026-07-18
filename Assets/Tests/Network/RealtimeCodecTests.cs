using System;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class RealtimeCodecTests
    {
        [Test]
        public void AuthorityFrame_RoundTrip_PreservesUIntMessageIdAndOrderedInputs()
        {
            var frame = new AuthorityFrame
            {
                MatchId = "11111111-2222-3333-4444-555555555555",
                Tick = 7,
                AckPlayer0 = 10,
                AckPlayer1 = 11,
                Checksum = 0x0102030405060708UL,
                OrderedInputs = new[]
                {
                    CommandEnvelope.NoOp(7, 0, 10),
                    CommandEnvelope.Create(7, 1, 11, 0x00010002u, new byte[] { 3, 4 })
                }
            };

            byte[] bytes = RealtimeCodec.Encode(frame);
            DecodeResult result = RealtimeCodec.Decode(bytes);

            Assert.That(result.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(result.Authority.OrderedInputs[1].MessageId, Is.EqualTo(0x00010002u));
            Assert.That(result.Authority.Checksum, Is.EqualTo(frame.Checksum));
            Assert.That(result.Authority.OrderedInputs[0].Sequence, Is.EqualTo(10));
        }

        [TestCase(0)]
        [TestCase(8)]
        [TestCase(65537)]
        public void Decode_InvalidLength_ReturnsProtocolError(int length)
        {
            var bytes = new byte[length];
            DecodeResult result = RealtimeCodec.Decode(bytes);
            Assert.That(result.Error, Is.Not.EqualTo(ProtocolError.None));
        }

        [Test]
        public void Decode_UnorderedCommands_ReturnsProtocolErrorWithoutThrowing()
        {
            var frame = new AuthorityFrame
            {
                MatchId = "11111111-2222-3333-4444-555555555555",
                Tick = 7,
                OrderedInputs = new[]
                {
                    CommandEnvelope.Create(7, 1, 2, 1, Array.Empty<byte>()),
                    CommandEnvelope.Create(7, 0, 1, 1, Array.Empty<byte>())
                }
            };

            Assert.Throws<ArgumentException>(() => RealtimeCodec.Encode(frame));
        }

        [Test]
        public void AuthorityFrame_CommandTickMustMatchFrameTick()
        {
            var frame = new AuthorityFrame
            {
                MatchId = "11111111-2222-3333-4444-555555555555",
                Tick = 7,
                OrderedInputs = new[]
                {
                    CommandEnvelope.NoOp(6, 0, 1)
                }
            };

            Assert.Throws<ArgumentException>(() => RealtimeCodec.Encode(frame));
        }

        [Test]
        public void AuthenticationMessages_RoundTrip_PreserveSessionAndResumeState()
        {
            Guid sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            DecodeResult auth = RealtimeCodec.Decode(RealtimeCodec.Encode(new AuthHello
            {
                Version = RealtimeProtocol.Version,
                SessionId = sessionId,
                OpaqueTicket = new byte[] { 1, 2, 3 }
            }));
            Assert.That(auth.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(auth.AuthHello.SessionId, Is.EqualTo(sessionId));
            Assert.That(auth.AuthHello.OpaqueTicket, Is.EqualTo(new byte[] { 1, 2, 3 }));

            DecodeResult resume = RealtimeCodec.Decode(RealtimeCodec.Encode(new ResumeHello
            {
                Version = RealtimeProtocol.Version,
                SessionId = sessionId,
                ResumeToken = new byte[] { 4, 5 },
                LastAcceptedSequence = uint.MaxValue,
                LastAuthorityTick = 27,
                TimelineRevision = 99
            }));
            Assert.That(resume.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(resume.ResumeHello.LastAcceptedSequence, Is.EqualTo(uint.MaxValue));
            Assert.That(resume.ResumeHello.TimelineRevision, Is.EqualTo(99));

            DecodeResult accepted = RealtimeCodec.Decode(RealtimeCodec.Encode(new AuthAccepted
            {
                SessionId = sessionId,
                LastAuthorityTick = 27,
                TimelineRevision = 99
            }));
            Assert.That(accepted.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(accepted.AuthAccepted.SessionId, Is.EqualTo(sessionId));
        }

        [Test]
        public void ResyncSnapshotRequestAndClose_RoundTrip_PreserveControlState()
        {
            var snapshot = new ResyncSnapshotEnvelope
            {
                MatchId = "match-1",
                SessionId = "session-1",
                SchemaVersion = 1,
                ContentHash = new byte[32],
                CompletedTick = 6,
                NextTick = 7,
                TimelineRevision = 3,
                UncompressedLength = 3,
                StateChecksum = 99,
                Payload = new byte[] { 1, 2, 3 },
                PayloadSha256 = new byte[32]
            };
            DecodeResult decodedSnapshot = RealtimeCodec.Decode(RealtimeCodec.Encode(snapshot));
            Assert.That(decodedSnapshot.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(decodedSnapshot.ResyncSnapshot.NextTick, Is.EqualTo(7));

            DecodeResult request = RealtimeCodec.Decode(RealtimeCodec.Encode(new ResyncRequest
            {
                MatchId = "match-1",
                SessionId = "session-1",
                FromTick = 7,
                TimelineRevision = 3,
                LocalChecksum = 99
            }));
            Assert.That(request.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(request.ResyncRequest.LocalChecksum, Is.EqualTo(99));

            DecodeResult close = RealtimeCodec.Decode(RealtimeCodec.Encode(new CloseMessage
            {
                Code = RealtimeCloseCode.Malformed,
                Message = "bad frame"
            }));
            Assert.That(close.Error, Is.EqualTo(ProtocolError.None));
            Assert.That(close.Close.Code, Is.EqualTo(RealtimeCloseCode.Malformed));
        }

        [Test]
        public void Decode_HostileHeadersAndAuthorityPayload_ReturnProtocolErrors()
        {
            byte[] valid = RealtimeCodec.Encode(new AuthorityFrame
            {
                MatchId = "11111111-2222-3333-4444-555555555555",
                Tick = 7,
                OrderedInputs = new[]
                {
                    CommandEnvelope.NoOp(7, 0, 10),
                    CommandEnvelope.Create(7, 1, 11, 1, Array.Empty<byte>())
                }
            });

            byte[] wrongVersion = Clone(valid);
            wrongVersion[0] = 2;
            Assert.That(RealtimeCodec.Decode(wrongVersion).Error,
                Is.EqualTo(ProtocolError.UnsupportedVersion));

            byte[] unknownKind = Clone(valid);
            unknownKind[2] = byte.MaxValue;
            Assert.That(RealtimeCodec.Decode(unknownKind).Error,
                Is.EqualTo(ProtocolError.UnknownMessageKind));

            byte[] trailing = new byte[valid.Length + 1];
            Buffer.BlockCopy(valid, 0, trailing, 0, valid.Length);
            Assert.That(RealtimeCodec.Decode(trailing).Error,
                Is.EqualTo(ProtocolError.InvalidLength));

            byte[] count65 = Clone(valid);
            WriteUInt16(count65, 70, 65);
            Assert.That(RealtimeCodec.Decode(count65).Error,
                Is.EqualTo(ProtocolError.InvalidCount));

            byte[] mismatchedTick = Clone(valid);
            WriteInt32(mismatchedTick, 74, 6);
            Assert.That(RealtimeCodec.Decode(mismatchedTick).Error,
                Is.EqualTo(ProtocolError.InvalidPayload));

            byte[] duplicate = Clone(valid);
            WriteInt32(duplicate, 100, 0);
            WriteUInt32(duplicate, 104, 10);
            Assert.That(RealtimeCodec.Decode(duplicate).Error,
                Is.EqualTo(ProtocolError.UnorderedCommands));

            byte[] invalidUtf8 = Clone(valid);
            invalidUtf8[14] = 0xC3;
            invalidUtf8[15] = 0x28;
            Assert.That(RealtimeCodec.Decode(invalidUtf8).Error,
                Is.EqualTo(ProtocolError.InvalidPayload));
        }

        [Test]
        public void Decode_ArbitraryMalformedBuffers_DoesNotThrow()
        {
            var random = new Random(12345);
            for (int index = 0; index < 1000; index++)
            {
                var bytes = new byte[random.Next(0, 2048)];
                random.NextBytes(bytes);
                Assert.DoesNotThrow(() => RealtimeCodec.Decode(bytes));
            }
        }

        private static byte[] Clone(byte[] bytes) => (byte[])bytes.Clone();

        private static void WriteUInt16(byte[] bytes, int offset, ushort value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteInt32(byte[] bytes, int offset, int value) =>
            WriteUInt32(bytes, offset, unchecked((uint)value));

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
