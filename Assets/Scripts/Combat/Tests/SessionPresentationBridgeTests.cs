using System.Collections.Generic;
using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class SessionPresentationBridgeTests
    {
        [Test]
        public void PresentationBridge_CreatesInitialViewsUsingCorrectArchetype()
        {
            var factory = new RecordingEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);

            bridge.Apply(Full(State(1, 1, 2, 40000)));

            Assert.That(factory.CreatedArchetypes, Is.EqualTo(new[] { 2 }));
            Assert.That(bridge.ViewCount, Is.EqualTo(1));
            Assert.That(factory.PositionX(1), Is.EqualTo(4f));
        }

        [Test]
        public void PresentationBridge_AppliesDeltasAndCleansUpIdempotently()
        {
            var factory = new RecordingEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            bridge.Apply(Full(State(1, 1, 2, 0)));
            bridge.Apply(new PresentationFrame(false, 0, 1, 0,
                System.Array.Empty<EntityPresentationState>(),
                new[] { State(2, 1, 10, 30000) },
                new[] { State(1, 1, 2, 10000) },
                System.Array.Empty<EntityPresentationKey>()));

            Assert.That(factory.PositionX(1), Is.EqualTo(1f));
            Assert.That(factory.PositionX(2), Is.EqualTo(3f));
            bridge.Apply(new PresentationFrame(false, 1, 2, 0,
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                new[] { new EntityPresentationKey(1, 1) }));
            Assert.That(bridge.ViewCount, Is.EqualTo(1));

            bridge.Cleanup();
            bridge.Cleanup();
            Assert.That(bridge.ViewCount, Is.Zero);
            Assert.That(factory.CreatedCount, Is.EqualTo(factory.DestroyedCount));
        }

        private static PresentationFrame Full(params EntityPresentationState[] states) =>
            new PresentationFrame(true, -1, 0, 0, states,
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationKey>());

        private static EntityPresentationState State(int id, int generation,
            int archetypeId, long xRaw) => new EntityPresentationState(id, generation,
                archetypeId, 0, SimTeam.Attacker, (int)(xRaw / 10000), 0,
                xRaw, 0, 0, 100);

        private sealed class RecordingEntityViewFactory : IEntityViewFactory
        {
            private readonly Dictionary<int, RecordingView> _views =
                new Dictionary<int, RecordingView>();
            private readonly List<int> _createdArchetypes = new List<int>();

            public IReadOnlyList<int> CreatedArchetypes => _createdArchetypes;
            public int CreatedCount { get; private set; }
            public int DestroyedCount { get; private set; }

            public IEntityView Create(EntityPresentationKey key, int archetypeId)
            {
                var view = new RecordingView();
                _views.Add(key.EntityId, view);
                _createdArchetypes.Add(archetypeId);
                CreatedCount++;
                return view;
            }

            public void Destroy(IEntityView view) => DestroyedCount++;

            public float PositionX(int entityId) => _views[entityId].X;
        }

        private sealed class RecordingView : IEntityView
        {
            public float X { get; private set; }
            public void SetPosition(float x, float y) => X = x;
        }
    }
}
