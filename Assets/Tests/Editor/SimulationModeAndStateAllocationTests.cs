using System;
using System.Reflection;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Editor.Tests
{
    public sealed class SimulationModeAndStateAllocationTests
    {
        [Test]
        public void SteadyStateTick_ModeEvaluationAndStateTransitionStayWithinAllocationBudget()
        {
            SimulationPerformanceReportDocument report = SimulationPerformanceProbe.Run();
            AssertScenario(report, "standard-300", 300, "1109F8052E1B6729");
            AssertScenario(report, "stress-500", 500, "ACE2247CE82E7B4F");
        }

        [Test]
        public void ModeRules_InternalEvaluationMatchesPublicEvaluation()
        {
            var profile = new SimModeProfile(SimCombatMode.VillageAttack,
                true, 40, 600, 3600, Array.Empty<int>());
            var rules = new SimModeRules(profile);
            MethodInfo scalarEvaluate = ScalarEvaluate();
            object[][] vectors =
            {
                new object[] { 10, 1, 0, 10000, 0, false, true },
                new object[] { 10, 0, 1, 5000, 0, false, true },
                new object[] { 10, 0, 0, 10000, 0, false, true },
                new object[] { 10, 1, 1, 10000, 0, true, true },
                new object[] { 10, 1, 1, 5000, 0, false, true },
                new object[] { 10, 0, 1, 0, 0, false, false },
            };

            foreach (object[] values in vectors)
            {
                SimTerminalOutcome expected = rules.Evaluate(Input(values));
                SimTerminalOutcome actual =
                    (SimTerminalOutcome)scalarEvaluate.Invoke(rules, values);
                AssertOutcome(actual, expected);
            }

            object[][] invalid =
            {
                new object[] { -1, 1, 1, 0, 0, false, true },
                new object[] { 0, -1, 1, 0, 0, false, true },
                new object[] { 0, 1, -1, 0, 0, false, true },
                new object[] { 0, 1, 1, -1, 0, false, true },
                new object[] { 0, 1, 1, 10001, 0, false, true },
                new object[] { 0, 1, 1, 0, -1, false, true },
            };
            foreach (object[] values in invalid)
            {
                var expected = Assert.Throws<ArgumentOutOfRangeException>(() => Input(values));
                var wrapped = Assert.Throws<TargetInvocationException>(
                    () => scalarEvaluate.Invoke(rules, values));
                Assert.That(wrapped.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(((ArgumentOutOfRangeException)wrapped.InnerException).ParamName,
                    Is.EqualTo(expected.ParamName));
            }
        }

        [Test]
        public void TickState_FinalChecksumSemanticsRemainStableAcrossRestoreAndTerminal()
        {
            using (SimBattleSession session = CreateOneHitSession())
            {
                TickResult terminal = null;
                for (int count = 0; count < 20 && terminal == null; count++)
                {
                    TickResult tick = session.Tick();
                    if (tick.Result != null) terminal = tick;
                }

                Assert.That(terminal, Is.Not.Null);
                ulong checksum = terminal.Checksum;
                Assert.That(session.State.Checksum, Is.EqualTo(checksum));
                Assert.That(terminal.Result.FinalChecksum, Is.EqualTo(checksum));
                Assert.That(session.State.Result.FinalChecksum, Is.EqualTo(checksum));
                SimSnapshot snapshot = session.CaptureSnapshot();
                Assert.That(snapshot.StateChecksum, Is.EqualTo(checksum));

                Assert.That(session.RestoreSnapshot(snapshot).Code,
                    Is.EqualTo(SnapshotRestoreCode.Restored));
                Assert.That(session.State.Checksum, Is.EqualTo(checksum));
                Assert.That(session.State.Result.FinalChecksum, Is.EqualTo(checksum));
            }
        }

        private static MethodInfo ScalarEvaluate()
        {
            foreach (MethodInfo method in typeof(SimModeRules).GetMethods(
                BindingFlags.Instance | BindingFlags.NonPublic))
                if (method.Name == "Evaluate" && method.GetParameters().Length == 7)
                    return method;
            Assert.Fail("Missing allocation-free scalar evaluation overload.");
            return null;
        }

        private static SimModeEvaluationInput Input(object[] values) =>
            new SimModeEvaluationInput((int)values[0], (int)values[1], (int)values[2],
                (int)values[3], (int)values[4], (bool)values[5], (bool)values[6]);

        private static void AssertOutcome(SimTerminalOutcome actual, SimTerminalOutcome expected)
        {
            if (expected == null) { Assert.That(actual, Is.Null); return; }
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual.WinnerTeamId, Is.EqualTo(expected.WinnerTeamId));
            Assert.That(actual.Stars, Is.EqualTo(expected.Stars));
            Assert.That(actual.LootGold, Is.EqualTo(expected.LootGold));
            Assert.That(actual.LootElixir, Is.EqualTo(expected.LootElixir));
        }

        private static void AssertScenario(SimulationPerformanceReportDocument report,
            string name, int entityCount, string checksum)
        {
            foreach (SimulationPerformanceScenario scenario in report.scenarios)
            {
                if (scenario.name != name) continue;
                Assert.That(scenario.initialEntityCount, Is.EqualTo(entityCount));
                Assert.That(scenario.finalEntityCount, Is.EqualTo(entityCount));
                Assert.That(scenario.finalChecksum, Is.EqualTo(checksum));
                Assert.That(scenario.managedAllocationEventsPerTick.median, Is.LessThanOrEqualTo(10));
                Assert.That(scenario.managedAllocationEventsPerTick.p95, Is.LessThanOrEqualTo(10));
                Assert.That(scenario.managedAllocationEventsPerTick.max, Is.LessThanOrEqualTo(10));
                return;
            }
            Assert.Fail("Missing scenario: " + name);
        }

        private static SimBattleSession CreateOneHitSession()
        {
            byte[] manifest = new DeterministicContentManifest(1, new[]
            {
                new SimArchetypeDefinition(2, "Defender", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Attacker", 100, 20000, 500, 10000, 20),
            }, ModeProfiles()).ToCanonicalBytes();
            byte[] hash = DeterministicContentManifest.Parse(manifest).ContentHash;
            var init = new BattleInit(1, SimComponentSchema.Version,
                "perf-005f", "perf-005f-session", 42, 20,
                SimCombatMode.VillageAttack, hash, new[]
                {
                    new SimPlayerIdentity(0, "attacker"),
                    new SimPlayerIdentity(1, "defender"),
                }, new[]
                {
                    new InitialEntitySpec(10, 7, 2, SimTeam.Attacker, 0),
                    new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1),
                }, 6);
            var session = (SimBattleSession)new SimBattleSessionFactory().Create(manifest);
            Assert.That(session.Start(init).Accepted, Is.True);
            Assert.That(session.Submit(new EndDeploymentCmd
            {
                Tick = 0, PlayerId = 0, Sequence = 1,
            }).Accepted, Is.True);
            return session;
        }

        private static SimModeProfile[] ModeProfiles() => new[]
        {
            new SimModeProfile(SimCombatMode.VillageAttack,
                true, 40, 600, 3600, Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.BuilderBase,
                true, 30, 600, 3600, Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.ClanWar,
                true, 40, 600, 3600, Array.Empty<int>()),
            new SimModeProfile(SimCombatMode.Campaign,
                false, 0, 0, 2400, new[] { 0, 100, 200, 300, 400 }),
        };
    }
}
