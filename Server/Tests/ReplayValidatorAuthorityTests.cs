using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Server;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server.Tests
{
    public sealed class ReplayValidatorAuthorityTests
    {
        [Test]
        public async Task ReplayValidator_TamperedSeedOrLayout_IsRejectedAgainstMatch()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            AuthoritativeMatchRecord wrongSeed = fixture.Match with
            {
                BattleInit = fixture.CopyBattleInit(seed: fixture.Match.BattleInit.Seed + 1)
            };
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, wrongSeed, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));

            InitialEntitySpec[] moved = fixture.Match.BattleInit.InitialEntities;
            InitialEntitySpec first = moved[0];
            moved[0] = new InitialEntitySpec(
                first.ArchetypeId, first.GridX + 1, first.GridY,
                first.TeamId, first.OwnerPlayerId);
            AuthoritativeMatchRecord wrongLayout = fixture.Match with
            {
                BattleInit = fixture.CopyBattleInit(initialEntities: moved)
            };
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, wrongLayout, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
        }

        [Test]
        public async Task ReplayValidator_AuthorityIdentityAndSubmitter_AreRequired()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, null, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match, "foreign-player",
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.UnauthorizedSubmitter));

            ReplayFile wrongMatch = fixture.CopyReplay();
            wrongMatch.MatchId = "foreign-match";
            Assert.That((await validator.ValidateAsync(
                    wrongMatch, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));

            ReplayFile wrongNonce = fixture.CopyReplay();
            wrongNonce.SubmissionNonce = "foreign-nonce";
            Assert.That((await validator.ValidateAsync(
                    wrongNonce, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));
        }

        [Test]
        public async Task ReplayValidator_RejectsAllowedSubmitterOutsideBattleParticipants()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            AuthoritativeMatchRecord match = fixture.Match with
            {
                AllowedSubmitterPlayerIds = new[] { "third-party" }
            };

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                fixture.Replay, match, "third-party", fixture.SerializedBytes,
                CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.UnauthorizedSubmitter));
        }

        [Test]
        public async Task ReplayValidator_AuthorityMetadataAndSnapshot_AreValidated()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match with { SimulationSchemaHash = Hash('9') },
                    fixture.Submitter, fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.SchemaMismatch));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match with { ContentConfigHash = Hash('8') },
                    fixture.Submitter, fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { BattleInit = fixture.CopyBattleInit(tickRate: 30) },
                    fixture.Submitter, fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.TickRateMismatch));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match with { InitialSnapshot = null },
                    fixture.Submitter, fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));

            SimSnapshot snapshot = fixture.Match.InitialSnapshot;
            var corrupt = new SimSnapshot(
                snapshot.MatchId, snapshot.SessionId, snapshot.SchemaVersion,
                snapshot.ContentHash, snapshot.CompletedTick, snapshot.NextTick,
                snapshot.TimelineRevision, snapshot.UncompressedLength,
                snapshot.StateChecksum, snapshot.Payload, HashBytes(7));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match with { InitialSnapshot = corrupt },
                    fixture.Submitter, fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));

            byte[] tamperedPayload = snapshot.Payload;
            tamperedPayload[0] ^= 1;
            var corruptPayload = new SimSnapshot(
                snapshot.MatchId, snapshot.SessionId, snapshot.SchemaVersion,
                snapshot.ContentHash, snapshot.CompletedTick, snapshot.NextTick,
                snapshot.TimelineRevision, snapshot.UncompressedLength,
                snapshot.StateChecksum, tamperedPayload, snapshot.PayloadSha256);
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { InitialSnapshot = corruptPayload },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
        }

        [Test]
        public async Task ReplayValidator_CompletedReplay_ReturnsCanonicalTerminalResult()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                fixture.Replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.Valid));
            Assert.That(result.FailedTick, Is.EqualTo(fixture.Replay.FinalTick));
            Assert.That(result.ExpectedChecksum, Is.EqualTo(fixture.Replay.FinalChecksum));
            Assert.That(result.ActualChecksum, Is.EqualTo(fixture.Replay.FinalChecksum));
            Assert.That(result.FinalResult, Is.SameAs(fixture.Replay.FinalResult));
        }

        [Test]
        public async Task ReplayValidator_TamperedTickChecksum_IsRejectedAtThatTick()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CopyReplay();
            replay.Records[0].Checksum ^= 1;

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.ChecksumMismatch));
            Assert.That(result.FailedTick, Is.Zero);
            Assert.That(result.ExpectedChecksum, Is.EqualTo(replay.Records[0].Checksum));
            Assert.That(result.ActualChecksum, Is.EqualTo(fixture.Replay.Records[0].Checksum));
        }

        [Test]
        public async Task ReplayValidator_TamperedFinalChecksum_IsRejectedAfterPlayback()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CopyReplay();
            replay.FinalChecksum ^= 1;

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.FinalChecksumMismatch));
        }

        [Test]
        public async Task ReplayValidator_RecordAfterActualTerminal_IsTrailing()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CopyReplay();
            replay.FinalTick = 1;
            replay.Records.Add(new ReplayTickRecord
            {
                Tick = 1,
                Commands = new System.Collections.Generic.List<SerializableCommand>(),
                Checksum = replay.FinalChecksum
            });

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.TrailingRecord));
            Assert.That(result.FailedTick, Is.EqualTo(1));
        }

        [Test]
        public async Task ReplayValidator_SessionRejectedCommand_IsRejectedAtThatTick()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CopyReplay();
            replay.Records[0].Commands[0] = new SerializableCommand
            {
                Type = SimCommandType.Deploy.ToString(),
                Tick = 0,
                PlayerId = 0,
                Sequence = 1,
                UnitTypeId = 999,
                GridX = 3,
                GridY = 3
            };

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.CommandRejected));
            Assert.That(result.FailedTick, Is.Zero);
        }

        [Test]
        public async Task ReplayValidator_CommandRejectedDuringTick_IsRejectedAtThatTick()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CreateExecutionRejectedReplay();

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.CommandRejected));
            Assert.That(result.FailedTick, Is.Zero);
        }

        [Test]
        public async Task ReplayValidator_NonTerminalPlayback_IsTruncated()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CreateTruncatedReplay();

            ReplayValidationResult result = await fixture.CreateValidator().ValidateAsync(
                replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code, Is.EqualTo(ReplayValidationCode.Truncated));
        }

        [Test]
        public async Task ReplayValidator_TamperedReplayOrAuthorityResult_IsRejected()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayFile replay = fixture.CopyReplay();
            replay.FinalResult = AuthorityFixture.ChangeWinner(replay.FinalResult);

            Assert.That((await fixture.CreateValidator().ValidateAsync(
                    replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ResultMismatch));

            AuthoritativeMatchRecord match = fixture.Match with
            {
                ExpectedResult = AuthorityFixture.ChangeWinner(fixture.Match.ExpectedResult)
            };
            Assert.That((await fixture.CreateValidator().ValidateAsync(
                    fixture.Replay, match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ResultMismatch));
        }

        [TestCase("MatchId")]
        [TestCase("ResultId")]
        [TestCase("WinnerTeamId")]
        [TestCase("Stars")]
        [TestCase("LootGold")]
        [TestCase("LootElixir")]
        [TestCase("FinalTick")]
        [TestCase("FinalChecksum")]
        public async Task ReplayValidator_EveryResultField_IsAuthorityBound(string field)
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            SimBattleResult changed = AuthorityFixture.ChangeResultField(
                fixture.Replay.FinalResult, field);

            ReplayFile replay = fixture.CopyReplay();
            replay.FinalResult = changed;
            Assert.That((await fixture.CreateValidator().ValidateAsync(
                    replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ResultMismatch));

            Assert.That((await fixture.CreateValidator().ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { ExpectedResult = changed },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ResultMismatch));
        }

        [Test]
        public void ReplayValidator_UnexpectedPlaybackFailure_IsNotAuthorityMismatch()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = new ReplayValidator(
                new StubFactory(fixture.Match.InitialSnapshot, throwOnTick: true),
                ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await validator.ValidateAsync(
                    fixture.Replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None));
        }

        [Test]
        public async Task ReplayValidator_ExhaustedPlaybackBudget_IsRejectedAtTick()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = new ReplayValidator(
                new SimBattleSessionFactory(), ReplayValidationLimits.Approved,
                new RejectingBudget());

            ReplayValidationResult result = await validator.ValidateAsync(
                fixture.Replay, fixture.Match, fixture.Submitter,
                fixture.SerializedBytes, CancellationToken.None);

            Assert.That(result.Code,
                Is.EqualTo(ReplayValidationCode.ValidationBudgetExceeded));
            Assert.That(result.FailedTick, Is.Zero);
        }

        [Test]
        public void ReplayValidator_CancellationDuringPlayback_IsPropagated()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            var cancellation = new CancellationTokenSource();
            ReplayValidator validator = new ReplayValidator(
                new SimBattleSessionFactory(), ReplayValidationLimits.Approved,
                new CancellingBudget(cancellation));

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await validator.ValidateAsync(
                    fixture.Replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token));
        }

        [Test]
        public void AuthoritativeMatchRecord_DefensivelyCopiesMutableArrays()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            byte[] manifest = fixture.Match.CanonicalManifestBytes;
            string[] submitters = fixture.Match.AllowedSubmitterPlayerIds;
            var record = new AuthoritativeMatchRecord(
                fixture.Match.MatchId, fixture.Match.InitialStateHash,
                fixture.Match.SubmissionNonce, fixture.Match.SimulationSchemaHash,
                fixture.Match.ContentConfigHash, manifest, fixture.Match.BattleInit,
                fixture.Match.InitialSnapshot, fixture.Match.ExpectedResult, submitters);
            byte originalManifestByte = manifest[0];
            string originalSubmitter = submitters[0];

            manifest[0] ^= 1;
            submitters[0] = "mutated";

            Assert.That(record.CanonicalManifestBytes[0],
                Is.EqualTo(originalManifestByte));
            Assert.That(record.AllowedSubmitterPlayerIds[0],
                Is.EqualTo(originalSubmitter));

            record.CanonicalManifestBytes[0] ^= 1;
            record.AllowedSubmitterPlayerIds[0] = "mutated-again";
            Assert.That(record.CanonicalManifestBytes[0],
                Is.EqualTo(originalManifestByte));
            Assert.That(record.AllowedSubmitterPlayerIds[0],
                Is.EqualTo(originalSubmitter));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public async Task ReplayValidator_RejectsMissingSubmitterBeforeAllowList(
            string submitter)
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();
            AuthoritativeMatchRecord match = fixture.Match with
            {
                AllowedSubmitterPlayerIds = new[] { null, fixture.Submitter }
            };

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, match, submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.UnauthorizedSubmitter));
        }

        [Test]
        public async Task ReplayValidator_RejectsAuthorityIdentityAndContentGraphMismatches()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with
                    {
                        BattleInit = fixture.CopyBattleInit(matchId: "other-match")
                    },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));

            SimSnapshot expected = fixture.Match.InitialSnapshot;
            SimSnapshot foreignSnapshot = new SimSnapshot(
                "other-match", expected.SessionId, expected.SchemaVersion,
                expected.ContentHash, expected.CompletedTick, expected.NextTick,
                expected.TimelineRevision, expected.UncompressedLength,
                expected.StateChecksum, expected.Payload, expected.PayloadSha256);
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { InitialSnapshot = foreignSnapshot },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with
                    {
                        BattleInit = fixture.CopyBattleInit(contentHash: HashBytes(8))
                    },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
        }

        [Test]
        public async Task ReplayValidator_RejectsMalformedManifestAsConfigMismatch()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            foreach (byte[] manifest in new byte[][] { null, Array.Empty<byte>(), new byte[] { 1 } })
            {
                Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { CanonicalManifestBytes = manifest },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
            }
        }

        [Test]
        public async Task ReplayValidator_RejectsInitialSnapshotContentHashThatDisagreesWithManifest()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            SimSnapshot expected = fixture.Match.InitialSnapshot;
            var mismatchedContent = new SimSnapshot(
                expected.MatchId, expected.SessionId, expected.SchemaVersion,
                HashBytes(8), expected.CompletedTick, expected.NextTick,
                expected.TimelineRevision, expected.UncompressedLength,
                expected.StateChecksum, expected.Payload, expected.PayloadSha256);
            ReplayValidator validator = fixture.CreateValidator();

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, fixture.Match with
                    {
                        InitialSnapshot = mismatchedContent
                    },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
        }

        [Test]
        public async Task ReplayValidator_ValidatesExpectedAndActualSnapshotPayloadBytes()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            SimSnapshot expected = fixture.Match.InitialSnapshot;
            byte[] payload = expected.Payload;
            payload[0] ^= 1;
            var tamperedExpected = new SimSnapshot(
                expected.MatchId, expected.SessionId, expected.SchemaVersion,
                expected.ContentHash, expected.CompletedTick, expected.NextTick,
                expected.TimelineRevision, expected.UncompressedLength,
                expected.StateChecksum, payload, expected.PayloadSha256);
            ReplayValidator expectedValidator = new ReplayValidator(
                new StubFactory(expected), ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());
            Assert.That((await expectedValidator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { InitialSnapshot = tamperedExpected },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));

            byte[] actualPayload = expected.Payload;
            actualPayload[0] ^= 1;
            var tamperedActual = new SimSnapshot(
                expected.MatchId, expected.SessionId, expected.SchemaVersion,
                expected.ContentHash, expected.CompletedTick, expected.NextTick,
                expected.TimelineRevision, expected.UncompressedLength,
                expected.StateChecksum, actualPayload, expected.PayloadSha256);
            ReplayValidator actualValidator = new ReplayValidator(
                new StubFactory(tamperedActual), ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());
            Assert.That((await actualValidator.ValidateAsync(
                    fixture.Replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
        }

        [Test]
        public async Task ReplayValidator_RejectsFactoryAndSessionFailures()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();

            foreach (ISimBattleSessionFactory factory in new ISimBattleSessionFactory[]
            {
                new ThrowingFactory(),
                new NullFactory(),
                new StubFactory(fixture.Match.InitialSnapshot, throwOnStart: true),
                new StubFactory(fixture.Match.InitialSnapshot, rejectStart: true),
                new StubFactory(fixture.Match.InitialSnapshot, throwOnCapture: true),
                new StubFactory(null),
            })
            {
                ReplayValidator validator = new ReplayValidator(
                    factory, ReplayValidationLimits.Approved,
                    new UnlimitedReplayValidationBudget());
                Assert.That((await validator.ValidateAsync(
                        fixture.Replay, fixture.Match, fixture.Submitter,
                        fixture.SerializedBytes, CancellationToken.None)).Code,
                    Is.EqualTo(ReplayValidationCode.InitialStateMismatch));
            }
        }

        [Test]
        public void ReplayValidator_CallerCancellationPrecedesFactoryWork()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ReplayValidator validator = new ReplayValidator(
                new ThrowingFactory(), ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());

            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await validator.ValidateAsync(
                    fixture.Replay, fixture.Match, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token));
        }

        [Test]
        public async Task ReplayValidator_DeterministicRejectionsPrecedeCancellationInOrder()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            ReplayFile structurallyInvalid = fixture.CopyReplay();
            structurallyInvalid.Records.Clear();
            Assert.That((await validator.ValidateAsync(
                    structurallyInvalid, null, "foreign-player", 0,
                    cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.FileTooLarge));

            ReplayFile wrongMatch = fixture.CopyReplay();
            wrongMatch.MatchId = "foreign-match";
            wrongMatch.SubmissionNonce = "foreign-nonce";
            wrongMatch.SimulationSchemaHash = Hash('2');
            wrongMatch.ContentConfigHash = Hash('3');
            AuthoritativeMatchRecord wrongTickRate = fixture.Match with
            {
                BattleInit = fixture.CopyBattleInit(tickRate: 30),
                InitialSnapshot = null
            };
            Assert.That((await validator.ValidateAsync(
                    wrongMatch, wrongTickRate, "foreign-player",
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));

            ReplayFile wrongNonce = fixture.CopyReplay();
            wrongNonce.SubmissionNonce = "foreign-nonce";
            wrongNonce.SimulationSchemaHash = Hash('2');
            wrongNonce.ContentConfigHash = Hash('3');
            Assert.That((await validator.ValidateAsync(
                    wrongNonce, wrongTickRate, "foreign-player",
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.UnauthorizedSubmitter));
            Assert.That((await validator.ValidateAsync(
                    wrongNonce, wrongTickRate, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.MatchMismatch));

            ReplayFile wrongSchema = fixture.CopyReplay();
            wrongSchema.SimulationSchemaHash = Hash('2');
            wrongSchema.ContentConfigHash = Hash('3');
            Assert.That((await validator.ValidateAsync(
                    wrongSchema, wrongTickRate, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.SchemaMismatch));

            ReplayFile wrongConfig = fixture.CopyReplay();
            wrongConfig.ContentConfigHash = Hash('3');
            Assert.That((await validator.ValidateAsync(
                    wrongConfig, wrongTickRate, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, wrongTickRate, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.TickRateMismatch));

            AuthoritativeMatchRecord missingSnapshot = fixture.Match with
            {
                InitialSnapshot = null
            };
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay, missingSnapshot, fixture.Submitter,
                    fixture.SerializedBytes, cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.InitialStateMismatch));

            SimSnapshot expected = fixture.Match.InitialSnapshot;
            var oversizedSnapshot = new SimSnapshot(
                expected.MatchId, expected.SessionId, expected.SchemaVersion,
                expected.ContentHash, expected.CompletedTick, expected.NextTick,
                expected.TimelineRevision,
                ReplayValidationLimits.Approved.MaxDecompressedSnapshotBytes + 1,
                expected.StateChecksum, expected.Payload, expected.PayloadSha256);
            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { InitialSnapshot = oversizedSnapshot },
                    fixture.Submitter, fixture.SerializedBytes,
                    cancellation.Token)).Code,
                Is.EqualTo(ReplayValidationCode.SnapshotTooLarge));
        }

        [Test]
        public async Task ReplayValidator_BindsAuthorityConfigHashToParsedManifest()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            ReplayValidator validator = fixture.CreateValidator();

            Assert.That((await validator.ValidateAsync(
                    fixture.Replay,
                    fixture.Match with
                    {
                        CanonicalManifestBytes = AuthorityFixture.AlternateManifestBytes()
                    },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.ConfigMismatch));
        }

        [Test]
        public async Task ReplayValidator_NullBattleInitHasStableTickRateRejection()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();

            Assert.That((await fixture.CreateValidator().ValidateAsync(
                    fixture.Replay,
                    fixture.Match with { BattleInit = null },
                    fixture.Submitter, fixture.SerializedBytes,
                    CancellationToken.None)).Code,
                Is.EqualTo(ReplayValidationCode.TickRateMismatch));
        }

        [Test]
        public void AuthoritativeMatchRecord_WithInitializerDefensivelyCopiesArrays()
        {
            AuthorityFixture fixture = AuthorityFixture.Create();
            byte[] manifest = fixture.Match.CanonicalManifestBytes;
            string[] submitters = fixture.Match.AllowedSubmitterPlayerIds;
            byte expectedManifestByte = manifest[0];
            string expectedSubmitter = submitters[0];
            AuthoritativeMatchRecord derived = fixture.Match with
            {
                CanonicalManifestBytes = manifest,
                AllowedSubmitterPlayerIds = submitters
            };

            manifest[0] ^= 1;
            submitters[0] = "mutated-source";
            derived.CanonicalManifestBytes[0] ^= 1;
            derived.AllowedSubmitterPlayerIds[0] = "mutated-getter";

            Assert.That(derived.CanonicalManifestBytes[0],
                Is.EqualTo(expectedManifestByte));
            Assert.That(derived.AllowedSubmitterPlayerIds[0],
                Is.EqualTo(expectedSubmitter));
        }

        private static string Hash(char value) => new string(value, 64);

        private static byte[] HashBytes(byte value) =>
            Enumerable.Repeat(value, 32).ToArray();

        private sealed class AuthorityFixture
        {
            private const string SubmitterValue = "player-0";

            public ReplayFile Replay { get; private set; }
            public AuthoritativeMatchRecord Match { get; private set; }
            public string Submitter => SubmitterValue;
            public int SerializedBytes => 512;

            public static AuthorityFixture Create()
            {
                byte[] manifestBytes = ManifestBytes();
                byte[] contentHash =
                    DeterministicContentManifest.Parse(manifestBytes).ContentHash;
                var init = new BattleInit(
                    1, SimComponentSchema.Version, "match-1", "session-1", 42,
                    ReplayFormat.TickRate, SimCombatMode.VillageAttack, contentHash,
                    new[]
                    {
                        new SimPlayerIdentity(0, SubmitterValue),
                        new SimPlayerIdentity(1, "player-1")
                    },
                    new[]
                    {
                        new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
                        new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1)
                    },
                    6);
                SimSnapshot snapshot;
                TickResult terminal;
                using (ISimBattleSession session =
                    new SimBattleSessionFactory().Create(manifestBytes))
                {
                    Assert.That(session.Start(init).Accepted, Is.True);
                    snapshot = session.CaptureSnapshot();
                    Assert.That(session.Submit(new EndDeploymentCmd
                    {
                        Tick = 0,
                        PlayerId = 0,
                        Sequence = 1
                    }).Accepted, Is.True);
                    terminal = session.Tick();
                    Assert.That(terminal.Code, Is.EqualTo(TickCode.Ended));
                }
                string initialHash = HashText.ToLowerHex(snapshot.PayloadSha256);
                var replay = new ReplayFile
                {
                    FormatVersion = ReplayFormat.CurrentVersion,
                    SimulationSchemaHash = Hash('1'),
                    ContentConfigHash = HashText.ToLowerHex(contentHash),
                    TickRate = ReplayFormat.TickRate,
                    MatchId = init.MatchId,
                    InitialStateHash = initialHash,
                    SubmissionNonce = "nonce-1",
                    FinalTick = 0,
                    Records = new System.Collections.Generic.List<ReplayTickRecord>
                    {
                        new ReplayTickRecord
                        {
                            Tick = 0,
                            Checksum = terminal.Checksum,
                            Commands = new System.Collections.Generic.List<SerializableCommand>
                            {
                                new SerializableCommand
                                {
                                    Type = SimCommandType.EndDeployment.ToString(),
                                    Tick = 0,
                                    PlayerId = 0,
                                    Sequence = 1
                                }
                            }
                        }
                    },
                    FinalChecksum = terminal.Checksum,
                    FinalResult = terminal.Result
                };
                return new AuthorityFixture
                {
                    Replay = replay,
                    Match = new AuthoritativeMatchRecord(
                        init.MatchId,
                        initialHash,
                        replay.SubmissionNonce,
                        replay.SimulationSchemaHash,
                        replay.ContentConfigHash,
                        manifestBytes,
                        init,
                        snapshot,
                        terminal.Result,
                        new[] { SubmitterValue })
                };
            }

            public ReplayValidator CreateValidator() => new ReplayValidator(
                new SimBattleSessionFactory(),
                ReplayValidationLimits.Approved,
                new UnlimitedReplayValidationBudget());

            public BattleInit CopyBattleInit(
                int? seed = null,
                int? tickRate = null,
                string matchId = null,
                byte[] contentHash = null,
                InitialEntitySpec[] initialEntities = null) =>
                new BattleInit(
                    Match.BattleInit.ProtocolVersion,
                    Match.BattleInit.SchemaVersion,
                    matchId ?? Match.BattleInit.MatchId,
                    Match.BattleInit.SessionId,
                    seed ?? Match.BattleInit.Seed,
                    tickRate ?? Match.BattleInit.TickRate,
                    Match.BattleInit.Mode,
                    contentHash ?? Match.BattleInit.ContentHash,
                    Match.BattleInit.Players,
                    initialEntities ?? Match.BattleInit.InitialEntities,
                    Match.BattleInit.InputDeadlineTicks);

            public ReplayFile CopyReplay() => new ReplayFile
            {
                FormatVersion = Replay.FormatVersion,
                SimulationSchemaHash = Replay.SimulationSchemaHash,
                ContentConfigHash = Replay.ContentConfigHash,
                TickRate = Replay.TickRate,
                MatchId = Replay.MatchId,
                InitialStateHash = Replay.InitialStateHash,
                SubmissionNonce = Replay.SubmissionNonce,
                FinalTick = Replay.FinalTick,
                FinalChecksum = Replay.FinalChecksum,
                FinalResult = Replay.FinalResult,
                Records = Replay.Records.Select(record => new ReplayTickRecord
                {
                    Tick = record.Tick,
                    Checksum = record.Checksum,
                    Commands = record.Commands.Select(command => new SerializableCommand
                    {
                        Type = command.Type,
                        Tick = command.Tick,
                        PlayerId = command.PlayerId,
                        Sequence = command.Sequence,
                        UnitTypeId = command.UnitTypeId,
                        EntityId = command.EntityId,
                        CasterId = command.CasterId,
                        SkillId = command.SkillId,
                        GridX = command.GridX,
                        TargetX = command.TargetX,
                        GridY = command.GridY,
                        TargetY = command.TargetY
                    }).ToList()
                }).ToList()
            };

            public ReplayFile CreateTruncatedReplay()
            {
                ulong checksum;
                using (ISimBattleSession session =
                    new SimBattleSessionFactory().Create(Match.CanonicalManifestBytes))
                {
                    Assert.That(session.Start(Match.BattleInit).Accepted, Is.True);
                    checksum = session.Tick().Checksum;
                }
                ReplayFile replay = CopyReplay();
                replay.Records[0].Commands.Clear();
                replay.Records[0].Checksum = checksum;
                replay.FinalChecksum = checksum;
                return replay;
            }

            public ReplayFile CreateExecutionRejectedReplay()
            {
                var commands = new[]
                {
                    new DeployCmd
                    {
                        Tick = 0, PlayerId = 0, Sequence = 1,
                        UnitTypeId = 10, GridX = 3, GridY = 3
                    },
                    new DeployCmd
                    {
                        Tick = 0, PlayerId = 0, Sequence = 2,
                        UnitTypeId = 10, GridX = 3, GridY = 3
                    }
                };
                TickResult tick;
                using (ISimBattleSession session =
                    new SimBattleSessionFactory().Create(Match.CanonicalManifestBytes))
                {
                    Assert.That(session.Start(Match.BattleInit).Accepted, Is.True);
                    foreach (DeployCmd command in commands)
                        Assert.That(session.Submit(command).Accepted, Is.True);
                    tick = session.Tick();
                    Assert.That(tick.CommandOutcomes[0].Disposition,
                        Is.EqualTo(CommandDisposition.Accepted));
                    Assert.That(tick.CommandOutcomes[1].Disposition,
                        Is.EqualTo(CommandDisposition.InvalidPayload));
                }

                ReplayFile replay = CopyReplay();
                replay.Records[0].Commands = commands.Select(command =>
                    new SerializableCommand
                    {
                        Type = SimCommandType.Deploy.ToString(),
                        Tick = command.Tick,
                        PlayerId = command.PlayerId,
                        Sequence = command.Sequence,
                        UnitTypeId = command.UnitTypeId,
                        GridX = command.GridX,
                        GridY = command.GridY
                    }).ToList();
                replay.Records[0].Checksum = tick.Checksum;
                replay.FinalChecksum = tick.Checksum;
                return replay;
            }

            public static SimBattleResult ChangeWinner(SimBattleResult result) =>
                new SimBattleResult(result.MatchId, result.ResultId,
                    result.WinnerTeamId == SimTeam.Attacker
                        ? SimTeam.Defender : SimTeam.Attacker,
                    result.Stars, result.LootGold, result.LootElixir,
                    result.FinalTick, result.FinalChecksum);

            public static SimBattleResult ChangeResultField(
                SimBattleResult result, string field) => new SimBattleResult(
                    field == "MatchId" ? result.MatchId + "-changed" : result.MatchId,
                    field == "ResultId" ? result.ResultId + "-changed" : result.ResultId,
                    field == "WinnerTeamId" ? result.WinnerTeamId + 1 : result.WinnerTeamId,
                    field == "Stars" ? result.Stars + 1 : result.Stars,
                    field == "LootGold" ? result.LootGold + 1 : result.LootGold,
                    field == "LootElixir" ? result.LootElixir + 1 : result.LootElixir,
                    field == "FinalTick" ? result.FinalTick + 1 : result.FinalTick,
                    field == "FinalChecksum" ? result.FinalChecksum + 1 : result.FinalChecksum);

            public static byte[] AlternateManifestBytes() =>
                new DeterministicContentManifest(1, new[]
                {
                    new SimArchetypeDefinition(1, "AlternateTownHall", 500, 0, 0, 0, 0)
                }).ToCanonicalBytes();

            private static byte[] ManifestBytes() =>
                new DeterministicContentManifest(1, new[]
                {
                    new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                    new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                    new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 20)
                }, new[]
                {
                    new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                        System.Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                        System.Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                        System.Array.Empty<int>()),
                    new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                        new[] { 0, 100, 200, 300, 400 })
                }).ToCanonicalBytes();
        }

        private sealed class RejectingBudget : IReplayValidationBudget
        {
            public bool TryConsumeTick(int commandCount) => false;
        }

        private sealed class CancellingBudget : IReplayValidationBudget
        {
            private readonly CancellationTokenSource _cancellation;

            public CancellingBudget(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public bool TryConsumeTick(int commandCount)
            {
                _cancellation.Cancel();
                return true;
            }
        }

        private sealed class ThrowingFactory : ISimBattleSessionFactory
        {
            public ISimBattleSession Create(byte[] canonicalManifestBytes,
                ISimLogger logger = null) => throw new InvalidOperationException();
        }

        private sealed class NullFactory : ISimBattleSessionFactory
        {
            public ISimBattleSession Create(byte[] canonicalManifestBytes,
                ISimLogger logger = null) => null;
        }

        private sealed class StubFactory : ISimBattleSessionFactory
        {
            private readonly SimSnapshot _snapshot;
            private readonly bool _throwOnStart;
            private readonly bool _rejectStart;
            private readonly bool _throwOnCapture;
            private readonly bool _throwOnTick;

            public StubFactory(SimSnapshot snapshot, bool throwOnStart = false,
                bool rejectStart = false, bool throwOnCapture = false,
                bool throwOnTick = false)
            {
                _snapshot = snapshot;
                _throwOnStart = throwOnStart;
                _rejectStart = rejectStart;
                _throwOnCapture = throwOnCapture;
                _throwOnTick = throwOnTick;
            }

            public ISimBattleSession Create(byte[] canonicalManifestBytes,
                ISimLogger logger = null) => new StubSession(
                    _snapshot, _throwOnStart, _rejectStart, _throwOnCapture,
                    _throwOnTick);
        }

        private sealed class StubSession : ISimBattleSession
        {
            private readonly SimSnapshot _snapshot;
            private readonly bool _throwOnStart;
            private readonly bool _rejectStart;
            private readonly bool _throwOnCapture;
            private readonly bool _throwOnTick;

            public StubSession(SimSnapshot snapshot, bool throwOnStart,
                bool rejectStart, bool throwOnCapture, bool throwOnTick)
            {
                _snapshot = snapshot;
                _throwOnStart = throwOnStart;
                _rejectStart = rejectStart;
                _throwOnCapture = throwOnCapture;
                _throwOnTick = throwOnTick;
            }

            public SimBattleState State => null;

            public StartResult Start(BattleInit init)
            {
                if (_throwOnStart) throw new InvalidOperationException();
                return new StartResult(_rejectStart
                    ? StartCode.InvalidMatch
                    : StartCode.Accepted, null);
            }

            public CommandSubmitResult Submit(ISimCommand command) =>
                new CommandSubmitResult(CommandDisposition.Accepted);

            public TickResult Tick()
            {
                if (_throwOnTick) throw new InvalidOperationException();
                return default;
            }

            public SimSnapshot CaptureSnapshot()
            {
                if (_throwOnCapture) throw new InvalidOperationException();
                return _snapshot;
            }

            public SnapshotRestoreResult RestoreSnapshot(SimSnapshot snapshot) => default;

            public void Dispose()
            {
            }
        }
    }
}
