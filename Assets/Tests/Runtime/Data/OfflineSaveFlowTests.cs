using NUnit.Framework;
using ZWebGLFrameWork.Data;

namespace ZWebGLFrameWork.Runtime.Tests.Data
{
    public sealed class OfflineSaveFlowTests
    {
        private sealed class FakeGateway : IOfflineSaveGateway
        {
            public bool ExistsResult;
            public bool CreateResult;
            public bool LoadResult;
            public bool Exists(int slot) => ExistsResult;
            public bool Create(int slot) => CreateResult;
            public bool Load(int slot) => LoadResult;
        }

        [Test]
        public void OfflineLogin_DefaultSaveCreationFailure_DoesNotEnterVillage()
        {
            var gateway = new FakeGateway { CreateResult = false, LoadResult = true };
            Assert.That(new OfflineSaveFlow(gateway).TryPrepareSlot(2), Is.False);
        }

        [Test]
        public void OfflineLogin_LoadFailure_DoesNotEnterVillage()
        {
            var gateway = new FakeGateway { ExistsResult = true, LoadResult = false };
            Assert.That(new OfflineSaveFlow(gateway).TryPrepareSlot(2), Is.False);
        }
    }
}
