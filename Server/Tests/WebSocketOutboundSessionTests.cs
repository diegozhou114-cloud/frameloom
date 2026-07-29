using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Server.Host;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class WebSocketOutboundSessionTests
    {
        [Test]
        public async Task BoundSession_SendsQueuedMessagesSequentially()
        {
            var socket = new RecordingWebSocket();
            using var session = new WebSocketOutboundSession(socket);
            session.Bind(Claims());

            Assert.That(session.TryEnqueue(new byte[] { 1 }), Is.True);
            Assert.That(session.TryEnqueue(new byte[] { 2 }), Is.True);
            await socket.WaitForSendsAsync(2);

            Assert.That(socket.Sent, Is.EqualTo(new[]
            {
                new byte[] { 1 }, new byte[] { 2 }
            }));
            Assert.That(socket.MaxConcurrentSends, Is.EqualTo(1));
        }

        [Test]
        public void UnboundOrDisposedSession_RejectsOutboundMessages()
        {
            var socket = new RecordingWebSocket();
            var session = new WebSocketOutboundSession(socket);

            Assert.That(session.TryEnqueue(new byte[] { 1 }), Is.False);
            session.Bind(Claims());
            session.Dispose();
            Assert.That(session.TryEnqueue(new byte[] { 2 }), Is.False);
        }

        private static ZWebGLFrameWork.FrameSync.RealtimeSessionClaims Claims() =>
            new ZWebGLFrameWork.FrameSync.RealtimeSessionClaims(Guid.NewGuid(),
                "match-1", 0, new byte[] { 1 }, 1_000);

        private sealed class RecordingWebSocket : WebSocket
        {
            private readonly List<byte[]> _sent = new List<byte[]>();
            private int _activeSends;
            private int _maxConcurrentSends;

            public IReadOnlyList<byte[]> Sent => _sent;
            public int MaxConcurrentSends => _maxConcurrentSends;
            public override WebSocketState State => WebSocketState.Open;
            public override WebSocketCloseStatus? CloseStatus => null;
            public override string CloseStatusDescription => null;
            public override string SubProtocol => "zweb-frame-sync-v1";

            public override void Abort()
            {
            }

            public override Task CloseAsync(WebSocketCloseStatus closeStatus,
                string statusDescription, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus,
                string statusDescription, CancellationToken cancellationToken) =>
                Task.CompletedTask;

            public override void Dispose()
            {
            }

            public override Task<WebSocketReceiveResult> ReceiveAsync(
                ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
                throw new NotSupportedException();

            public override async Task SendAsync(ArraySegment<byte> buffer,
                WebSocketMessageType messageType, bool endOfMessage,
                CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref _activeSends);
                _maxConcurrentSends = Math.Max(_maxConcurrentSends, active);
                try
                {
                    await Task.Delay(10, cancellationToken);
                    var copy = new byte[buffer.Count];
                    Buffer.BlockCopy(buffer.Array!, buffer.Offset, copy, 0, copy.Length);
                    lock (_sent) _sent.Add(copy);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeSends);
                }
            }

            public async Task WaitForSendsAsync(int count)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(2);
                while (true)
                {
                    lock (_sent)
                        if (_sent.Count >= count) return;
                    if (DateTime.UtcNow >= deadline)
                        throw new TimeoutException("Timed out waiting for websocket sends.");
                    await Task.Delay(1);
                }
            }
        }
    }
}
