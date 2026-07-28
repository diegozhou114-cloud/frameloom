using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;
using ZWebGLFrameWork.Runtime.Tests.Support;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class ResourceManagerProviderTests : RuntimeTestBase
    {
        private sealed class AssetMarker : ScriptableObject { }

        private sealed class FakeProvider : IResourceProvider
        {
            private readonly Object _asset;

            public bool UnloadUnusedCalled { get; private set; }
            public bool SupportsSyncLoad => true;

            public FakeProvider(Object asset)
            {
                _asset = asset;
            }

            public UniTask<T> LoadAsync<T>(string path) where T : Object =>
                UniTask.FromResult(_asset as T);

            public T Load<T>(string path) where T : Object => _asset as T;
            public void Unload(string path) { }
            public void UnloadUnused() => UnloadUnusedCalled = true;
        }

        private sealed class DelayedProvider : IResourceProvider
        {
            private readonly UniTaskCompletionSource<Object> _completion = new();

            public bool SupportsSyncLoad => true;

            public async UniTask<T> LoadAsync<T>(string path) where T : Object
            {
                Object asset = await _completion.Task;
                return asset as T;
            }

            public void Complete(Object asset) => _completion.TrySetResult(asset);
            public T Load<T>(string path) where T : Object => null;
            public void Unload(string path) { }
            public void UnloadUnused() { }
        }

        [Test]
        public async System.Threading.Tasks.Task ResourceManager_SetProvider_InvalidatesOldProviderCache()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                var firstProvider = new FakeProvider(firstAsset);
                var secondProvider = new FakeProvider(secondAsset);

                Assert.That(manager.Init(firstProvider), Is.True);
                Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"),
                    Is.SameAs(firstAsset));

                manager.SetProvider(secondProvider);

                Assert.That(firstProvider.UnloadUnusedCalled, Is.True);
                Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"),
                    Is.SameAs(secondAsset));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task ResourceManager_InitWithNewProvider_InvalidatesOldProviderCache()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                var firstProvider = new FakeProvider(firstAsset);
                var secondProvider = new FakeProvider(secondAsset);
                Assert.That(manager.Init(firstProvider), Is.True);
                Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"),
                    Is.SameAs(firstAsset));

                Assert.That(manager.Init(secondProvider), Is.True);

                Assert.That(firstProvider.UnloadUnusedCalled, Is.True);
                Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"),
                    Is.SameAs(secondAsset));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task
            ResourceManager_SetProvider_DoesNotCacheAnInFlightOldProviderResult()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                var firstProvider = new DelayedProvider();
                var secondProvider = new FakeProvider(secondAsset);
                Assert.That(manager.Init(firstProvider), Is.True);

                UniTask<AssetMarker> pending = manager.LoadAsync<AssetMarker>("shared/key");
                manager.SetProvider(secondProvider);
                firstProvider.Complete(firstAsset);

                Assert.That(await pending, Is.SameAs(firstAsset));
                Assert.That(await manager.LoadAsync<AssetMarker>("shared/key"),
                    Is.SameAs(secondAsset));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }
    }
}
