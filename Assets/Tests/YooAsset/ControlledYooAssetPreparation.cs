using System;
using System.Collections;
using System.Reflection;
using global::YooAsset;

namespace ZWebGLFrameWork.YooAsset.Tests
{
    /// <summary>只在测试中控制真实清单操作的结果，并从全局队列移除本测试的操作。</summary>
    internal sealed class ControlledYooAssetPreparation : IDisposable
    {
        private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
        private readonly ResourcePackage _package;

        public YooAssetBusinessContentUpdater Updater { get; }
        public AsyncOperationBase Current => (AsyncOperationBase)typeof(YooAssetBusinessContentUpdater)
            .GetField("_runningOperation", InstanceFlags).GetValue(Updater);

        public ControlledYooAssetPreparation(ResourcePackage package)
        {
            _package = package;
            Updater = new YooAssetBusinessContentUpdater(package);
        }

        public void CompleteVersion()
        {
            Set(Current, "PackageVersion", "controlled-version");
            Set(Current, "Status", EOperationStatus.Succeed);
        }

        public void CompleteManifest() => Set(Current, "Status", EOperationStatus.Succeed);

        public void Fail(string error)
        {
            Set(Current, "Error", error);
            Set(Current, "Status", EOperationStatus.Failed);
        }

        public void Dispose()
        {
            // 所有反射只针对锁定的 YooAsset 测试夹具，不改变其他包或生产代码。
            var system = typeof(ResourcePackage).Assembly.GetType("YooAsset.OperationSystem", true);
            foreach (string field in new[] { "_newList", "_operations" })
            {
                var operations = (IList)system.GetField(field, StaticFlags).GetValue(null);
                for (int i = operations.Count - 1; i >= 0; i--)
                    if (((AsyncOperationBase)operations[i]).PackageName == _package.PackageName)
                        operations.RemoveAt(i);
            }
        }

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property, InstanceFlags).SetValue(target, value);
    }
}
