using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class AutosaveCoordinatorTests
    {
        [Test]
        public void MainVillageAutosave_AllPersistentChangesTrigger()
        {
            var data = new PlayerSaveData
            {
                Gold = 1,
                Elixir = 2,
                DarkElixir = 3,
                Gems = 4,
                Trophies = 5,
                Buildings = new System.Collections.Generic.List<BuildingSaveData>
                {
                    new BuildingSaveData { Id = "b1", TypeId = 1, Level = 1 },
                },
            };
            int saves = 0;
            var coordinator = new AutosaveCoordinator(
                () => data,
                () => { saves++; return true; });
            coordinator.InitializeBaseline();

            data.Gold++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Elixir++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.DarkElixir++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Gems++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Trophies++;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Buildings.Add(new BuildingSaveData { Id = "b2", TypeId = 2, Level = 1 });
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            data.Buildings[0].UpgradeEndTime = 1234;
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            Assert.That(saves, Is.EqualTo(7));
        }

        [Test]
        public void MainVillageAutosave_FailureDoesNotAdvanceBaseline()
        {
            var data = new PlayerSaveData { Gold = 1 };
            int attempts = 0;
            var coordinator = new AutosaveCoordinator(
                () => data,
                () => { attempts++; return attempts > 1; });
            coordinator.InitializeBaseline();
            data = new PlayerSaveData { Gold = 2 };
            Assert.That(coordinator.TrySaveIfChanged(), Is.False);
            Assert.That(coordinator.TrySaveIfChanged(), Is.True);
            Assert.That(attempts, Is.EqualTo(2));
        }

        [Test]
        public void MainVillageExit_SavesPendingChanges()
        {
            var data = new PlayerSaveData { Gold = 3 };
            int saves = 0;
            var coordinator = new AutosaveCoordinator(
                () => data,
                () => { saves++; return true; });
            coordinator.InitializeBaseline();
            data.Gold = 4;

            Assert.That(coordinator.FlushPending(), Is.True);
            Assert.That(saves, Is.EqualTo(1));
        }
    }
}
