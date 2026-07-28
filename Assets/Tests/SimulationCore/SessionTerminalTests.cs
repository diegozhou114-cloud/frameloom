using System;
using System.Linq;
using System.Security.Cryptography;
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
            Assert.That(serialized.UnitTypeId, Is.Zero);
            Assert.That(serialized.EntityId, Is.Zero);
            Assert.That(serialized.CasterId, Is.Zero);
            Assert.That(serialized.SkillId, Is.Zero);
            Assert.That(serialized.GridX, Is.Zero);
            Assert.That(serialized.GridY, Is.Zero);
            Assert.That(serialized.TargetX, Is.Zero);
            Assert.That(serialized.TargetY, Is.Zero);
            var restoredCommand = (EndDeploymentCmd)CommandRecorder.DeserializeCommand(serialized);
            Assert.That(restoredCommand.Tick, Is.EqualTo(command.Tick));
            Assert.That(restoredCommand.PlayerId, Is.EqualTo(command.PlayerId));
            Assert.That(restoredCommand.Sequence, Is.EqualTo(command.Sequence));

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
        public void BattleSession_AttackDamageDeathResult_EndToEnd()
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
            SimBattleEvent destroyed = terminal.Events.Single(evt =>
                evt.Type == SimBattleEventType.EntityDestroyed);
            SimBattleEvent damage = terminal.Events.Last(evt =>
                evt.Type == SimBattleEventType.DamageApplied &&
                evt.TargetEntity.EntityId == destroyed.TargetEntity.EntityId &&
                evt.TargetEntity.Generation == destroyed.TargetEntity.Generation);
            Assert.That(destroyed.SourceEntity.EntityId,
                Is.EqualTo(damage.SourceEntity.EntityId));
            Assert.That(destroyed.SourceEntity.Generation,
                Is.EqualTo(damage.SourceEntity.Generation));
            Assert.That(destroyed.TargetEntity.EntityId,
                Is.EqualTo(damage.TargetEntity.EntityId));
            Assert.That(destroyed.TargetEntity.Generation,
                Is.EqualTo(damage.TargetEntity.Generation));
            Assert.That(destroyed.Amount, Is.Zero);
            Assert.That(terminal.Result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
            Assert.That(terminal.Result.FinalChecksum, Is.EqualTo(terminal.Checksum));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Ended));
            Assert.That(session.State.Result.ResultId, Is.EqualTo(terminal.Result.ResultId));
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
        public void ManualDeployment_CombatWaitsUntilEndDeploymentTransitionTick()
        {
            byte[] manifestBytes = SessionTestDriver.OneHitManifestBytes();
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(manifestBytes);
            Assert.That(session.Start(SessionTestDriver.OneHitBattleInit(manifestBytes)).Accepted,
                Is.True);
            EntityPresentationState[] initial = session.State.Presentation.Entities.ToArray();

            for (int tick = 0; tick < 3; tick++)
            {
                TickResult deploying = session.Tick();
                Assert.That(deploying.Code, Is.EqualTo(TickCode.Advanced));
                Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Running));
                Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(deploying.Result, Is.Null);
                Assert.That(session.State.Presentation.Entities.Select(EntityCombatState),
                    Is.EqualTo(initial.Select(EntityCombatState)));
            }

            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 3, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            TickResult transition = session.Tick();

            Assert.That(transition.ExecutedCommands.Single(), Is.TypeOf<EndDeploymentCmd>());
            Assert.That(transition.Code, Is.EqualTo(TickCode.Ended));
            Assert.That(transition.Result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
        }

        [Test]
        public void AutomaticDuration_AttackingEntitiesWaitUntilBoundaryTick()
        {
            DeterministicContentManifest oneHit =
                DeterministicContentManifest.Parse(SessionTestDriver.OneHitManifestBytes());
            byte[] manifestBytes = new DeterministicContentManifest(1,
                oneHit.Archetypes.ToArray(), new[]
                {
                    new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 3, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                        Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                        new[] { 0, 100, 200, 300, 400 }),
                }).ToCanonicalBytes();
            DeterministicContentManifest manifest =
                DeterministicContentManifest.Parse(manifestBytes);
            BattleInit baseline = SessionTestDriver.OneHitBattleInit(manifestBytes);
            var init = new BattleInit(baseline.ProtocolVersion, baseline.SchemaVersion,
                baseline.MatchId, baseline.SessionId, baseline.Seed, baseline.TickRate,
                baseline.Mode, manifest.ContentHash, baseline.Players,
                baseline.InitialEntities, baseline.InputDeadlineTicks);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(manifestBytes);
            Assert.That(session.Start(init).Accepted, Is.True);
            EntityPresentationState[] initial = session.State.Presentation.Entities.ToArray();

            for (int tick = 0; tick < 2; tick++)
            {
                TickResult deploying = session.Tick();
                Assert.That(deploying.Code, Is.EqualTo(TickCode.Advanced));
                Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(session.State.Presentation.Entities.Select(EntityCombatState),
                    Is.EqualTo(initial.Select(EntityCombatState)));
            }

            TickResult boundary = session.Tick();
            Assert.That(boundary.Code, Is.EqualTo(TickCode.Ended));
            Assert.That(boundary.Result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
        }

        [Test]
        public void Snapshot_ChangedDeathCountersAreRejectedBeforeLiveMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.DefenderDead++;
            byte[] forgedPayload = SessionSnapshotCodec.Encode(data);
            byte[] forgedHash;
            using (SHA256 sha256 = SHA256.Create())
                forgedHash = sha256.ComputeHash(forgedPayload);
            var forged = new SimSnapshot(baseline.MatchId, baseline.SessionId,
                baseline.SchemaVersion, baseline.ContentHash, baseline.CompletedTick,
                baseline.NextTick, baseline.TimelineRevision, forgedPayload.Length,
                baseline.StateChecksum, forgedPayload, forgedHash);
            ulong checksumBefore = session.State.Checksum;
            SimCombatPhase phaseBefore = session.State.Phase;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
            Assert.That(session.State.Phase, Is.EqualTo(phaseBefore));
        }

        [Test]
        public void Snapshot_GenerationCountExceedsPayload_IsRejectedBeforeAllocation()
        {
            var writer = new DeterministicBinaryWriter();
            writer.WriteBytes(new byte[] { 0x5A, 0x53, 0x4E, 0x50 });
            writer.WriteInt32(2);
            writer.WriteInt32((int)SessionLifecycle.Created);
            writer.WriteInt32((int)SimCombatPhase.Idle);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteByte(0);
            writer.WriteInt32(0);
            writer.WriteByte(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(1);

            FormatException error = Assert.Throws<FormatException>(() =>
                SessionSnapshotCodec.Decode(writer.ToArray()));

            Assert.That(error.Message, Is.EqualTo("Generation count exceeds the snapshot payload."));
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

        private static string EntityCombatState(EntityPresentationState entity) =>
            entity.EntityId + ":" + entity.GridX + ":" + entity.GridY + ":" + entity.Health;
    }
}
