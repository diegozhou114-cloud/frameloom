using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class BattleRewardServiceTests
    {
        private static SimBattleResult Result(string matchId = "match-1", int gold = 10,
            int elixir = 5, string resultId = null, int winnerTeamId = SimTeam.Attacker,
            int stars = 3)
        {
            const int finalTick = 120;
            const ulong checksum = 0xA11CEUL;
            return new SimBattleResult(matchId,
                resultId ?? $"{matchId}:{finalTick}:{checksum:X16}", winnerTeamId, stars,
                gold, elixir, finalTick, checksum);
        }

        [Test]
        public void BattleResult_ValidCompletion_AppliesAndSavesRewardsOnce()
        {
            var player = new PlayerData();
            int initialGold = player.Gold.Value;
            int initialElixir = player.Elixir.Value;
            int saves = 0;
            int presentations = 0;

            BattleRewardCommitStatus status = new BattleRewardService().Commit("match-1",
                Result(), BattleResultTrust.Accepted, player, () => { saves++; return true; },
                _ => presentations++);

            Assert.That(status, Is.EqualTo(BattleRewardCommitStatus.Applied));
            Assert.That(player.Gold.Value, Is.EqualTo(initialGold + 10));
            Assert.That(player.Elixir.Value, Is.EqualTo(initialElixir + 5));
            Assert.That(saves, Is.EqualTo(1));
            Assert.That(presentations, Is.EqualTo(1));
            Assert.That(player.AppliedBattleResultIds, Has.Member(Result().ResultId));
        }

        [Test]
        public void BattleResult_DuplicateInvalidOrRejected_DoesNotReward()
        {
            var player = new PlayerData();
            int saves = 0;
            var service = new BattleRewardService();
            SimBattleResult accepted = Result();
            Assert.That(service.Commit("match-1", accepted, BattleResultTrust.Accepted, player,
                () => { saves++; return true; }, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Applied));

            PlayerSaveData persisted = SaveDataMapper.Capture(player, new VillageData());
            var restarted = new PlayerData();
            SaveDataMapper.RestoreRewardReceipts(persisted, restarted);
            int originalGold = restarted.Gold.Value;
            int duplicatePresentations = 0;

            Assert.That(new BattleRewardService().Commit("match-1", accepted,
                BattleResultTrust.Accepted, restarted, () => { saves++; return true; },
                _ => duplicatePresentations++),
                Is.EqualTo(BattleRewardCommitStatus.Duplicate));
            Assert.That(duplicatePresentations, Is.EqualTo(1));
            Assert.That(service.Commit("match-1", Result(resultId: "forged"),
                BattleResultTrust.Accepted, restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Invalid));
            Assert.That(service.Commit("match-1", Result(winnerTeamId: 99, stars: 4),
                BattleResultTrust.Accepted, restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Invalid));
            Assert.That(service.Commit("match-1", Result(winnerTeamId: SimTeam.Defender),
                BattleResultTrust.Accepted, restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Invalid));
            Assert.That(service.Commit("match-1", Result("foreign"),
                BattleResultTrust.Accepted, restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.ForeignMatch));
            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Rejected,
                restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Rejected));
            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.ReplayInvalid,
                restarted, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.ReplayInvalid));
            Assert.That(restarted.Gold.Value, Is.EqualTo(originalGold));
            Assert.That(saves, Is.EqualTo(1));
        }

        [Test]
        public void BattleResult_NearCap_Clamps()
        {
            var player = new PlayerData();
            player.Gold.Value = int.MaxValue - 3;
            player.Elixir.Value = int.MaxValue - 2;

            Assert.That(new BattleRewardService().Commit("match-1", Result(gold: 100,
                elixir: 100), BattleResultTrust.Accepted, player, () => true, _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.Applied));
            Assert.That(player.Gold.Value, Is.EqualTo(int.MaxValue - 1));
            Assert.That(player.Elixir.Value, Is.EqualTo(int.MaxValue - 1));
        }

        [Test]
        public void BattleResult_ReturnWaitsForSave()
        {
            var player = new PlayerData();
            int initialGold = player.Gold.Value;
            var service = new BattleRewardService();
            var order = new List<string>();

            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => { order.Add("save-failed"); return false; }, _ => order.Add("ui")),
                Is.EqualTo(BattleRewardCommitStatus.SaveFailed));
            CollectionAssert.AreEqual(new[] { "save-failed" }, order);
            Assert.That(player.Gold.Value, Is.EqualTo(initialGold));
            Assert.That(player.AppliedBattleResultIds, Is.Empty);

            order.Clear();
            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => { order.Add("save"); return true; }, _ => order.Add("ui")),
                Is.EqualTo(BattleRewardCommitStatus.Applied));
            CollectionAssert.AreEqual(new[] { "save", "ui" }, order);
        }

        [Test]
        public void BattleResult_SaveException_RollsBackAndCanRetry()
        {
            var player = new PlayerData();
            int initialGold = player.Gold.Value;
            var service = new BattleRewardService();

            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => throw new System.InvalidOperationException("disk unavailable"), _ => { }),
                Is.EqualTo(BattleRewardCommitStatus.SaveFailed));
            Assert.That(player.Gold.Value, Is.EqualTo(initialGold));
            Assert.That(player.AppliedBattleResultIds, Is.Empty);

            Assert.That(service.Commit("match-1", Result(), BattleResultTrust.Accepted, player,
                () => true, _ => { }), Is.EqualTo(BattleRewardCommitStatus.Applied));
            Assert.That(player.Gold.Value, Is.EqualTo(initialGold + 10));
        }
    }
}
