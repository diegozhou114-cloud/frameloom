using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using global::YooAsset;
using Object = UnityEngine.Object;

namespace ZWebGLFrameWork.YooAsset.Tests
{
    /// <summary>
    /// 用锁定版本的真实包和 CompletedProvider 控制完成时机，不更改生产加载接口。
    /// </summary>
    internal sealed class ControlledYooAssetPackage
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const string Location = "test";
        private const string AssetPath = "Assets/ControlledYooAsset.asset";
        private readonly object _manager;
        private readonly object _packageAsset;

        public ResourcePackage Package { get; }
        public object Operation { get; }
        public int RefCount => GetRefCount(Operation);

        public ControlledYooAssetPackage()
        {
            Package = (ResourcePackage)Create("ResourcePackage", "ControlledYooAsset" + Guid.NewGuid().ToString("N"));
            _manager = Create("ResourceManager", Package.PackageName);
            _packageAsset = Create("PackageAsset");
            SetField(_packageAsset, "AssetPath", AssetPath);
            SetField(_packageAsset, "Address", Location);

            var manifest = Create("PackageManifest");
            SetField(manifest, "PackageName", Package.PackageName);
            SetField(manifest, "AssetPathMapping1", new Dictionary<string, string> { [Location] = AssetPath });
            var assetsField = manifest.GetType().GetField("AssetDic", Flags);
            var assets = (IDictionary)Activator.CreateInstance(assetsField.FieldType);
            assets.Add(AssetPath, _packageAsset);
            assetsField.SetValue(manifest, assets);

            var playMode = Create("PlayModeImpl", Package.PackageName, EPlayMode.EditorSimulateMode);
            SetProperty(playMode, "ActiveManifest", manifest);
            SetField(Package, "_playModeImpl", playMode);
            SetField(Package, "_resourceManager", _manager);
            SetField(Package, "_isInitialize", true);
            SetField(Package, "_initializeStatus", EOperationStatus.Succeed);
            Operation = AddAssetType(typeof(UnityEngine.TextAsset));
        }

        public object AddAssetType(Type type)
        {
            var info = Create("AssetInfo", Package.PackageName, _packageAsset, type);
            var operation = Create("CompletedProvider", _manager, info);
            var providers = (IDictionary)GetField(_manager, "ProviderDic");
            providers.Add("LoadAssetAsync" + info.GetType().GetProperty("GUID", Flags).GetValue(info), operation);
            return operation;
        }

        public void Complete(Object asset) => Complete(Operation, asset);

        public void Complete(object operation, Object asset)
        {
            SetProperty(operation, "AssetObject", asset);
            SetProperty(operation, "Status", EOperationStatus.Succeed);
        }

        public void Fail(string error)
        {
            Operation.GetType().GetMethod("SetCompletedWithError", Flags).Invoke(Operation, new object[] { error });
        }

        public int GetRefCount(object operation) =>
            (int)operation.GetType().GetProperty("RefCount", Flags).GetValue(operation);

        private static object Create(string type, params object[] arguments) =>
            Activator.CreateInstance(typeof(ResourcePackage).Assembly.GetType("YooAsset." + type, true),
                Flags, null, arguments, null);

        private static object GetField(object target, string field) =>
            target.GetType().GetField(field, Flags).GetValue(target);

        private static void SetField(object target, string field, object value) =>
            target.GetType().GetField(field, Flags).SetValue(target, value);

        private static void SetProperty(object target, string property, object value) =>
            target.GetType().GetProperty(property, Flags).SetValue(target, value);
    }
}
