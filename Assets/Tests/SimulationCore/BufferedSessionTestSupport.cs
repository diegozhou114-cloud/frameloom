using System.Collections.Generic;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    internal static class BufferedSessionTestSupport
    {
        internal static IBufferedSimBattleSession Buffered(ISimBattleSession session) =>
            (IBufferedSimBattleSession)session;

        internal static void AssertMatches(ISimBattleSession owningSession, TickResult owning,
            SimTickOutputBuffer buffered)
        {
            Assert.That(buffered.Code, Is.EqualTo(owning.Code));
            Assert.That(buffered.Lifecycle, Is.EqualTo(owningSession.State.Lifecycle));
            Assert.That(buffered.Phase, Is.EqualTo(owningSession.State.Phase));
            Assert.That(buffered.Tick, Is.EqualTo(owning.CompletedTick));
            Assert.That(buffered.Revision, Is.EqualTo(owning.TimelineRevision));
            Assert.That(buffered.Checksum, Is.EqualTo(owning.Checksum));
            AssertCommands(owning.ExecutedCommands, buffered.ExecutedCommands);
            AssertOutcomes(owning.CommandOutcomes, buffered.CommandOutcomes);
            AssertEvents(owning.Events, buffered.Events);
            AssertStates(owningSession.State.Presentation.Entities,
                buffered.Presentation.Entities);
            AssertStates(owning.Presentation.Created, buffered.Presentation.Created);
            AssertStates(owning.Presentation.Updated, buffered.Presentation.Updated);
            AssertKeys(owning.Presentation.Destroyed, buffered.Presentation.Destroyed);
            AssertResults(owning.Result, buffered.Result);
        }

        internal static InitialEntitySpec[] MultiChunkEntities()
        {
            var entities = new InitialEntitySpec[130];
            for (int index = 0; index < 129; index++)
                entities[index] = new InitialEntitySpec(
                    1, index, 20, SimTeam.Attacker, 0);
            entities[129] = new InitialEntitySpec(2, 300, 20, SimTeam.Defender, 1);
            return entities;
        }

        internal static void SubmitBoth(ISimBattleSession first, ISimBattleSession second,
            ISimCommand firstCommand, ISimCommand secondCommand)
        {
            Assert.That(first.Submit(firstCommand).Accepted, Is.True);
            Assert.That(second.Submit(secondCommand).Accepted, Is.True);
        }

        private static void AssertCommands(IReadOnlyList<ISimCommand> expected,
            IReadOnlyList<ISimCommand> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].GetType(), Is.EqualTo(expected[index].GetType()));
                Assert.That(actual[index].Type, Is.EqualTo(expected[index].Type));
                Assert.That(actual[index].Tick, Is.EqualTo(expected[index].Tick));
                Assert.That(actual[index].PlayerId, Is.EqualTo(expected[index].PlayerId));
                Assert.That(actual[index].Sequence, Is.EqualTo(expected[index].Sequence));
            }
        }

        private static void AssertOutcomes(IReadOnlyList<CommandExecutionResult> expected,
            IReadOnlyList<CommandExecutionResult> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].Disposition, Is.EqualTo(expected[index].Disposition));
                Assert.That(actual[index].EntityId, Is.EqualTo(expected[index].EntityId));
                Assert.That(actual[index].EntityGeneration,
                    Is.EqualTo(expected[index].EntityGeneration));
                Assert.That(actual[index].Command.Type, Is.EqualTo(expected[index].Command.Type));
            }
        }

        private static void AssertEvents(IReadOnlyList<SimBattleEvent> expected,
            IReadOnlyList<SimBattleEvent> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                Assert.That(actual[index].Type, Is.EqualTo(expected[index].Type));
                Assert.That(actual[index].SourceEntity, Is.EqualTo(expected[index].SourceEntity));
                Assert.That(actual[index].TargetEntity, Is.EqualTo(expected[index].TargetEntity));
                Assert.That(actual[index].Amount, Is.EqualTo(expected[index].Amount));
            }
        }

        private static void AssertStates(IReadOnlyList<EntityPresentationState> expected,
            IReadOnlyList<EntityPresentationState> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
            {
                EntityPresentationState left = expected[index];
                EntityPresentationState right = actual[index];
                Assert.That(right.EntityId, Is.EqualTo(left.EntityId));
                Assert.That(right.Generation, Is.EqualTo(left.Generation));
                Assert.That(right.ArchetypeId, Is.EqualTo(left.ArchetypeId));
                Assert.That(right.OwnerPlayerId, Is.EqualTo(left.OwnerPlayerId));
                Assert.That(right.TeamId, Is.EqualTo(left.TeamId));
                Assert.That(right.GridX, Is.EqualTo(left.GridX));
                Assert.That(right.GridY, Is.EqualTo(left.GridY));
                Assert.That(right.PosXRaw, Is.EqualTo(left.PosXRaw));
                Assert.That(right.PosYRaw, Is.EqualTo(left.PosYRaw));
                Assert.That(right.Health, Is.EqualTo(left.Health));
            }
        }

        private static void AssertKeys(IReadOnlyList<EntityPresentationKey> expected,
            IReadOnlyList<EntityPresentationKey> actual)
        {
            Assert.That(actual, Has.Count.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++)
                Assert.That(actual[index], Is.EqualTo(expected[index]));
        }

        private static void AssertResults(SimBattleResult expected, SimBattleResult actual)
        {
            if (expected == null)
            {
                Assert.That(actual, Is.Null);
                return;
            }
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.ResultId, Is.EqualTo(expected.ResultId));
            Assert.That(actual.WinnerTeamId, Is.EqualTo(expected.WinnerTeamId));
            Assert.That(actual.FinalTick, Is.EqualTo(expected.FinalTick));
            Assert.That(actual.FinalChecksum, Is.EqualTo(expected.FinalChecksum));
        }
    }
}
