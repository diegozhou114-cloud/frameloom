using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.Profiling;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.Combat.Tests
{
    public sealed class BufferedSessionPresentationBridgeTests
    {
        [Test]
        public void BufferedPresentation_AppliesWithoutRetainingBorrowedCollections()
        {
            var factory = new RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 2);
            Fill(output.Presentation, new[] { State(1, 20000) });

            bridge.Apply(output.Presentation, 0);
            output.Clear();
            Fill(output.Presentation, new[] { State(1, 90000) });
            bridge.Sample(1f);

            Assert.That(factory.PositionX(1), Is.EqualTo(2f));
        }

        [Test]
        public void BufferedPresentation_RevisionChangePerformsFullResync()
        {
            var factory = new RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 4);
            Fill(output.Presentation, new[] { State(1, 10000), State(2, 20000) });
            bridge.Apply(output.Presentation, 0);

            output.Clear();
            Fill(output.Presentation, new[] { State(1, 30000), State(3, 40000) },
                new[] { State(3, 40000) }, new[] { State(1, 30000) },
                new[] { new EntityPresentationKey(2, 1) });
            bridge.Apply(output.Presentation, 0);
            Assert.That(factory.ActiveIds, Is.EquivalentTo(new[] { 1, 3 }));

            output.Clear();
            Fill(output.Presentation, new[] { State(1, 50000), State(4, 60000) },
                new[] { State(99, 990000) }, Array.Empty<EntityPresentationState>(),
                new[] { new EntityPresentationKey(1, 1) });
            bridge.Apply(output.Presentation, 1);
            bridge.Sample(0f);

            Assert.That(factory.ActiveIds, Is.EquivalentTo(new[] { 1, 4 }));
            Assert.That(factory.PositionX(1), Is.EqualTo(5f));
            Assert.That(factory.PositionX(4), Is.EqualTo(6f));
        }

        [Test]
        public void SteadyStateBufferedPresentation_ApplyAndSampleDoNotAllocateAfterWarmup()
        {
            var factory = new RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 16);
            var states = new EntityPresentationState[16];
            for (int index = 0; index < states.Length; index++)
                states[index] = State(index + 1, (index + 1) * 10000L);
            Fill(output.Presentation, states);
            bridge.Apply(Full(states));
            for (int index = 0; index < 32; index++)
                ApplyAndSample(bridge, output.Presentation);
            Recorder recorder = CreateValidatedRecorder();
            long events;
            try
            {
                recorder.enabled = false;
                recorder.enabled = true;
                ApplyAndSample(bridge, output.Presentation);
                recorder.enabled = false;
                events = recorder.sampleBlockCount;
            }
            finally
            {
                recorder.enabled = false;
            }
            Assert.That(events, Is.Zero, "GC.Alloc events=" + events);
        }

        private static void ApplyAndSample(SessionPresentationBridge bridge,
            SimPresentationOutputBuffer presentation)
        {
            bridge.Apply(presentation, 0);
            bridge.Sample(0.5f);
        }

        private static void Fill(SimPresentationOutputBuffer destination,
            IReadOnlyList<EntityPresentationState> entities,
            IReadOnlyList<EntityPresentationState> created = null,
            IReadOnlyList<EntityPresentationState> updated = null,
            IReadOnlyList<EntityPresentationKey> destroyed = null)
        {
            MethodInfo copy = typeof(SimPresentationOutputBuffer).GetMethod("CopyFrom",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(copy, Is.Not.Null);
            copy.Invoke(destination, new object[] { entities,
                created ?? Array.Empty<EntityPresentationState>(),
                updated ?? Array.Empty<EntityPresentationState>(),
                destroyed ?? Array.Empty<EntityPresentationKey>() });
        }

        private static PresentationFrame Full(EntityPresentationState[] states) =>
            new PresentationFrame(true, -1, 0, 0, states,
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationState>(),
                Array.Empty<EntityPresentationKey>());

        private static EntityPresentationState State(int id, long x) =>
            new EntityPresentationState(id, 1, 10, 0, SimTeam.Attacker,
                (int)(x / 10000), 0, x, 0, 0, 100);

        private static Recorder CreateValidatedRecorder()
        {
            Recorder recorder = Recorder.Get("GC.Alloc");
            Assert.That(recorder, Is.Not.Null);
            Assert.That(recorder.isValid, Is.True);
            recorder.FilterToCurrentThread();
            recorder.enabled = false;
            Allocate();
            recorder.enabled = false;
            recorder.enabled = true;
            Allocate();
            recorder.enabled = false;
            Assert.That(recorder.sampleBlockCount, Is.GreaterThanOrEqualTo(1));
            return recorder;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Allocate()
        {
            var buffer = new byte[4096];
            GC.KeepAlive(buffer);
        }

        private sealed class RecordingFactory : IEntityViewFactory
        {
            private readonly Dictionary<int, View> _views = new Dictionary<int, View>();
            public IEnumerable<int> ActiveIds => _views.Keys;
            public IEntityView Create(EntityPresentationKey key, int archetypeId)
            {
                var view = new View(key.EntityId);
                _views.Add(key.EntityId, view);
                return view;
            }
            public void Destroy(IEntityView view) => _views.Remove(((View)view).EntityId);
            public float PositionX(int id) => _views[id].X;
        }

        private sealed class View : IEntityView
        {
            public View(int entityId) => EntityId = entityId;
            public int EntityId { get; }
            public float X { get; private set; }
            public void SetPosition(float x, float y) => X = x;
        }
    }
}
