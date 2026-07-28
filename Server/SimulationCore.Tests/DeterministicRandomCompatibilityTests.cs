using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.ServerTests
{
    public sealed class DeterministicRandomCompatibilityTests
    {
        [Test]
        public void Next_FullIntRange_UsesValuesBeyondLowerBound()
        {
            var rng = new DeterministicRandom(42);

            for (int index = 0; index < 8; index++)
            {
                int value = rng.Next(int.MinValue, int.MaxValue);
                Assert.That(value, Is.GreaterThanOrEqualTo(int.MinValue));
                Assert.That(value, Is.LessThan(int.MaxValue));
                Assert.That(value, Is.Not.EqualTo(int.MinValue));
            }
        }
    }
}
