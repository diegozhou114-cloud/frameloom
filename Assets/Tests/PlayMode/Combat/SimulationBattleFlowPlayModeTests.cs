using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.PlayModeTests
{
    public sealed class SimulationBattleFlowPlayModeTests
    {
        [UnityTest]
        public IEnumerator BattleState_UsesSingleSimulationSession_EndToEnd()
        {
            var host = new GameObject("CombatManagerTestHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());
            BattleParams parameters = SessionPlayModeFixture.VillageAttackParams();
            UniTask<SimBattleResult> completion = manager.StartBattleAsync(parameters,
                CancellationToken.None);

            Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted, Is.True);
            Assert.That(manager.EndDeployPhase().Accepted, Is.True);
            for (int frame = 0; frame < 600 && completion.Status == UniTaskStatus.Pending;
                frame++)
                yield return null;

            Assert.That(completion.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Ended));
            Assert.That(manager.LastResult, Is.Not.Null);
            manager.StopBattle();
            Assert.That(manager.ActiveViewCount, Is.Zero);
            Object.DestroyImmediate(host);
        }

        [UnityTest]
        public IEnumerator BattleState_ExitBeforeStartCompletes_CancelsLateStart()
        {
            var host = new GameObject("BattleStateCancellationHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());
            var state = new ZWebGLFrameWork.FSM.BattleState(manager);

            state.OnEnter(SessionPlayModeFixture.VillageAttackParams());
            yield return null;
            Assert.That(manager.HasActiveSession, Is.True);

            state.OnExit();
            yield return null;

            Assert.That(manager.HasActiveSession, Is.False);
            Assert.That(manager.ActiveViewCount, Is.Zero);
            Assert.That(manager.LastResult, Is.Null,
                "a late completion after state exit must not commit or present a result");
            Object.DestroyImmediate(host);
        }

        [UnityTest]
        public IEnumerator CompletedBattle_ReentrantReplacementDoesNotCommitStaleResult()
        {
            var host = new GameObject("CombatManagerReentrantCompletionHost");
            var manager = host.AddComponent<CombatManager>();
            manager.ConfigureForTests(new SimBattleSessionFactory(),
                FrameworkBattleContent.CanonicalBytes(), new RecordingEntityViewFactory());

            try
            {
                BattleParams firstParameters = SessionPlayModeFixture.VillageAttackParams();
                UniTask<SimBattleResult> firstCompletion = manager.StartBattleAsync(
                    firstParameters, CancellationToken.None);
                UniTask<SimBattleResult> replacementCompletion = default;
                bool replacementStarted = false;
                firstCompletion.GetAwaiter().OnCompleted(() =>
                {
                    BattleParams replacement = SessionPlayModeFixture.VillageAttackParams();
                    replacement.MatchId = "playmode-replacement-match";
                    replacement.SessionId = "playmode-replacement-session";
                    replacementCompletion = manager.StartBattleAsync(
                        replacement, CancellationToken.None);
                    replacementStarted = true;
                });

                Assert.That(manager.SubmitDeployUnit(new Vector2Int(2, 2), 10).Accepted,
                    Is.True);
                Assert.That(manager.EndDeployPhase().Accepted, Is.True);
                for (int frame = 0; frame < 600 && !replacementStarted; frame++)
                    yield return null;

                Assert.That(firstCompletion.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(replacementStarted, Is.True);
                Assert.That(replacementCompletion.Status, Is.EqualTo(UniTaskStatus.Pending));
                Assert.That(manager.HasActiveSession, Is.True);
                Assert.That(manager.CurrentPhase, Is.EqualTo(SimCombatPhase.Deploying));
                Assert.That(manager.LastResult, Is.Null,
                    "the completed battle must not commit into its replacement Session");
            }
            finally
            {
                manager.StopBattle();
                Object.DestroyImmediate(host);
            }
        }
    }

    internal static class SessionPlayModeFixture
    {
        public static BattleParams VillageAttackParams()
        {
            var attacker = new ZWebGLFrameWork.Data.VillageData();
            var defender = new ZWebGLFrameWork.Data.VillageData();
            defender.AddBuilding(new ZWebGLFrameWork.Data.BuildingData
            {
                Id = "defender-cannon",
                TypeId = 5,
                GridX = { Value = 8 },
                GridY = { Value = 2 }
            });
            return new BattleParams
            {
                ModeId = "village_attack",
                AttackerVillage = attacker,
                DefenderVillage = defender,
                MatchId = "playmode-match",
                SessionId = "playmode-session",
                Seed = 42,
                AttackerPlayerId = 0,
                DefenderPlayerId = 1,
                AttackerAccountId = "playmode-attacker",
                DefenderAccountId = "playmode-defender",
            };
        }
    }

    internal sealed class RecordingEntityViewFactory : IEntityViewFactory
    {
        private sealed class View : IEntityView
        {
            public void SetPosition(float x, float y) { }
        }

        public IEntityView Create(EntityPresentationKey key, int archetypeId) => new View();
        public void Destroy(IEntityView view) { }
    }
}
