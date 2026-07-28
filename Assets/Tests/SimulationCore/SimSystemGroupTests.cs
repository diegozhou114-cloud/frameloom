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
    }
}
