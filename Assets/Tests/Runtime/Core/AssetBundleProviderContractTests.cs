using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using ZWebGLFrameWork.Core;

namespace ZWebGLFrameWork.Runtime.Tests.Core
{
    public sealed class AssetBundleProviderContractTests
    {
        private sealed class ProbeProvider : AssetBundleProvider
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> UnloadedBundles = new List<string>();

            public override UniTask LoadDependenciesAsync(string bundleName)
            {
                Calls.Add($"deps:{bundleName}");
                return UniTask.CompletedTask;
            }

            public override UniTask<AssetBundle> LoadBundleAsync(string bundleName)
            {
                Calls.Add($"bundle:{bundleName}");
                return UniTask.FromResult<AssetBundle>(null);
            }

            protected override IReadOnlyList<string> GetDependencies(string bundleName) =>
                bundleName == "units" || bundleName == "buildings"
                    ? new[] { "shared-materials" }
                    : System.Array.Empty<string>();

            public void Retain(string bundleName) => RetainBundleReferences(bundleName);
            public void Release(string bundleName) => ReleaseBundleReferences(bundleName);

            protected override void UnloadBundle(string bundleName) =>
                UnloadedBundles.Add(bundleName);
        }

        [Test]
        public async System.Threading.Tasks.Task AssetBundleProvider_LoadAndUnload_UsesDependenciesAndBundleIdentity()
        {
            var provider = new ProbeProvider();

            await provider.LoadAsync<GameObject>("units/barbarian");

            CollectionAssert.AreEqual(
                new[] { "deps:units", "bundle:units" },
                provider.Calls);
            provider.Unload("units/barbarian");
            Assert.That(provider.UnloadedBundles, Has.Member("units"));
        }

        [Test]
        public void Unload_ConvertsAssetPathToBundleIdentity()
        {
            var provider = new ProbeProvider();

            provider.Unload("units/barbarian");

            Assert.That(provider.UnloadedBundles, Has.Member("units"));
        }

        [Test]
        public void SharedDependency_UnloadsOnlyAfterEveryOwnerReleases()
        {
            var provider = new ProbeProvider();
            provider.Retain("units");
            provider.Retain("buildings");

            provider.Release("units");
            Assert.That(provider.UnloadedBundles, Has.Member("units"));
            Assert.That(provider.UnloadedBundles, Has.No.Member("shared-materials"));

            provider.Release("buildings");
            Assert.That(provider.UnloadedBundles, Has.Member("buildings"));
            Assert.That(provider.UnloadedBundles.Count(
                value => value == "shared-materials"), Is.EqualTo(1));
        }
    }
}
