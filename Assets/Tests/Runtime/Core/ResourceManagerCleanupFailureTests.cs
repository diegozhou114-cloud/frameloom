using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceManagerCleanupFailureTests : ResourceManagerLeaseFixture
    {
        [Test]
        public async Task CancelledAcquire_WhenLateLeaseReleaseThrows_PreservesCancellation()
        {
            var provider = new CleanupFailureProvider(Asset, deferred: true) { FailRelease = true };
            Manager.SetBusinessProvider(provider);
            using var cancellation = new CancellationTokenSource();
            var pending = Manager.AcquireAsync<TextAsset>(
                ResourceLayer.Business, "late/resource", cancellation.Token);
            cancellation.Cancel();
            LogAssert.Expect(LogType.Exception, new Regex("lease release failed"));

            provider.Complete();
            Exception failure = await CaptureFailure(pending);

            Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
            Assert.That(((OperationCanceledException)failure).CancellationToken,
                Is.EqualTo(cancellation.Token));
            Assert.That(provider.ReleaseCalls, Is.EqualTo(1));
            Assert.That(Manager.BusinessProvider, Is.SameAs(provider));
        }

        [Test]
        public async Task StaleUpdate_WhenUnusedCandidateDisposeThrows_PreservesStaleUpdateFailure()
        {
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var pending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            Manager.SetBusinessContentUpdater(new DeferredBusinessUpdater());
            var candidate = new CleanupFailureProvider(Asset) { FailDispose = true };
            LogAssert.Expect(LogType.Exception, new Regex("provider dispose failed"));

            updater.Complete(0, candidate);
            Exception failure = await CaptureFailure(pending);

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(candidate.DisposeCalls, Is.EqualTo(1));
            Assert.That(Manager.BusinessProvider, Is.SameAs(Provider));
            Assert.That(Provider.DisposeCalls, Is.Zero);
        }

        [Test]
        public async Task ProviderRetirementCleanupThrows_UpdateStillCommitsAndAttemptsBothCleanups()
        {
            var previous = new CleanupFailureProvider(Asset)
            {
                FailUnloadUnused = true,
                FailDispose = true
            };
            Manager.SetBusinessProvider(previous);
            var replacement = new LeaseProbeProvider(Asset);
            var updater = new DeferredBusinessUpdater();
            Manager.SetBusinessContentUpdater(updater);
            var pending = Manager.PrepareBusinessContentAsync(CancellationToken.None);
            LogAssert.Expect(LogType.Exception, new Regex("retirement unload failed"));
            LogAssert.Expect(LogType.Exception, new Regex("provider dispose failed"));

            updater.Complete(0, replacement);
            BusinessContentUpdateResult result = await pending;

            Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Updated));
            Assert.That(result.BusinessProvider, Is.SameAs(replacement));
            Assert.That(Manager.BusinessProvider, Is.SameAs(replacement));
            Assert.That(previous.UnloadUnusedCalls, Is.EqualTo(1));
            Assert.That(previous.DisposeCalls, Is.EqualTo(1));
            Assert.That(replacement.DisposeCalls, Is.Zero);
        }

        [Test]
        public void DestroyedManager_RejectsConfigurationWithoutTakingCandidateOwnership()
        {
            var candidate = new CleanupFailureProvider(Asset);
            Object.DestroyImmediate(Manager.gameObject);

            try
            {
                Assert.Throws<ObjectDisposedException>(() => Manager.SetBusinessProvider(candidate));
                Assert.Throws<ObjectDisposedException>(() =>
                    Manager.SetBusinessContentUpdater(new DeferredBusinessUpdater()));
                Assert.That(candidate.DisposeCalls, Is.Zero);
                Assert.That(candidate.UnloadUnusedCalls, Is.Zero);
                Assert.That(ResourceManager.HasInstance, Is.False);
            }
            finally { candidate.Dispose(); }

            Assert.That(candidate.DisposeCalls, Is.EqualTo(1));
        }

        private sealed class CleanupFailureProvider : IResourceLeaseProvider, IDisposable
        {
            private readonly Object _asset;
            private readonly UniTaskCompletionSource<bool> _completion;
            public bool FailRelease, FailDispose, FailUnloadUnused;
            public int ReleaseCalls, DisposeCalls, UnloadUnusedCalls;
            public bool SupportsSyncLoad => true;

            public CleanupFailureProvider(Object asset, bool deferred = false)
            {
                _asset = asset;
                if (deferred) _completion = new UniTaskCompletionSource<bool>();
            }

            public async UniTask<ResourceLease<T>> AcquireAsync<T>(string path,
                CancellationToken token = default) where T : Object
            {
                if (_completion != null) await _completion.Task;
                return new ResourceLease<T>(_asset as T, () =>
                {
                    ReleaseCalls++;
                    if (FailRelease) throw new ApplicationException("lease release failed");
                });
            }

            public void Complete() => _completion.TrySetResult(true);
            public T Load<T>(string path) where T : Object => _asset as T;
            public UniTask<T> LoadAsync<T>(string path) where T : Object =>
                UniTask.FromResult(Load<T>(path));
            public void Unload(string path) { }
            public void UnloadUnused()
            {
                UnloadUnusedCalls++;
                if (FailUnloadUnused) throw new ApplicationException("retirement unload failed");
            }
            public void Dispose()
            {
                DisposeCalls++;
                if (FailDispose) throw new ApplicationException("provider dispose failed");
            }
        }
    }
}
