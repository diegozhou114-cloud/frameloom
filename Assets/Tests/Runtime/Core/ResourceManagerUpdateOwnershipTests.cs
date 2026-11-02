using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZWebGLFrameWork.Core;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceManagerUpdateOwnershipTests : ResourceManagerLeaseFixture
    {
        [Test]
        public async Task CancelledUpdate_LateCandidateIsDisposedWithoutInstalling()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            using var cancellation = new CancellationTokenSource();
            var pending = Manager.PrepareBusinessContentAsync(cancellation.Token);
            var candidate = new LeaseProbeProvider(Asset);

            cancellation.Cancel();
            updater.Complete(0, candidate);

            Assert.That(await CaptureFailure(pending), Is.InstanceOf<OperationCanceledException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(Provider));
            Assert.That(candidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(Provider.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ManagerDestroyed_LateUpdateCandidateIsDisposed()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var pending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var candidate = new LeaseProbeProvider(Asset);

            Object.DestroyImmediate(Manager.gameObject);
            updater.Complete(0, candidate);

            Assert.That(await CaptureFailure(pending), Is.TypeOf<ObjectDisposedException>());
            Assert.That(candidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(Provider.DisposeCalls, Is.EqualTo(1));
            Assert.That(ResourceManager.HasInstance, Is.False);
        }

        [Test]
        public async Task ConcurrentUpdates_OlderCompletionCannotReplaceLatestProvider()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var older = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var newer = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var olderCandidate = new LeaseProbeProvider(Asset);
            var newerCandidate = new LeaseProbeProvider(Asset);

            updater.Complete(1, newerCandidate);
            Assert.That((await newer).Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
            updater.Complete(0, olderCandidate);

            Assert.That(await CaptureFailure(older), Is.TypeOf<InvalidOperationException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(newerCandidate));
            Assert.That(olderCandidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(newerCandidate.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ConcurrentUpdates_OlderCompletionCannotInstallWhileNewerIsPending()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var older = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var newer = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var olderCandidate = new LeaseProbeProvider(Asset);
            var newerCandidate = new LeaseProbeProvider(Asset);

            updater.Complete(0, olderCandidate);

            Assert.That(await CaptureFailure(older), Is.TypeOf<InvalidOperationException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(Provider));
            Assert.That(olderCandidate.DisposeCalls, Is.EqualTo(1));
            updater.Complete(1, newerCandidate);
            Assert.That((await newer).Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
            Assert.That(Manager.BusinessProvider, Is.SameAs(newerCandidate));
            Assert.That(newerCandidate.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ReplacedUpdater_LateCompletionCannotInstallBeforeReplacementStarts()
        {
            var oldUpdater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(oldUpdater);
            var oldPending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            Manager.SetBusinessContentUpdater(new DeferredBusinessUpdater());
            var oldCandidate = new LeaseProbeProvider(Asset);

            oldUpdater.Complete(0, oldCandidate);

            Assert.That(await CaptureFailure(oldPending), Is.TypeOf<InvalidOperationException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(Provider));
            Assert.That(oldCandidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(Provider.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ProviderReplaced_PendingUpdateCannotOverwriteManualReplacement()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var pending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var replacement = new LeaseProbeProvider(Asset);
            var candidate = new LeaseProbeProvider(Asset);

            Manager.SetBusinessProvider(replacement);
            updater.Complete(0, candidate);

            Assert.That(await CaptureFailure(pending), Is.TypeOf<InvalidOperationException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(replacement));
            Assert.That(candidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(replacement.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task StaleUpdate_ReturningInstalledProviderDoesNotDisposeCurrentOwner()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var pending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            var replacement = new LeaseProbeProvider(Asset);

            Manager.SetBusinessProvider(replacement);
            updater.Complete(0, replacement);

            Assert.That(await CaptureFailure(pending), Is.TypeOf<InvalidOperationException>());
            Assert.That(Manager.BusinessProvider, Is.SameAs(replacement));
            Assert.That(replacement.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task Update_PreCancelledDoesNotStartUpdaterOrReplaceProvider()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Exception failure = await CaptureFailure(
                Manager.PrepareBusinessContentAsync(cancellation.Token));

            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(updater.Calls, Is.Zero);
            Assert.That(Manager.BusinessProvider, Is.SameAs(Provider));
        }
    }
}
