using NUnit.Framework;
using ZWebGLFrameWork.Combat;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.PublicApiContract.Tests
{
    public sealed class SessionPresentationBridgePublicContractTests
    {
        [Test]
        public void BufferedApply_PublishedOutputCreatesAndSamplesView()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 4);
            var factory = new PublicApiContractTestSupport.RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            try
            {
                PublicApiContractTestSupport.Publish(session, output);
                Assert.That(output.Presentation.Entities, Is.Not.Empty);
                ApplyBorrowed(bridge, output);

                EntityPresentationState state = output.Presentation.Entities[0];
                var key = new EntityPresentationKey(state.EntityId, state.Generation);
                Assert.That(factory.ActiveCount, Is.EqualTo(output.Presentation.Entities.Count));
                Assert.That(factory.PositionX(key), Is.EqualTo(state.PosXRaw / 10000f));
            }
            finally
            {
                bridge.Cleanup();
            }
        }

        [Test]
        public void BufferedApply_OutputClearDoesNotChangeLaterSample()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 4);
            var factory = new PublicApiContractTestSupport.RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            try
            {
                PublicApiContractTestSupport.Publish(session, output);
                EntityPresentationState state = output.Presentation.Entities[0];
                var key = new EntityPresentationKey(state.EntityId, state.Generation);
                ApplyBorrowed(bridge, output);
                float expected = state.PosXRaw / 10000f;

                output.Clear();
                bridge.Sample(1f);
                Assert.That(factory.PositionX(key), Is.EqualTo(expected));
            }
            finally
            {
                bridge.Cleanup();
            }
        }

        [Test]
        public void BufferedApply_BufferReuseUpdatesOnlyAfterExplicitApply()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 4);
            var factory = new PublicApiContractTestSupport.RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            try
            {
                PublicApiContractTestSupport.Publish(session, output);
                ApplyBorrowed(bridge, output);
                int initialCount = factory.ActiveCount;
                long revision = output.Revision;

                Assert.That(session.Submit(new DeployCmd
                {
                    Tick = output.Tick + 1,
                    PlayerId = 0,
                    Sequence = 1,
                    UnitTypeId = 10,
                    GridX = 2,
                    GridY = 2,
                }).Accepted, Is.True);
                PublicApiContractTestSupport.Publish(session, output);
                Assert.That(output.Revision, Is.EqualTo(revision));
                Assert.That(output.Presentation.Created, Has.Count.EqualTo(1));
                EntityPresentationState created = output.Presentation.Created[0];
                var createdKey = new EntityPresentationKey(created.EntityId, created.Generation);

                bridge.Sample(1f);
                Assert.That(factory.ActiveCount, Is.EqualTo(initialCount));
                Assert.That(factory.Contains(createdKey), Is.False);

                ApplyBorrowed(bridge, output);
                bridge.Sample(1f);
                Assert.That(factory.ActiveCount, Is.EqualTo(initialCount + 1));
                Assert.That(factory.Contains(createdKey), Is.True);
            }
            finally
            {
                bridge.Cleanup();
            }
        }

        [Test]
        public void BufferedApply_OutputDisposePreservesConsumedValuesAndViews()
        {
            using ISimBattleSession session = PublicApiContractTestSupport.CreateStartedSession();
            using var output = new SimTickOutputBuffer(SimTickOutputFields.Presentation, 4);
            var factory = new PublicApiContractTestSupport.RecordingFactory();
            var bridge = new SessionPresentationBridge(factory);
            try
            {
                PublicApiContractTestSupport.Publish(session, output);
                EntityPresentationState state = output.Presentation.Entities[0];
                var key = new EntityPresentationKey(state.EntityId, state.Generation);
                ApplyBorrowed(bridge, output);
                float expected = state.PosXRaw / 10000f;

                output.Dispose();
                bridge.Sample(1f);
                Assert.That(factory.PositionX(key), Is.EqualTo(expected));
                Assert.That(factory.ActiveCount, Is.EqualTo(1));
            }
            finally
            {
                bridge.Cleanup();
            }
        }

        private static void ApplyBorrowed(SessionPresentationBridge bridge,
            SimTickOutputBuffer output)
        {
            bridge.Apply(output.Presentation, output.Revision);
        }
    }
}
