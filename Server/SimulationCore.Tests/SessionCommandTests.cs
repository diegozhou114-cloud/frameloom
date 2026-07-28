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
