using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using global::YooAsset;

namespace ZWebGLFrameWork.YooAsset.Tests
{
    public sealed class YooAssetBusinessContentUpdaterTests
    {
        [UnityTest]
        public IEnumerator Success_ReturnsProviderOnlyAfterBothOperationsSucceed() => UniTask.ToCoroutine(async () =>
        {
            var package = new ControlledYooAssetPackage().Package;
            using var preparation = new ControlledYooAssetPreparation(package);
            var pending = preparation.Updater.PrepareAsync(CancellationToken.None);
            Assert.That(preparation.Current, Is.InstanceOf<RequestPackageVersionOperation>());
            preparation.CompleteVersion();
            await UniTask.WaitUntil(() => preparation.Current is UpdatePackageManifestOperation);
            Assert.That(pending.Status, Is.EqualTo(UniTaskStatus.Pending));
            preparation.CompleteManifest();
            var result = await pending;
            Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
            Assert.That(result.BusinessProvider, Is.TypeOf<YooAssetProvider>());
            ((IDisposable)result.BusinessProvider).Dispose();
            Assert.That(package.PackageValid, Is.True);
        });

        [UnityTest]
        public IEnumerator VersionFailure_ReturnsFailureWithoutStartingManifest() => UniTask.ToCoroutine(async () =>
        {
            using var preparation = new ControlledYooAssetPreparation(new ControlledYooAssetPackage().Package);
            var pending = preparation.Updater.PrepareAsync(CancellationToken.None);
            var version = preparation.Current;
            preparation.Fail("version request failed");
            var result = await pending;
            Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Failed));
            Assert.That(result.Message, Is.EqualTo("version request failed"));
            Assert.That(result.BusinessProvider, Is.Null);
            Assert.That(preparation.Current, Is.SameAs(version));
        });

        [UnityTest]
        public IEnumerator ManifestFailure_DoesNotOfferProvider() => UniTask.ToCoroutine(async () =>
        {
            using var preparation = new ControlledYooAssetPreparation(new ControlledYooAssetPackage().Package);
            var pending = preparation.Updater.PrepareAsync(CancellationToken.None);
            preparation.CompleteVersion();
            await UniTask.WaitUntil(() => preparation.Current is UpdatePackageManifestOperation);
            preparation.Fail("manifest request failed");
            var result = await pending;
            Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Failed));
            Assert.That(result.Message, Is.EqualTo("manifest request failed"));
            Assert.That(result.BusinessProvider, Is.Null);
        });

        [UnityTest]
        public IEnumerator CompletedOperation_BlocksReentryUntilFirstPreparationReturns() => UniTask.ToCoroutine(async () =>
        {
            using var preparation = new ControlledYooAssetPreparation(new ControlledYooAssetPackage().Package);
            var pending = preparation.Updater.PrepareAsync(CancellationToken.None);
            preparation.CompleteVersion();
            var overlap = await preparation.Updater.PrepareAsync(CancellationToken.None);
            Assert.That(overlap.Status, Is.EqualTo(BusinessContentUpdateStatus.Failed));
            await UniTask.WaitUntil(() => preparation.Current is UpdatePackageManifestOperation);
            preparation.CompleteManifest();
            var result = await pending;
            Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
            ((IDisposable)result.BusinessProvider).Dispose();
        });

        [UnityTest]
        public IEnumerator Cancellation_PropagatesAndRejectsOverlapUntilUnderlyingOperationCompletes() =>
            UniTask.ToCoroutine(async () =>
            {
                using var preparation = new ControlledYooAssetPreparation(new ControlledYooAssetPackage().Package);
                using var cancellation = new CancellationTokenSource();
                var pending = preparation.Updater.PrepareAsync(cancellation.Token);
                cancellation.Cancel();
                var result = await pending.SuppressCancellationThrow();
                Assert.That(result.IsCanceled, Is.True);
                Assert.That(result.Result, Is.Null);

                var overlap = await preparation.Updater.PrepareAsync(CancellationToken.None);
                Assert.That(overlap.Status, Is.EqualTo(BusinessContentUpdateStatus.Failed));
                preparation.CompleteVersion();
            });
    }
}
