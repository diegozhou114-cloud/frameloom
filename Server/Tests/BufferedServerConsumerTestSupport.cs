using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    internal static class BufferedServerConsumerTestSupport
    {
        internal const string Submitter = "player-0";

        internal static byte[] ManifestBytes() =>
            new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 20)
            }, new[]
            {
                new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                    new[] { 0, 100, 200, 300, 400 })
            }).ToCanonicalBytes();

        internal static BattleInit Init(byte[] manifest, string matchId,
            string sessionId, string player0 = Submitter, string player1 = "player-1")
        {
            byte[] hash = DeterministicContentManifest.Parse(manifest).ContentHash;
            return new BattleInit(1, SimComponentSchema.Version, matchId, sessionId, 42,
                ReplayFormat.TickRate, SimCombatMode.VillageAttack, hash,
                new[] { new SimPlayerIdentity(0, player0), new SimPlayerIdentity(1, player1) },
                new[]
                {
                    new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
                }, 6);
        }

        internal static (ReplayFile Replay, AuthoritativeMatchRecord Match) AuthorityCase()
        {
            byte[] manifest = ManifestBytes();
            BattleInit init = Init(manifest, "match-1", "session-1");
            ReplayFile replay = Record(manifest, init, out SimSnapshot snapshot,
                out TickResult terminal);
            replay.SimulationSchemaHash = new string('1', 64);
            replay.ContentConfigHash = HashText.ToLowerHex(init.ContentHash);
            replay.InitialStateHash = HashText.ToLowerHex(snapshot.PayloadSha256);
            replay.SubmissionNonce = "nonce-1";
            var match = new AuthoritativeMatchRecord(init.MatchId,
                replay.InitialStateHash, replay.SubmissionNonce,
                replay.SimulationSchemaHash, replay.ContentConfigHash,
                manifest, init, snapshot, terminal.Result, new[] { Submitter });
            return (replay, match);
        }

        internal static ReplayFile LegacyReplay(byte[] manifest)
        {
            BattleInit init = Init(manifest, "replay-match", "replay-session",
                "attacker", "defender");
            ReplayFile replay = Record(manifest, init, out _, out _);
            replay.BattleInit = null;
            return replay;
        }

        private static ReplayFile Record(byte[] manifest, BattleInit init,
            out SimSnapshot snapshot, out TickResult terminal)
        {
            using ISimBattleSession session =
                new SimBattleSessionFactory().Create(manifest);
            if (!session.Start(init).Accepted) throw new InvalidOperationException();
            snapshot = session.CaptureSnapshot();
            if (!session.Submit(new EndDeploymentCmd
                { Tick = 0, PlayerId = 0, Sequence = 1 }).Accepted)
                throw new InvalidOperationException();
            terminal = session.Tick();
            if (terminal.Code != TickCode.Ended) throw new InvalidOperationException();
            return new ReplayFile
            {
                FormatVersion = ReplayFormat.CurrentVersion,
                Version = 1,
                BattleInit = ReplayBattleInit.From(init),
                TickRate = init.TickRate,
                MatchId = init.MatchId,
                Seed = init.Seed,
                InitialLayout = new[]
                {
                    new SimEntitySpawnInfo(10, 7, 2, SimTeam.Attacker),
                    new SimEntitySpawnInfo(2, 8, 2, SimTeam.Defender)
                },
                FinalTick = 0,
                FinalChecksum = terminal.Checksum,
                FinalResult = terminal.Result,
                Records = new List<ReplayTickRecord>
                {
                    new ReplayTickRecord
                    {
                        Tick = 0,
                        Checksum = terminal.Checksum,
                        Commands = new List<SerializableCommand>
                        {
                            new SerializableCommand
                            {
                                Type = SimCommandType.EndDeployment.ToString(),
                                Tick = 0, PlayerId = 0, Sequence = 1
                            }
                        }
                    }
                }
            };
        }
    }

    internal class OwningSessionSpy : ISimBattleSession
    {
        protected readonly ISimBattleSession Inner;
        public int TickCalls { get; protected set; }
        public int StateCalls { get; protected set; }
        public int DisposeCalls { get; private set; }
        internal OwningSessionSpy(ISimBattleSession inner) => Inner = inner;
        public virtual SimBattleState State
        {
            get { StateCalls++; return Inner.State; }
        }
        public StartResult Start(BattleInit init) => Inner.Start(init);
        public CommandSubmitResult Submit(ISimCommand command) => Inner.Submit(command);
        public virtual TickResult Tick() { TickCalls++; return Inner.Tick(); }
        public SimSnapshot CaptureSnapshot() => Inner.CaptureSnapshot();
        public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) =>
            Inner.RestoreSnapshot(snapshot);
        public void Dispose() { DisposeCalls++; Inner.Dispose(); }
    }

    internal sealed class BufferedSessionSpy : OwningSessionSpy, IBufferedSimBattleSession
    {
        private bool _dirty;
        public int TickIntoCalls { get; private set; }
        public SimTickOutputFields LastFields { get; private set; }
        internal BufferedSessionSpy(ISimBattleSession inner) : base(inner) { }
        public override SimBattleState State
        {
            get
            {
                StateCalls++;
                if (_dirty) throw new InvalidOperationException("Dirty State was read.");
                return Inner.State;
            }
        }
        public override TickResult Tick()
        {
            TickCalls++;
            throw new InvalidOperationException("Owning Tick was used.");
        }
        public void TickInto(SimTickOutputBuffer destination)
        {
            TickIntoCalls++;
            LastFields = destination.Fields;
            ((IBufferedSimBattleSession)Inner).TickInto(destination);
            _dirty = true;
        }
    }

    internal sealed class BufferedSpyFactory : ISimBattleSessionFactory
    {
        public BufferedSessionSpy Last { get; private set; }
        public ISimBattleSession Create(byte[] bytes, ISimLogger logger = null)
        {
            Last = new BufferedSessionSpy(
                new SimBattleSessionFactory().Create(bytes, logger));
            return Last;
        }
    }
}
