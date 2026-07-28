using System;
using System.Globalization;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.FSM
{
    public enum BattleResultTrust
    {
        Accepted,
        Rejected,
        ReplayInvalid,
    }

    public enum BattleRewardCommitStatus
    {
        Applied,
        Duplicate,
        Invalid,
        ForeignMatch,
        Rejected,
        ReplayInvalid,
        SaveFailed,
    }

    public sealed class BattleRewardService
    {
        private const int ResourceCap = int.MaxValue - 1;

        public BattleRewardCommitStatus Commit(string expectedMatchId, SimBattleResult result,
            BattleResultTrust trust, PlayerData player, Func<bool> save,
            Action<SimBattleResult> present)
        {
            if (trust == BattleResultTrust.Rejected)
                return BattleRewardCommitStatus.Rejected;
            if (trust == BattleResultTrust.ReplayInvalid)
                return BattleRewardCommitStatus.ReplayInvalid;
            if (!IsStructurallyValid(result))
                return BattleRewardCommitStatus.Invalid;
            if (!string.Equals(expectedMatchId, result.MatchId, StringComparison.Ordinal))
                return BattleRewardCommitStatus.ForeignMatch;
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (save == null) throw new ArgumentNullException(nameof(save));

            if (player.AppliedBattleResultIds.Contains(result.ResultId))
            {
                present?.Invoke(result);
                return BattleRewardCommitStatus.Duplicate;
            }

            int oldGold = player.Gold.Value;
            int oldElixir = player.Elixir.Value;
            player.Gold.Value = SaturatingAdd(oldGold, result.LootGold);
            player.Elixir.Value = SaturatingAdd(oldElixir, result.LootElixir);
            player.AppliedBattleResultIds.Add(result.ResultId);

            bool saved;
            try
            {
                saved = save();
            }
            catch
            {
                saved = false;
            }

            if (!saved)
            {
                player.Gold.Value = oldGold;
                player.Elixir.Value = oldElixir;
                player.AppliedBattleResultIds.Remove(result.ResultId);
                return BattleRewardCommitStatus.SaveFailed;
            }

            present?.Invoke(result);
            return BattleRewardCommitStatus.Applied;
        }

        private static bool IsStructurallyValid(SimBattleResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.MatchId) ||
                string.IsNullOrWhiteSpace(result.ResultId) || result.FinalTick < 0 ||
                result.FinalChecksum == 0UL || result.LootGold < 0 || result.LootElixir < 0 ||
                result.Stars < 0 || result.Stars > 3 ||
                (result.WinnerTeamId != SimTeam.None &&
                 result.WinnerTeamId != SimTeam.Attacker &&
                 result.WinnerTeamId != SimTeam.Defender) ||
                (result.WinnerTeamId != SimTeam.Attacker &&
                 (result.Stars != 0 || result.LootGold != 0 || result.LootElixir != 0)))
                return false;

            string expected = result.MatchId + ":" +
                result.FinalTick.ToString(CultureInfo.InvariantCulture) + ":" +
                result.FinalChecksum.ToString("X16", CultureInfo.InvariantCulture);
            return string.Equals(result.ResultId, expected, StringComparison.Ordinal);
        }

        private static int SaturatingAdd(int current, int delta)
        {
            long sum = (long)current + delta;
            return (int)Math.Min(ResourceCap, sum);
        }
    }
}
