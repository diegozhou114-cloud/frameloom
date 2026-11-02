using System;
using System.IO;
using System.Reflection;
using global::YooAsset;
using global::YooAsset.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.YooAsset.IntegrationTests
{
    /// <summary>构建真实本地包；临时 collector 仅在内存中替换，测试资产和输出全部清理。</summary>
    internal sealed class YooAssetBundleFixture : IDisposable
    {
        private readonly string _assetRoot;
        private readonly string _outputRoot;
        private readonly string _cacheRoot;

        public string PackageName { get; } = "LeaseAcceptance" + Guid.NewGuid().ToString("N");
        public string BuildinDirectory => Path.Combine(_outputRoot, "Buildin", PackageName);

        public YooAssetBundleFixture()
        {
            _assetRoot = "Assets/" + PackageName;
            _outputRoot = Path.Combine(Path.GetTempPath(), PackageName);
            var cacheRoot = typeof(YooAssetSettingsData).GetMethod("GetYooDefaultCacheRoot",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            _cacheRoot = Path.Combine((string)cacheRoot, PackageName);
            Assert.That(Directory.Exists(_cacheRoot), Is.False, "Test cache must be unique.");
        }

        public void Build()
        {
            CreateAssets();
            var field = typeof(AssetBundleCollectorSettingData).GetField("_setting",
                BindingFlags.Static | BindingFlags.NonPublic);
            var original = field.GetValue(null);
            var setting = ScriptableObject.CreateInstance<AssetBundleCollectorSetting>();
            try
            {
                var collector = new AssetBundleCollector
                {
                    CollectPath = _assetRoot + "/Mains",
                    CollectorGUID = AssetDatabase.AssetPathToGUID(_assetRoot + "/Mains"),
                    PackRuleName = nameof(PackSeparately)
                };
                var group = new AssetBundleCollectorGroup { GroupName = "LeaseAcceptance" };
                group.Collectors.Add(collector);
                var package = new AssetBundleCollectorPackage
                {
                    PackageName = PackageName,
                    EnableAddressable = true
                };
                package.Groups.Add(group);
                setting.Packages.Add(package);
                field.SetValue(null, setting);

                var parameters = new BuiltinBuildParameters
                {
                    BuildOutputRoot = Path.Combine(_outputRoot, "Build"),
                    BuildinFileRoot = Path.Combine(_outputRoot, "Buildin"),
                    BuildPipeline = EBuildPipeline.BuiltinBuildPipeline.ToString(),
                    BuildBundleType = (int)EBuildBundleType.AssetBundle,
                    BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                    PackageName = PackageName,
                    PackageVersion = "1",
                    EnableSharePackRule = true,
                    VerifyBuildingResult = true,
                    BuildinFileCopyOption = EBuildinFileCopyOption.ClearAndCopyAll,
                    CompressOption = ECompressOption.LZ4
                };
                var result = new BuiltinBuildPipeline().Run(parameters, enableLog: false);
                Assert.That(result.Success, Is.True, result.ErrorInfo + "\n" + result.ErrorStack);
                TestContext.Progress.WriteLine("Built real YooAsset bundle package: " + BuildinDirectory);
            }
            finally
            {
                field.SetValue(null, original);
                Object.DestroyImmediate(setting);
            }
        }

        private void CreateAssets()
        {
            AssetDatabase.CreateFolder("Assets", PackageName);
            AssetDatabase.CreateFolder(_assetRoot, "Mains");
            AssetDatabase.CreateFolder(_assetRoot, "Shared");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { Color.magenta, Color.magenta, Color.magenta, Color.magenta });
            texture.Apply();
            AssetDatabase.CreateAsset(texture, _assetRoot + "/Shared/SharedTexture.asset");
            var shader = Shader.Find("Unlit/Texture");
            Assert.That(shader, Is.Not.Null);
            foreach (string name in new[] { "First", "Second" })
            {
                var material = new Material(shader) { name = name, mainTexture = texture };
                AssetDatabase.CreateAsset(material, _assetRoot + "/Mains/" + name + ".mat");
                AssetDatabase.SaveAssetIfDirty(material);
            }
            AssetDatabase.SaveAssetIfDirty(texture);
        }

        public void Dispose()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(_assetRoot))
                    Assert.That(AssetDatabase.DeleteAsset(_assetRoot), Is.True);
            }
            finally
            {
                if (Directory.Exists(_outputRoot)) Directory.Delete(_outputRoot, true);
                if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, true);
            }
        }
    }
}
