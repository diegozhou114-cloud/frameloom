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
    }
}
