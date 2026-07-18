using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Network.Contracts.Dispatch;

namespace ZWebGLFrameWork.Network.Tests
{
    public sealed class BoundedMessagePumpTests
    {
        [Test]
        public void MessageDispatcher_UIntIdAndBackpressure_ArePreserved()
        {
            var pump = new BoundedMessagePump(capacity: 2, maxMessagesPerPump: 1);
            var received = new List<uint>();
            pump.Register(0xFFFF0001u, message => received.Add(message.MessageId));

            Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 1 })), Is.True);
            Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 2 })), Is.True);
            Assert.That(pump.TryEnqueue(new NetworkMessage(0xFFFF0001u, new byte[] { 3 })), Is.False);
            Assert.That(pump.Pump(), Is.EqualTo(1));
            Assert.That(received, Is.EqualTo(new[] { 0xFFFF0001u }));
            Assert.That(pump.PendingCount, Is.EqualTo(1));
            Assert.That(pump.DroppedCount, Is.EqualTo(1));
        }
    }
}
