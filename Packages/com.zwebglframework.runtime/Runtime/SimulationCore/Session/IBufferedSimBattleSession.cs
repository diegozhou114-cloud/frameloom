using System;

namespace ZWebGLFrameWork.SimulationCore
{
    /// <summary>Selects which borrowed collections a buffered tick populates.</summary>
    [Flags]
    public enum SimTickOutputFields
    {
        /// <summary>Publishes only scalar tick metadata.</summary>
        None = 0,
        /// <summary>Publishes accepted commands executed by the tick.</summary>
        ExecutedCommands = 1,
        /// <summary>Publishes command execution outcomes.</summary>
        CommandOutcomes = 2,
        /// <summary>Publishes simulation events emitted by the tick.</summary>
        Events = 4,
        /// <summary>Publishes full and delta presentation data.</summary>
        Presentation = 8,
        /// <summary>Publishes every buffered output field.</summary>
        All = ExecutedCommands | CommandOutcomes | Events | Presentation,
    }

    /// <summary>Provides a reusable borrowed-output simulation tick.</summary>
    /// <remarks>
    /// Output remains valid only until the bound session next accepts TickInto or calls Tick,
    /// Start, RestoreSnapshot, or Dispose. An illegal alternating or cross-session TickInto is
    /// rejected before changing bound output. Reading session State after TickInto materializes an
    /// owning snapshot and may allocate. A destination stays bound until either it or its session
    /// is disposed.
    /// </remarks>
    public interface IBufferedSimBattleSession
    {
        /// <summary>Advances once and writes selected output into <paramref name="destination"/>.</summary>
        /// <param name="destination">
        /// Reusable output owned by the caller. Passing null clears the currently bound output
        /// before throwing.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="destination"/> is null.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The session is already bound to another output buffer, or the destination is bound to
        /// another session.
        /// </exception>
        void TickInto(SimTickOutputBuffer destination);
    }
}
