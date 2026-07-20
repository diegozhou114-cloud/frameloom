using System;
using System.Threading;
using System.Threading.Tasks;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.Network.Contracts.Transport;

namespace ZWebGLFrameWork.Network.Runtime
{
    public sealed class RealtimeConnectionController
    {
        private readonly object _gate = new object();
        private readonly IRealtimeChannelFactory _factory;
        private IRealtimeChannel _current;
        private PendingConnect _pendingConnect;
        private int _generation;

        public IRealtimeChannel Current
        {
            get
            {
                lock (_gate) return _current;
            }
        }

        public int Generation
        {
            get
            {
                lock (_gate) return _generation;
            }
        }

        public RealtimeConnectionController(IRealtimeChannelFactory factory)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public async Task ConnectAsync(
            RealtimeEndpoint endpoint,
            RealtimeCredentials credentials,
            CancellationToken cancellationToken)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));

            int generation;
            PendingConnect ownedConnect;
            PendingConnect previousConnect;
            lock (_gate)
            {
                generation = ++_generation;
                previousConnect = _pendingConnect;
                ownedConnect = new PendingConnect();
                _pendingConnect = ownedConnect;
            }
            previousConnect?.RequestCancellation();

            IRealtimeChannel candidate = null;
            IRealtimeChannel previous = null;
            CancellationTokenRegistration callerCancellation = default;
            try
            {
                callerCancellation = cancellationToken.Register(
                    state => ((PendingConnect)state).RequestCancellation(), ownedConnect);
                candidate = _factory.Create(endpoint.Transport)
                    ?? throw new InvalidOperationException("Realtime channel factory returned null.");
                await candidate.ConnectAsync(endpoint, credentials, ownedConnect.Token);

                lock (_gate)
                {
                    if (generation != _generation ||
                        !ReferenceEquals(_pendingConnect, ownedConnect) ||
                        ownedConnect.IsCancellationRequested)
                        return;
                    previous = _current;
                    _current = candidate;
                    candidate = null;
                    _pendingConnect = null;
                }
            }
            catch
            {
                bool stale;
                lock (_gate)
                {
                    stale = generation != _generation ||
                        !ReferenceEquals(_pendingConnect, ownedConnect) ||
                        ownedConnect.IsCancellationRequested;
                }
                if (!stale) throw;
            }
            finally
            {
                try
                {
                    candidate?.Dispose();
                }
                finally
                {
                    lock (_gate)
                    {
                        if (ReferenceEquals(_pendingConnect, ownedConnect))
                            _pendingConnect = null;
                    }
                    try
                    {
                        callerCancellation.Dispose();
                    }
                    finally
                    {
                        ownedConnect.Complete();
                    }
                }
            }

            if (previous != null)
                await CloseAndDisposeAsync(
                    previous, RealtimeCloseReason.Replaced, CancellationToken.None,
                    suppressCloseErrors: true);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            PendingConnect pendingConnect;
            IRealtimeChannel current;
            lock (_gate)
            {
                _generation++;
                pendingConnect = _pendingConnect;
                _pendingConnect = null;
                current = _current;
                _current = null;
            }
            pendingConnect?.RequestCancellation();
            if (current != null)
                await CloseAndDisposeAsync(
                    current, RealtimeCloseReason.Normal, cancellationToken,
                    suppressCloseErrors: false);
        }

        private sealed class PendingConnect
        {
            private readonly object _gate = new object();
            private readonly CancellationTokenSource _source;
            private readonly CancellationToken _token;
            private bool _cancelRequested;
            private bool _cancelling;
            private bool _completed;
            private bool _disposed;

            public CancellationToken Token => _token;
            public bool IsCancellationRequested => _token.IsCancellationRequested;

            public PendingConnect()
            {
                _source = new CancellationTokenSource();
                _token = _source.Token;
            }

            public void RequestCancellation()
            {
                lock (_gate)
                {
                    if (_disposed || _cancelRequested) return;
                    _cancelRequested = true;
                    _cancelling = true;
                }

                try
                {
                    _source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                catch (AggregateException)
                {
                }
                finally
                {
                    lock (_gate)
                    {
                        _cancelling = false;
                        TryDisposeLocked();
                    }
                }
            }

            public void Complete()
            {
                lock (_gate)
                {
                    _completed = true;
                    TryDisposeLocked();
                }
            }

            private void TryDisposeLocked()
            {
                if (_disposed || !_completed || _cancelling) return;
                _disposed = true;
                _source.Dispose();
            }
        }

        private static async Task CloseAndDisposeAsync(
            IRealtimeChannel channel,
            RealtimeCloseReason reason,
            CancellationToken cancellationToken,
            bool suppressCloseErrors)
        {
            try
            {
                await channel.CloseAsync(reason, cancellationToken);
            }
            catch when (suppressCloseErrors)
            {
            }
            finally
            {
                channel.Dispose();
            }
        }
    }
}
