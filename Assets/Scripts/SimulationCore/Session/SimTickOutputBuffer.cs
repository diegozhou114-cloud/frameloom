using System;
using System.Collections.Generic;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>Caller-owned storage for borrowed output from a buffered simulation tick.</summary>
    /// <remarks>
    /// Content is invalidated by the bound session's next accepted TickInto or its next Tick,
    /// Start, RestoreSnapshot, or Dispose. An illegal alternating or cross-session TickInto is
    /// rejected before changing bound content. Clear retains the session binding; disposing the
    /// buffer or its session releases the binding so another session can use the buffer.
    /// Collection views remain stable across reuse.
    /// </remarks>
    public sealed class SimTickOutputBuffer : IDisposable
    {
        private readonly List<ISimCommand> _executedCommands;
        private readonly List<CommandExecutionResult> _commandOutcomes;
        private readonly List<SimBattleEvent> _events;
        private TickCode _code;
        private SessionLifecycle _lifecycle;
        private SimCombatPhase _phase;
        private int _tick;
        private long _revision;
        private ulong _checksum;
        private SimBattleResult _result;
        private SimBattleSession _owner;

        /// <summary>Creates a reusable output buffer with the requested fields and capacities.</summary>
        /// <param name="fields">Collections populated by TickInto.</param>
        /// <param name="initialEntityCapacity">Initial capacity for each presentation list.</param>
        /// <param name="initialEventCapacity">Initial event-list capacity.</param>
        /// <param name="initialCommandCapacity">Initial command and outcome capacities.</param>
        public SimTickOutputBuffer(SimTickOutputFields fields = SimTickOutputFields.All,
            int initialEntityCapacity = 0, int initialEventCapacity = 0,
            int initialCommandCapacity = 0)
        {
            ValidateFields(fields);
            ValidateCapacities(initialEntityCapacity, initialEventCapacity,
                initialCommandCapacity);
            Fields = fields;
            _executedCommands = new List<ISimCommand>(initialCommandCapacity);
            _commandOutcomes = new List<CommandExecutionResult>(initialCommandCapacity);
            _events = new List<SimBattleEvent>(initialEventCapacity);
            ExecutedCommands = _executedCommands.AsReadOnly();
            CommandOutcomes = _commandOutcomes.AsReadOnly();
            Events = _events.AsReadOnly();
            Presentation = new SimPresentationOutputBuffer(initialEntityCapacity);
            Clear();
        }

        /// <summary>Gets the selected output fields.</summary>
        public SimTickOutputFields Fields { get; }
        /// <summary>Gets the tick completion code.</summary>
        public TickCode Code => _code;
        /// <summary>Gets the lifecycle after the tick attempt.</summary>
        public SessionLifecycle Lifecycle => _lifecycle;
        /// <summary>Gets the combat phase after the tick attempt.</summary>
        public SimCombatPhase Phase => _phase;
        /// <summary>Gets the completed tick, or -1 before an output is published.</summary>
        public int Tick => _tick;
        /// <summary>Gets the timeline revision.</summary>
        public long Revision => _revision;
        /// <summary>Gets the deterministic checksum.</summary>
        public ulong Checksum => _checksum;
        /// <summary>Gets the terminal result when the session has ended.</summary>
        public SimBattleResult Result => _result;
        /// <summary>Gets the borrowed accepted-command view.</summary>
        public IReadOnlyList<ISimCommand> ExecutedCommands { get; }
        /// <summary>Gets the borrowed command-outcome view.</summary>
        public IReadOnlyList<CommandExecutionResult> CommandOutcomes { get; }
        /// <summary>Gets the borrowed simulation-event view.</summary>
        public IReadOnlyList<SimBattleEvent> Events { get; }
        /// <summary>Gets reusable borrowed presentation output.</summary>
        public SimPresentationOutputBuffer Presentation { get; }

        /// <summary>Raises capacities without changing content, binding, or session state.</summary>
        /// <remarks>This method may allocate when a requested capacity exceeds current storage.</remarks>
        /// <param name="entityCapacity">Minimum capacity for each presentation list.</param>
        /// <param name="eventCapacity">Minimum event-list capacity.</param>
        /// <param name="commandCapacity">Minimum command and outcome capacities.</param>
        public void EnsureCapacity(int entityCapacity, int eventCapacity, int commandCapacity)
        {
            ValidateCapacities(entityCapacity, eventCapacity, commandCapacity);
            Presentation.EnsureCapacity(entityCapacity);
            if (_events.Capacity < eventCapacity) _events.Capacity = eventCapacity;
            if (_executedCommands.Capacity < commandCapacity)
                _executedCommands.Capacity = commandCapacity;
            if (_commandOutcomes.Capacity < commandCapacity)
                _commandOutcomes.Capacity = commandCapacity;
        }

        /// <summary>Clears borrowed content while retaining capacity and session binding.</summary>
        public void Clear()
        {
            _executedCommands.Clear();
            _commandOutcomes.Clear();
            _events.Clear();
            Presentation.Clear();
            _code = default;
            _lifecycle = default;
            _phase = default;
            _tick = -1;
            _revision = 0;
            _checksum = 0;
            _result = null;
        }

        /// <summary>Clears content and releases the session binding for reuse elsewhere.</summary>
        public void Dispose()
        {
            Clear();
            SimBattleSession owner = _owner;
            _owner = null;
            owner?.DetachOutputBuffer(this);
        }

        internal SimBattleSession Owner => _owner;

        internal void Bind(SimBattleSession owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        internal void Release(SimBattleSession owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            Clear();
            _owner = null;
        }

        internal bool Includes(SimTickOutputFields field) => (Fields & field) != 0;

        internal void CopyCollections(IReadOnlyList<ISimCommand> executedCommands,
            IReadOnlyList<CommandExecutionResult> commandOutcomes,
            IReadOnlyList<SimBattleEvent> events)
        {
            if ((Fields & SimTickOutputFields.ExecutedCommands) != 0)
                Copy(executedCommands, _executedCommands);
            if ((Fields & SimTickOutputFields.CommandOutcomes) != 0)
                Copy(commandOutcomes, _commandOutcomes);
            if ((Fields & SimTickOutputFields.Events) != 0)
                Copy(events, _events);
        }

        internal void Publish(TickCode code, SessionLifecycle lifecycle,
            SimCombatPhase phase, int tick, long revision, ulong checksum,
            SimBattleResult result)
        {
            _code = code;
            _lifecycle = lifecycle;
            _phase = phase;
            _tick = tick;
            _revision = revision;
            _checksum = checksum;
            _result = result;
        }

        private static void Copy<T>(IReadOnlyList<T> source, List<T> destination)
        {
            for (int index = 0; index < source.Count; index++)
                destination.Add(source[index]);
        }

        private static void ValidateFields(SimTickOutputFields fields)
        {
            if ((fields & ~SimTickOutputFields.All) != 0)
                throw new ArgumentOutOfRangeException(nameof(fields));
        }

        private static void ValidateCapacities(
            int entityCapacity, int eventCapacity, int commandCapacity)
        {
            if (entityCapacity < 0) throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            if (eventCapacity < 0) throw new ArgumentOutOfRangeException(nameof(eventCapacity));
            if (commandCapacity < 0) throw new ArgumentOutOfRangeException(nameof(commandCapacity));
        }
    }
}
