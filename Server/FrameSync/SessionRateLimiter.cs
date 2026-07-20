namespace ZWebGLFrameWork.FrameSync
{
    public sealed class SessionRateLimiter
    {
        private readonly int _messagesPerSecond;
        private readonly long _capacityUnits;
        private readonly object _gate = new object();
        private readonly Dictionary<Guid, Bucket> _buckets = new Dictionary<Guid, Bucket>();

        public SessionRateLimiter(int messagesPerSecond, int burst)
        {
            if (messagesPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(messagesPerSecond));
            if (burst <= 0) throw new ArgumentOutOfRangeException(nameof(burst));
            _messagesPerSecond = messagesPerSecond;
            _capacityUnits = checked((long)burst * 1000);
        }

        public bool TryConsume(Guid sessionId, long milliseconds)
        {
            if (sessionId == Guid.Empty)
                throw new ArgumentException("Session ID is required.", nameof(sessionId));
            if (milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds));
            lock (_gate)
            {
                if (!_buckets.TryGetValue(sessionId, out Bucket bucket))
                    bucket = new Bucket(_capacityUnits, milliseconds);
                long elapsed = Math.Max(0, milliseconds - bucket.LastMilliseconds);
                long missingUnits = _capacityUnits - bucket.TokenUnits;
                long millisecondsToFull = missingUnits == 0 ? 0 :
                    1 + (missingUnits - 1) / _messagesPerSecond;
                long refillMilliseconds = Math.Min(elapsed, millisecondsToFull);
                long refilled = Math.Min(_capacityUnits,
                    bucket.TokenUnits + refillMilliseconds * _messagesPerSecond);
                long observedMilliseconds = Math.Max(
                    bucket.LastMilliseconds, milliseconds);
                if (refilled < 1000)
                {
                    _buckets[sessionId] = new Bucket(
                        refilled, observedMilliseconds);
                    return false;
                }
                _buckets[sessionId] = new Bucket(
                    refilled - 1000, observedMilliseconds);
                return true;
            }
        }

        private readonly struct Bucket
        {
            public long TokenUnits { get; }
            public long LastMilliseconds { get; }
            public Bucket(long tokenUnits, long lastMilliseconds)
            {
                TokenUnits = tokenUnits;
                LastMilliseconds = lastMilliseconds;
            }
        }
    }
}
