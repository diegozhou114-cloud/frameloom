using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public sealed class BattleInitFactory
    {
        private readonly DeterministicContentManifest _manifest;

        public BattleInitFactory(byte[] canonicalManifestBytes)
        {
            if (canonicalManifestBytes == null)
                throw new ArgumentNullException(nameof(canonicalManifestBytes));

            byte[] bytes = (byte[])canonicalManifestBytes.Clone();
            _manifest = DeterministicContentManifest.Parse(bytes);
        }

        public BattleInit Create(BattleParams parameters)
        {
            if (parameters == null) throw new ArgumentNullException(nameof(parameters));
            RequireString(parameters.ModeId, nameof(parameters.ModeId));
            RequireString(parameters.MatchId, nameof(parameters.MatchId));
            RequireString(parameters.SessionId, nameof(parameters.SessionId));
            RequireString(parameters.AttackerAccountId, nameof(parameters.AttackerAccountId));
            RequireString(parameters.DefenderAccountId, nameof(parameters.DefenderAccountId));
            if (parameters.AttackerVillage == null || parameters.DefenderVillage == null)
                throw new ArgumentException("Both villages are required.", nameof(parameters));
            if (parameters.AttackerPlayerId == parameters.DefenderPlayerId)
                throw new ArgumentException("Players must be distinct.", nameof(parameters));

            SimCombatMode mode = ToMode(parameters.ModeId);
            var entities = new List<InitialEntitySpec>();
            foreach (BuildingData building in parameters.DefenderVillage.Buildings)
            {
                if (building == null || !FrameworkBattleContent.TryGetBuildingArchetypeId(
                        building.TypeId, out int archetypeId))
                    continue;

                entities.Add(new InitialEntitySpec(archetypeId, building.GridX.Value,
                    building.GridY.Value, SimTeam.Defender, parameters.DefenderPlayerId));
            }

            if (entities.Count == 0)
                throw new ArgumentException("At least one supported defender entity is required.",
                    nameof(parameters));

            return new BattleInit(
                protocolVersion: 1,
                schemaVersion: SimComponentSchema.Version,
                matchId: parameters.MatchId,
                sessionId: parameters.SessionId,
                seed: parameters.Seed,
                tickRate: 20,
                mode: mode,
                contentHash: _manifest.ContentHash,
                players: new[]
                {
                    new SimPlayerIdentity(parameters.AttackerPlayerId, parameters.AttackerAccountId),
                    new SimPlayerIdentity(parameters.DefenderPlayerId, parameters.DefenderAccountId),
                },
                initialEntities: entities.ToArray(),
                inputDeadlineTicks: 6);
        }

        private static void RequireString(string value, string field)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("A required string is missing.", field);
        }

        private static SimCombatMode ToMode(string modeId)
        {
            switch (modeId)
            {
                case "village_attack": return SimCombatMode.VillageAttack;
                case "builder_base": return SimCombatMode.BuilderBase;
                case "clan_war": return SimCombatMode.ClanWar;
                case "campaign": return SimCombatMode.Campaign;
                default: throw new ArgumentException("Unsupported battle mode.", nameof(modeId));
            }
        }
    }
}
