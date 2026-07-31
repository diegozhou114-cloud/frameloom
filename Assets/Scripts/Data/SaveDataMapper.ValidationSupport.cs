using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZWebGLFrameWork.Data
{
    public static partial class SaveDataMapper
    {
        private static bool TryStageReceipts(
            PlayerSaveData saved, out List<string> receipts, out string error)
        {
            receipts = new List<string>();
            error = null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string resultId in saved.AppliedBattleResultIds ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(resultId) || resultId.Length > 256 ||
                    !ids.Add(resultId) || receipts.Count >= 10_000)
                    return Fail("战斗结算收据无效", out error);
                receipts.Add(resultId);
            }
            return true;
        }

        private static bool HasValidSaveTime(string value, long nowUnixSeconds) =>
            DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp) &&
            timestamp.Offset == TimeSpan.Zero && timestamp.ToUnixTimeSeconds() >= 0 &&
            timestamp.ToUnixTimeSeconds() <= nowUnixSeconds + 300;

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
