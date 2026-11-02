using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceManagerLeaseTests : ResourceManagerLeaseFixture
    {
        private const string Path = "shared/resource";

        [Test]
        public async Task Acquire_BypassesBareCacheAndKeepsIndependentOwnership()
        {
            Assert.That(await Manager.LoadAsync<TextAsset>(ResourceLayer.Business, Path), Is.SameAs(Asset));
            using var first = await Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);
            using var second = await Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(Provider.AcquireCalls, Is.EqualTo(2));
            Assert.That(Provider.LoadCalls, Is.EqualTo(1));
            first.Dispose();
            Manager.Unload(ResourceLayer.Business, Path);
            Manager.ClearCache();

            Assert.That(second.Asset, Is.SameAs(Asset));
            Assert.That(Provider.ReleaseCalls, Is.EqualTo(1));
            second.Dispose();
            Assert.That(Provider.ReleaseCalls, Is.EqualTo(2));
        }

        [Test]
        public async Task LegacyProvider_RejectsAcquireButKeepsBareLoadCompatible()
        {
            var legacy = new LegacyLeaseProbeProvider(Asset);
            Manager.SetBusinessProvider(legacy);

            Exception failure = await CaptureFailure(
                Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path));

            Assert.That(failure, Is.TypeOf<NotSupportedException>());
            Assert.That(legacy.LoadCalls, Is.Zero);
            Assert.That(await Manager.LoadAsync<TextAsset>(ResourceLayer.Business, Path), Is.SameAs(Asset));
        }

        [Test]
        public async Task ProviderSwap_ExistingLeaseRemainsValidAndReleasesOriginalProvider()
        {
            using var lease = await Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);
            var original = Provider;
            var replacement = new LeaseProbeProvider(Asset);

            Manager.SetBusinessProvider(replacement);

            Assert.That(original.DisposeCalls, Is.EqualTo(1));
            Assert.That(lease.Asset, Is.SameAs(Asset));
            Assert.That(original.ReleaseCalls, Is.Zero);
            lease.Dispose();
            Assert.That(original.ReleaseCalls, Is.EqualTo(1));
            Assert.That(replacement.ReleaseCalls, Is.Zero);
        }

        [Test]
        public async Task ProviderSwap_PendingOldAcquireIsReleasedAndRejected()
        {
            var original = new LeaseProbeProvider(Asset, deferred: true);
            Manager.SetBusinessProvider(original);
            var pending = Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);
            var replacement = new LeaseProbeProvider(Asset);

            Manager.SetBusinessProvider(replacement);
            original.CompleteAcquires();

            Assert.That(await CaptureFailure(pending), Is.TypeOf<InvalidOperationException>());
            Assert.That(original.ReleaseCalls, Is.EqualTo(1));
            Assert.That(replacement.ReleaseCalls, Is.Zero);
            Assert.That(Manager.BusinessProvider, Is.SameAs(replacement));
        }

        [Test]
        public async Task Acquire_PreCancelledDoesNotCallProvider()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Exception failure = await CaptureFailure(Manager.AcquireAsync<TextAsset>(
                ResourceLayer.Business, Path, cancellation.Token));

            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(Provider.AcquireCalls, Is.Zero);
        }

        [Test]
        public async Task Acquire_CancelledWhileLoadingReleasesIgnoredCancellationResult()
        {
            var provider = new LeaseProbeProvider(Asset, deferred: true);
            Manager.SetBusinessProvider(provider);
            using var cancellation = new CancellationTokenSource();
            var pending = Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path, cancellation.Token);

            cancellation.Cancel();
            provider.CompleteAcquires();

            Assert.That(await CaptureFailure(pending), Is.InstanceOf<OperationCanceledException>());
            Assert.That(provider.ReleaseCalls, Is.EqualTo(1));
            Assert.That(provider.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ManagerDestroyed_PendingAcquireIsReleasedAndRejected()
        {
            var provider = new LeaseProbeProvider(Asset, deferred: true);
            Manager.SetBusinessProvider(provider);
            var pending = Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);

            Object.DestroyImmediate(Manager.gameObject);
            provider.CompleteAcquires();

            Assert.That(await CaptureFailure(pending), Is.TypeOf<ObjectDisposedException>());
            Assert.That(provider.ReleaseCalls, Is.EqualTo(1));
            Assert.That(provider.DisposeCalls, Is.EqualTo(1));
            Assert.That(ResourceManager.HasInstance, Is.False);
        }

        [Test]
        public async Task ManagerDestroyed_AlreadyDeliveredLeaseRemainsCallerOwned()
        {
            using var lease = await Manager.AcquireAsync<TextAsset>(ResourceLayer.Business, Path);

            Object.DestroyImmediate(Manager.gameObject);

            Assert.That(Provider.DisposeCalls, Is.EqualTo(1));
            Assert.That(lease.Asset, Is.SameAs(Asset));
            Assert.That(Provider.ReleaseCalls, Is.Zero);
            lease.Dispose();
            Assert.That(Provider.ReleaseCalls, Is.EqualTo(1));
        }
    }
}
