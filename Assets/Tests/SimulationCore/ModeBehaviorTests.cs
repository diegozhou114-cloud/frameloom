using System;
using System.Linq;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class ModeBehaviorTests
    {
        [Test]
        public void SupportedModes_MatchApprovedBehaviorMatrix()
        {
            DeterministicContentManifest manifest = ModeTestFixture.Manifest();
            SimCombatMode[] expectedModes =
            {
                SimCombatMode.VillageAttack,
                SimCombatMode.BuilderBase,
                SimCombatMode.ClanWar,
                SimCombatMode.Campaign,
            };

            Assert.That(manifest.Modes.Select(profile => profile.Mode), Is.EqualTo(expectedModes));
            AssertProfile(manifest.GetMode(SimCombatMode.VillageAttack), true, 40, 600, 3600,
                Array.Empty<int>());
            AssertProfile(manifest.GetMode(SimCombatMode.BuilderBase), true, 30, 600, 3600,
                Array.Empty<int>());
            AssertProfile(manifest.GetMode(SimCombatMode.ClanWar), true, 40, 600, 3600,
                Array.Empty<int>());
            AssertProfile(manifest.GetMode(SimCombatMode.Campaign), false, 0, 0, 2400,
                new[] { 0, 100, 200, 300, 400 });

        }

        [Test]
        public void SupportedModes_DefendCopies()
        {
            DeterministicContentManifest manifest = ModeTestFixture.Manifest();
            int[] waves = manifest.GetMode(SimCombatMode.Campaign).WaveTicks;
            waves[0] = 999;
            Assert.That(manifest.GetMode(SimCombatMode.Campaign).WaveTicks,
                Is.EqualTo(new[] { 0, 100, 200, 300, 400 }));

            var copiedModes = manifest.Modes as System.Collections.Generic.IList<SimModeProfile>;
            Assert.That(copiedModes, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => copiedModes[0] = copiedModes[1]);
            Assert.That(manifest.GetMode(SimCombatMode.VillageAttack).Mode,
                Is.EqualTo(SimCombatMode.VillageAttack));
        }

        [Test]
        public void CampaignAndBuilderBase_StartupDoesNotPrematurelyEnd()
        {
            foreach (SimCombatMode mode in new[] { SimCombatMode.Campaign, SimCombatMode.BuilderBase })
            {
                SimModeProfile profile = ModeTestFixture.Manifest().GetMode(mode);
                var rules = new SimModeRules(profile);
                int futureWaves = mode == SimCombatMode.Campaign
                    ? profile.WaveTicks.Length - 1
                    : 0;
                bool deploymentSealed = !profile.ManualDeployment;

                Assert.That(rules.Evaluate(new SimModeEvaluationInput(
                    0, 0, 1, 0, futureWaves, false, deploymentSealed)),
                    Is.Null, mode.ToString());
            }
        }

        [Test]
        public void CombatResult_UnityAndDotNet_AreIdentical()
        {
            SimBattleResult result = ModeTestFixture.EvaluateVillageAttackVector();
            Assert.That(result.ResultId, Is.EqualTo("mode-vector:120:00000000000A11CE"));
            Assert.That(result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
            Assert.That(result.FinalChecksum, Is.EqualTo(0xA11CEUL));
            Assert.That(result.Stars, Is.EqualTo(3));
            Assert.That(result.LootGold, Is.EqualTo(500));
            Assert.That(result.LootElixir, Is.EqualTo(300));
        }

        [Test]
        public void SimulationModes_ImplementApprovedStartupWaveAndTerminalMatrix()
        {
            DeterministicContentManifest manifest = ModeTestFixture.Manifest();
            Assert.That(manifest.Modes.Count, Is.EqualTo(4));
            foreach (SimModeProfile profile in manifest.Modes)
            {
                var rules = new SimModeRules(profile);
                int futureWaves = profile.WaveTicks.Length > 0 ? 1 : 0;
                bool deploymentSealed = !profile.ManualDeployment;

                Assert.That(rules.Evaluate(new SimModeEvaluationInput(
                    0, 0, 1, 0, futureWaves, false, deploymentSealed)),
                    Is.Null, profile.Mode.ToString());

                SimTerminalOutcome exhausted = rules.Evaluate(new SimModeEvaluationInput(
                    1, 0, 1, 0, 0, false, true));
                Assert.That(exhausted.WinnerTeamId, Is.EqualTo(SimTeam.Defender),
                    profile.Mode.ToString());

                SimTerminalOutcome attackerWin = rules.Evaluate(new SimModeEvaluationInput(
                    1, 1, 0, 10000, 0, false, true));
                Assert.That(attackerWin.WinnerTeamId, Is.EqualTo(SimTeam.Attacker),
                    profile.Mode.ToString());
                Assert.That(attackerWin.Stars, Is.EqualTo(3), profile.Mode.ToString());

                SimTerminalOutcome draw = rules.Evaluate(new SimModeEvaluationInput(
                    1, 0, 0, 0, 0, false, true));
                Assert.That(draw.WinnerTeamId, Is.EqualTo(SimTeam.None),
                    profile.Mode.ToString());

                SimTerminalOutcome timeout = rules.Evaluate(new SimModeEvaluationInput(
                    profile.TimeoutTicks, 1, 1, 0, 0, true, true));
                Assert.That(timeout.WinnerTeamId, Is.EqualTo(SimTeam.Defender),
                    profile.Mode.ToString());
            }
        }

        [Test]
        public void ManifestConstructor_RejectsInvalidModeTablesAndProfiles()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new DeterministicContentManifest(1, Array.Empty<SimArchetypeDefinition>(), null));
            Assert.Throws<ArgumentException>(() =>
                new DeterministicContentManifest(1, Array.Empty<SimArchetypeDefinition>(),
                    new SimModeProfile[] { null }));
            Assert.Throws<ArgumentException>(() =>
                new DeterministicContentManifest(1, Array.Empty<SimArchetypeDefinition>(), new[]
                {
                    Profile(SimCombatMode.VillageAttack),
                    Profile(SimCombatMode.VillageAttack),
                    Profile(SimCombatMode.ClanWar),
                    Profile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 0 }),
                }));
            Assert.Throws<ArgumentException>(() =>
                new DeterministicContentManifest(1, Array.Empty<SimArchetypeDefinition>(), new[]
                {
                    Profile(SimCombatMode.VillageAttack),
                    Profile(SimCombatMode.BuilderBase),
                    Profile(SimCombatMode.ClanWar),
                }));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Profile((SimCombatMode)99));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Profile(SimCombatMode.VillageAttack, true, -1, 600, 3600, Array.Empty<int>()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Profile(SimCombatMode.VillageAttack, true, 40, -1, 3600, Array.Empty<int>()));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Profile(SimCombatMode.VillageAttack, true, 40, 600, -1, Array.Empty<int>()));
            Assert.Throws<ArgumentNullException>(() =>
                new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400, null));
            Assert.Throws<ArgumentException>(() =>
                Profile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { -1 }));
            Assert.Throws<ArgumentException>(() =>
                Profile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 0, 0 }));
            Assert.Throws<ArgumentException>(() =>
                Profile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 100, 0 }));
        }

        [Test]
        public void ManifestCanonicalization_SortsModeRowsAndIncludesEveryProfileField()
        {
            SimModeProfile[] profiles = ModeTestFixture.Profiles();
            byte[] baselineBytes = new DeterministicContentManifest(1,
                Array.Empty<SimArchetypeDefinition>(), profiles).ToCanonicalBytes();
            byte[] permutedBytes = new DeterministicContentManifest(1,
                Array.Empty<SimArchetypeDefinition>(), profiles.Reverse().ToArray()).ToCanonicalBytes();
            Assert.That(permutedBytes, Is.EqualTo(baselineBytes));

            byte[] baselineHash = new DeterministicContentManifest(1,
                Array.Empty<SimArchetypeDefinition>(), profiles).ContentHash;
            SimModeProfile[][] variants =
            {
                Replace(profiles, SimCombatMode.VillageAttack,
                    Profile(SimCombatMode.VillageAttack, false, 40, 600, 3600, Array.Empty<int>())),
                Replace(profiles, SimCombatMode.VillageAttack,
                    Profile(SimCombatMode.VillageAttack, true, 41, 600, 3600, Array.Empty<int>())),
                Replace(profiles, SimCombatMode.VillageAttack,
                    Profile(SimCombatMode.VillageAttack, true, 40, 601, 3600, Array.Empty<int>())),
                Replace(profiles, SimCombatMode.VillageAttack,
                    Profile(SimCombatMode.VillageAttack, true, 40, 600, 3601, Array.Empty<int>())),
                Replace(profiles, SimCombatMode.Campaign,
                    Profile(SimCombatMode.Campaign, false, 0, 0, 2400,
                        new[] { 0, 100, 200, 300, 401 })),
            };

            foreach (SimModeProfile[] variant in variants)
            {
                var manifest = new DeterministicContentManifest(1,
                    Array.Empty<SimArchetypeDefinition>(), variant);
                Assert.That(manifest.ToCanonicalBytes(), Is.Not.EqualTo(baselineBytes));
                Assert.That(manifest.ContentHash, Is.Not.EqualTo(baselineHash));
            }
        }

        [Test]
        public void ManifestParse_AcceptsLegacyPayloadAndRejectsMalformedModeEncoding()
        {
            byte[] legacy = LegacyManifestBytes();
            DeterministicContentManifest parsedLegacy = DeterministicContentManifest.Parse(legacy);
            Assert.That(parsedLegacy.Modes, Is.Empty);

            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles(), manualOverride: 2)));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(new[]
                {
                    Profile(SimCombatMode.VillageAttack),
                    Profile(SimCombatMode.VillageAttack),
                    Profile(SimCombatMode.ClanWar),
                    Profile(SimCombatMode.Campaign, false, 0, 0, 2400, new[] { 0 }),
                })));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles().Take(3).ToArray())));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles().Reverse().ToArray())));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles(), waveOverride: new[] { 0, 200, 100 })));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles(), waveOverride: new[] { 0, 100, 100 })));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                RawManifestBytes(ModeTestFixture.Profiles(), waveOverride: new[] { -1 })));
            Assert.Throws<FormatException>(() => DeterministicContentManifest.Parse(
                SessionTestDriver.ManifestBytes().Concat(new byte[] { 0xFF }).ToArray()));
        }

        [Test]
        public void Evaluate_DeploymentAndFutureWaveGatesPreventPrematureTerminalState()
        {
            var manualRules = new SimModeRules(
                ModeTestFixture.Manifest().GetMode(SimCombatMode.VillageAttack));
            Assert.That(manualRules.Evaluate(Input(0, 0, 1, 0, 0, false, false)), Is.Null);
            Assert.That(manualRules.Evaluate(Input(600, 0, 1, 0, 0, false, false)), Is.Null);

            SimTerminalOutcome sealedOutcome = manualRules.Evaluate(
                Input(1, 0, 1, 0, 0, false, true));
            Assert.That(sealedOutcome.WinnerTeamId, Is.EqualTo(SimTeam.Defender));

            var campaignRules = new SimModeRules(
                ModeTestFixture.Manifest().GetMode(SimCombatMode.Campaign));
            Assert.That(campaignRules.Evaluate(Input(50, 0, 1, 0, 1, false, true)), Is.Null);
            SimTerminalOutcome wavesExhausted = campaignRules.Evaluate(
                Input(401, 0, 1, 0, 0, false, true));
            Assert.That(wavesExhausted.WinnerTeamId, Is.EqualTo(SimTeam.Defender));
        }

        [Test]
        public void Evaluate_ResolvesAttackerDefenderDrawAndTimeoutCanonically()
        {
            var rules = new SimModeRules(
                ModeTestFixture.Manifest().GetMode(SimCombatMode.VillageAttack));

            Assert.That(rules.Evaluate(Input(10, 1, 0, 10000, 0, false, true)).WinnerTeamId,
                Is.EqualTo(SimTeam.Attacker));
            Assert.That(rules.Evaluate(Input(10, 0, 1, 5000, 0, false, true)).WinnerTeamId,
                Is.EqualTo(SimTeam.Defender));
            Assert.That(rules.Evaluate(Input(10, 0, 0, 10000, 0, false, true)).WinnerTeamId,
                Is.EqualTo(SimTeam.None));
            Assert.That(rules.Evaluate(Input(10, 1, 1, 10000, 0, true, true)).WinnerTeamId,
                Is.EqualTo(SimTeam.Defender));
            Assert.That(rules.Evaluate(Input(10, 0, 0, 10000, 0, true, true)).WinnerTeamId,
                Is.EqualTo(SimTeam.None));
            Assert.That(rules.Evaluate(Input(10, 1, 1, 5000, 0, false, true)), Is.Null);
        }

        [TestCase(0, 0, 0, 0)]
        [TestCase(1, 1, 0, 0)]
        [TestCase(4999, 1, 249, 149)]
        [TestCase(5000, 2, 250, 150)]
        [TestCase(9999, 2, 499, 299)]
        [TestCase(10000, 3, 500, 300)]
        public void Evaluate_UsesExactIntegerStarAndLootBoundaries(
            int destructionBasisPoints, int stars, int gold, int elixir)
        {
            foreach (SimModeProfile profile in ModeTestFixture.Manifest().Modes)
            {
                var rules = new SimModeRules(profile);
                SimTerminalOutcome outcome = rules.Evaluate(
                    Input(120, 1, 0, destructionBasisPoints, 0, false, true));
                Assert.That(outcome.Stars, Is.EqualTo(stars), profile.Mode.ToString());
                Assert.That(outcome.LootGold, Is.EqualTo(gold), profile.Mode.ToString());
                Assert.That(outcome.LootElixir, Is.EqualTo(elixir), profile.Mode.ToString());
            }
        }

        [Test]
        public void Evaluate_DefenderAndDrawReceiveNoRewards()
        {
            var rules = new SimModeRules(
                ModeTestFixture.Manifest().GetMode(SimCombatMode.ClanWar));
            SimTerminalOutcome defender = rules.Evaluate(
                Input(100, 0, 1, 10000, 0, false, true));
            SimTerminalOutcome draw = rules.Evaluate(
                Input(100, 0, 0, 10000, 0, false, true));

            Assert.That(new[] { defender.Stars, defender.LootGold, defender.LootElixir },
                Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(new[] { draw.Stars, draw.LootGold, draw.LootElixir },
                Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void EvaluationInput_RejectsInvalidNumericState()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(-1, 1, 1, 0, 0, false, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(0, -1, 1, 0, 0, false, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(0, 1, -1, 0, 0, false, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(0, 1, 1, -1, 0, false, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(0, 1, 1, 10001, 0, false, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Input(0, 1, 1, 0, -1, false, true));
        }

        [Test]
        public void BattleResult_FromOutcomeMapsFieldsAndCanonicalResultIdExactly()
        {
            SimBattleResult result = ModeTestFixture.EvaluateVillageAttackVector();

            Assert.That(result.MatchId, Is.EqualTo("mode-vector"));
            Assert.That(result.ResultId, Is.EqualTo("mode-vector:120:00000000000A11CE"));
            Assert.That(result.WinnerTeamId, Is.EqualTo(SimTeam.Attacker));
            Assert.That(result.Stars, Is.EqualTo(3));
            Assert.That(result.LootGold, Is.EqualTo(500));
            Assert.That(result.LootElixir, Is.EqualTo(300));
            Assert.That(result.FinalTick, Is.EqualTo(120));
            Assert.That(result.FinalChecksum, Is.EqualTo(0xA11CEUL));

            Assert.Throws<ArgumentNullException>(() =>
                SimBattleResult.FromOutcome(null, new SimTerminalOutcome(SimTeam.None, 0, 0, 0), 0, 0));
            Assert.Throws<ArgumentException>(() =>
                SimBattleResult.FromOutcome(string.Empty,
                    new SimTerminalOutcome(SimTeam.None, 0, 0, 0), 0, 0));
            Assert.Throws<ArgumentNullException>(() =>
                SimBattleResult.FromOutcome("match", null, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SimBattleResult.FromOutcome("match",
                    new SimTerminalOutcome(SimTeam.None, 0, 0, 0), -1, 0));
        }

        [Test]
        public void SharedFixture_ProducesCanonicalManifestAndResultVector()
        {
            byte[] bytes = SessionTestDriver.ManifestBytes();
            DeterministicContentManifest manifest = ModeTestFixture.Manifest();
            Assert.That(manifest.ToCanonicalBytes(), Is.EqualTo(bytes));
            Assert.That(manifest.ContentHash.Length, Is.EqualTo(32));
            Assert.That(ModeTestFixture.EvaluateVillageAttackVector().ResultId,
                Is.EqualTo("mode-vector:120:00000000000A11CE"));
        }

        private static void AssertProfile(SimModeProfile profile, bool manualDeployment,
            int maxDeployCount, int deployDurationTicks, int timeoutTicks, int[] waveTicks)
        {
            Assert.That(profile, Is.Not.Null);
            Assert.That(profile.ManualDeployment, Is.EqualTo(manualDeployment));
            Assert.That(profile.MaxDeployCount, Is.EqualTo(maxDeployCount));
            Assert.That(profile.DeployDurationTicks, Is.EqualTo(deployDurationTicks));
            Assert.That(profile.TimeoutTicks, Is.EqualTo(timeoutTicks));
            Assert.That(profile.WaveTicks, Is.EqualTo(waveTicks));
        }

        private static SimModeProfile Profile(SimCombatMode mode, bool manual = true,
            int maxDeploy = 40, int deployTicks = 600, int timeoutTicks = 3600,
            int[] waves = null) =>
            new SimModeProfile(mode, manual, maxDeploy, deployTicks, timeoutTicks,
                waves ?? Array.Empty<int>());

        private static SimModeProfile[] Replace(SimModeProfile[] profiles, SimCombatMode mode,
            SimModeProfile replacement) => profiles
            .Select(profile => profile.Mode == mode ? replacement : profile)
            .ToArray();

        private static SimModeEvaluationInput Input(int completedTick, int attackerAlive,
            int defenderAlive, int destructionBasisPoints, int futureWaves, bool timedOut,
            bool manualDeploymentSealed) =>
            new SimModeEvaluationInput(completedTick, attackerAlive, defenderAlive,
                destructionBasisPoints, futureWaves, timedOut, manualDeploymentSealed);

        private static byte[] LegacyManifestBytes()
        {
            var writer = new DeterministicBinaryWriter();
            writer.WriteBytes(new byte[] { 0x5A, 0x53, 0x49, 0x4D });
            writer.WriteInt32(1);
            writer.WriteInt32(0);
            return writer.ToArray();
        }

        private static byte[] RawManifestBytes(SimModeProfile[] profiles,
            int? manualOverride = null, int[] waveOverride = null)
        {
            var writer = new DeterministicBinaryWriter();
            writer.WriteBytes(new byte[] { 0x5A, 0x53, 0x49, 0x4D });
            writer.WriteInt32(1);
            writer.WriteInt32(0);
            writer.WriteInt32(profiles.Length);
            foreach (SimModeProfile profile in profiles)
            {
                writer.WriteByte((byte)profile.Mode);
                writer.WriteByte((byte)(manualOverride ?? (profile.ManualDeployment ? 1 : 0)));
                writer.WriteInt32(profile.MaxDeployCount);
                writer.WriteInt32(profile.DeployDurationTicks);
                writer.WriteInt32(profile.TimeoutTicks);
                int[] waves = profile.Mode == SimCombatMode.Campaign && waveOverride != null
                    ? waveOverride
                    : profile.WaveTicks;
                writer.WriteInt32(waves.Length);
                foreach (int wave in waves) writer.WriteInt32(wave);
            }
            return writer.ToArray();
        }
    }

    internal static class ModeTestFixture
    {
        public static SimModeProfile[] Profiles() => new[]
        {
            new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                new[] { 0, 100, 200, 300, 400 }),
        };

        public static DeterministicContentManifest Manifest() =>
            DeterministicContentManifest.Parse(SessionTestDriver.ManifestBytes());

        public static SimBattleResult EvaluateVillageAttackVector()
        {
            var rules = new SimModeRules(Manifest().GetMode(SimCombatMode.VillageAttack));
            SimTerminalOutcome outcome = rules.Evaluate(new SimModeEvaluationInput(
                120, 1, 0, 10000, 0, false, true));
            return SimBattleResult.FromOutcome(
                "mode-vector", outcome, 120, 0xA11CEUL);
        }
    }
}
