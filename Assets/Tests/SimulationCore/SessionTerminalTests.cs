using System;
using System.Linq;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionTerminalTests
    {
        [Test]
        public void EndDeployment_RoundTripsRecorderAndTransitionsManualSession()
        {
            var command = new EndDeploymentCmd { Tick = 0, PlayerId = 0, Sequence = 7 };
            var buffer = new CommandBuffer();
            buffer.AddCommand(command);
            var recorder = new CommandRecorder(42, Array.Empty<SimEntitySpawnInfo>(), buffer);
            recorder.Record(0, 123UL);

            SerializableCommand serialized = recorder.Export().Records.Single().Commands.Single();
            Assert.That(serialized.Type, Is.EqualTo(nameof(SimCommandType.EndDeployment)));
            Assert.That(CommandRecorder.DeserializeCommand(serialized), Is.TypeOf<EndDeploymentCmd>());

            using SimBattleSession session = SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(command).Disposition, Is.EqualTo(CommandDisposition.Accepted));
            SimSnapshot pending = session.CaptureSnapshot();
            TickResult tick = session.Tick();
            Assert.That(tick.ExecutedCommands.Single(), Is.TypeOf<EndDeploymentCmd>());
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Battling));
            Assert.That(session.RestoreSnapshot(pending).Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.Tick().ExecutedCommands.Single(), Is.TypeOf<EndDeploymentCmd>());
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 8,
            }).Disposition, Is.EqualTo(CommandDisposition.WrongPhase));
        }

        [Test]
        public void ManualDeployment_ZeroAttackersRemainOpenAndDeployLimitIsEnforced()
        {
            using SimBattleSession session = SessionTestDriver.StartedDeployingBattle();
            TickResult first = session.Tick();
            Assert.That(first.Result, Is.Null);
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));

            for (int index = 0; index < 40; index++)
            {
                Assert.That(session.Submit(new DeployCmd
                {
                    Tick = 1,
                    PlayerId = 0,
                    Sequence = index + 1,
                    UnitTypeId = 10,
                    GridX = index + 20,
                    GridY = 3,
                }).Disposition, Is.EqualTo(CommandDisposition.Accepted));
            }
            Assert.That(session.Tick().ExecutedCommands.Count, Is.EqualTo(40));

            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 2, PlayerId = 0, Sequence = 41,
                UnitTypeId = 10, GridX = 100, GridY = 3,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
        }

        [Test]
        public void OneHitBattle_ProducesCanonicalTerminalResultAndSnapshotRestoresIt()
        {
            using SimBattleSession session = SessionTestDriver.StartedOneHitBattle();
            TickResult terminal = null;
            for (int count = 0; count < 20 && terminal == null; count++)
            {
                TickResult tick = session.Tick();
                if (tick.Result != null) terminal = tick;
            }

            Assert.That(terminal, Is.Not.Null);
            Assert.That(terminal.Code, Is.EqualTo(TickCode.Ended));
            Assert.That(terminal.Result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
            Assert.That(terminal.Result.Stars, Is.EqualTo(3));
            Assert.That(terminal.Result.LootGold, Is.EqualTo(500));
            Assert.That(terminal.Result.LootElixir, Is.EqualTo(300));
            Assert.That(terminal.Checksum, Is.EqualTo(session.State.Checksum));
            Assert.That(terminal.Result.FinalChecksum, Is.EqualTo(terminal.Checksum));
            Assert.That(terminal.Result.ResultId, Is.EqualTo("one-hit-match:0:"
                + terminal.Checksum.ToString("X16")));

            SimSnapshot snapshot = session.CaptureSnapshot();
            TickResult repeated = session.Tick();
            Assert.That(repeated.Code, Is.EqualTo(TickCode.NotRunning));
            Assert.That(repeated.CompletedTick, Is.EqualTo(terminal.CompletedTick));
            Assert.That(repeated.Checksum, Is.EqualTo(terminal.Checksum));
            Assert.That(repeated.Result.ResultId, Is.EqualTo(terminal.Result.ResultId));

            Assert.That(session.RestoreSnapshot(snapshot).Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Ended));
            Assert.That(session.State.Result.ResultId, Is.EqualTo(terminal.Result.ResultId));
            Assert.That(session.State.Checksum, Is.EqualTo(terminal.Checksum));
        }

        [Test]
        public void DeathSystem_DestructionBasisPointsRemainCumulativeAcrossTicksAndRestore()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            var deaths = new SimDeathSystem();
            var systems = new SimSystemGroup();
            systems.Add(deaths);
            systems.InitAll(world);
            CreateHealthEntity(world, SimTeam.Attacker, 10);
            SimEntity first = CreateHealthEntity(world, SimTeam.Defender, 0);
            CreateHealthEntity(world, SimTeam.Defender, 10);

            deaths.Update();
            Assert.That(first.IsValid, Is.True);
            Assert.That(deaths.DefenderDead, Is.EqualTo(1));
            Assert.That(deaths.DestructionBasisPoints, Is.EqualTo(5000));

            SimEntity second = SimEntity.Invalid;
            world.ForEachEntities<SimHealthComponent, SimTeamComponent>((entity, health, team) =>
            {
                if (team.TeamId == SimTeam.Defender) second = entity;
            });
            Assert.That(world.TryGetComponent(second, out SimHealthComponent remaining), Is.True);
            remaining.CurrentHP = 0;
            world.SetComponent(second, remaining);
            deaths.Update();
            Assert.That(deaths.DefenderDead, Is.EqualTo(2));
            Assert.That(deaths.DestructionBasisPoints, Is.EqualTo(10000));

            deaths.RestoreState(1, 0, 0, 2, SimTeam.Attacker);
            Assert.That(deaths.DestructionBasisPoints, Is.EqualTo(10000));
        }

        [Test]
        public void IdenticalOneHitBattles_ProduceIdenticalChecksumsAndResults()
        {
            using SimBattleSession first = SessionTestDriver.StartedOneHitBattle();
            using SimBattleSession second = SessionTestDriver.StartedOneHitBattle();

            TickResult firstTick = first.Tick();
            TickResult secondTick = second.Tick();

            Assert.That(secondTick.Checksum, Is.EqualTo(firstTick.Checksum));
            Assert.That(secondTick.Result.ResultId, Is.EqualTo(firstTick.Result.ResultId));
            Assert.That(secondTick.Result.FinalChecksum, Is.EqualTo(firstTick.Result.FinalChecksum));
        }

        [Test]
        public void SnapshotRestore_PreservesDeploymentCountAndPhase()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            for (int index = 0; index < 40; index++)
            {
                Assert.That(session.Submit(new DeployCmd
                {
                    Tick = 0, PlayerId = 0, Sequence = index + 1,
                    UnitTypeId = 1, GridX = index + 20, GridY = 3,
                }).Accepted, Is.True);
            }
            session.Tick();
            SimSnapshot snapshot = session.CaptureSnapshot();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 41,
            }).Accepted, Is.True);
            session.Tick();

            Assert.That(session.RestoreSnapshot(snapshot).Code,
                Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 41,
                UnitTypeId = 1, GridX = 100, GridY = 3,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
        }

        [Test]
        public void Start_ManifestWithoutSelectedModeRejectsWithTypedCode()
        {
            byte[] bytes = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
            }).ToCanonicalBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "legacy-match", "legacy-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(0, "attacker") },
                Array.Empty<InitialEntitySpec>(), 6);

            Assert.That(session.Start(init).Code, Is.EqualTo(StartCode.InvalidMode));
        }

        [Test]
        public void AutomaticDuration_TransitionsManualDeploymentAtConfiguredBoundary()
        {
            byte[] manifestBytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(manifestBytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(manifestBytes);
            var init = new BattleInit(1, 1, "duration-match", "duration-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                new[]
                {
                    new InitialEntitySpec(1, 2, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(1, 8, 2, SimTeam.Defender, 1),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            for (int count = 0; count < 599; count++) session.Tick();
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));

            session.Tick();

            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Battling));
            Assert.That(session.State.Result, Is.Null);
        }

        [Test]
        public void Checksum_ChangesWhenTerminalNumericOutcomeChanges()
        {
            byte[] contentHash = { 1, 2, 3 };
            var attacker = new SessionChecksumState(1, 1, contentHash, 42,
                SessionLifecycle.Ended, SimCombatPhase.Ended, 0, 1, 0,
                new SimTerminalOutcome(SimTeam.Attacker, 3, 500, 300), 0, 0);
            var defender = new SessionChecksumState(1, 1, contentHash, 42,
                SessionLifecycle.Ended, SimCombatPhase.Ended, 0, 1, 0,
                new SimTerminalOutcome(SimTeam.Defender, 0, 0, 0), 0, 0);

            Assert.That(attacker.HashInto(DeterministicHash64.OffsetBasis),
                Is.Not.EqualTo(defender.HashInto(DeterministicHash64.OffsetBasis)));
        }

        private static SimEntity CreateHealthEntity(SimWorld world, int teamId, int health)
        {
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent), typeof(SimTeamComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = health, MaxHP = 10 });
            world.SetComponent(entity, SimTeamComponent.Create(teamId));
            return entity;
        }
    }
}
