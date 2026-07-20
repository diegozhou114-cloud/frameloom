namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimBattleState
    {
        public SessionLifecycle Lifecycle { get; }
        public SimCombatPhase Phase { get; }
        public int CompletedTick { get; }
        public int NextTick { get; }
        public long TimelineRevision { get; }
        public ulong Checksum { get; }
        public PresentationFrame Presentation { get; }
        public SimBattleResult Result { get; }

        public SimBattleState(SessionLifecycle lifecycle, SimCombatPhase phase, int completedTick,
            int nextTick, long timelineRevision, ulong checksum,
            PresentationFrame presentation, SimBattleResult result)
        {
            Lifecycle = lifecycle;
            Phase = phase;
            CompletedTick = completedTick;
            NextTick = nextTick;
            TimelineRevision = timelineRevision;
            Checksum = checksum;
            Presentation = presentation;
            Result = result;
        }
    }

    public enum SimBattleEventType : byte
    {
        None,
        EntityCreated,
        DamageApplied,
        EntityDestroyed,
    }

    public readonly struct SimBattleEvent
    {
        public SimBattleEventType Type { get; }
        public EntityPresentationKey SourceEntity { get; }
        public EntityPresentationKey TargetEntity { get; }
        public int Amount { get; }

        public SimBattleEvent(SimBattleEventType type, EntityPresentationKey sourceEntity,
            EntityPresentationKey targetEntity, int amount)
        {
            Type = type;
            SourceEntity = sourceEntity;
            TargetEntity = targetEntity;
            Amount = amount;
        }
    }

    public sealed class TickResult
    {
        public TickCode Code { get; }
        public int CompletedTick { get; }
        public int NextTick { get; }
        public long TimelineRevision { get; }
        public System.Collections.Generic.IReadOnlyList<ISimCommand> ExecutedCommands { get; }
        public System.Collections.Generic.IReadOnlyList<CommandExecutionResult> CommandOutcomes { get; }
        public System.Collections.Generic.IReadOnlyList<SimBattleEvent> Events { get; }
        public PresentationFrame Presentation { get; }
        public ulong Checksum { get; }
        public SimBattleResult Result { get; }

        public TickResult(TickCode code, int completedTick, int nextTick, long timelineRevision,
            ISimCommand[] executedCommands, CommandExecutionResult[] commandOutcomes,
            SimBattleEvent[] events, PresentationFrame presentation, ulong checksum,
            SimBattleResult result)
        {
            Code = code;
            CompletedTick = completedTick;
            NextTick = nextTick;
            TimelineRevision = timelineRevision;
            ExecutedCommands = System.Array.AsReadOnly((ISimCommand[])executedCommands.Clone());
            CommandOutcomes = System.Array.AsReadOnly((CommandExecutionResult[])commandOutcomes.Clone());
            Events = System.Array.AsReadOnly((SimBattleEvent[])events.Clone());
            Presentation = presentation;
            Checksum = checksum;
            Result = result;
        }
    }

    public sealed class SimBattleResult
    {
        public string MatchId { get; }
        public string ResultId { get; }
        public int WinnerTeamId { get; }
        public int Stars { get; }
        public int LootGold { get; }
        public int LootElixir { get; }
        public int FinalTick { get; }
        public ulong FinalChecksum { get; }

        public SimBattleResult(string matchId, string resultId, int winnerTeamId, int stars,
            int lootGold, int lootElixir, int finalTick, ulong finalChecksum)
        {
            MatchId = matchId ?? throw new System.ArgumentNullException(nameof(matchId));
            ResultId = resultId ?? throw new System.ArgumentNullException(nameof(resultId));
            WinnerTeamId = winnerTeamId;
            Stars = stars;
            LootGold = lootGold;
            LootElixir = lootElixir;
            FinalTick = finalTick;
            FinalChecksum = finalChecksum;
        }

        public static SimBattleResult FromOutcome(string matchId, SimTerminalOutcome outcome,
            int finalTick, ulong finalChecksum)
        {
            if (matchId == null) throw new System.ArgumentNullException(nameof(matchId));
            if (matchId.Length == 0) throw new System.ArgumentException("Match ID cannot be empty.", nameof(matchId));
            if (outcome == null) throw new System.ArgumentNullException(nameof(outcome));
            if (finalTick < 0) throw new System.ArgumentOutOfRangeException(nameof(finalTick));

            string resultId = matchId + ":"
                + finalTick.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                + finalChecksum.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
            return new SimBattleResult(matchId, resultId, outcome.WinnerTeamId, outcome.Stars,
                outcome.LootGold, outcome.LootElixir, finalTick, finalChecksum);
        }
    }
}
