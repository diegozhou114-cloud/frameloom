using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Network.Tests
{
    [TestFixture]
    public sealed class NativeRealtimeAuthenticationTests
    {
        [Test]
        public async Task NativeRealtime_Handshake_AgreesSessionAndRejectsForgedAndReplayedPackets()
        {
            using var pair = await Kcp2kTestPair.ConnectAsync(
                DatagramImpairment.None, TestCredentials.Valid);
            Assert.That(
                pair.Client.State, Is.EqualTo(RealtimeChannelState.Connected));

            byte[] firstPayload = { 1, 3, 3, 7 };
            byte[] accepted = pair.Server.EncodeApplicationPacket(
                TestCredentials.Valid, sequence: 2, firstPayload);
            pair.Server.SendApplicationPacket(accepted);
            pair.Server.SendApplicationPacket(accepted);
            Assert.That(
                await pair.Client.ReceiveAsync(CancellationToken.None),
                Is.EqualTo(firstPayload));
            await AssertNoMessageAsync(pair);

            byte[] forged = pair.Server.EncodeApplicationPacket(
                TestCredentials.Valid, sequence: 3, new byte[] { 2, 4, 6, 8 });
            forged[forged.Length - 1] ^= 1;
            pair.Server.SendApplicationPacket(forged);
            await AssertNoMessageAsync(pair);

            byte[] foreignSession = pair.Server.EncodeApplicationPacket(
                TestCredentials.Foreign, sequence: 4, new byte[] { 5, 7, 9 });
            pair.Server.SendApplicationPacket(foreignSession);
            await AssertNoMessageAsync(pair);

            byte[] recoveryPayload = { 9, 7, 5, 3 };
            pair.Server.SendApplicationPacket(
                pair.Server.EncodeApplicationPacket(
                    TestCredentials.Valid, sequence: 3, recoveryPayload));
            Assert.That(
                await pair.Client.ReceiveAsync(CancellationToken.None),
                Is.EqualTo(recoveryPayload));
        }

        private static async Task AssertNoMessageAsync(Kcp2kTestPair pair)
        {
            using var cancellation = new CancellationTokenSource(
                TimeSpan.FromMilliseconds(100));
            try
            {
                byte[] message = await pair.Client.ReceiveAsync(cancellation.Token);
                Assert.Fail(
                    "Rejected authenticated packet reached the client: " +
                    Convert.ToBase64String(message));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
