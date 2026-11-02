using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.YooAsset.Tests
{
    public sealed class YooAssetProviderTests
    {
        private ControlledYooAssetPackage _fixture;
        private YooAssetProvider _provider;
        private TextAsset _asset;

        [SetUp]
        public void SetUp()
        {
            _fixture = new ControlledYooAssetPackage();
            _provider = new YooAssetProvider(_fixture.Package);
            _asset = new TextAsset("controlled asset");
        }

        [TearDown]
        public void TearDown()
        {
            _provider.Dispose();
            Object.DestroyImmediate(_asset);
        }

        [UnityTest]
        public IEnumerator Acquire_UsesIndependentHandles() => UniTask.ToCoroutine(async () =>
        {
            _fixture.Complete(_asset);
            using var first = await _provider.AcquireAsync<TextAsset>("test");
            using var second = await _provider.AcquireAsync<TextAsset>("test");
            Assert.That(_fixture.RefCount, Is.EqualTo(2));
            first.Dispose();
            first.Dispose();
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            Assert.That(second.Asset, Is.SameAs(_asset));
        });

        [UnityTest]
        public IEnumerator Cancellation_ReleasesOnlyItsOwnHandle() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            var canceled = _provider.AcquireAsync<TextAsset>("test", cancellation.Token);
            var surviving = _provider.AcquireAsync<TextAsset>("test");
            Assert.That(_fixture.RefCount, Is.EqualTo(2));
            cancellation.Cancel();
            _fixture.Complete(_asset);
            Assert.That(await Capture(canceled), Is.InstanceOf<OperationCanceledException>());
            using var lease = await surviving;
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            Assert.That(lease.Asset, Is.SameAs(_asset));
        });

        [UnityTest]
        public IEnumerator Failure_ReleasesHandle() => UniTask.ToCoroutine(async () =>
        {
            var pending = _provider.AcquireAsync<TextAsset>("test");
            _fixture.Fail("controlled failure");
            var error = await Capture(pending);
            Assert.That(error, Is.InstanceOf<InvalidOperationException>());
            Assert.That(error.Message, Does.Contain("controlled failure"));
            Assert.That(_fixture.RefCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Dispose_KeepsIssuedLeaseAndRejectsNewRequests() => UniTask.ToCoroutine(async () =>
        {
            _fixture.Complete(_asset);
            using var lease = await _provider.AcquireAsync<TextAsset>("test");
            _provider.Dispose();
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            Assert.That(lease.Asset, Is.SameAs(_asset));
            Assert.That(await Capture(_provider.AcquireAsync<TextAsset>("test")),
                Is.InstanceOf<ObjectDisposedException>());
            Assert.That(_fixture.Package.PackageValid, Is.True);
        });

        [UnityTest]
        public IEnumerator Dispose_ReleasesPendingWithoutWaitingForCompletion() => UniTask.ToCoroutine(async () =>
        {
            var pending = _provider.AcquireAsync<TextAsset>("test");
            _provider.Dispose();
            Assert.That(await Capture(pending), Is.InstanceOf<ObjectDisposedException>());
            Assert.That(_fixture.RefCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator LegacyCache_DoesNotReleaseIndependentLease() => UniTask.ToCoroutine(async () =>
        {
            _fixture.Complete(_asset);
            Assert.That(await _provider.LoadAsync<TextAsset>("test"), Is.SameAs(_asset));
            Assert.That(await _provider.LoadAsync<TextAsset>("test"), Is.SameAs(_asset));
            using var lease = await _provider.AcquireAsync<TextAsset>("test");
            Assert.That(_fixture.RefCount, Is.EqualTo(2));
            _provider.Unload("test");
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            Assert.That(lease.Asset, Is.SameAs(_asset));
        });

        [UnityTest]
        public IEnumerator ConcurrentLegacyLoads_KeepOnlyOneCacheHandle() => UniTask.ToCoroutine(async () =>
        {
            var first = _provider.LoadAsync<TextAsset>("test");
            var second = _provider.LoadAsync<TextAsset>("test");
            _fixture.Complete(_asset);
            Assert.That(await first, Is.SameAs(_asset));
            Assert.That(await second, Is.SameAs(_asset));
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            _provider.Dispose();
            Assert.That(_fixture.RefCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Unload_PreventsOldPendingLoadFromRefillingCache() => UniTask.ToCoroutine(async () =>
        {
            var old = _provider.LoadAsync<TextAsset>("test");
            _provider.Unload("test");
            var current = _provider.LoadAsync<TextAsset>("test");
            _fixture.Complete(_asset);
            Assert.That(await Capture(old), Is.InstanceOf<OperationCanceledException>());
            Assert.That(await current, Is.SameAs(_asset));
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator LegacyCache_SeparatesRequestedTypes() => UniTask.ToCoroutine(async () =>
        {
            var untyped = _fixture.AddAssetType(typeof(Object));
            _fixture.Complete(untyped, _asset);
            _fixture.Complete(_asset);
            await _provider.LoadAsync<Object>("test");
            await _provider.LoadAsync<TextAsset>("test");
            Assert.That(_fixture.GetRefCount(untyped), Is.EqualTo(1));
            Assert.That(_fixture.RefCount, Is.EqualTo(1));
            _provider.Unload("test");
            Assert.That(_fixture.GetRefCount(untyped), Is.Zero);
            Assert.That(_fixture.RefCount, Is.Zero);
        });

        [Test]
        public void SynchronousLoad_IsExplicitlyUnsupported()
        {
            Assert.That(_provider.SupportsSyncLoad, Is.False);
            Assert.Throws<NotSupportedException>(() => _provider.Load<TextAsset>("test"));
        }

        [UnityTest]
        public IEnumerator Updater_PreCanceledRequestDoesNotStartAnOperation() => UniTask.ToCoroutine(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var updater = new YooAssetBusinessContentUpdater(_fixture.Package);
            Assert.That(await Capture(updater.PrepareAsync(cancellation.Token)),
                Is.InstanceOf<OperationCanceledException>());
        });

        private static async UniTask<Exception> Capture<T>(UniTask<T> operation)
        {
            try { await operation; return null; }
            catch (Exception exception) { return exception; }
        }
    }
}
