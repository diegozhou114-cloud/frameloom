using NUnit.Framework;
using ZWebGLFrameWork.SimulationCore.MiniSiege;

namespace ZWebGLFrameWork.ServerTests
{
    /// <summary>Gameplay rules and replay invariants for the runnable local duel.</summary>
    [TestFixture]
    public sealed class MiniSiegeTests
    {
        private static SiegeInput[] Empty() => new[]
        {
            new SiegeInput { Player = 0 }, new SiegeInput { Player = 1 }
        };

        private static void Deploy(SiegeSimulation sim, int player, int sequence, int unit, int lane = 0)
        {
            var inputs = Empty();
            inputs[player] = new SiegeInput { Player = player, Sequence = sequence, Unit = unit, Lane = lane };
            sim.Step(inputs);
        }

        [Test]
        public void NoInputBattle_TimesOutAsDraw_AndStopsAdvancing()
        {
            var sim = new SiegeSimulation(7);
            for (int i = 0; i < SiegeSimulation.DurationTicks; i++) sim.Step(Empty());
            Assert.That(sim.Winner, Is.EqualTo(2));
            Assert.That(sim.BaseHealth(0), Is.EqualTo(SiegeSimulation.BaseHP));
            Assert.That(sim.BaseHealth(1), Is.EqualTo(SiegeSimulation.BaseHP));
            ulong hash = sim.Checksum;
            sim.Step(Empty());
            Assert.That(sim.Tick, Is.EqualTo(SiegeSimulation.DurationTicks));
            Assert.That(sim.Checksum, Is.EqualTo(hash));
        }

        [Test]
        public void EnergyCosts_PreventOverspending_AndDuplicateInputsDoNotSpawn()
        {
            var sim = new SiegeSimulation(7);
            Deploy(sim, 0, 1, 1);
            Assert.That(sim.Energy(0), Is.EqualTo(5));
            Deploy(sim, 0, 1, 1);
            Assert.That(sim.Energy(0), Is.EqualTo(5));
            Deploy(sim, 0, 2, 2);
            Assert.That(sim.Energy(0), Is.EqualTo(1));
            Deploy(sim, 0, 3, 2);
            Assert.That(sim.Energy(0), Is.EqualTo(1));
            var views = new System.Collections.Generic.List<SiegeUnitView>();
            sim.CopyUnits(views);
            Assert.That(views.Count, Is.EqualTo(4)); // two bases and exactly two troops
        }

        [Test]
        public void Pulse_HasCooldown_AndCannotDamageEitherBase()
        {
            var sim = new SiegeSimulation(7);
            Deploy(sim, 0, 1, 3);
            Assert.That(sim.Energy(0), Is.EqualTo(2));
            Assert.That(sim.SkillRemaining(0), Is.EqualTo(SiegeSimulation.SkillCooldownTicks - 1));
            for (int i = 0; i < 120; i++) sim.Step(Empty());
            int energy = sim.Energy(0);
            Deploy(sim, 0, 2, 3);
            Assert.That(sim.Energy(0), Is.EqualTo(energy));
            Assert.That(sim.BaseHealth(0), Is.EqualTo(300));
            Assert.That(sim.BaseHealth(1), Is.EqualTo(300));
        }

        [Test]
        public void Pulse_DamagesOnlyEnemyTroopsInSelectedMidfieldLane()
        {
            var sim = new SiegeSimulation(7);
            Deploy(sim, 1, 1, 2, 0);
            Deploy(sim, 1, 2, 2, 1);
            for (int i = 0; i < 100; i++) sim.Step(Empty());
            Deploy(sim, 0, 1, 3, 0);
            var views = new System.Collections.Generic.List<SiegeUnitView>();
            sim.CopyUnits(views);
            Assert.That(views.Find(unit => unit.Kind == 2 && unit.Lane == 0).HP, Is.EqualTo(10));
            Assert.That(views.Find(unit => unit.Kind == 2 && unit.Lane == 1).HP, Is.EqualTo(45));
        }

        [Test]
        public void SecondHalf_RecoversTwoEnergyPerSecond()
        {
            var sim = new SiegeSimulation(7);
            for (int i = 0; i < 1799; i++) sim.Step(Empty());
            Deploy(sim, 0, 1, 1);
            Assert.That(sim.Energy(0), Is.EqualTo(9));
            sim.Step(Empty());
            Assert.That(sim.Energy(0), Is.EqualTo(11));
        }

        [Test]
        public void GuardPush_DestroyingBaseEndsBattle_WithReplayMatchingEveryTick()
        {
            var live = new SiegeSimulation(321);
            var replay = new SiegeSimulation(321);
            int sequence = 0;
            while (live.Winner == -1)
            {
                var inputs = Empty();
                if (live.Tick % 20 == 0)
                    inputs[0] = new SiegeInput { Player = 0, Sequence = ++sequence, Unit = 1, Lane = 0 };
                live.Step(inputs);
                replay.Step(inputs);
                Assert.That(replay.Checksum, Is.EqualTo(live.Checksum), "Tick " + live.Tick);
            }
            Assert.That(live.Winner, Is.EqualTo(0));
            Assert.That(live.BaseHealth(1), Is.Zero);
            Assert.That(live.Tick, Is.LessThan(SiegeSimulation.DurationTicks));
        }

        [Test]
        public void MirroredInputs_HaveNoFirstPlayerCombatAdvantage()
        {
            var sim = new SiegeSimulation(7);
            int sequence = 0;
            while (sim.Winner == -1)
            {
                var inputs = Empty();
                if (sim.Tick % 40 == 0)
                {
                    sequence++;
                    for (int player = 0; player < 2; player++)
                        inputs[player] = new SiegeInput { Player = player, Sequence = sequence,
                            Unit = sequence % 3 == 0 ? 2 : 1, Lane = sequence % 2 };
                }
                sim.Step(inputs);
                Assert.That(sim.BaseHealth(0), Is.EqualTo(sim.BaseHealth(1)), "Tick " + sim.Tick);
                Assert.That(sim.Energy(0), Is.EqualTo(sim.Energy(1)));
            }
            Assert.That(sim.Winner, Is.EqualTo(2));
        }

        [Test]
        public void InvalidLane_DoesNotConsumeEnergy()
        {
            var sim = new SiegeSimulation(7);
            Deploy(sim, 0, 1, 1, 2);
            Assert.That(sim.Energy(0), Is.EqualTo(8));
        }

        [Test]
        public void FramesRequireBothPlayersInCanonicalOrder()
        {
            var sim = new SiegeSimulation(7);
            Assert.Throws<System.ArgumentException>(() => sim.Step(new[] { new SiegeInput { Player = 1 } }));
            Assert.That(sim.Tick, Is.Zero);
        }
    }
}
