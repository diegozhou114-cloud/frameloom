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
        private CancellationTokenSource _connectCts;
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
            CancellationTokenSource ownedCts;
            CancellationTokenSource previousCts;
            lock (_gate)
            {
                generation = ++_generation;
                previousCts = _connectCts;
                ownedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _connectCts = ownedCts;
            }
            CancelAndDispose(previousCts);

            IRealtimeChannel candidate = null;
            IRealtimeChannel previous = null;
            try
            {
                candidate = _factory.Create(endpoint.Transport)
                    ?? throw new InvalidOperationException("Realtime channel factory returned null.");
                await candidate.ConnectAsync(endpoint, credentials, ownedCts.Token);

                lock (_gate)
                {
                    if (generation != _generation ||
                        !ReferenceEquals(_connectCts, ownedCts) ||
                        ownedCts.IsCancellationRequested)
                        return;
                    previous = _current;
                    _current = candidate;
                    candidate = null;
                    _connectCts = null;
                }
            }
            catch (OperationCanceledException) when (ownedCts.IsCancellationRequested)
            {
            }
            finally
            {
                candidate?.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_connectCts, ownedCts)) _connectCts = null;
                }
                ownedCts.Dispose();
            }

            if (previous != null)
                await CloseAndDisposeAsync(
                    previous, RealtimeCloseReason.Replaced, CancellationToken.None,
                    suppressCloseErrors: true);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            CancellationTokenSource pendingConnect;
            IRealtimeChannel current;
            lock (_gate)
            {
                _generation++;
                pendingConnect = _connectCts;
                _connectCts = null;
                current = _current;
                _current = null;
            }
            CancelAndDispose(pendingConnect);
            if (current != null)
                await CloseAndDisposeAsync(
                    current, RealtimeCloseReason.Normal, cancellationToken,
                    suppressCloseErrors: false);
        }

        private static void CancelAndDispose(CancellationTokenSource source)
        {
            if (source == null) return;
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                source.Dispose();
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
