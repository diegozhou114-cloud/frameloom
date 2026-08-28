using System;
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

        [Test]
        public void Presentation_RendersBetweenTicks_AndResyncsAfterRollbackWithoutLeaks()
        {
            var factory = new RecordingEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            bridge.Apply(FullAt(0, -1, 1, 0));
            bridge.Apply(DeltaAt(0, 0, 1, 10000));

            bridge.Sample(0.5f);
            Assert.That(factory.PositionX(1), Is.EqualTo(0.5f).Within(0.0001f));

            bridge.Apply(FullAt(1, 0, 1, 30000));
            bridge.Sample(0.25f);
            Assert.That(factory.PositionX(1), Is.EqualTo(3f).Within(0.0001f));

            bridge.Cleanup();
            bridge.Cleanup();
            Assert.That(bridge.ViewCount, Is.Zero);
            Assert.That(factory.CreatedCount, Is.EqualTo(factory.DestroyedCount));
        }

        [Test]
        public void Apply_CreateReplacesBattle_ReturnedStaleViewIsDestroyed()
        {
            var factory = new ReentrantEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            ReentrantView staleView = null;
            factory.OnCreate = (key, view) =>
            {
                factory.OnCreate = null;
                staleView = view;
                bridge.Cleanup();
                bridge.Apply(Full(State(2, 1, 202, 20000)));
            };

            bridge.Apply(Full(State(1, 1, 101, 10000)));

            Assert.That(bridge.ViewCount, Is.EqualTo(1));
            Assert.That(factory.HasLiveArchetype(202), Is.True);
            Assert.That(factory.HasLiveArchetype(101), Is.False);
            Assert.That(factory.DestroyCount(staleView), Is.EqualTo(1));
        }

        [Test]
        public void Apply_CreateReplacesBattleThenThrows_PreservesReplacement()
        {
            var factory = new ReentrantEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            ReentrantView unreturnedView = null;
            factory.OnCreate = (key, view) =>
            {
                factory.OnCreate = null;
                unreturnedView = view;
                bridge.Cleanup();
                bridge.Apply(Full(State(2, 1, 222, 20000)));
                throw new InvalidOperationException("create failed after replacement");
            };

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                bridge.Apply(Full(State(1, 1, 111, 10000))));

            Assert.That(error.Message, Is.EqualTo("create failed after replacement"));
            Assert.That(bridge.ViewCount, Is.EqualTo(1));
            Assert.That(factory.HasLiveArchetype(222), Is.True);
            Assert.That(factory.HasLiveArchetype(111), Is.False,
                "the factory retains ownership when Create throws before returning");
            Assert.That(factory.WasAborted(unreturnedView), Is.True);
            Assert.That(factory.DestroyCount(unreturnedView), Is.Zero);
        }

        [Test]
        public void Cleanup_DestroyReplacesBattle_DestroysSnapshotAndPreservesReplacement()
        {
            var factory = new ReentrantEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            bridge.Apply(Full(State(1, 1, 101, 10000),
                State(2, 1, 102, 20000)));
            factory.OnDestroy = view =>
            {
                factory.OnDestroy = null;
                bridge.Apply(Full(State(3, 1, 203, 30000)));
            };

            Assert.DoesNotThrow(bridge.Cleanup);

            Assert.That(bridge.ViewCount, Is.EqualTo(1));
            Assert.That(factory.LiveCount, Is.EqualTo(1));
            Assert.That(factory.HasLiveArchetype(203), Is.True);
            Assert.That(factory.DestroyCountForArchetype(101), Is.EqualTo(1));
            Assert.That(factory.DestroyCountForArchetype(102), Is.EqualTo(1));
        }

        [Test]
        public void ApplyFull_DestroyReplacesBattle_StopsAndPreservesReplacementViews()
        {
            var factory = new ReentrantEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            bridge.Apply(Full(State(1, 1, 101, 10000),
                State(2, 1, 102, 20000), State(3, 1, 103, 30000)));
            factory.OnDestroy = view =>
            {
                factory.OnDestroy = null;
                bridge.Cleanup();
                bridge.Apply(Full(State(3, 1, 303, 30000),
                    State(4, 1, 404, 40000)));
            };

            bridge.Apply(Full(State(1, 1, 101, 10000)));

            Assert.That(bridge.ViewCount, Is.EqualTo(2));
            Assert.That(factory.LiveCount, Is.EqualTo(2));
            Assert.That(factory.HasLiveArchetype(303), Is.True);
            Assert.That(factory.HasLiveArchetype(404), Is.True);
            Assert.That(factory.DestroyCountForArchetype(303), Is.Zero);
        }

        [Test]
        public void Sample_SetPositionReplacesBattle_StopsAndPreservesReplacement()
        {
            var factory = new ReentrantEntityViewFactory();
            var bridge = new SessionPresentationBridge(factory);
            bridge.Apply(Full(State(1, 1, 101, 10000),
                State(2, 1, 102, 20000)));
            factory.OnSetPosition = view =>
            {
                factory.OnSetPosition = null;
                bridge.Cleanup();
                bridge.Apply(Full(State(3, 1, 203, 30000)));
            };

            Assert.DoesNotThrow(() => bridge.Sample(0.5f));

            Assert.That(bridge.ViewCount, Is.EqualTo(1));
            Assert.That(factory.LiveCount, Is.EqualTo(1));
            Assert.That(factory.HasLiveArchetype(203), Is.True);
            Assert.That(factory.PositionXForArchetype(203), Is.EqualTo(3f));
        }

        private static PresentationFrame Full(params EntityPresentationState[] states) =>
            new PresentationFrame(true, -1, 0, 0, states,
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationKey>());

        private static PresentationFrame FullAt(long revision, int completedTick,
            int entityId, long xRaw) => new PresentationFrame(true, completedTick,
                completedTick + 1, revision, new[] { State(entityId, 1, 10, xRaw) },
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationKey>());

        private static PresentationFrame DeltaAt(long revision, int completedTick,
            int entityId, long xRaw) => new PresentationFrame(false, completedTick,
                completedTick + 1, revision, System.Array.Empty<EntityPresentationState>(),
                System.Array.Empty<EntityPresentationState>(),
                new[] { State(entityId, 1, 10, xRaw) },
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

        private sealed class ReentrantEntityViewFactory : IEntityViewFactory
        {
            private readonly HashSet<ReentrantView> _liveViews =
                new HashSet<ReentrantView>();
            private readonly HashSet<ReentrantView> _abortedViews =
                new HashSet<ReentrantView>();
            private readonly Dictionary<ReentrantView, int> _destroyCounts =
                new Dictionary<ReentrantView, int>();

            public Action<EntityPresentationKey, ReentrantView> OnCreate { get; set; }
            public Action<ReentrantView> OnDestroy { get; set; }
            public Action<ReentrantView> OnSetPosition { get; set; }
            public int LiveCount => _liveViews.Count;

            public IEntityView Create(EntityPresentationKey key, int archetypeId)
            {
                var view = new ReentrantView(this, archetypeId);
                _liveViews.Add(view);
                try
                {
                    OnCreate?.Invoke(key, view);
                    return view;
                }
                catch
                {
                    _liveViews.Remove(view);
                    _abortedViews.Add(view);
                    throw;
                }
            }

            public void Destroy(IEntityView view)
            {
                var recordingView = (ReentrantView)view;
                _liveViews.Remove(recordingView);
                _destroyCounts.TryGetValue(recordingView, out int count);
                _destroyCounts[recordingView] = count + 1;
                OnDestroy?.Invoke(recordingView);
            }

            public bool HasLiveArchetype(int archetypeId)
            {
                foreach (ReentrantView view in _liveViews)
                    if (view.ArchetypeId == archetypeId) return true;
                return false;
            }

            public int DestroyCount(ReentrantView view) => view != null &&
                _destroyCounts.TryGetValue(view, out int count) ? count : 0;

            public int DestroyCountForArchetype(int archetypeId)
            {
                int count = 0;
                foreach (KeyValuePair<ReentrantView, int> pair in _destroyCounts)
                    if (pair.Key.ArchetypeId == archetypeId) count += pair.Value;
                return count;
            }

            public bool WasAborted(ReentrantView view) => view != null &&
                _abortedViews.Contains(view);

            public float PositionXForArchetype(int archetypeId)
            {
                foreach (ReentrantView view in _liveViews)
                    if (view.ArchetypeId == archetypeId) return view.X;
                throw new KeyNotFoundException();
            }

            public void SetPosition(ReentrantView view, float x)
            {
                view.X = x;
                OnSetPosition?.Invoke(view);
            }
        }

        private sealed class ReentrantView : IEntityView
        {
            private readonly ReentrantEntityViewFactory _factory;

            public int ArchetypeId { get; }
            public float X { get; set; }

            public ReentrantView(ReentrantEntityViewFactory factory, int archetypeId)
            {
                _factory = factory;
                ArchetypeId = archetypeId;
            }

            public void SetPosition(float x, float y) => _factory.SetPosition(this, x);
        }
    }
}
