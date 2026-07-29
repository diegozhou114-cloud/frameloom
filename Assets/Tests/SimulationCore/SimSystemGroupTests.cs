using System.Collections.Generic;
using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class SimSystemGroupTests
    {
        [Test]
        public void UpdateAll_SystemAddedAfterInitialization_ExecutesByOrder()
        {
            var calls = new List<string>();
            var group = new SimSystemGroup();
            group.Add(new RecordingSystem("high", 20, calls));
            group.InitAll(null);
            group.Add(new RecordingSystem("low", 10, calls));

            group.UpdateAll();

            Assert.That(calls, Is.EqualTo(new[] { "low", "high" }));
        }

        [Test]
        public void CleanupAll_SystemAddedAfterInitialization_ExecutesByOrder()
        {
            var calls = new List<string>();
            var group = new SimSystemGroup();
            group.Add(new RecordingSystem("high", 20, calls));
            group.InitAll(null);
            group.Add(new RecordingSystem("low", 10, calls));

            group.CleanupAll();

            Assert.That(calls, Is.EqualTo(new[] { "low", "high" }));
        }

        [Test]
        public void UpdateAll_SystemAddedAfterInitialization_IsInitializedWithTheGroupWorld()
        {
            SimWorld world = CreateWorld();
            var group = new SimSystemGroup();
            group.InitAll(world);
            var system = new LifecycleSystem(world);

            group.Add(system);
            group.UpdateAll();

            Assert.That(system.Initialized, Is.True);
            Assert.That(system.UpdatedWithExpectedWorld, Is.True);
        }

        [Test]
        public void UpdateAll_SystemEnabledAfterInitialization_IsInitializedBeforeUpdate()
        {
            SimWorld world = CreateWorld();
            var system = new LifecycleSystem(world) { Enabled = false };
            var group = new SimSystemGroup();
            group.Add(system);
            group.InitAll(world);

            system.Enabled = true;
            group.UpdateAll();

            Assert.That(system.Initialized, Is.True);
            Assert.That(system.UpdatedWithExpectedWorld, Is.True);
        }

        [Test]
        public void CleanupAll_CleansInitializedSystemAfterItIsDisabled()
        {
            SimWorld world = CreateWorld();
            var system = new LifecycleSystem(world);
            var group = new SimSystemGroup();
            group.Add(system);
            group.InitAll(world);

            system.Enabled = false;
            group.CleanupAll();

            Assert.That(system.Cleaned, Is.True);
        }

        private static SimWorld CreateWorld() =>
            new SimWorld(SimComponentSchema.CreateRegistry(), 8);

        private sealed class RecordingSystem : SimSystemBase
        {
            private readonly string _name;
            private readonly List<string> _calls;

            public RecordingSystem(string name, int order, List<string> calls)
            {
                _name = name;
                _calls = calls;
                Order = order;
            }

            public override void Update()
            {
                _calls.Add(_name);
            }

            public override void Cleanup()
            {
                _calls.Add(_name);
            }
        }

        private sealed class LifecycleSystem : SimSystemBase
        {
            private readonly SimWorld _expectedWorld;

            public bool Initialized { get; private set; }
            public bool UpdatedWithExpectedWorld { get; private set; }
            public bool Cleaned { get; private set; }

            public LifecycleSystem(SimWorld expectedWorld)
            {
                _expectedWorld = expectedWorld;
            }

            public override void Init()
            {
                Initialized = World == _expectedWorld;
            }

            public override void Update()
            {
                UpdatedWithExpectedWorld = Initialized && World == _expectedWorld;
            }

            public override void Cleanup()
            {
                Cleaned = true;
            }
        }
    }
}
