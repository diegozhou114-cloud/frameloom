using NUnit.Framework;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public sealed class SimulationBenchmarkFixtureTests
    {
        [Test]
        public void BenchmarkFixture_CreatesDeclaredEntityCount()
        {
            using ISimBattleSession session =
                SimulationBenchmarkFixture.CreateSession(entityCount: 100);

            Assert.That(session.State.Presentation.Entities.Count, Is.EqualTo(100));
        }
    }

    public static class SimulationBenchmarkFixture
    {
        public static ISimBattleSession CreateSession(int entityCount)
        {
            if (entityCount < 0)
                throw new System.ArgumentOutOfRangeException(nameof(entityCount));
            var entities = new InitialEntitySpec[entityCount];
            for (int index = 0; index < entityCount; index++)
            {
                bool attacker = index % 2 == 0;
                entities[index] = new InitialEntitySpec(
                    attacker ? 10 : 2, index % 20, index / 20,
                    attacker ? SimTeam.Attacker : SimTeam.Defender,
                    attacker ? 0 : 1);
            }

            var session = new SimBattleSessionFactory().Create(
                SessionTestDriver.ManifestBytes());
            StartResult started = session.Start(SessionTestDriver.BattleInit(42, entities));
            if (!started.Accepted)
            {
                session.Dispose();
                throw new System.InvalidOperationException(started.Code.ToString());
            }
            return session;
        }
    }
}
