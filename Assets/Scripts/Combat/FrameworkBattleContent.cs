using System;
using UnityEngine;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat
{
    public static class FrameworkBattleContent
    {
        // TODO(PRODUCTION-CONTENT): Replace framework manifest and ID mappings with versioned project content.
        private static bool _warningEmitted;
        private static readonly byte[] CanonicalManifestBytes = new DeterministicContentManifest(
            SimComponentSchema.Version,
            new[]
            {
                new SimArchetypeDefinition(1, "TownHall", 500, 0, 0, 0, 0),
                new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 500, 10000, 1)
            },
            new[]
            {
                new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 600, new[] { 0 })
            }).ToCanonicalBytes();

        public static byte[] CanonicalBytes()
        {
            WarnOnce();
            return (byte[])CanonicalManifestBytes.Clone();
        }

        public static bool TryGetBuildingArchetypeId(int buildingTypeId, out int archetypeId)
        {
            WarnOnce();
            if (buildingTypeId == 1) { archetypeId = 1; return true; }
            if (buildingTypeId == 5) { archetypeId = 2; return true; }
            archetypeId = 0;
            return false;
        }

        public static bool TryGetUnitArchetypeId(int unitConfigId, out int archetypeId)
        {
            WarnOnce();
            archetypeId = unitConfigId == 1 ? 10 : 0;
            return archetypeId != 0;
        }

        public static SimModeProfile GetModeProfile(string modeId)
        {
            WarnOnce();
            SimCombatMode mode;
            switch (modeId)
            {
                case "village_attack": mode = SimCombatMode.VillageAttack; break;
                case "builder_base": mode = SimCombatMode.BuilderBase; break;
                case "clan_war": mode = SimCombatMode.ClanWar; break;
                case "campaign": mode = SimCombatMode.Campaign; break;
                default: throw new ArgumentOutOfRangeException(nameof(modeId));
            }

            return DeterministicContentManifest.Parse(CanonicalManifestBytes).GetMode(mode);
        }

        private static void WarnOnce()
        {
            if (_warningEmitted) return;
            _warningEmitted = true;
            Debug.LogWarning("[FrameworkBattleContent] Using framework fallback battle content.");
        }
    }
}
