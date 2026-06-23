using NUnit.Framework;
using System.Collections.Generic;
using ZWebGLFrameWork.SimulationCore;

namespace ZWebGLFrameWork.SimulationCore.Tests
{
    public class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var rng1 = new DeterministicRandom(42);
            var rng2 = new DeterministicRandom(42);

            var seq1 = new List<int>();
            var seq2 = new List<int>();

            for (int i = 0; i < 100; i++)
            {
                seq1.Add(rng1.Next(100));
                seq2.Add(rng2.Next(100));
            }

            Assert.That(seq1, Is.EqualTo(seq2));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var rng1 = new DeterministicRandom(42);
            var rng2 = new DeterministicRandom(99);

            bool allSame = true;
            for (int i = 0; i < 100; i++)
            {
                if (rng1.Next(100) != rng2.Next(100))
                {
                    allSame = false;
                    break;
                }
            }

            Assert.That(allSame, Is.False,
                "不同 seed 应产生不同序列（极端低概率下可能相同，但不应常态化）");
        }

        [Test]
        public void SequenceOf10000_AcrossThreeSeeds_EachSelfReproducible()
        {
            int[] seeds = { 1, 42, int.MaxValue };
            foreach (int seed in seeds)
            {
                var rng1 = new DeterministicRandom(seed);
                var rng2 = new DeterministicRandom(seed);

                for (int i = 0; i < 10000; i++)
                {
                    Assert.That(rng1.Next(), Is.EqualTo(rng2.Next()),
                        $"seed={seed}, index={i}: 序列不一致");
                }
            }
        }

        [Test]
        public void Next_RangeLimits_Respected()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int val = rng.Next(10);
                Assert.That(val, Is.InRange(0, 9));
            }
        }

        [Test]
        public void Next_MinMax_Respected()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int val = rng.Next(5, 15);
                Assert.That(val, Is.InRange(5, 14));
            }
        }

        [Test]
        public void NextProbability_Range0To9999()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 1000; i++)
            {
                int prob = rng.NextProbability();
                Assert.That(prob, Is.InRange(0, 9999));
            }
        }

        [Test]
        public void RollChance_AlwaysTrue_When100Percent()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(rng.RollChance(10000), Is.True);
            }
        }

        [Test]
        public void RollChance_AlwaysFalse_When0Percent()
        {
            var rng = new DeterministicRandom(42);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(rng.RollChance(0), Is.False);
            }
        }

        [Test]
        public void Reset_SameSeed_ReproducesOriginalSequence()
        {
            var rng = new DeterministicRandom(42);
            var first = new int[10];
            for (int i = 0; i < 10; i++) first[i] = rng.Next();

            rng.Reset(42);
            for (int i = 0; i < 10; i++)
                Assert.That(rng.Next(), Is.EqualTo(first[i]));
        }
    }
}
