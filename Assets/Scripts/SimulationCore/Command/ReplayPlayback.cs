using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public readonly struct ReplayPlaybackResult
    {
        public bool Valid { get; }
        public int FailedTick { get; }
        public ulong ExpectedChecksum { get; }
        public ulong ActualChecksum { get; }
        public int ExecutedCommandCount { get; }
        public ulong FinalChecksum { get; }

        public ReplayPlaybackResult(bool valid, int failedTick,
            ulong expectedChecksum, ulong actualChecksum,
            int executedCommandCount, ulong finalChecksum)
        {
            Valid = valid;
            FailedTick = failedTick;
            ExpectedChecksum = expectedChecksum;
            ActualChecksum = actualChecksum;
            ExecutedCommandCount = executedCommandCount;
            FinalChecksum = finalChecksum;
        }
    }

    public static class ReplayPlayback
    {
        public static ReplayPlaybackResult Run(
            ISimBattleSession session, ReplayFile replay)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (replay?.BattleInit == null || replay.Records == null ||
                replay.Records.Count == 0)
                return Invalid();

            StartResult started;
            try
            {
                started = session.Start(replay.BattleInit.ToBattleInit());
            }
            catch (ArgumentException)
            {
                return Invalid();
            }
            if (!started.Accepted) return Invalid();

            IBufferedSimBattleSession bufferedSession =
                session as IBufferedSimBattleSession;
            using SimTickOutputBuffer tickOutput = bufferedSession == null
                ? null
                : new SimTickOutputBuffer(SimTickOutputFields.ExecutedCommands);
            int executedCount = 0;
            for (int index = 0; index < replay.Records.Count; index++)
            {
                ReplayTickRecord record = replay.Records[index];
                if (record == null || record.Tick != index ||
                    record.Commands == null)
                    return Invalid(index, record?.Checksum ?? 0,
                        session.State.Checksum, executedCount);

                foreach (SerializableCommand serialized in record.Commands)
                {
                    ISimCommand command;
                    try
                    {
                        command = CommandRecorder.DeserializeCommand(serialized);
                    }
                    catch (Exception exception) when (
                        exception is ArgumentException ||
                        exception is InvalidOperationException)
                    {
                        return Invalid(index, record.Checksum,
                            session.State.Checksum, executedCount);
                    }
                    if (command == null || !session.Submit(command).Accepted)
                        return Invalid(index, record.Checksum,
                            session.State.Checksum, executedCount);
                }

                int completedTick;
                ulong actualChecksum;
                SimBattleResult tickResult;
                int tickExecutedCount;
                if (bufferedSession != null)
                {
                    bufferedSession.TickInto(tickOutput);
                    completedTick = tickOutput.Tick;
                    actualChecksum = tickOutput.Checksum;
                    tickResult = tickOutput.Result;
                    tickExecutedCount = tickOutput.ExecutedCommands.Count;
                }
                else
                {
                    TickResult tick = session.Tick();
                    if (tick == null)
                        return Invalid(index, record.Checksum, 0, executedCount);
                    completedTick = tick.CompletedTick;
                    actualChecksum = tick.Checksum;
                    tickResult = tick.Result;
                    tickExecutedCount = tick.ExecutedCommands.Count;
                }
                if (completedTick != record.Tick ||
                    actualChecksum != record.Checksum)
                    return Invalid(index, record.Checksum,
                        actualChecksum, executedCount);
                executedCount += tickExecutedCount;
                if (tickResult != null && index != replay.Records.Count - 1)
                    return Invalid(index + 1, 0, actualChecksum, executedCount);
            }

            ulong finalChecksum;
            if (bufferedSession != null)
            {
                finalChecksum = tickOutput.Checksum;
                if (replay.FinalTick != tickOutput.Tick ||
                    replay.FinalChecksum != finalChecksum ||
                    !SameResult(replay.FinalResult, tickOutput.Result))
                    return Invalid(replay.FinalTick, replay.FinalChecksum,
                        finalChecksum, executedCount);
            }
            else
            {
                finalChecksum = session.State.Checksum;
                if (replay.FinalTick != session.State.CompletedTick ||
                    replay.FinalChecksum != finalChecksum ||
                    !SameResult(replay.FinalResult, session.State.Result))
                    return Invalid(replay.FinalTick, replay.FinalChecksum,
                        finalChecksum, executedCount);
            }
            return new ReplayPlaybackResult(true, -1, finalChecksum,
                finalChecksum, executedCount, finalChecksum);
        }

        private static bool SameResult(SimBattleResult expected, SimBattleResult actual)
        {
            if (expected == null || actual == null) return expected == actual;
            return expected.MatchId == actual.MatchId &&
                expected.ResultId == actual.ResultId &&
                expected.WinnerTeamId == actual.WinnerTeamId &&
                expected.Stars == actual.Stars &&
                expected.LootGold == actual.LootGold &&
                expected.LootElixir == actual.LootElixir &&
                expected.FinalTick == actual.FinalTick &&
                expected.FinalChecksum == actual.FinalChecksum;
        }

        private static ReplayPlaybackResult Invalid(int failedTick = -1,
            ulong expectedChecksum = 0, ulong actualChecksum = 0,
            int executedCommandCount = 0) =>
            new ReplayPlaybackResult(false, failedTick, expectedChecksum,
                actualChecksum, executedCommandCount, actualChecksum);
    }
}
