using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SessionTerminalTests
    {
        private struct UnregisteredComponent : ISimComponent
        {
        }

        [Test]
        public void CreateEntity_InvalidComponentSet_DoesNotMutateWorld()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 1);

            Assert.Throws<ArgumentNullException>(() => world.CreateEntity((Type[])null));
            Assert.Throws<KeyNotFoundException>(() => world.CreateEntity(
                typeof(UnregisteredComponent)));
            Assert.Throws<ArgumentException>(() => world.CreateEntity(
                typeof(SimHealthComponent), typeof(SimHealthComponent)));
            Assert.That(world.EntityCount, Is.Zero);

            SimEntity empty = world.CreateEntity();
            Assert.That(empty.Id, Is.Zero);
            Assert.That(empty.Generation, Is.EqualTo(1));

            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            Assert.That(entity.Id, Is.EqualTo(1));
            Assert.That(entity.Generation, Is.EqualTo(1));
        }

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
        public void CommandSubmit_NegativeSequence_IsRejectedBeforeBuffering()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            int entityId = session.State.Presentation.Entities[0].EntityId;

            CommandSubmitResult result = session.Submit(new MoveCmd
            {
                Tick = 0, PlayerId = 0, Sequence = -1,
                EntityId = entityId, TargetX = 5, TargetY = 2,
            });

            Assert.That(result.Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            Assert.That(session.Tick().ExecutedCommands, Is.Empty);
        }

        [Test]
        public void CommandsAndInitialEntities_RejectCoordinatesOutsideWaypointEncodingRange()
        {
            const int outsideWaypointRange = ushort.MaxValue + 1;
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            int entityId = session.State.Presentation.Entities[0].EntityId;

            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            Assert.That(session.Submit(new MoveCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 2,
                EntityId = entityId, TargetX = outsideWaypointRange, TargetY = 2,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));

            byte[] manifest = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest content = DeterministicContentManifest.Parse(manifest);
            using ISimBattleSession initial = new SimBattleSessionFactory().Create(manifest);
            var init = new BattleInit(1, 1, "out-of-range-match", "out-of-range-session",
                42, 20, SimCombatMode.VillageAttack, content.ContentHash,
                new[]
                {
                    new SimPlayerIdentity(0, "attacker"),
                    new SimPlayerIdentity(1, "defender"),
                },
                new[]
                {
                    new InitialEntitySpec(10, outsideWaypointRange, 2,
                        SimTeam.Attacker, 0),
                }, 6);

            Assert.That(initial.Start(init).Code, Is.EqualTo(StartCode.InvalidEntity));
        }

        [Test]
        public void DeserializeCommand_InvalidOrMissingType_ReturnsNull()
        {
            int invalidType = 999;
            Assert.That(CommandRecorder.DeserializeCommand(null), Is.Null);
            Assert.That(CommandRecorder.DeserializeCommand(new SerializableCommand()), Is.Null);
            Assert.That(CommandRecorder.DeserializeCommand(new SerializableCommand
            {
                Type = "Unknown"
            }), Is.Null);
            Assert.That(CommandRecorder.DeserializeCommand(new SerializableCommand
            {
                Type = "Deploy, Move"
            }), Is.Null);
            Assert.That(CommandRecorder.DeserializeCommand(new SerializableCommand
            {
                Type = ((SimCommandType)invalidType).ToString()
            }), Is.Null);
        }

        [Test]
        public void ContentArchetype_RejectsInvalidDeterministicValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(0, "unit", 1, 0, 0, 0, 0));
            Assert.Throws<ArgumentException>(() =>
                new SimArchetypeDefinition(1, " ", 1, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(1, "unit", 0, 0, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(1, "unit", 1, -1, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(1, "unit", 1, 0, -1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(1, "unit", 1, 0, 0, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SimArchetypeDefinition(1, "unit", 1, 0, 0, 0, -1));
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
        public void Snapshot_UnknownPlayerPendingCommand_IsRejectedBeforeLiveMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 999,
                    Sequence = 0,
                    UnitTypeId = 10,
                    GridX = 20,
                    GridY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(
                session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void Snapshot_DefenderDeploymentCommand_IsRejectedBeforeLiveMutation()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "snapshot-role-match", "snapshot-role-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[] { new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20) }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[20] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 20,
                    Sequence = 1,
                    UnitTypeId = 10,
                    GridX = 20,
                    GridY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void Snapshot_DuplicatePendingCommandSequence_IsRejectedBeforeLiveMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[0] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 0,
                    Sequence = 1,
                    UnitTypeId = 10,
                    GridX = 20,
                    GridY = 2,
                },
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 0,
                    Sequence = 1,
                    UnitTypeId = 10,
                    GridX = 21,
                    GridY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(
                session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void Snapshot_TransformOutsideWaypointEncodingRange_IsRejectedBeforeLiveMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            ChunkSnapshot chunk = data.WorldSnapshot.ChunkSnapshots.First(candidate =>
                Array.IndexOf(candidate.ComponentTypeIds,
                    SimComponentTypeId.Transform) >= 0);
            int transformColumn = Array.IndexOf(chunk.ComponentTypeIds,
                SimComponentTypeId.Transform);
            var transforms = (SimTransformComponent[])chunk.Columns[transformColumn];
            transforms[0].GridX = ushort.MaxValue + 1;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(
                session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void Snapshot_MovementTargetOutsideWaypointEncodingRange_IsRejectedBeforeLiveMutation()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            ChunkSnapshot chunk = data.WorldSnapshot.ChunkSnapshots.First(candidate =>
                Array.IndexOf(candidate.ComponentTypeIds,
                    SimComponentTypeId.Movement) >= 0);
            int movementColumn = Array.IndexOf(chunk.ComponentTypeIds,
                SimComponentTypeId.Movement);
            var movements = (SimMovementComponent[])chunk.Columns[movementColumn];
            movements[0].TargetGridX = ushort.MaxValue + 1;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(
                session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void Snapshot_GenerationCountExceedsPayload_IsRejectedBeforeAllocation()
        {
            var writer = new DeterministicBinaryWriter();
            WriteSnapshotPrefix(writer);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(1);

            FormatException error = Assert.Throws<FormatException>(() =>
                SessionSnapshotCodec.Decode(writer.ToArray()));

            Assert.That(error.Message, Is.EqualTo("Generation count exceeds the snapshot payload."));
        }

        [Test]
        public void Snapshot_PlayerSequenceCountExceedsPayload_IsRejectedBeforeAllocation()
        {
            var writer = new DeterministicBinaryWriter();
            WriteSnapshotPrefix(writer);
            writer.WriteInt32(1);

            FormatException error = Assert.Throws<FormatException>(() =>
                SessionSnapshotCodec.Decode(writer.ToArray()));

            Assert.That(error.Message, Is.EqualTo("Player sequence count exceeds the snapshot payload."));
        }

        [Test]
        public void Snapshot_PendingCommandCountExceedsPayload_IsRejectedBeforeAllocation()
        {
            var writer = new DeterministicBinaryWriter();
            WriteSnapshotPrefix(writer);
            writer.WriteInt32(0);
            writer.WriteInt32(1);

            FormatException error = Assert.Throws<FormatException>(() =>
                SessionSnapshotCodec.Decode(writer.ToArray()));

            Assert.That(error.Message, Is.EqualTo("Pending command count exceeds the snapshot payload."));
        }

        [Test]
        public void Snapshot_MovementPathLengthExceedsRemainingPayload_IsRejectedBeforeAllocation()
        {
            var writer = new DeterministicBinaryWriter();
            WriteSnapshotPrefix(writer);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteUInt64(0);
            writer.WriteInt32(1);
            writer.WriteInt32(1);
            writer.WriteInt32(SimComponentTypeId.Movement);
            writer.WriteInt32(1);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt64(0);
            writer.WriteInt64(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(0);
            writer.WriteInt32(1);

            FormatException error = Assert.Throws<FormatException>(() =>
                SessionSnapshotCodec.Decode(writer.ToArray()));

            Assert.That(error.Message, Is.EqualTo("Movement path length exceeds the snapshot payload."));
        }

        [Test]
        public void WorldSnapshot_DuplicateEntityAcrossChunks_IsRejectedBeforeRestore()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = 1, MaxHP = 1 });
            TickSnapshot snapshot = world.ExportFullSnapshot();
            snapshot.ChunkSnapshots.Add(snapshot.ChunkSnapshots[0]);

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                world.RestoreFromFull(snapshot));

            Assert.That(error.Message, Is.EqualTo("Snapshot entity metadata is inconsistent. (Parameter 'snapshot')"));
        }

        [Test]
        public void WorldSnapshot_WrongColumnType_IsRejectedBeforeLiveWorldMutation()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = 1, MaxHP = 1 });
            ulong checksumBefore = world.ComputeChecksum();
            TickSnapshot snapshot = world.ExportFullSnapshot();
            snapshot.ChunkSnapshots[0].Columns[0] = new int[1];

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                world.RestoreFromFull(snapshot));

            Assert.That(error.Message, Is.EqualTo("Invalid world snapshot chunk. (Parameter 'snapshot')"));
            Assert.That(world.ComputeChecksum(), Is.EqualTo(checksumBefore));
        }

        [Test]
        public void WorldSnapshot_DuplicateComponentType_IsRejectedBeforeRestore()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = 1, MaxHP = 1 });
            TickSnapshot snapshot = world.ExportFullSnapshot();
            ChunkSnapshot chunk = snapshot.ChunkSnapshots[0];
            chunk.ComponentTypeIds = new[] { SimComponentTypeId.Health, SimComponentTypeId.Health };
            chunk.Columns = new Array[]
            {
                new[] { new SimHealthComponent { CurrentHP = 1, MaxHP = 1 } },
                new[] { new SimHealthComponent { CurrentHP = 1, MaxHP = 1 } },
            };

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                world.RestoreFromFull(snapshot));

            Assert.That(error.Message, Is.EqualTo("Invalid world snapshot chunk. (Parameter 'snapshot')"));
        }

        [Test]
        public void WorldSnapshot_EntityIdBeyondAllocator_IsRejectedBeforeRestore()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = 1, MaxHP = 1 });
            TickSnapshot snapshot = world.ExportFullSnapshot();
            snapshot.ChunkSnapshots[0].EntityIds[0] = snapshot.NextId;
            snapshot.Generations[snapshot.NextId] = snapshot.ChunkSnapshots[0].Generations[0];

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                world.RestoreFromFull(snapshot));

            Assert.That(error.Message, Is.EqualTo("Snapshot entity metadata is inconsistent. (Parameter 'snapshot')"));
        }

        [Test]
        public void WorldSnapshot_ZeroGenerationEntity_IsRejectedBeforeRestore()
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(), 8);
            SimEntity entity = world.CreateEntity(typeof(SimHealthComponent));
            world.SetComponent(entity, new SimHealthComponent { CurrentHP = 1, MaxHP = 1 });
            TickSnapshot snapshot = world.ExportFullSnapshot();
            snapshot.Generations[entity.Id] = 0;
            snapshot.ChunkSnapshots[0].Generations[0] = 0;

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                world.RestoreFromFull(snapshot));

            Assert.That(error.Message, Is.EqualTo("Snapshot entity metadata is inconsistent. (Parameter 'snapshot')"));
        }

        [Test]
        public void Checksum_ChangesWhenTerminalNumericOutcomeChanges()
        {
            byte[] contentHash = { 1, 2, 3 };
            var attacker = new SessionChecksumState(1, 1, contentHash, 42,
                SessionLifecycle.Ended, SimCombatPhase.Ended, 0, 1, 0,
                new SimTerminalOutcome(SimTeam.Attacker, 3, 500, 300), 0, 0, 0);
            var defender = new SessionChecksumState(1, 1, contentHash, 42,
                SessionLifecycle.Ended, SimCombatPhase.Ended, 0, 1, 0,
                new SimTerminalOutcome(SimTeam.Defender, 0, 0, 0), 0, 0, 0);

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

        private static SimSnapshot CreateSnapshotWithCalculatedChecksum(
            SimBattleSession session, SimSnapshot baseline, SessionSnapshotData data)
        {
            var world = new SimWorld(SimComponentSchema.CreateRegistry(),
                Math.Max(8, data.WorldSnapshot.Generations.Length));
            world.RestoreFromFull(data.WorldSnapshot);
            data.WorldSnapshot.Checksum = world.ComputeChecksum();
            MethodInfo checksumMethod = typeof(SimBattleSession).GetMethod(
                "ComputeChecksum", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(SessionSnapshotData), typeof(SimWorld) }, null);
            ulong checksum = (ulong)checksumMethod.Invoke(session, new object[] { data, world });
            byte[] payload = SessionSnapshotCodec.Encode(data);
            byte[] payloadHash;
            using (SHA256 sha256 = SHA256.Create())
                payloadHash = sha256.ComputeHash(payload);
            return new SimSnapshot(baseline.MatchId, baseline.SessionId,
                baseline.SchemaVersion, baseline.ContentHash, data.CompletedTick,
                data.NextTick, baseline.TimelineRevision, payload.Length, checksum,
                payload, payloadHash);
        }

        private static void WriteSnapshotPrefix(DeterministicBinaryWriter writer)
        {
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
        }
    }
}
