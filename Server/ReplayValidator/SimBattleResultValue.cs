using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Server
{
    internal static class SimBattleResultValue
    {
        public static bool Equals(SimBattleResult left, SimBattleResult right) =>
            object.ReferenceEquals(left, right) ||
            (left != null && right != null &&
             left.MatchId == right.MatchId &&
             left.ResultId == right.ResultId &&
             left.WinnerTeamId == right.WinnerTeamId &&
             left.Stars == right.Stars &&
             left.LootGold == right.LootGold &&
             left.LootElixir == right.LootElixir &&
             left.FinalTick == right.FinalTick &&
             left.FinalChecksum == right.FinalChecksum);
    }
}
