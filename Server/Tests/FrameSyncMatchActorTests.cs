using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.FrameSync;
using ZWebGLFrameWork.Network.Contracts.Protocol;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class FrameSyncMatchActorTests
    {
        [Test]
        public void MissingInput_IsNotSealedBeforeDeadline_ThenGetsNoOp()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1)),
                Is.EqualTo(InputAcceptance.Accepted));

            fixture.Clock.Set(299);
            Assert.That(fixture.Actor.Pump(), Is.Zero);
            Assert.That(fixture.Session.TickCalls, Is.Zero);

            fixture.Clock.Set(300);
            Assert.That(fixture.Actor.Pump(), Is.EqualTo(1));
            AuthorityFrame frame = fixture.Broadcaster.Frames.Single();
            Assert.That(frame.OrderedInputs.Select(input => input.PlayerId),
                Is.EqualTo(new[] { 0, 1 }));
            Assert.That(frame.OrderedInputs[0].IsNoOp, Is.False);
            Assert.That(frame.OrderedInputs[1].IsNoOp, Is.True);
            Assert.That(fixture.SealedTicks, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void FrameSyncServer_MissingInputAfterDeadline_InsertsNoOp()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1)),
                Is.EqualTo(InputAcceptance.Accepted));

            fixture.Clock.Set(299);
            Assert.That(fixture.Actor.Pump(), Is.Zero);
            Assert.That(fixture.Session.TickCalls, Is.Zero);

            fixture.Clock.Set(300);
            Assert.That(fixture.Actor.Pump(), Is.EqualTo(1));
            AuthorityFrame frame = fixture.Broadcaster.Frames.Single();
            Assert.That(frame.Tick, Is.Zero);
            Assert.That(frame.OrderedInputs, Has.Length.EqualTo(2));
            Assert.That(frame.OrderedInputs[0].IsNoOp, Is.False);
            Assert.That(frame.OrderedInputs[1].IsNoOp, Is.True);
            Assert.That(fixture.SealedTicks, Is.EqualTo(new[] { 0 }));
        }

        [Test]
        public void BothInputs_AdvanceImmediatelyWithoutWaitingForDeadline()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();
            fixture.Actor.SubmitInput(1, fixture.Input(0, 1, 8));
            fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 3));

            Assert.That(fixture.Actor.Pump(), Is.EqualTo(1));
            AuthorityFrame frame = fixture.Broadcaster.Frames.Single();
            Assert.That(frame.OrderedInputs.Select(input => input.PlayerId),
                Is.EqualTo(new[] { 0, 1 }));
            Assert.That(frame.AckPlayer0, Is.EqualTo(3));
            Assert.That(frame.AckPlayer1, Is.EqualTo(8));
        }

        [Test]
        public void Inbox_RejectsWrongLateFutureDuplicateAndReorderedInputs()
        {
            MatchActorFixture fixture = MatchActorFixture.Create(maxFutureTicks: 2);
            Assert.That(fixture.Actor.SubmitInput(1, fixture.Input(0, 0, 1)),
                Is.EqualTo(InputAcceptance.WrongPlayer));
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(3, 0, 1)),
                Is.EqualTo(InputAcceptance.TooFarAhead));
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 2)),
                Is.EqualTo(InputAcceptance.Accepted));
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 3)),
                Is.EqualTo(InputAcceptance.DuplicateInput));
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(1, 0, 2)),
                Is.EqualTo(InputAcceptance.DuplicateOrReorderedSequence));

            fixture.Clock.Set(300);
            fixture.Actor.Pump();
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 4)),
                Is.EqualTo(InputAcceptance.LateRejected));
            Assert.That(fixture.Broadcaster.Frames, Has.Count.EqualTo(1));
        }

        [Test]
        public void Pump_CatchUpIsBoundedToFourTicks()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();
            fixture.Clock.Set(10000);

            Assert.That(fixture.Actor.Pump(), Is.EqualTo(4));
            Assert.That(fixture.Session.TickCalls, Is.EqualTo(4));
            Assert.That(fixture.Broadcaster.Frames, Has.Count.EqualTo(4));
            Assert.That(fixture.Actor.Pump(), Is.EqualTo(4));
        }

        [Test]
        public void TerminalTick_IsBroadcastOnceAndStopsFurtherPumps()
        {
            MatchActorFixture fixture = MatchActorFixture.Create(endAtTick: 0);
            fixture.Clock.Set(300);

            Assert.That(fixture.Actor.Pump(), Is.EqualTo(1));
            Assert.That(fixture.Actor.IsEnded, Is.True);
            Assert.That(fixture.Actor.Pump(), Is.Zero);
            Assert.That(fixture.Broadcaster.Frames, Has.Count.EqualTo(1));
        }

        [Test]
        public void Inbox_RejectsMalformedAndOverloadedInput_AndCopiesPayload()
        {
            MatchActorFixture fixture = MatchActorFixture.Create(maxMailboxEntries: 1);
            CommandEnvelope input = fixture.Input(0, 0, 1);
            Assert.That(fixture.Actor.SubmitInput(0, null),
                Is.EqualTo(InputAcceptance.WrongPlayer));
            input.MatchId = "other";
            Assert.That(fixture.Actor.SubmitInput(0, input),
                Is.EqualTo(InputAcceptance.WrongMatch));
            input = fixture.Input(0, 0, 1);
            input.Version++;
            Assert.That(fixture.Actor.SubmitInput(0, input),
                Is.EqualTo(InputAcceptance.UnsupportedVersion));
            input = fixture.Input(0, 0, 1);
            input.MessageId = 0;
            Assert.That(fixture.Actor.SubmitInput(0, input),
                Is.EqualTo(InputAcceptance.InvalidInput));
            input = fixture.Input(0, 0, 1);
            input.Payload = Array.Empty<byte>();
            Assert.That(fixture.Actor.SubmitInput(0, input),
                Is.EqualTo(InputAcceptance.InvalidInput));

            input = fixture.Input(0, 0, 1);
            Assert.That(fixture.Actor.SubmitInput(0, input),
                Is.EqualTo(InputAcceptance.Accepted));
            input.Payload[0] = 99;
            Assert.That(fixture.Actor.SubmitInput(1, fixture.Input(0, 1, 1)),
                Is.EqualTo(InputAcceptance.MailboxOverloaded));
            fixture.Clock.Set(300);
            fixture.Actor.Pump();
            Assert.That(fixture.Broadcaster.Frames.Single()
                .OrderedInputs[0].Payload[0], Is.EqualTo(1));
        }

        [Test]
        public void ProcessingFailure_FaultsActorAndCannotRewriteTickAsNoOps()
        {
            MatchActorFixture fixture = MatchActorFixture.Create(rejectSubmit: true);
            fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1));
            fixture.Actor.SubmitInput(1, fixture.Input(0, 1, 1));

            Assert.Throws<InvalidOperationException>(() => fixture.Actor.Pump());
            Assert.That(fixture.Actor.IsFaulted, Is.True);
            Assert.That(fixture.Actor.Pump(), Is.Zero);
            Assert.That(fixture.Broadcaster.Frames, Is.Empty);
            Assert.That(fixture.SealedTicks, Is.Empty);
        }

        [Test]
        public void Dispose_IsIdempotentAndStopsInputAndPumps()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();

            fixture.Actor.Dispose();
            fixture.Actor.Dispose();

            Assert.That(fixture.Session.DisposeCalls, Is.EqualTo(1));
            Assert.That(fixture.Actor.Pump(), Is.Zero);
            Assert.That(fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1)),
                Is.EqualTo(InputAcceptance.LateRejected));
        }

        [Test]
        public async Task ConcurrentIngress_CannotRaceTickSealing()
        {
            using var mapper = new BlockingMapper();
            MatchActorFixture fixture = MatchActorFixture.Create(mapper: mapper);
            fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1));
            fixture.Clock.Set(300);

            Task<int> pump = Task.Run(fixture.Actor.Pump);
            Assert.That(mapper.WaitUntilEntered(TimeSpan.FromSeconds(5)), Is.True);
            Task<InputAcceptance> concurrentSubmit = Task.Run(() =>
                fixture.Actor.SubmitInput(1, fixture.Input(0, 1, 1)));

            try
            {
                Assert.That(concurrentSubmit.Wait(TimeSpan.FromMilliseconds(100)), Is.False);
            }
            finally
            {
                mapper.Release();
            }

            Assert.That(await pump, Is.EqualTo(1));
            Assert.That(await concurrentSubmit, Is.EqualTo(InputAcceptance.LateRejected));
            Assert.That(fixture.Broadcaster.Frames.Single().OrderedInputs[1].IsNoOp,
                Is.True);
        }

        [Test]
        public void Inbox_FutureWindowComparisonDoesNotOverflow()
        {
            var inbox = new InputInbox("match-1", 64, 2);
            CommandEnvelope input = MatchActorFixture.Create().Input(
                int.MaxValue, 0, 1);

            Assert.That(inbox.Submit(0, input, int.MaxValue),
                Is.EqualTo(InputAcceptance.Accepted));
        }

        [Test]
        public void Inbox_RejectsSequenceOutsideSimulationRange()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();

            Assert.That(fixture.Actor.SubmitInput(0,
                fixture.Input(0, 0, (uint)int.MaxValue + 1)),
                Is.EqualTo(InputAcceptance.InvalidInput));
        }

        [Test]
        public void Inbox_RejectsTerminalSimulationSequenceValue()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();

            Assert.That(fixture.Actor.SubmitInput(0,
                fixture.Input(0, 0, int.MaxValue)),
                Is.EqualTo(InputAcceptance.InvalidInput));
        }

        [Test]
        public void MapperMetadataMismatch_FaultsBeforeSimulationOrBroadcast()
        {
            MatchActorFixture fixture = MatchActorFixture.Create(
                mapper: new MetadataMismatchMapper());
            fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1));
            fixture.Actor.SubmitInput(1, fixture.Input(0, 1, 1));

            Assert.Throws<InvalidOperationException>(() => fixture.Actor.Pump());
            Assert.That(fixture.Actor.IsFaulted, Is.True);
            Assert.That(fixture.Session.SubmittedCommands, Is.Empty);
            Assert.That(fixture.Broadcaster.Frames, Is.Empty);
        }

        [Test]
        public void BroadcastReentrancy_CannotRewriteJustSealedTick()
        {
            MatchActorFixture fixture = MatchActorFixture.Create();
            InputAcceptance reentrantResult = InputAcceptance.Accepted;
            fixture.Broadcaster.OnBroadcast = _ => reentrantResult =
                fixture.Actor.SubmitInput(0, fixture.Input(0, 0, 1));
            fixture.Clock.Set(300);

            fixture.Actor.Pump();

            Assert.That(reentrantResult, Is.EqualTo(InputAcceptance.LateRejected));
            Assert.That(fixture.Actor.NextTick, Is.EqualTo(1));
        }

        [Test]
        public void BroadcastReentrancy_DisposeOrPumpCannotAdvanceAnotherTick()
        {
            MatchActorFixture disposed = MatchActorFixture.Create();
            disposed.Broadcaster.OnBroadcast = _ => disposed.Actor.Dispose();
            disposed.Clock.Set(10000);

            Assert.That(disposed.Actor.Pump(), Is.EqualTo(1));
            Assert.That(disposed.Session.TickCalls, Is.EqualTo(1));
            Assert.That(disposed.Session.DisposeCalls, Is.EqualTo(1));

            MatchActorFixture reentrant = MatchActorFixture.Create();
            int reentrantPumpResult = -1;
            reentrant.Broadcaster.OnBroadcast = _ =>
                reentrantPumpResult = reentrant.Actor.Pump();
            reentrant.Clock.Set(10000);

            Assert.That(reentrant.Actor.Pump(), Is.EqualTo(4));
            Assert.That(reentrantPumpResult, Is.Zero);
            Assert.That(reentrant.Session.TickCalls, Is.EqualTo(4));
        }

        [TestCase(0)]
        [TestCase(30)]
        [TestCase(333)]
        public void Construction_RejectsInvalidTickRates(int tickRate)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MatchActorFixture.Create(tickRate: tickRate));
        }

        private sealed class MatchActorFixture
        {
            public ManualClock Clock { get; }
            public StubSession Session { get; }
            public RecordingBroadcaster Broadcaster { get; }
            public List<int> SealedTicks { get; }
            public FrameSyncMatchActor Actor { get; }

            private MatchActorFixture(ManualClock clock, StubSession session,
                RecordingBroadcaster broadcaster, List<int> sealedTicks,
                FrameSyncMatchActor actor)
            {
                Clock = clock;
                Session = session;
                Broadcaster = broadcaster;
                SealedTicks = sealedTicks;
                Actor = actor;
            }

            public static MatchActorFixture Create(int tickRate = 20,
                int inputDeadlineTicks = 6, int maxFutureTicks = 64,
                int endAtTick = -1, int maxMailboxEntries = 256,
                bool rejectSubmit = false,
                IAuthorityCommandMapper mapper = null)
            {
                var clock = new ManualClock();
                var session = new StubSession(endAtTick, rejectSubmit);
                var broadcaster = new RecordingBroadcaster();
                var sealedTicks = new List<int>();
                var actor = new FrameSyncMatchActor(
                    "match-1", tickRate, inputDeadlineTicks,
                    session, mapper ?? new StubMapper(), sealedTicks.Add,
                    broadcaster, clock, maxFutureTicks, maxMailboxEntries);
                return new MatchActorFixture(
                    clock, session, broadcaster, sealedTicks, actor);
            }

            public CommandEnvelope Input(int tick, int playerId, uint sequence) =>
                new CommandEnvelope
                {
                    Version = RealtimeProtocol.Version,
                    MatchId = "match-1",
                    Tick = tick,
                    PlayerId = playerId,
                    Sequence = sequence,
                    MessageId = 1,
                    Payload = new byte[] { 1 }
                };
        }

        private sealed class ManualClock : IMonotonicClock
        {
            public long Milliseconds { get; private set; }
            public void Set(long value) => Milliseconds = value;
        }

        private sealed class RecordingBroadcaster : IFrameBroadcaster
        {
            public List<AuthorityFrame> Frames { get; } = new List<AuthorityFrame>();
            public Action<AuthorityFrame> OnBroadcast { get; set; }
            public void Broadcast(AuthorityFrame frame)
            {
                Frames.Add(frame);
                OnBroadcast?.Invoke(frame);
            }
        }

        private sealed class StubMapper : IAuthorityCommandMapper
        {
            public ISimCommand ToSimulation(CommandEnvelope input) =>
                new EndDeploymentCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = checked((int)input.Sequence)
                };
        }

        private sealed class BlockingMapper : IAuthorityCommandMapper, IDisposable
        {
            private readonly ManualResetEventSlim _entered = new ManualResetEventSlim();
            private readonly ManualResetEventSlim _release = new ManualResetEventSlim();

            public ISimCommand ToSimulation(CommandEnvelope input)
            {
                _entered.Set();
                _release.Wait();
                return new EndDeploymentCmd
                {
                    Tick = input.Tick,
                    PlayerId = input.PlayerId,
                    Sequence = checked((int)input.Sequence)
                };
            }

            public bool WaitUntilEntered(TimeSpan timeout) => _entered.Wait(timeout);
            public void Release() => _release.Set();
            public void Dispose()
            {
                _entered.Dispose();
                _release.Dispose();
            }
        }

        private sealed class MetadataMismatchMapper : IAuthorityCommandMapper
        {
            public ISimCommand ToSimulation(CommandEnvelope input) =>
                new EndDeploymentCmd
                {
                    Tick = input.Tick + 1,
                    PlayerId = input.PlayerId,
                    Sequence = checked((int)input.Sequence)
                };
        }

        private sealed class StubSession : ISimBattleSession
        {
            private readonly int _endAtTick;
            private readonly bool _rejectSubmit;
            private int _nextTick;
            public int TickCalls { get; private set; }
            public int DisposeCalls { get; private set; }
            public List<ISimCommand> SubmittedCommands { get; } = new List<ISimCommand>();

            public StubSession(int endAtTick, bool rejectSubmit)
            {
                _endAtTick = endAtTick;
                _rejectSubmit = rejectSubmit;
            }
            public SimBattleState State => null;
            public StartResult Start(BattleInit init) => default;
            public CommandSubmitResult Submit(ISimCommand command)
            {
                SubmittedCommands.Add(command);
                return new CommandSubmitResult(_rejectSubmit
                    ? CommandDisposition.InvalidPayload
                    : CommandDisposition.Accepted);
            }
            public TickResult Tick()
            {
                int tick = _nextTick++;
                TickCalls++;
                TickCode code = tick == _endAtTick ? TickCode.Ended : TickCode.Advanced;
                return new TickResult(code, tick, tick + 1, 0,
                    Array.Empty<ISimCommand>(), Array.Empty<CommandExecutionResult>(),
                    Array.Empty<SimBattleEvent>(), null, (ulong)(100 + tick), null);
            }
            public SimSnapshot CaptureSnapshot() => null;
            public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) => default;
            public void Dispose() => DisposeCalls++;
        }
    }
}
