using System;
using System.Collections.Generic;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.PublicApiContract.Tests
{
    public static class PublicApiContractTestSupport
    {
        public static ISimBattleSession CreateStartedSession()
        {
            byte[] manifest = ManifestBytes();
            ISimBattleSession session = new SimBattleSessionFactory().Create(manifest);
            try
            {
                byte[] contentHash = DeterministicContentManifest.Parse(manifest).ContentHash;
                StartResult result = session.Start(new BattleInit(
                    1, SimComponentSchema.Version, "public-contract-match",
                    "public-contract-session", 42, 20, SimCombatMode.VillageAttack,
                    contentHash,
                    new[]
                    {
                        new SimPlayerIdentity(0, "attacker"),
                        new SimPlayerIdentity(1, "defender"),
                    },
                    new[]
                    {
                        new InitialEntitySpec(2, 8, 2, SimTeam.Defender, 1),
                    }, 6));
                if (!result.Accepted)
                    throw new InvalidOperationException(result.Code.ToString());
                return session;
            }
            catch
            {
                session.Dispose();
                throw;
            }
        }

        public static void Publish(ISimBattleSession session, SimTickOutputBuffer output)
        {
            ((IBufferedSimBattleSession)session).TickInto(output);
        }

        public static byte[] ManifestBytes() => new DeterministicContentManifest(1,
            new[]
            {
                new SimArchetypeDefinition(2, "Cannon", 200, 0, 25, 30000, 20),
                new SimArchetypeDefinition(10, "Barbarian", 100, 20000, 20, 10000, 20),
            },
            new[]
            {
                new SimModeProfile(SimCombatMode.VillageAttack, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.BuilderBase, true, 30, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.ClanWar, true, 40, 600, 3600,
                    Array.Empty<int>()),
                new SimModeProfile(SimCombatMode.Campaign, false, 0, 0, 2400,
                    new[] { 0, 100, 200, 300, 400 }),
            }).ToCanonicalBytes();

        public sealed class RecordingFactory : IEntityViewFactory
        {
            private readonly Dictionary<EntityPresentationKey, RecordingView> _views =
                new Dictionary<EntityPresentationKey, RecordingView>();

            public int ActiveCount => _views.Count;

            public bool Contains(EntityPresentationKey key) => _views.ContainsKey(key);

            public float PositionX(EntityPresentationKey key) => _views[key].X;

            public IEntityView Create(EntityPresentationKey key, int archetypeId)
            {
                var view = new RecordingView(key);
                _views.Add(key, view);
                return view;
            }

            public void Destroy(IEntityView view)
            {
                var recording = view as RecordingView;
                if (recording != null) _views.Remove(recording.Key);
            }

            private sealed class RecordingView : IEntityView
            {
                public RecordingView(EntityPresentationKey key) => Key = key;
                public EntityPresentationKey Key { get; }
                public float X { get; private set; }

                public void SetPosition(float x, float y) => X = x;
            }
        }
    }
}
