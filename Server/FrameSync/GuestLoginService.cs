namespace ZWebGLFrameWork.FrameSync
{
    public sealed class GuestLoginService
    {
        private readonly InMemoryRealtimeTicketStore _tickets;
        private readonly IRealtimeLoginAllocator _allocator;

        public GuestLoginService(InMemoryRealtimeTicketStore tickets,
            IRealtimeLoginAllocator allocator)
        {
            _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
            _allocator = allocator ?? throw new ArgumentNullException(nameof(allocator));
        }

        public GuestLoginOutcome Login(long utcUnixSeconds)
        {
            if (!_allocator.TryAssignGuest(out RealtimeLoginAssignment? assignment) ||
                assignment == null)
                return GuestLoginOutcome.Unavailable;
            return new GuestLoginOutcome(assignment, _tickets.Issue(
                assignment.MatchId, assignment.PlayerId, utcUnixSeconds));
        }
    }

    public sealed class GuestLoginOutcome
    {
        public static readonly GuestLoginOutcome Unavailable =
            new GuestLoginOutcome(null, null);

        public RealtimeLoginAssignment? Assignment { get; }
        public IssuedRealtimeCredentials? Credentials { get; }
        public bool IsAvailable => Assignment != null && Credentials != null;

        public GuestLoginOutcome(RealtimeLoginAssignment? assignment,
            IssuedRealtimeCredentials? credentials)
        {
            if ((assignment == null) != (credentials == null))
                throw new ArgumentException("Assignment and credentials must be paired.");
            Assignment = assignment;
            Credentials = credentials;
        }
    }
}
