using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.Network.Contracts.Auth;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.Native
{
    public sealed class Kcp2kRealtimeChannel : IRealtimeChannel
    {
        private readonly Func<KcpNativeCallbacks, kcp2k.KcpConfig, IKcp2kClient>
            _clientFactory;
        private readonly object _gate = new object();
        private readonly Queue<byte[]> _receiveQueue = new Queue<byte[]>();
        private readonly SemaphoreSlim _receiveSignal = new SemaphoreSlim(0);
        private readonly TaskCompletionSource<bool> _terminalCompletion =
            NewCompletionSource();
        private readonly kcp2k.KcpConfig _config = new kcp2k.KcpConfig(
            DualMode: false,
            Interval: 10,
            FastResend: 2,
            CongestionWindow: false,
            SendWindowSize: 256,
            ReceiveWindowSize: 256,
            Timeout: 10000);

        private IKcp2kClient _client;
        private CancellationTokenSource _pumpCancellation;
        private Task _pumpTask;
        private TaskCompletionSource<bool> _connectCompletion;
        private RealtimeCredentials _credentials;
        private SequenceReplayWindow _receiveReplay;
        private RealtimeChannelState _state;
        private Uri _pendingEndpoint;
        private Uri _authenticatedEndpoint;
        private ulong _sendSequence;
        private bool _hasStarted;

        public RealtimeChannelState State
        {
            get
            {
                lock (_gate) return _state;
            }
        }

        public Kcp2kRealtimeChannel()
            : this((callbacks, config) => new Kcp2kClientFacade(callbacks, config))
        {
        }

        internal Kcp2kRealtimeChannel(
            Func<KcpNativeCallbacks, kcp2k.KcpConfig, IKcp2kClient> clientFactory)
        {
            _clientFactory = clientFactory ??
                throw new ArgumentNullException(nameof(clientFactory));
            _state = RealtimeChannelState.Disconnected;
        }

        public async Task ConnectAsync(
            RealtimeEndpoint endpoint,
            RealtimeCredentials credentials,
            CancellationToken cancellationToken)
        {
            ValidateConnectArguments(endpoint, credentials);
            Task connectCompletion;
            try
            {
                lock (_gate)
                {
                    if (_hasStarted || _state != RealtimeChannelState.Disconnected)
                        throw new InvalidOperationException(
                            "A native realtime channel can only be connected once.");
                    _hasStarted = true;
                    _state = RealtimeChannelState.Connecting;
                    _credentials = CloneCredentials(credentials);
                    _receiveReplay = new SequenceReplayWindow();
                    _sendSequence = 0;
                    _pendingEndpoint = endpoint.Uri;
                    _authenticatedEndpoint = null;
                    _connectCompletion = NewCompletionSource();
                    _pumpCancellation = new CancellationTokenSource();
                    _client = _clientFactory(CreateCallbacks(), _config) ??
                        throw new InvalidOperationException(
                            "The kcp2k client factory returned null.");
                    IKcp2kClient client = _client;
                    CancellationTokenSource pumpCancellation = _pumpCancellation;
                    _pumpTask = Task.Run(
                        () => PumpLoopAsync(client, pumpCancellation));
                    connectCompletion = _connectCompletion.Task;
                    client.Connect(endpoint.Uri.Host, (ushort)endpoint.Uri.Port);
                }

                await WaitForConnectAsync(connectCompletion, cancellationToken);
            }
            catch
            {
                await CloseAsync(RealtimeCloseReason.AuthenticationFailed,
                    CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        public Task SendAsync(byte[] completeMessage, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (completeMessage == null) throw new ArgumentNullException(nameof(completeMessage));
            lock (_gate)
            {
                EnsureConnected();
                byte[] packet = AuthenticatedPacketCodec.Encode(
                    _credentials.SessionId, ++_sendSequence,
                    completeMessage, _credentials.MacKey);
                _client.Send(packet);
            }
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (_receiveQueue.Count > 0) return _receiveQueue.Dequeue();
                    if (_state == RealtimeChannelState.Disconnected)
                        throw new InvalidOperationException("The channel is disconnected.");
                }

                using var waitCancellation =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task signal = _receiveSignal.WaitAsync(waitCancellation.Token);
                Task winner = await Task.WhenAny(signal, _terminalCompletion.Task)
                    .ConfigureAwait(false);
                if (winner == _terminalCompletion.Task)
                {
                    waitCancellation.Cancel();
                    try
                    {
                        await signal.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    throw new InvalidOperationException("The channel is disconnected.");
                }
                await signal.ConfigureAwait(false);
                lock (_gate)
                {
                    if (_receiveQueue.Count > 0) return _receiveQueue.Dequeue();
                    if (_state == RealtimeChannelState.Disconnected)
                        throw new InvalidOperationException("The channel is disconnected.");
                }
            }
        }

        public async Task CloseAsync(
            RealtimeCloseReason reason,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task pump;
            IKcp2kClient client;
            CancellationTokenSource pumpCancellation;
            TaskCompletionSource<bool> connectCompletion;
            lock (_gate)
            {
                if (_client == null && _pumpTask == null &&
                    _pumpCancellation == null)
                {
                    _hasStarted = true;
                    _state = RealtimeChannelState.Disconnected;
                    _terminalCompletion.TrySetResult(true);
                    return;
                }
                _state = RealtimeChannelState.Closing;
                pump = _pumpTask;
                client = _client;
                pumpCancellation = _pumpCancellation;
                connectCompletion = _connectCompletion;
            }

            pumpCancellation?.Cancel();
            if (pump != null)
            {
                await pump.ConfigureAwait(false);
            }
            else
            {
                client?.Dispose();
                pumpCancellation?.Dispose();
            }

            lock (_gate)
            {
                _state = RealtimeChannelState.Disconnected;
                if (ReferenceEquals(_client, client)) _client = null;
                if (ReferenceEquals(_pumpTask, pump)) _pumpTask = null;
                if (ReferenceEquals(_pumpCancellation, pumpCancellation))
                    _pumpCancellation = null;
                ClearSessionLocked();
                connectCompletion?.TrySetException(
                    new InvalidOperationException($"Realtime channel closed: {reason}"));
                _terminalCompletion.TrySetResult(true);
            }
        }

        public void Dispose()
        {
            try
            {
                CloseAsync(RealtimeCloseReason.Normal, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }
        }

        private KcpNativeCallbacks CreateCallbacks()
        {
            return new KcpNativeCallbacks
            {
                Connected = HandleTransportConnected,
                Data = HandleData,
                Disconnected = HandleTransportDisconnected,
                Error = HandleError
            };
        }

        private async Task PumpLoopAsync(
            IKcp2kClient client, CancellationTokenSource pumpCancellation)
        {
            CancellationToken token = pumpCancellation.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        lock (_gate)
                        {
                            client.TickIncoming();
                            client.TickOutgoing();
                        }
                        await Task.Delay(1, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception exception)
                    {
                        HandleError(kcp2k.ErrorCode.Timeout, exception.Message);
                        break;
                    }
                }
            }
            finally
            {
                client.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_client, client)) _client = null;
                    if (ReferenceEquals(_pumpCancellation, pumpCancellation))
                        _pumpCancellation = null;
                    _pumpTask = null;
                    if (_state != RealtimeChannelState.Closing)
                        _state = RealtimeChannelState.Disconnected;
                    ClearSessionLocked();
                    _terminalCompletion.TrySetResult(true);
                }
                pumpCancellation.Dispose();
            }
        }

        private void HandleTransportConnected()
        {
            lock (_gate)
            {
                if (_state != RealtimeChannelState.Connecting) return;
                byte[] hello = RealtimeCodec.Encode(new AuthHello
                {
                    Version = RealtimeProtocol.Version,
                    SessionId = _credentials.SessionId,
                    OpaqueTicket = _credentials.OpaqueTicket
                });
                byte[] packet = AuthenticatedPacketCodec.Encode(
                    _credentials.SessionId, ++_sendSequence,
                    hello, _credentials.MacKey);
                _client.Send(packet);
            }
        }

        private void HandleData(
            ArraySegment<byte> segment, kcp2k.KcpChannel channel)
        {
            var packet = new byte[segment.Count];
            Buffer.BlockCopy(segment.Array, segment.Offset, packet, 0, segment.Count);
            lock (_gate)
            {
                if (_state != RealtimeChannelState.Connecting &&
                    _state != RealtimeChannelState.Connected) return;
                ProtocolError error = AuthenticatedPacketCodec.TryDecode(
                    packet, _credentials.SessionId, _credentials.MacKey,
                    _receiveReplay, out AuthenticatedPacket decoded);
                if (error != ProtocolError.None) return;

                if (_state == RealtimeChannelState.Connecting)
                {
                    DecodeResult accepted = RealtimeCodec.Decode(decoded.Payload);
                    if (accepted.Error != ProtocolError.None ||
                        accepted.Kind != RealtimeMessageKind.AuthAccepted ||
                        accepted.AuthAccepted.SessionId != _credentials.SessionId)
                        return;
                    _authenticatedEndpoint = _pendingEndpoint;
                    _state = RealtimeChannelState.Connected;
                    _connectCompletion.TrySetResult(true);
                    return;
                }

                _receiveQueue.Enqueue(decoded.Payload);
                _receiveSignal.Release();
            }
        }

        private void HandleTransportDisconnected()
        {
            CancellationTokenSource pumpCancellation;
            lock (_gate)
            {
                if (_state == RealtimeChannelState.Closing ||
                    _state == RealtimeChannelState.Disconnected) return;
                _state = RealtimeChannelState.Disconnected;
                _connectCompletion?.TrySetException(
                    new InvalidOperationException("kcp2k disconnected."));
                pumpCancellation = _pumpCancellation;
            }
            pumpCancellation?.Cancel();
        }

        private void HandleError(kcp2k.ErrorCode code, string message)
        {
            lock (_gate)
            {
                if (_state == RealtimeChannelState.Connecting)
                    _connectCompletion.TrySetException(
                        new InvalidOperationException($"kcp2k {code}: {message}"));
            }
        }

        private void EnsureConnected()
        {
            if (_state != RealtimeChannelState.Connected || _authenticatedEndpoint == null)
                throw new InvalidOperationException("The channel is not connected.");
        }

        private static void ValidateConnectArguments(
            RealtimeEndpoint endpoint, RealtimeCredentials credentials)
        {
            if (endpoint.Transport != RealtimeTransport.NativeKcp)
                throw new ArgumentOutOfRangeException(nameof(endpoint));
            if (endpoint.Uri == null || endpoint.Uri.Port < 1 || endpoint.Uri.Port > ushort.MaxValue ||
                string.IsNullOrWhiteSpace(endpoint.Uri.Host))
                throw new ArgumentException("A native KCP endpoint is required.", nameof(endpoint));
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (credentials.SessionId == Guid.Empty || credentials.OpaqueTicket == null ||
                credentials.OpaqueTicket.Length == 0 || credentials.MacKey == null ||
                credentials.MacKey.Length != 32)
                throw new ArgumentException("Credentials are incomplete.", nameof(credentials));
            if (credentials.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                throw new ArgumentException("Credentials are expired.", nameof(credentials));
        }

        private static RealtimeCredentials CloneCredentials(RealtimeCredentials credentials)
        {
            return new RealtimeCredentials
            {
                SessionId = credentials.SessionId,
                OpaqueTicket = credentials.OpaqueTicket.ToArray(),
                ResumeToken = credentials.ResumeToken?.ToArray() ?? Array.Empty<byte>(),
                MacKey = credentials.MacKey.ToArray(),
                ExpiresAtUnixSeconds = credentials.ExpiresAtUnixSeconds
            };
        }

        private static TaskCompletionSource<bool> NewCompletionSource()
        {
            return new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private void ClearSessionLocked()
        {
            _receiveQueue.Clear();
            while (_receiveSignal.Wait(0))
            {
            }
            _credentials = null;
            _receiveReplay = null;
            _pendingEndpoint = null;
            _authenticatedEndpoint = null;
            _connectCompletion = null;
            _sendSequence = 0;
        }

        private static async Task WaitForConnectAsync(
            Task completion, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
            {
                await completion;
                return;
            }
            var cancellation = NewCompletionSource();
            using (cancellationToken.Register(() => cancellation.TrySetCanceled()))
            {
                Task winner = await Task.WhenAny(completion, cancellation.Task);
                await winner;
            }
        }
    }
}
