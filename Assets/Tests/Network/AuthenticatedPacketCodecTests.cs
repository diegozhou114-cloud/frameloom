using System;
using System.Linq;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class AuthenticatedPacketCodecTests
    {
        [Test]
        public void AuthorityEnvelope_RejectsShortOversizedWrongVersionAndForeignSession()
        {
            Guid session = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            byte[] key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            byte[] packet = AuthenticatedPacketCodec.Encode(
                session, 9, new byte[] { 4, 5, 6 }, key);
            var window = new SequenceReplayWindow();

            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet, session, key, window, out AuthenticatedPacket decoded),
                Is.EqualTo(ProtocolError.None));
            Assert.That(decoded.Sequence, Is.EqualTo(9));
            Assert.That(decoded.Payload, Is.EqualTo(new byte[] { 4, 5, 6 }));
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet, session, key, window, out _),
                Is.EqualTo(ProtocolError.ReplayedPacket));

            var boundary = new SequenceReplayWindow();
            Assert.That(boundary.TryAccept(2048), Is.True);
            Assert.That(boundary.TryAccept(1025), Is.True);
            Assert.That(boundary.TryAccept(1024), Is.False);

            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet.Take(20).ToArray(), session, key,
                new SequenceReplayWindow(), out _),
                Is.EqualTo(ProtocolError.Truncated));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                AuthenticatedPacketCodec.Encode(session, 10, new byte[65537], key));

            byte[] wrongVersion = (byte[])packet.Clone();
            wrongVersion[0] = 2;
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                wrongVersion, session, key, new SequenceReplayWindow(), out _),
                Is.EqualTo(ProtocolError.UnsupportedVersion));

            packet[packet.Length - 1] ^= 0x40;
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet, session, key, new SequenceReplayWindow(), out _),
                Is.EqualTo(ProtocolError.AuthenticationFailed));
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet, Guid.Empty, key, new SequenceReplayWindow(), out _),
                Is.EqualTo(ProtocolError.WrongSession));
        }

        [Test]
        public void ReplayWindow_AcceptsOutOfOrderOnceWithinWindow()
        {
            var window = new SequenceReplayWindow();
            Assert.That(window.TryAccept(100), Is.True);
            Assert.That(window.TryAccept(98), Is.True);
            Assert.That(window.TryAccept(99), Is.True);
            Assert.That(window.TryAccept(98), Is.False);
            Assert.That(window.TryAccept(1124), Is.True);
            Assert.That(window.TryAccept(100), Is.False);
            Assert.That(window.TryAccept(101), Is.True);
        }

        [Test]
        public void AuthenticationFailure_DoesNotConsumeSequence()
        {
            Guid session = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
            byte[] key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
            byte[] packet = AuthenticatedPacketCodec.Encode(
                session, 42, new byte[] { 7 }, key);
            var window = new SequenceReplayWindow();

            byte[] tampered = (byte[])packet.Clone();
            tampered[tampered.Length - 1] ^= 1;
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                tampered, session, key, window, out _),
                Is.EqualTo(ProtocolError.AuthenticationFailed));
            Assert.That(AuthenticatedPacketCodec.TryDecode(
                packet, session, key, window, out _),
                Is.EqualTo(ProtocolError.None));
        }
    }
}
