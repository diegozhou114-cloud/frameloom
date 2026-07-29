using System.Net.WebSockets;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.Server.Host;

public sealed class WebSocketOutboundSession : IRealtimeServerSession,
    IRealtimeOutboundSession, IDisposable
{
    private const int MaxQueuedMessages = 256;
    private readonly object _gate = new();
    private readonly WebSocket _socket;
    private readonly Queue<byte[]> _outgoing = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _drain;
    private bool _disposed;

    public WebSocketOutboundSession(WebSocket socket)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
    }

    public bool IsAuthenticated => Claims != null;
    public RealtimeSessionClaims? Claims { get; private set; }
    public Guid SessionId => Claims?.SessionId ?? Guid.Empty;
    public string MatchId => Claims?.MatchId ?? string.Empty;

    public void Bind(RealtimeSessionClaims claims)
    {
        if (claims == null) throw new ArgumentNullException(nameof(claims));
        lock (_gate)
        {
            if (_disposed || Claims != null)
                throw new InvalidOperationException("WebSocket session is already bound.");
            Claims = claims;
        }
    }

    public bool TryEnqueue(byte[] completeMessage)
    {
        if (completeMessage == null || completeMessage.Length == 0 ||
            completeMessage.Length > RealtimeProtocol.HeaderBytes +
            RealtimeProtocol.MaxMessageBytes)
            return false;
        lock (_gate)
        {
            if (_disposed || !IsAuthenticated || _socket.State != WebSocketState.Open ||
                _outgoing.Count >= MaxQueuedMessages)
                return false;
            _outgoing.Enqueue((byte[])completeMessage.Clone());
            if (_drain == null || _drain.IsCompleted)
                _drain = DrainAsync();
            return true;
        }
    }

    public void Dispose()
    {
        Task? drain;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _outgoing.Clear();
            _cancellation.Cancel();
            drain = _drain;
        }
        if (drain == null) _cancellation.Dispose();
        else _ = DisposeAfterDrainAsync(drain);
    }

    private async Task DrainAsync()
    {
        try
        {
            while (true)
            {
                byte[] message;
                lock (_gate)
                {
                    if (_disposed || _outgoing.Count == 0) return;
                    message = _outgoing.Dequeue();
                }
                await _socket.SendAsync(new ArraySegment<byte>(message),
                    WebSocketMessageType.Binary, true, _cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
            ClearAfterTransportFailure();
        }
    }

    private async Task DisposeAfterDrainAsync(Task drain)
    {
        try { await drain.ConfigureAwait(false); }
        catch { }
        _cancellation.Dispose();
    }

    private void ClearAfterTransportFailure()
    {
        lock (_gate)
        {
            _disposed = true;
            _outgoing.Clear();
        }
    }
}
