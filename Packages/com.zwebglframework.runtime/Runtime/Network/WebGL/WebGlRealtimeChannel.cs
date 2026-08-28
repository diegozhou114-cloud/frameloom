using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.WebGL
{
    public sealed class WebGlRealtimeChannel : IRealtimeChannel
    {
        private readonly object _gate = new object();
        private readonly INativeWebSocketClientFactory _clientFactory;
        private readonly Queue<byte[]> _received = new Queue<byte[]>();
        private const int MaxRealtimeFrameBytes = RealtimeProtocol.HeaderBytes +
            RealtimeProtocol.MaxMessageBytes;
        private INativeWebSocketClient _client;
        private RealtimeCredentials _credentials;
        private TaskCompletionSource<bool> _authentication;
        private TaskCompletionSource<byte[]> _pendingReceive;
        private RealtimeChannelState _state;
        private bool _started;
        private bool _closed;
        private CancellationTokenSource _dispatchCancellation;
        private Task _dispatchTask;
        private Task _closeTask;

        public WebGlRealtimeChannel() : this(new NativeWebSocketClientFactory()) { }

        internal WebGlRealtimeChannel(INativeWebSocketClientFactory clientFactory)
        {
            _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
            _state = RealtimeChannelState.Disconnected;
        }

        public RealtimeChannelState State { get { lock (_gate) return _state; } }

        public async Task ConnectAsync(RealtimeEndpoint endpoint, RealtimeCredentials credentials,
            CancellationToken cancellationToken)
        {
            Validate(endpoint, credentials);
            INativeWebSocketClient client;
            TaskCompletionSource<bool> authentication;
            CancellationTokenSource dispatchCancellation;
            lock (_gate)
            {
                if (_started) throw new InvalidOperationException("A WebGL channel can only connect once.");
                _started = true;
                _state = RealtimeChannelState.Connecting;
                _credentials = credentials;
                _authentication = authentication = NewCompletion<bool>();
                client = _client = _clientFactory.Create(endpoint.Uri,
                    new[] { RealtimeProtocol.WebSocketSubprotocol }) ?? throw new InvalidOperationException("WebSocket factory returned null.");
                client.Opened += OnOpened;
                client.MessageReceived += OnMessage;
                client.Error += OnError;
                client.Closed += OnClosed;
                _dispatchCancellation = dispatchCancellation = new CancellationTokenSource();
            }
            try
            {
                _dispatchTask = DispatchLoopAsync(client, dispatchCancellation.Token);
                Task native = client.ConnectAsync();
                ObserveNativeConnect(native);
                using (cancellationToken.Register(() => _ = CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None)))
                    await authentication.Task.ConfigureAwait(false);
            }
            catch
            {
                await CloseAsync(RealtimeCloseReason.AuthenticationFailed, CancellationToken.None).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);
                throw;
            }
        }

        public Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completeMessage == null) throw new ArgumentNullException(nameof(completeMessage));
            if (completeMessage.Length == 0 ||
                completeMessage.Length > MaxRealtimeFrameBytes)
                throw new ArgumentOutOfRangeException(nameof(completeMessage));
            INativeWebSocketClient client;
            lock (_gate)
            {
                if (_state != RealtimeChannelState.Connected) throw new InvalidOperationException("Channel is not connected.");
                client = _client;
            }
            return client.SendAsync(completeMessage);
        }

        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_received.Count > 0) return Task.FromResult(_received.Dequeue());
                if (_state != RealtimeChannelState.Connected) throw new InvalidOperationException("Channel is not connected.");
                if (_pendingReceive != null) throw new InvalidOperationException("Only one receive may wait at a time.");
                _pendingReceive = NewCompletion<byte[]>();
                return AwaitReceive(_pendingReceive, cancellationToken);
            }
        }

        public Task CloseAsync(RealtimeCloseReason reason, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completion = NewCompletion<bool>();
            lock (_gate)
            {
                if (_closeTask != null) return _closeTask;
                _closeTask = completion.Task;
            }
            _ = CompleteCloseAsync(reason, completion);
            return completion.Task;
        }

        private async Task CompleteCloseAsync(
            RealtimeCloseReason reason, TaskCompletionSource<bool> completion)
        {
            INativeWebSocketClient client;
            TaskCompletionSource<bool> authentication;
            TaskCompletionSource<byte[]> receiver;
            CancellationTokenSource dispatchCancellation;
            Task dispatchTask;
            try
            {
                lock (_gate)
                {
                    _closed = true; _started = true; _state = RealtimeChannelState.Closing;
                    client = _client; _client = null;
                    authentication = _authentication; receiver = _pendingReceive; _pendingReceive = null;
                    _received.Clear();
                    dispatchCancellation = _dispatchCancellation; _dispatchCancellation = null;
                    dispatchTask = _dispatchTask; _dispatchTask = null;
                }
                lock (_gate) _state = RealtimeChannelState.Disconnected;
                dispatchCancellation?.Cancel();
                authentication?.TrySetException(new InvalidOperationException("Realtime channel closed: " + reason));
                receiver?.TrySetException(new InvalidOperationException("Realtime channel closed."));
                try
                {
                    if (dispatchTask != null) await dispatchTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    dispatchCancellation?.Dispose();
                }
                if (client != null) { try { await client.CloseAsync().ConfigureAwait(false); } catch { } client.Dispose(); }
                completion.TrySetResult(true);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        public void Dispose() => CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None).GetAwaiter().GetResult();

        private async Task<byte[]> AwaitReceive(TaskCompletionSource<byte[]> pending, CancellationToken token)
        {
            using (token.Register(() => pending.TrySetCanceled(token)))
            {
                try { return await pending.Task.ConfigureAwait(false); }
                finally { lock (_gate) if (ReferenceEquals(_pendingReceive, pending)) _pendingReceive = null; }
            }
        }

        private async void ObserveNativeConnect(Task native)
        {
            try { await native.ConfigureAwait(false); }
            catch { await CloseAsync(RealtimeCloseReason.AuthenticationFailed, CancellationToken.None).ConfigureAwait(false); }
        }

        private async Task DispatchLoopAsync(INativeWebSocketClient client, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    client.DispatchMessageQueue();
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async void OnOpened()
        {
            INativeWebSocketClient client; RealtimeCredentials credentials;
            lock (_gate) { if (_closed) return; client = _client; credentials = _credentials; }
            try { await client.SendAsync(RealtimeCodec.Encode(new AuthHello { Version = RealtimeProtocol.Version, SessionId = credentials.SessionId, OpaqueTicket = credentials.OpaqueTicket })).ConfigureAwait(false); }
            catch { await CloseAsync(RealtimeCloseReason.AuthenticationFailed, CancellationToken.None).ConfigureAwait(false); }
        }

        private void OnMessage(byte[] message)
        {
            TaskCompletionSource<byte[]> receiver = null; bool fail = false;
            if (message == null || message.Length == 0 ||
                message.Length > MaxRealtimeFrameBytes)
            {
                lock (_gate)
                    fail = !_closed && _state == RealtimeChannelState.Connecting;
                if (fail)
                    _ = CloseAsync(RealtimeCloseReason.AuthenticationFailed,
                        CancellationToken.None);
                return;
            }
            lock (_gate)
            {
                if (_closed) return;
                if (_state == RealtimeChannelState.Connecting)
                {
                    DecodeResult decoded = RealtimeCodec.Decode(message);
                    if (decoded.Error != ProtocolError.None || decoded.Kind != RealtimeMessageKind.AuthAccepted || decoded.AuthAccepted.SessionId != _credentials.SessionId) fail = true;
                    else { _state = RealtimeChannelState.Connected; _authentication.TrySetResult(true); }
                }
                else if (_state == RealtimeChannelState.Connected)
                {
                    receiver = _pendingReceive; _pendingReceive = null;
                    if (receiver == null) _received.Enqueue(message);
                }
            }
            if (fail) _ = CloseAsync(RealtimeCloseReason.AuthenticationFailed, CancellationToken.None);
            else receiver?.TrySetResult(message);
        }

        private void OnError(string error) => _ = CloseAsync(RealtimeCloseReason.AuthenticationFailed, CancellationToken.None);
        private void OnClosed(ushort code) => _ = CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None);
        private static TaskCompletionSource<T> NewCompletion<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static void Validate(RealtimeEndpoint endpoint, RealtimeCredentials credentials)
        {
            if (endpoint.Transport != RealtimeTransport.WebSocket) throw new ArgumentOutOfRangeException(nameof(endpoint));
            if (!endpoint.Uri.IsAbsoluteUri || endpoint.Uri.Scheme != "wss" || endpoint.Uri.AbsolutePath != RealtimeProtocol.WebSocketPath) throw new ArgumentException("Realtime endpoint must be wss /v1/realtime.", nameof(endpoint));
            if (credentials == null || credentials.SessionId == Guid.Empty || credentials.OpaqueTicket == null || credentials.OpaqueTicket.Length == 0) throw new ArgumentException("Credentials are invalid.", nameof(credentials));
            if (credentials.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw new ArgumentException("Credentials are expired.", nameof(credentials));
        }
    }
}
