using ZWebGLFrameWork.Network.Contracts.Protocol;

namespace ZWebGLFrameWork.FrameSync
{
    public sealed class FrameSyncMatchRegistry : IRealtimeMatchSink, IDisposable
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, FrameSyncMatchActor> _actors =
            new Dictionary<string, FrameSyncMatchActor>(StringComparer.Ordinal);
        private bool _disposed;

        public void Register(string matchId, FrameSyncMatchActor actor)
        {
            if (string.IsNullOrWhiteSpace(matchId))
                throw new ArgumentException("Match ID is required.", nameof(matchId));
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!_actors.TryAdd(matchId, actor))
                    throw new InvalidOperationException("Match is already registered.");
            }
        }

        public Task<ProtocolError> PostAsync(string matchId,
            int authenticatedPlayerId, CommandEnvelope command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FrameSyncMatchActor? actor;
            lock (_gate)
            {
                if (_disposed || !_actors.TryGetValue(matchId, out actor))
                    return Task.FromResult(ProtocolError.WrongSession);
            }
            return Task.FromResult(Map(actor.SubmitInput(authenticatedPlayerId, command)));
        }

        public void Dispose()
        {
            FrameSyncMatchActor[] actors;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                actors = _actors.Values.ToArray();
                _actors.Clear();
            }
            foreach (FrameSyncMatchActor actor in actors) actor.Dispose();
        }

        private static ProtocolError Map(InputAcceptance acceptance)
        {
            return acceptance switch
            {
                InputAcceptance.Accepted => ProtocolError.None,
                InputAcceptance.MailboxOverloaded => ProtocolError.RateLimited,
                InputAcceptance.WrongPlayer => ProtocolError.WrongPlayer,
                InputAcceptance.WrongMatch => ProtocolError.WrongSession,
                InputAcceptance.UnsupportedVersion => ProtocolError.UnsupportedVersion,
                _ => ProtocolError.LateRejected
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FrameSyncMatchRegistry));
        }
    }
}
