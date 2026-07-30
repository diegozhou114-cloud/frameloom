using NUnit.Framework;
using ZWebGLFrameWork.Data;
using ZWebGLFrameWork.FSM;

namespace ZWebGLFrameWork.Runtime.Tests.FSM
{
    [Category("MainVillageSuspend")]
    public sealed class MainVillageSuspendTests
    {
        [Test]
        public void Suspend_FlushesPendingPersistentChangesAndDeactivatesState()
        {
            var data = new PlayerSaveData { Gold = 3 };
            int saves = 0;
            var autosave = new AutosaveCoordinator(
                () => data,
                () => { saves++; return true; });
            autosave.InitializeBaseline();
            var state = new MainVillageState(autosave) { IsActive = true };
            data.Gold = 4;

            state.OnSuspend(null);

            Assert.That(saves, Is.EqualTo(1));
            Assert.That(state.IsActive, Is.False);
        }

        [Test]
        public void Suspend_WithoutPersistentChanges_DoesNotSave()
        {
            var data = new PlayerSaveData { Gold = 3 };
            int saves = 0;
            var autosave = new AutosaveCoordinator(
                () => data,
                () => { saves++; return true; });
            autosave.InitializeBaseline();
            var state = new MainVillageState(autosave) { IsActive = true };

            state.OnSuspend(null);

            Assert.That(saves, Is.Zero);
            Assert.That(state.IsActive, Is.False);
        }
    }
}
