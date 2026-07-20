using System;

namespace ZWebGLFrameWork.SimulationCore
{
    public sealed class SimModeEvaluationInput
    {
        public int CompletedTick { get; }
        public int AttackerAlive { get; }
        public int DefenderAlive { get; }
        public int DestructionBasisPoints { get; }
        public int FutureWaves { get; }
        public bool TimedOut { get; }
        public bool ManualDeploymentSealed { get; }

        public SimModeEvaluationInput(int completedTick, int attackerAlive, int defenderAlive,
            int destructionBasisPoints, int futureWaves, bool timedOut,
            bool manualDeploymentSealed)
        {
            if (completedTick < 0) throw new ArgumentOutOfRangeException(nameof(completedTick));
            if (attackerAlive < 0) throw new ArgumentOutOfRangeException(nameof(attackerAlive));
            if (defenderAlive < 0) throw new ArgumentOutOfRangeException(nameof(defenderAlive));
            if (destructionBasisPoints < 0 || destructionBasisPoints > 10000)
                throw new ArgumentOutOfRangeException(nameof(destructionBasisPoints));
            if (futureWaves < 0) throw new ArgumentOutOfRangeException(nameof(futureWaves));

            CompletedTick = completedTick;
            AttackerAlive = attackerAlive;
            DefenderAlive = defenderAlive;
            DestructionBasisPoints = destructionBasisPoints;
            FutureWaves = futureWaves;
            TimedOut = timedOut;
            ManualDeploymentSealed = manualDeploymentSealed;
        }
    }

    public sealed class SimTerminalOutcome
    {
        public int WinnerTeamId { get; }
        public int Stars { get; }
        public int LootGold { get; }
        public int LootElixir { get; }

        public SimTerminalOutcome(int winnerTeamId, int stars, int lootGold, int lootElixir)
        {
            if (winnerTeamId != SimTeam.None && winnerTeamId != SimTeam.Attacker
                && winnerTeamId != SimTeam.Defender)
                throw new ArgumentOutOfRangeException(nameof(winnerTeamId));
            if (stars < 0 || stars > 3) throw new ArgumentOutOfRangeException(nameof(stars));
            if (lootGold < 0) throw new ArgumentOutOfRangeException(nameof(lootGold));
            if (lootElixir < 0) throw new ArgumentOutOfRangeException(nameof(lootElixir));
            if (winnerTeamId != SimTeam.Attacker
                && (stars != 0 || lootGold != 0 || lootElixir != 0))
                throw new ArgumentException("Only the attacker can receive stars or loot.");

            WinnerTeamId = winnerTeamId;
            Stars = stars;
            LootGold = lootGold;
            LootElixir = lootElixir;
        }
    }

    public sealed class SimModeRules
    {
        private const int BasisPointScale = 10000;
        private const int MaxGold = 500;
        private const int MaxElixir = 300;
        private readonly SimModeProfile _profile;

        public SimModeRules(SimModeProfile profile)
        {
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public SimTerminalOutcome Evaluate(SimModeEvaluationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            if (input.AttackerAlive == 0 && input.DefenderAlive == 0)
                return NoRewards(SimTeam.None);
            if (input.TimedOut)
                return NoRewards(SimTeam.Defender);
            if (input.DefenderAlive == 0 && input.AttackerAlive > 0)
                return AttackerOutcome(input.DestructionBasisPoints);
            if (input.AttackerAlive > 0)
                return null;

            bool deploymentOpen = _profile.ManualDeployment
                && !input.ManualDeploymentSealed;
            if (deploymentOpen || input.FutureWaves > 0)
                return null;
            return NoRewards(SimTeam.Defender);
        }

        private static SimTerminalOutcome AttackerOutcome(int destructionBasisPoints)
        {
            int stars = destructionBasisPoints == BasisPointScale
                ? 3
                : destructionBasisPoints >= 5000
                    ? 2
                    : destructionBasisPoints > 0 ? 1 : 0;
            int gold = MaxGold * destructionBasisPoints / BasisPointScale;
            int elixir = MaxElixir * destructionBasisPoints / BasisPointScale;
            return new SimTerminalOutcome(SimTeam.Attacker, stars, gold, elixir);
        }

        private static SimTerminalOutcome NoRewards(int winnerTeamId) =>
            new SimTerminalOutcome(winnerTeamId, 0, 0, 0);
    }
}
