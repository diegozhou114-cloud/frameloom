using System;
using System.Collections.Generic;
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
    internal class LegacyLeaseProbeProvider : IResourceProvider
    {
        protected readonly Object Resource;
        public int LoadCalls { get; private set; }
        public bool SupportsSyncLoad => true;

        public LegacyLeaseProbeProvider(Object resource) => Resource = resource;
        public T Load<T>(string path) where T : Object
        {
            LoadCalls++;
            return Resource as T;
        }
        public UniTask<T> LoadAsync<T>(string path) where T : Object =>
            UniTask.FromResult(Load<T>(path));
        public void Unload(string path) { }
        public void UnloadUnused() { }
    }

    internal sealed class LeaseProbeProvider : LegacyLeaseProbeProvider,
        IResourceLeaseProvider, IDisposable
    {
        private readonly UniTaskCompletionSource<bool> _completion;
        public int AcquireCalls { get; private set; }
        public int ReleaseCalls { get; private set; }
        public int DisposeCalls { get; private set; }

        public LeaseProbeProvider(Object resource, bool deferred = false) : base(resource)
        {
            if (deferred) _completion = new UniTaskCompletionSource<bool>();
        }

        public async UniTask<ResourceLease<T>> AcquireAsync<T>(string path,
            CancellationToken token = default) where T : Object
        {
            AcquireCalls++;
            // Deliberately ignore cancellation: the manager must clean up late ownership.
            if (_completion != null) await _completion.Task;
            return new ResourceLease<T>(Resource as T, () => ReleaseCalls++);
        }

        public void CompleteAcquires() => _completion.TrySetResult(true);
        public void Dispose() => DisposeCalls++;
    }

    internal sealed class DeferredBusinessUpdater : IBusinessContentUpdater
    {
        private readonly List<UniTaskCompletionSource<BusinessContentUpdateResult>> _requests = new();
        public int Calls => _requests.Count;

        public UniTask<BusinessContentUpdateResult> PrepareAsync(CancellationToken token)
        {
            var request = new UniTaskCompletionSource<BusinessContentUpdateResult>();
            _requests.Add(request);
            return request.Task;
        }

        public void Complete(int index, IResourceProvider provider) =>
            _requests[index].TrySetResult(BusinessContentUpdateResult.Updated(provider));
    }

    public abstract class ResourceManagerLeaseFixture : RuntimeTestBase
    {
        protected ResourceManager Manager;
        protected TextAsset Asset;
        internal LeaseProbeProvider Provider;

        [SetUp]
        public void SetUpResourceManager()
        {
            Asset = new TextAsset("owned resource");
            RegisterCleanup(() => Object.DestroyImmediate(Asset));
            Manager = CreateOwnedObject("ResourceLeaseManager").AddComponent<ResourceManager>();
            Assert.That(Manager.Init(), Is.True);
            Provider = new LeaseProbeProvider(Asset);
            Manager.SetBusinessProvider(Provider);
        }

        protected static async Task<Exception> CaptureFailure<T>(UniTask<T> operation)
        {
            try { await operation; }
            catch (Exception exception) { return exception; }
            Assert.Fail("Expected the operation to fail.");
            return null;
        }
    }
}
