using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceLeaseTests : RuntimeTestBase
    {
        private TextAsset CreateAsset()
        {
            var asset = new TextAsset("lease content");
            RegisterCleanup(() => Object.DestroyImmediate(asset));
            return asset;
        }

        [Test]
        public void Dispose_ReleasesOnceAndClearsAsset()
        {
            int releases = 0;
            using var lease = new ResourceLease<TextAsset>(CreateAsset(), () => releases++);

            lease.Dispose();
            lease.Dispose();

            Assert.That(lease.Asset, Is.Null);
            Assert.That(lease.IsDisposed, Is.True);
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void Dispose_WhenReleaseThrows_StillClearsOwnershipAndDoesNotRetry()
        {
            int releases = 0;
            using var lease = new ResourceLease<TextAsset>(CreateAsset(), () =>
            {
                releases++;
                throw new InvalidOperationException("release failure");
            });

            Assert.Throws<InvalidOperationException>(() => lease.Dispose());

            Assert.That(lease.Asset, Is.Null);
            Assert.That(lease.IsDisposed, Is.True);
            Assert.DoesNotThrow(() => lease.Dispose());
            Assert.That(releases, Is.EqualTo(1));
        }

        [Test]
        public void SameAsset_TwoLeasesReleaseIndependentOwnership()
        {
            var asset = CreateAsset();
            int firstReleases = 0, secondReleases = 0;
            using var first = new ResourceLease<TextAsset>(asset, () => firstReleases++);
            using var second = new ResourceLease<TextAsset>(asset, () => secondReleases++);

            first.Dispose();

            Assert.That(second.Asset, Is.SameAs(asset));
            Assert.That(second.Asset.text, Is.EqualTo("lease content"));
            Assert.That(firstReleases, Is.EqualTo(1));
            Assert.That(secondReleases, Is.Zero);
            second.Dispose();
            Assert.That(secondReleases, Is.EqualTo(1));
        }

        [Test]
        public async Task ResourcesProvider_RepeatedAcquireReturnsIndependentLeases()
        {
            var provider = new ResourcesProvider();
            using var first = await provider.AcquireAsync<TextAsset>(
                "Config/Languages/invalid-structure");
            using var second = await provider.AcquireAsync<TextAsset>(
                "Config/Languages/invalid-structure");

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Asset, Is.SameAs(first.Asset));
            first.Dispose();
            Assert.That(second.Asset != null, Is.True);
            Assert.That(second.Asset.text, Is.Not.Empty);
        }

        [Test]
        public async Task ResourcesProvider_ActiveLeaseSurvivesUnusedAssetCollection()
        {
            using var lease = await new ResourcesProvider().AcquireAsync<TextAsset>(
                "Config/Languages/invalid-structure");

            await Resources.UnloadUnusedAssets().ToUniTask();

            Assert.That(lease.Asset != null, Is.True);
            Assert.That(lease.Asset.text, Is.Not.Empty);
        }

        [Test]
        public async Task ResourcesProvider_PreCancelledAcquireDoesNotReturnOwnership()
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            ResourceLease<TextAsset> lease = null;
            try
            {
                lease = await new ResourcesProvider().AcquireAsync<TextAsset>(
                    "Config/Languages/invalid-structure", cancellation.Token);
                Assert.Fail("Expected cancellation before loading.");
            }
            catch (OperationCanceledException) { }
            finally { lease?.Dispose(); }
            Assert.That(lease, Is.Null);
        }
    }
}
