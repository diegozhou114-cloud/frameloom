using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Combat
{
    public sealed class FrameworkBattleAuthority
    {
        private const int FrameworkAttackerPlayerId = 1000;
        private const int FrameworkDefenderPlayerId = 2000;
        private readonly Func<string> _idFactory;
        private static bool _warningEmitted;

        public FrameworkBattleAuthority() : this(() => Guid.NewGuid().ToString("N")) { }

        internal FrameworkBattleAuthority(Func<string> idFactory) =>
            _idFactory = idFactory ?? throw new ArgumentNullException(nameof(idFactory));

        public BattleParams CreateVillageAttack(PlayerData player, VillageData attacker,
            VillageData defender)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));
            WarnOnce();
            string matchId = "framework-match-" + RequireToken(_idFactory());
            string sessionId = "framework-session-" + RequireToken(_idFactory());
            return new BattleParams
            {
                ModeId = "village_attack",
                AttackerVillage = attacker,
                DefenderVillage = defender,
                MatchId = matchId,
                SessionId = sessionId,
                Seed = StableSeed(matchId + ":" + sessionId),
                // TODO(PRODUCTION-AUTHORITY): Replace reserved local identities with persisted/server authority.
                AttackerPlayerId = FrameworkAttackerPlayerId,
                DefenderPlayerId = FrameworkDefenderPlayerId,
                AttackerAccountId = "framework-local-attacker",
                DefenderAccountId = "framework-local-defender",
            };
        }

        private static string RequireToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                throw new InvalidOperationException("The framework battle identity factory returned no token.");
            return token;
        }

        private static void WarnOnce()
        {
            if (_warningEmitted) return;
            _warningEmitted = true;
            Debug.LogWarning("[FrameworkBattleAuthority] Using framework fallback battle authority.");
        }

        private static int StableSeed(string value)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            int result = (hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3];
            return result == 0 ? 1 : result;
        }
    }
}
