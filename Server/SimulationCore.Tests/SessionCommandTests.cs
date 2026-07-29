using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore.Tests;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class SessionCommandTests
    {
        [Test]
        public void Start_RejectsInitialEntitiesThatOccupyTheSameCell()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "overlap-match", "overlap-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(0, "attacker"), new SimPlayerIdentity(1, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 2, 2, SimTeam.Defender, 1),
                }, 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidEntity));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void Start_RejectsBattleWithoutPlayers()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "empty-players-match", "empty-players-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                System.Array.Empty<SimPlayerIdentity>(),
                System.Array.Empty<InitialEntitySpec>(), 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidPlayer));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void Start_RejectsNegativeInputDeadlineTicks()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "negative-deadline-match",
                "negative-deadline-session", 42, 20, SimCombatMode.VillageAttack,
                manifest.ContentHash,
                new[] { new SimPlayerIdentity(0, "attacker"),
                    new SimPlayerIdentity(1, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1),
                }, -1);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidMatch));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void Start_RejectsPlayersWithDuplicateAccountIds()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "duplicate-account-match",
                "duplicate-account-session", 42, 20, SimCombatMode.VillageAttack,
                manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "same-account"),
                    new SimPlayerIdentity(20, "same-account") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 10),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidPlayer));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void Start_RejectsInitialEntityOwnedByOpposingPlayer()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "wrong-team-owner-match",
                "wrong-team-owner-session", 42, 20, SimCombatMode.VillageAttack,
                manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"),
                    new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 10),
                }, 6);

            StartResult result = session.Start(init);

            Assert.That(result.Code, Is.EqualTo(StartCode.InvalidEntity));
            Assert.That(session.State.Lifecycle, Is.EqualTo(SessionLifecycle.Created));
        }

        [Test]
        public void SessionSkill_DotNetHostProducesDamageAndEvent()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();

            CommandSubmitResult submitted = session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            });
            TickResult result = session.Tick();

            Assert.That(submitted.Accepted, Is.True);
            Assert.That(session.State.Presentation.Entities[1].Health,
                Is.EqualTo(target.Health - 30));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Type, Is.EqualTo(SimBattleEventType.DamageApplied));
        }

        [Test]
        public void InvalidSessionCommand_DotNetHostDoesNotConsumeSequenceOrMutateWorld()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();
            EntityPresentationState caster = session.State.Presentation.Entities[0];
            EntityPresentationState target = session.State.Presentation.Entities[1];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = caster.OwnerPlayerId, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 99,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = caster.OwnerPlayerId,
                Sequence = 2,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            session.Tick();
            Assert.That(session.State.Presentation.Entities[1].Health,
                Is.EqualTo(target.Health - 30));
        }

        [Test]
        public void Submit_RejectsTerminalSequenceValueWithoutConsumingSequence()
        {
            using SimBattleSession session = SessionTestDriver.StartedBattle();

            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = int.MaxValue,
            }).Disposition, Is.EqualTo(CommandDisposition.InvalidPayload));
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
        }

        [Test]
        public void SkillTargeting_UsesEntityTeamInsteadOfAssumingPlayerIdsZeroAndOne()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "custom-player-match", "custom-player-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 10),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            EntityPresentationState caster = session.State.Presentation.Entities[1];
            EntityPresentationState target = session.State.Presentation.Entities[0];
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 10, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            target = session.State.Presentation.Entities.Single(entity =>
                entity.OwnerPlayerId == target.OwnerPlayerId);

            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 1,
                PlayerId = 20,
                Sequence = 1,
                CasterId = caster.EntityId,
                SkillId = 1,
                TargetX = target.GridX,
                TargetY = target.GridY,
            }).Accepted, Is.True);

            Assert.That(session.Tick().Events.Count, Is.EqualTo(1));
        }

        [Test]
        public void Deploy_RejectsDefenderWithCustomPlayerIds()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "custom-deploy-match", "custom-deploy-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(10, 2, 2, SimTeam.Attacker, 10),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 20, Sequence = 1, UnitTypeId = 10, GridX = 7, GridY = 2,
            }).Disposition, Is.EqualTo(CommandDisposition.UnauthorizedEntity));

            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
            EntityPresentationState defender = session.State.Presentation.Entities.Single(entity =>
                entity.OwnerPlayerId == 20);
            Assert.That(session.Submit(new SkillCmd
            {
                Tick = 0, PlayerId = 20, Sequence = 1, CasterId = defender.EntityId,
                SkillId = 1, TargetX = 2, TargetY = 2,
            }).Accepted, Is.True);
            Assert.That(session.State.Presentation.Entities.Count, Is.EqualTo(2));
        }

        [Test]
        public void EndDeployment_RejectsDefenderWithCustomPlayerIds()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using ISimBattleSession session = new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "custom-end-match", "custom-end-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[]
                {
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20),
                }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);

            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 20, Sequence = 1,
            }).Disposition, Is.EqualTo(CommandDisposition.UnauthorizedEntity));

            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
        }

        [Test]
        public void RestoreSnapshot_RejectsDefenderDeploymentCommand()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "server-snapshot-role-match", "server-snapshot-role-session", 42, 20,
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
        public void RestoreSnapshot_RejectsDefenderEndDeploymentCommand()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "server-snapshot-end-match", "server-snapshot-end-session", 42, 20,
                SimCombatMode.VillageAttack, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[] { new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20) }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[20] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new EndDeploymentCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 20,
                    Sequence = 1,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);
            ulong checksumBefore = session.State.Checksum;

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(checksumBefore));
        }

        [Test]
        public void RestoreSnapshot_RejectsDeploymentPhaseForCampaign()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "campaign-snapshot-match", "campaign-snapshot-session", 42, 20,
                SimCombatMode.Campaign, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[] { new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20) }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.Phase = SimCombatPhase.Deploying;
            data.LastSequenceByPlayer[10] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 10,
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
        public void RestoreSnapshot_RejectsDeploymentCommandForCampaign()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = DeterministicContentManifest.Parse(bytes);
            using var session = (SimBattleSession)new SimBattleSessionFactory().Create(bytes);
            var init = new BattleInit(1, 1, "campaign-command-match", "campaign-command-session", 42, 20,
                SimCombatMode.Campaign, manifest.ContentHash,
                new[] { new SimPlayerIdentity(10, "attacker"), new SimPlayerIdentity(20, "defender") },
                new[] { new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 20) }, 6);
            Assert.That(session.Start(init).Accepted, Is.True);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[10] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick,
                    PlayerId = 10,
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
        public void RestoreSnapshot_PreservesQueuedDeploymentAfterCellBecomesOccupied()
        {
            using var session = SessionTestDriver.StartedDeployingBattle();
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 2,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot pending = session.CaptureSnapshot();

            session.Tick();
            SnapshotRestoreResult restored = session.RestoreSnapshot(pending);
            TickResult replayed = session.Tick();

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(replayed.CommandOutcomes.Single().Disposition,
                Is.EqualTo(CommandDisposition.InvalidPayload));
        }

        [Test]
        public void RestoreSnapshot_PreservesQueuedDeploymentAfterPhaseChanges()
        {
            using var session = SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 1, PlayerId = 0, Sequence = 2,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot pending = session.CaptureSnapshot();

            session.Tick();
            SnapshotRestoreResult restored = session.RestoreSnapshot(pending);
            TickResult replayed = session.Tick();

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(replayed.CommandOutcomes.Single().Disposition,
                Is.EqualTo(CommandDisposition.WrongPhase));
        }

        [Test]
        public void RestoreSnapshot_PreservesSameCellDeploymentConflictForExecution()
        {
            using var session = SessionTestDriver.StartedDeployingBattle();
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            Assert.That(session.Submit(new DeployCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 2,
                UnitTypeId = 10, GridX = 4, GridY = 4,
            }).Accepted, Is.True);
            SimSnapshot pending = session.CaptureSnapshot();

            session.Tick();
            SnapshotRestoreResult restored = session.RestoreSnapshot(pending);
            TickResult replayed = session.Tick();

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.Restored));
            Assert.That(replayed.CommandOutcomes.Count, Is.EqualTo(2));
            Assert.That(replayed.CommandOutcomes[0].Disposition,
                Is.EqualTo(CommandDisposition.Accepted));
            Assert.That(replayed.CommandOutcomes[1].Disposition,
                Is.EqualTo(CommandDisposition.InvalidPayload));
        }

        [Test]
        public void RestoreSnapshot_RejectsRunningSessionWithEndedPhase()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.Phase = SimCombatPhase.Ended;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
        }

        [Test]
        public void RestoreSnapshot_RejectsRunningStateAfterTimeout()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.CompletedTick = 3599;
            data.NextTick = 3600;
            data.AttackSystemTick = 3600;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.CompletedTick, Is.EqualTo(baseline.CompletedTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsDeploymentStateAfterDeploymentWindow()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.CompletedTick = 599;
            data.NextTick = 600;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Phase, Is.EqualTo(SimCombatPhase.Deploying));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsAttackClockAheadOfNextTick()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.AttackSystemTick = data.NextTick + 1;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(0));
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedDeathStatistics()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.DefenderDead++;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(baseline.StateChecksum));
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedTerminalOutcome()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedOneHitBattle();
            Assert.That(session.Tick().Code, Is.EqualTo(TickCode.Ended));
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.TerminalOutcome = new SimTerminalOutcome(
                SimTeam.Attacker, 3, 499, 300);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Result.LootGold, Is.EqualTo(500));
        }

        [Test]
        public void RestoreSnapshot_RejectsInjectedPendingDamageEvent()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot baseline = session.CaptureSnapshot();
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.WorldSnapshot.PendingDamageEvents.Add(new SimDamageEvent
            {
                Amount = 1,
                Attacker = SimEntity.Invalid,
                Target = new SimEntity(defender.EntityId, defender.Generation),
            });
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Checksum, Is.EqualTo(baseline.StateChecksum));
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedEntityOwnership()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetOwner(data.WorldSnapshot, defender.EntityId, 0);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.Submit(new SkillCmd
            {
                Tick = session.State.NextTick, PlayerId = 0, Sequence = 2,
                CasterId = defender.EntityId, SkillId = 1,
                TargetX = attacker.GridX, TargetY = attacker.GridY,
            }).Disposition, Is.EqualTo(CommandDisposition.UnauthorizedEntity));
        }

        [Test]
        public void RestoreSnapshot_RejectsPendingMoveForUnknownEntity()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[0] = 2;
            data.PendingCommands = new ISimCommand[]
            {
                new MoveCmd
                {
                    Tick = data.NextTick, PlayerId = 0, Sequence = 2,
                    EntityId = 999, TargetX = 3, TargetY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsPendingMoveForForeignLiveEntity()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[0] = 2;
            data.PendingCommands = new ISimCommand[]
            {
                new MoveCmd
                {
                    Tick = data.NextTick, PlayerId = 0, Sequence = 2,
                    EntityId = defender.EntityId, TargetX = 7, TargetY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsPendingSkillWithUnsupportedSkillId()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[attacker.OwnerPlayerId] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new SkillCmd
                {
                    Tick = data.NextTick, PlayerId = attacker.OwnerPlayerId, Sequence = 1,
                    CasterId = attacker.EntityId, SkillId = 99,
                    TargetX = defender.GridX, TargetY = defender.GridY,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsPendingDeploymentWithUnknownArchetype()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[0] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new DeployCmd
                {
                    Tick = data.NextTick, PlayerId = 0, Sequence = 1,
                    UnitTypeId = 999, GridX = 3, GridY = 3,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsPendingMoveForStationaryLiveEntity()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[defender.OwnerPlayerId] = 1;
            data.PendingCommands = new ISimCommand[]
            {
                new MoveCmd
                {
                    Tick = data.NextTick, PlayerId = defender.OwnerPlayerId, Sequence = 1,
                    EntityId = defender.EntityId, TargetX = 7, TargetY = 2,
                },
            };
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.NextTick, Is.EqualTo(baseline.NextTick));
        }

        [Test]
        public void RestoreSnapshot_RejectsUnreachableAcceptedSequenceWatermark()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            data.LastSequenceByPlayer[0] = int.MaxValue;
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = session.State.NextTick, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedEntityHealthCapacity()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetHealth(data.WorldSnapshot, defender.EntityId, 999, 999);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
            Assert.That(session.State.Presentation.Entities.Single(
                entity => entity.EntityId == defender.EntityId).Health,
                Is.EqualTo(defender.Health));
        }

        [Test]
        public void RestoreSnapshot_RejectsEntityHealthAboveContentCapacity()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            session.Tick();
            EntityPresentationState defender = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Defender);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetHealth(data.WorldSnapshot, defender.EntityId, defender.Health + 1, defender.Health);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedEntityAttackDamage()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetAttackDamage(data.WorldSnapshot, attacker.EntityId, 999);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
        }

        [Test]
        public void RestoreSnapshot_RejectsOverflowingAttackClock()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetAttackLastTick(data.WorldSnapshot, attacker.EntityId, int.MinValue);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
        }

        [Test]
        public void RestoreSnapshot_RejectsForgedEntityMovementSpeed()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetMovementSpeed(data.WorldSnapshot, attacker.EntityId, 99999);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
        }

        [Test]
        public void RestoreSnapshot_RejectsTransformOutsideItsGridCell()
        {
            using var session = (SimBattleSession)SessionTestDriver.StartedBattle();
            EntityPresentationState attacker = session.State.Presentation.Entities.Single(
                entity => entity.TeamId == SimTeam.Attacker);
            SimSnapshot baseline = session.CaptureSnapshot();
            SessionSnapshotData data = SessionSnapshotCodec.Decode(baseline.Payload);
            SetTransformPosition(data.WorldSnapshot, attacker.EntityId,
                LogicInt.FromInt(500).RawValue, LogicInt.FromInt(500).RawValue);
            SimSnapshot forged = CreateSnapshotWithCalculatedChecksum(session, baseline, data);

            SnapshotRestoreResult restored = session.RestoreSnapshot(forged);

            Assert.That(restored.Code, Is.EqualTo(SnapshotRestoreCode.CorruptPayload));
        }

        private static void SetOwner(TickSnapshot snapshot, int entityId, int ownerPlayerId)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int identityIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Identity);
                if (entityIndex < 0 || identityIndex < 0) continue;

                var identities = (SimIdentityComponent[])chunk.Columns[identityIndex];
                SimIdentityComponent identity = identities[entityIndex];
                identity.OwnerPlayerId = ownerPlayerId;
                identities[entityIndex] = identity;
                return;
            }

            Assert.Fail("Expected identity component was not found in snapshot.");
        }

        private static void SetHealth(TickSnapshot snapshot, int entityId, int currentHp, int maxHp)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int healthIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Health);
                if (entityIndex < 0 || healthIndex < 0) continue;

                var health = (SimHealthComponent[])chunk.Columns[healthIndex];
                health[entityIndex] = new SimHealthComponent
                {
                    CurrentHP = currentHp,
                    MaxHP = maxHp,
                };
                return;
            }

            Assert.Fail("Expected health component was not found in snapshot.");
        }

        private static void SetAttackDamage(TickSnapshot snapshot, int entityId, int damage)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int attackIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Attack);
                if (entityIndex < 0 || attackIndex < 0) continue;

                var attacks = (SimAttackComponent[])chunk.Columns[attackIndex];
                SimAttackComponent attack = attacks[entityIndex];
                attack.Damage = damage;
                attacks[entityIndex] = attack;
                return;
            }

            Assert.Fail("Expected attack component was not found in snapshot.");
        }

        private static void SetAttackLastTick(TickSnapshot snapshot, int entityId, int lastAttackTick)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int attackIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Attack);
                if (entityIndex < 0 || attackIndex < 0) continue;

                var attacks = (SimAttackComponent[])chunk.Columns[attackIndex];
                SimAttackComponent attack = attacks[entityIndex];
                attack.LastAttackTick = lastAttackTick;
                attacks[entityIndex] = attack;
                return;
            }

            Assert.Fail("Expected attack component was not found in snapshot.");
        }

        private static void SetMovementSpeed(TickSnapshot snapshot, int entityId, long speedRaw)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int movementIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Movement);
                if (entityIndex < 0 || movementIndex < 0) continue;

                var movement = (SimMovementComponent[])chunk.Columns[movementIndex];
                SimMovementComponent component = movement[entityIndex];
                component.Speed = LogicInt.FromRaw(speedRaw);
                movement[entityIndex] = component;
                return;
            }

            Assert.Fail("Expected movement component was not found in snapshot.");
        }

        private static void SetTransformPosition(TickSnapshot snapshot, int entityId,
            long posXRaw, long posYRaw)
        {
            foreach (ChunkSnapshot chunk in snapshot.ChunkSnapshots)
            {
                int entityIndex = System.Array.IndexOf(chunk.EntityIds, entityId);
                int transformIndex = System.Array.IndexOf(
                    chunk.ComponentTypeIds, SimComponentTypeId.Transform);
                if (entityIndex < 0 || transformIndex < 0) continue;

                var transforms = (SimTransformComponent[])chunk.Columns[transformIndex];
                SimTransformComponent transform = transforms[entityIndex];
                transform.PosX = LogicInt.FromRaw(posXRaw);
                transform.PosY = LogicInt.FromRaw(posYRaw);
                transforms[entityIndex] = transform;
                return;
            }

            Assert.Fail("Expected transform component was not found in snapshot.");
        }

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
    }
}
