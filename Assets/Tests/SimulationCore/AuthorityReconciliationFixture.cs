using System;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    internal sealed class AuthorityReconciliationFixture : IDisposable
    {
        private readonly ISimCommand[] _sharedCommands;
        private readonly ISimCommand[] _predictedCommands;
        private readonly ISimCommand[] _authorityCommands;
        private SimSnapshot _snapshot;

        public SimBattleSession Session { get; }

        private AuthorityReconciliationFixture(SimBattleSession session,
            ISimCommand[] sharedCommands, ISimCommand[] predictedCommands,
            ISimCommand[] authorityCommands)
        {
            Session = session;
            _sharedCommands = sharedCommands;
            _predictedCommands = predictedCommands;
            _authorityCommands = authorityCommands;
        }

        public static AuthorityReconciliationFixture RunPredictedToTick(int finalTick)
        {
            var shared = new ISimCommand[]
            {
                new EndDeploymentCmd { Tick = 0, PlayerId = 0, Sequence = 1 }
            };
            var predicted = new ISimCommand[]
            {
                new MoveCmd
                {
                    Tick = 12, PlayerId = 0, Sequence = 2, EntityId = 0,
                    TargetX = 6, TargetY = 2
                }
            };
            var authority = new ISimCommand[]
            {
                new MoveCmd
                {
                    Tick = 12, PlayerId = 0, Sequence = 2, EntityId = 0,
                    TargetX = 2, TargetY = 6
                }
            };
            SimBattleSession session = CreateStartedSession();
            var fixture = new AuthorityReconciliationFixture(session, shared,
                predicted, authority);
            Submit(session, shared);
            fixture.TickThrough(9, captureCompletedTick: 9);
            Submit(session, predicted);
            fixture.TickThrough(finalTick, captureCompletedTick: -1);
            return fixture;
        }

        public ulong RunAuthorityBranchFromTick(int completedTick, int finalTick)
        {
            if (_snapshot == null || _snapshot.CompletedTick != completedTick)
                throw new InvalidOperationException("Authority snapshot is unavailable.");

            using (SimBattleSession authority = CreateStartedSession())
            {
                Submit(authority, _sharedCommands);
                TickThrough(authority, completedTick);
                Submit(authority, _authorityCommands);
                TickThrough(authority, finalTick);
                return authority.State.Checksum;
            }
        }

        public void RestoreAuthorityAtTick(int completedTick)
        {
            if (_snapshot == null || _snapshot.CompletedTick != completedTick)
                throw new InvalidOperationException("Authority snapshot is unavailable.");
            SnapshotRestoreResult restored = Session.RestoreSnapshot(_snapshot);
            if (!restored.Restored)
                throw new InvalidOperationException(restored.Code.ToString());
        }

        public ulong ReplayAuthorityCommandsToTick(int finalTick)
        {
            Submit(Session, _authorityCommands);
            TickThrough(Session, finalTick);
            return Session.State.Checksum;
        }

        public void Dispose() => Session?.Dispose();

        private void TickThrough(int finalTick, int captureCompletedTick) =>
            TickThrough(Session, finalTick, captureCompletedTick);

        private void TickThrough(SimBattleSession session, int finalTick,
            int captureCompletedTick = -1)
        {
            if (finalTick < 0) throw new ArgumentOutOfRangeException(nameof(finalTick));
            while (session.State.CompletedTick < finalTick)
            {
                TickResult tick = session.Tick();
                if (tick.Code != TickCode.Advanced && tick.Code != TickCode.Ended)
                    throw new InvalidOperationException(tick.Code.ToString());
                if (session.State.CompletedTick == captureCompletedTick)
                    _snapshot = session.CaptureSnapshot();
                if (tick.Code == TickCode.Ended && session.State.CompletedTick < finalTick)
                    throw new InvalidOperationException("Session ended before the requested tick.");
            }
        }

        private static SimBattleSession CreateStartedSession()
        {
            var session = (SimBattleSession)new SimBattleSessionFactory().Create(
                SessionTestDriver.ManifestBytes());
            StartResult started = session.Start(SessionTestDriver.BattleInit(42));
            if (!started.Accepted) throw new InvalidOperationException(started.Code.ToString());
            return session;
        }

        private static void Submit(SimBattleSession session,
            System.Collections.Generic.IEnumerable<ISimCommand> commands)
        {
            foreach (ISimCommand command in commands)
            {
                CommandSubmitResult submitted = session.Submit(command);
                if (!submitted.Accepted)
                    throw new InvalidOperationException(submitted.Disposition.ToString());
            }
        }
    }
}
