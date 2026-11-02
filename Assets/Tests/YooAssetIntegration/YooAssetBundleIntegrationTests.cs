using System;
using System.Collections;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using global::YooAsset;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ZWebGLFrameWork.Core;
using Object = UnityEngine.Object;
using ResourceManager = ZWebGLFrameWork.Core.ResourceManager;

namespace ZWebGLFrameWork.YooAsset.IntegrationTests
{
    public sealed class YooAssetBundleIntegrationTests
    {
        /// <summary>真实构建、版本和清单准备、共享依赖加载、替换提供者和最终卸载。</summary>
        [UnityTest]
        public IEnumerator OfflineBundles_KeepIndependentLeasesAcrossProviderReplacement() =>
            UniTask.ToCoroutine(async () =>
        {
            Assert.That(ResourceManager.HasInstance, Is.False, "Run without an active game manager.");
            using var fixture = new YooAssetBundleFixture();
            using var pump = new YooAssetOperationPump(fixture.PackageName);
            using var cancellation = new CancellationTokenSource();
            ResourcePackage package = null;
            GameObject owner = null;
            YooAssetProvider provider = null;
            YooAssetProvider replacement = null;
            ResourceLease<Material> first = null, repeated = null, second = null, current = null;
            try
            {
                fixture.Build();
                cancellation.CancelAfter(TimeSpan.FromSeconds(90));
                package = pump.CreatePackage();
                await pump.Wait(package.InitializeAsync(new OfflinePlayModeParameters
                {
                    BuildinFileSystemParameters = FileSystemParameters
                        .CreateDefaultBuildinFileSystemParameters(packageRoot: fixture.BuildinDirectory)
                }));
                var result = await pump.Complete(new YooAssetBusinessContentUpdater(package)
                    .PrepareAsync(cancellation.Token));
                Assert.That(result.Status, Is.EqualTo(BusinessContentUpdateStatus.Updated), result.Message);
                Assert.That(package.GetPackageVersion(), Is.EqualTo("1"));
                provider = (YooAssetProvider)result.BusinessProvider;

                owner = new GameObject("YooAssetBundleAcceptance");
                var manager = owner.AddComponent<ResourceManager>();
                Assert.That(manager.Init(), Is.True);
                var framework = manager.FrameworkProvider;
                manager.SetBusinessProvider(provider);
                string firstPath = package.GetAssetInfo("First").AssetPath.ToLowerInvariant();
                string secondPath = package.GetAssetInfo("Second").AssetPath.ToLowerInvariant();
                Assert.That(AssetBundle.GetAllLoadedAssetBundles().Any(bundle => bundle.Contains(firstPath)),
                    Is.False, "The test material bundle must not be loaded before acquisition.");
                first = await pump.Complete(manager.AcquireAsync<Material>(
                    ResourceLayer.Business, "First", cancellation.Token));
                repeated = await pump.Complete(manager.AcquireAsync<Material>(
                    ResourceLayer.Business, "First", cancellation.Token));
                second = await pump.Complete(manager.AcquireAsync<Material>(
                    ResourceLayer.Business, "Second", cancellation.Token));
                Material firstAsset = first.Asset;
                Material secondAsset = second.Asset;
                var sharedTexture = (Texture2D)firstAsset.mainTexture;
                var firstBundle = AssertBundleSource(firstPath, firstAsset);
                var secondBundle = AssertBundleSource(secondPath, secondAsset);
                Assert.That(firstBundle, Is.Not.SameAs(secondBundle), "Materials must use separate main bundles.");
                Assert.That(firstAsset, Is.SameAs(repeated.Asset));
                Assert.That(firstAsset, Is.Not.SameAs(secondAsset));
                Assert.That(sharedTexture, Is.Not.Null);
                Assert.That(secondAsset.mainTexture, Is.SameAs(sharedTexture));
                Assert.That(sharedTexture.GetPixel(0, 0), Is.EqualTo(Color.magenta));

                first.Dispose();
                await pump.Wait(package.UnloadUnusedAssetsAsync());
                Assert.That(repeated.Asset == null, Is.False, "A second lease must retain its material.");
                Assert.That(second.Asset.mainTexture, Is.SameAs(sharedTexture));

                replacement = new YooAssetProvider(package);
                manager.SetBusinessProvider(replacement);
                await pump.Wait(package.UnloadUnusedAssetsAsync());
                Assert.That(manager.FrameworkProvider, Is.SameAs(framework));
                Assert.That(repeated.Asset == null, Is.False, "Provider retirement must preserve issued leases.");
                Assert.That(second.Asset.mainTexture, Is.SameAs(sharedTexture));
                current = await pump.Complete(manager.AcquireAsync<Material>(
                    ResourceLayer.Business, "First", cancellation.Token));
                Assert.That(current.Asset, Is.SameAs(firstAsset));

                repeated.Dispose();
                second.Dispose();
                await pump.Wait(package.UnloadUnusedAssetsAsync());
                Assert.That(secondAsset == null, Is.True, "Unused main bundle should unload.");
                Assert.That(current.Asset.mainTexture, Is.SameAs(sharedTexture),
                    "A live lease must retain the shared dependency after the other main bundle unloads.");
                current.Dispose();
                await pump.Wait(package.UnloadUnusedAssetsAsync());
                Assert.That(firstAsset == null, Is.True, "The final main bundle should unload.");
                Assert.That(sharedTexture == null, Is.True, "The unused shared dependency should unload.");
            }
            finally
            {
                first?.Dispose();
                repeated?.Dispose();
                second?.Dispose();
                current?.Dispose();
                provider?.Dispose();
                replacement?.Dispose();
                if (owner != null) Object.DestroyImmediate(owner);
                if (package != null)
                {
                    pump.AbortPackageOperations();
                    await pump.Wait(package.DestroyAsync());
                }
            }
        });

        private static AssetBundle AssertBundleSource(string assetPath, Material asset)
        {
            var loaded = AssetBundle.GetAllLoadedAssetBundles().ToArray();
            var matches = loaded.Where(bundle => bundle.Contains(assetPath)).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1),
                "Expected one real loaded bundle containing " + assetPath + "; loaded: " +
                string.Join(", ", loaded.Select(bundle => bundle.name)));
            var source = matches[0];
            Assert.That(source.LoadAsset<Material>(assetPath), Is.SameAs(asset),
                "The lease must return the exact object owned by the real loaded bundle.");
            Debug.Log($"[YooAssetAcceptance] asset={assetPath}, instance={asset.GetInstanceID()}, " +
                $"bundle={source.name}, bundleInstance={source.GetInstanceID()}, " +
                $"editorPath={AssetDatabase.GetAssetPath(asset)}, persistent={AssetDatabase.Contains(asset)}");
            return source;
        }
    }
}
