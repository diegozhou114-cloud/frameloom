using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System.Reflection;
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
        public async System.Threading.Tasks.Task ResourceManager_BusinessLoadsUseBusinessProvider()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var businessAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                Assert.That(manager.Init(), Is.True);
                manager.SetBusinessProvider(new FakeProvider(businessAsset));

                Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
                    Is.SameAs(businessAsset));
            }
            finally
            {
                Object.DestroyImmediate(businessAsset);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task ResourceManager_BusinessProviderSwapKeepsFrameworkProvider()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstBusinessAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondBusinessAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                Assert.That(manager.Init(), Is.True);
                IResourceProvider frameworkProvider = manager.FrameworkProvider;
                var firstProvider = new FakeProvider(firstBusinessAsset);
                var secondProvider = new FakeProvider(secondBusinessAsset);
                manager.SetBusinessProvider(firstProvider);
                Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
                    Is.SameAs(firstBusinessAsset));

                manager.SetBusinessProvider(secondProvider);

                Assert.That(firstProvider.UnloadUnusedCalled, Is.True);
                Assert.That(manager.FrameworkProvider, Is.SameAs(frameworkProvider));
                Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
                    Is.SameAs(secondBusinessAsset));
            }
            finally
            {
                Object.DestroyImmediate(firstBusinessAsset);
                Object.DestroyImmediate(secondBusinessAsset);
            }
        }

        [Test]
        public async System.Threading.Tasks.Task
            ResourceManager_BusinessProviderSwap_DoesNotCacheAnInFlightOldProviderResult()
        {
            var manager = CreateOwnedObject("ResourceManager").AddComponent<ResourceManager>();
            var firstAsset = ScriptableObject.CreateInstance<AssetMarker>();
            var secondAsset = ScriptableObject.CreateInstance<AssetMarker>();
            try
            {
                var firstProvider = new DelayedProvider();
                var secondProvider = new FakeProvider(secondAsset);
                Assert.That(manager.Init(), Is.True);
                manager.SetBusinessProvider(firstProvider);

                UniTask<AssetMarker> pending =
                    manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key");
                manager.SetBusinessProvider(secondProvider);
                firstProvider.Complete(firstAsset);

                Assert.That(await pending, Is.SameAs(firstAsset));
                Assert.That(await manager.LoadAsync<AssetMarker>(ResourceLayer.Business, "shared/key"),
                    Is.SameAs(secondAsset));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [Test]
        public void ResourceManager_FrameworkProviderCannotBeReplacedThroughPublicApi()
        {
            Assert.That(typeof(ResourceManager).GetMethod("SetFrameworkProvider",
                BindingFlags.Instance | BindingFlags.Public), Is.Null);

            PropertyInfo frameworkProvider = typeof(ResourceManager).GetProperty("FrameworkProvider",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(frameworkProvider?.CanWrite, Is.False);
        }
    }
}
